"""Bounded Frida lifecycle and calibration; platform I/O is dependency injected."""
from __future__ import annotations

import queue
import threading
import time

from calibration import Calibration


class BridgeFailure(RuntimeError):
    pass


class KeyBridge:
    def __init__(self, frida, find_target, verify_target, source, emit,
                 retry_delays=(2, 5, 10, 30), lease_interval=3,
                 operation_timeout=5, ready_timeout=3):
        self.frida = frida
        self.find_target = find_target
        self.verify_target = verify_target
        self.source = source
        self.emit = emit
        self.retry_delays = retry_delays
        self.lease_interval = lease_interval
        self.operation_timeout = operation_timeout
        self.ready_timeout = ready_timeout
        self.calibration = Calibration()
        self._stop = threading.Event()
        self._recalibrate = threading.Event()
        self._overflow = threading.Event()
        self._events = queue.Queue(maxsize=64)
        self._token_lock = threading.Lock()
        self._token = None
        self._generation = 0
        self._thread = threading.Thread(target=self._run, name="keybridge-observer", daemon=True)
        self.finished = threading.Event()
        self.exit_reason = "stopped"

    def start(self):
        self._thread.start()

    def recalibrate(self):
        self._recalibrate.set()

    def stop(self):
        self._stop.set()
        with self._token_lock:
            if self._token is not None:
                self._token.cancel()

    def close(self):
        self.stop()
        if self._thread.ident is None:
            return True
        self._thread.join(timeout=self.operation_timeout * 2 + 2)
        return not self._thread.is_alive()

    def _call(self, function, *args, cleanup=False, **kwargs):
        # A cancelled attach token must never be reused for unload/detach.
        token = self.frida.Cancellable()
        with self._token_lock:
            if not cleanup:
                if self._stop.is_set():
                    raise BridgeFailure("stopping")
                self._token = token
        timer = threading.Timer(self.operation_timeout, token.cancel)
        timer.daemon = True
        timer.start()
        try:
            return function(*args, cancellable=token, **kwargs)
        finally:
            timer.cancel()
            if not cleanup:
                with self._token_lock:
                    if self._token is token:
                        self._token = None

    def _enqueue(self, generation, value):
        # Runs on Frida's callback thread: no pipe writes, RPC or blocking waits.
        if self._stop.is_set() or generation != self._generation:
            return
        try:
            self._events.put_nowait((generation, value))
        except queue.Full:
            self._overflow.set()

    def _on_message(self, generation, message, _data):
        if not isinstance(message, dict):
            self._enqueue(generation, {"kind": "error"})
        elif message.get("type") == "send" and isinstance(message.get("payload"), dict):
            payload = message["payload"]
            if payload.get("kind") in ("report", "invalid", "ready", "stopped"):
                self._enqueue(generation, payload)
        elif message.get("type") == "error":
            # Frida exception strings/stacks can contain device identifiers.
            self._enqueue(generation, {"kind": "error"})

    def _drain(self):
        while True:
            try:
                self._events.get_nowait()
            except queue.Empty:
                return

    def _reset(self):
        self.calibration.reset()
        self._drain()
        self._overflow.clear()

    def _status(self, phase, detail):
        self.emit({"type": "status", "phase": phase, "detail": detail})

    def _cleanup(self, script, session):
        unloaded = script is None
        detached = session is None
        if script is not None:
            try:
                self._call(script.unload, cleanup=True)
                unloaded = True
            except Exception:
                pass
        if session is not None:
            try:
                self._call(session.detach, cleanup=True)
                detached = True
            except Exception:
                pass
        # Either successful unload or detach removes this non-eternal script.
        # If both are uncertain, terminate and let the 10-second lease expire.
        return unloaded or detached

    def _observe(self, script, host_pid, generation):
        ready = False
        ready_deadline = time.monotonic() + self.ready_timeout
        renew_at = time.monotonic() + self.lease_interval
        while not self._stop.is_set():
            if self._overflow.is_set():
                raise BridgeFailure("observer_backpressure")
            if self._recalibrate.is_set():
                self._recalibrate.clear()
                self._reset()
                if ready:
                    self.emit(self.calibration.status())
            now = time.monotonic()
            if not ready and now >= ready_deadline:
                raise BridgeFailure("observer_not_ready")
            if now >= renew_at:
                if self.find_target() != host_pid:
                    raise BridgeFailure("remote_host_changed")
                self.verify_target(host_pid)
                renewed = self._call(script.exports_sync.renew_lease)
                if renewed is not True:
                    raise BridgeFailure("observer_lease_expired")
                renew_at = time.monotonic() + self.lease_interval
            try:
                event_generation, event = self._events.get(timeout=0.05)
            except queue.Empty:
                continue
            if event_generation != generation:
                continue
            # A command arriving while get() waited invalidates queued edges too.
            if self._recalibrate.is_set():
                continue
            kind = event.get("kind")
            if kind in ("error", "stopped", "detached"):
                raise BridgeFailure("observer_disconnected")
            if kind == "ready":
                if not ready:
                    ready = True
                    self.emit(self.calibration.status())
            elif ready and kind == "report":
                for message in self.calibration.observe(event):
                    self.emit(message)
            elif ready and kind == "invalid":
                stream = event.get("stream")
                if type(stream) is int and 1 <= stream <= 16:
                    for message in self.calibration.invalidate(stream):
                        self.emit(message)

    def _run(self):
        failures = 0
        try:
            while not self._stop.is_set():
                self._generation += 1
                generation = self._generation
                self._reset()
                self._recalibrate.clear()
                script = session = None
                failure = "remote_host_unavailable"
                cleanup_ok = True
                try:
                    self._status("connecting" if failures == 0 else "reconnecting", "Connecting remote keys.")
                    host_pid = self.find_target()
                    self.verify_target(host_pid)
                    if self.find_target() != host_pid:
                        raise BridgeFailure("remote_host_changed")
                    manager = self.frida.get_device_manager()
                    device = self._call(manager.get_device_matching,
                                        lambda candidate: candidate.type == "local", timeout=0)
                    session = self._call(device.attach, host_pid, persist_timeout=0)
                    session.on("detached", lambda *_args, g=generation:
                               self._enqueue(g, {"kind": "detached"}))
                    script = self._call(session.create_script, self.source)
                    script.on("message", lambda msg, data, g=generation:
                              self._on_message(g, msg, data))
                    self._call(script.load)
                    self._observe(script, host_pid, generation)
                except BridgeFailure as error:
                    failure = str(error)
                except Exception:
                    failure = "remote_host_unavailable"
                finally:
                    self._generation += 1
                    self._reset()
                    cleanup_ok = self._cleanup(script, session)
                if self._stop.is_set():
                    break
                if not cleanup_ok:
                    self.exit_reason = "observer_cleanup_failed"
                    self._status("error", self.exit_reason)
                    break
                self._status("reconnecting", failure)
                delay = self.retry_delays[min(failures, len(self.retry_delays) - 1)]
                failures += 1
                if self._stop.wait(delay):
                    break
        except Exception:
            # Transport/backpressure failure is terminal. Never reconnect a pipe.
            self.exit_reason = "bridge_disconnected"
        finally:
            self._stop.set()
            self.finished.set()
