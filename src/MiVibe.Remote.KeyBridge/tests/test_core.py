"""Pure protocol, calibration and mocked lifecycle tests: never attach to a PID."""
import json
from pathlib import Path
import queue
import sys
import threading
import time
import unittest
from unittest.mock import Mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from bridge import KeyBridge
from calibration import Calibration, ORDER
from main import parse_arguments, serve
from protocol import CommandDecoder, ProtocolError, command, encode, pipe_path


def report(key=None, stream=1, other=False):
    return {"kind": "report", "stream": stream,
            "active": [] if key is None else [key], "other": other}


def arm(state, stream=1):
    result = []
    for key in ORDER:
        result.extend(state.observe(report(key, stream)))
        result.extend(state.observe(report(None, stream)))
    return result


class ProtocolTests(unittest.TestCase):
    def test_only_local_random_name(self):
        self.assertEqual(pipe_path("MiVibeRemote.Keys." + "a" * 32),
                         "\\\\.\\pipe\\MiVibeRemote.Keys." + "a" * 32)
        for value in ("\\\\server\\pipe\\name", "../name", "MiVibeRemote.Keys.x", None):
            with self.assertRaises(ProtocolError):
                pipe_path(value)

    def test_command_allowlist_and_exact_fields(self):
        for value in ("stop", "recalibrate", "ping"):
            self.assertEqual(command(json.dumps({"command": value}).encode()), value)
        for value in (b'{}', b'{"command":"eval"}', b'{"command":"ping","pid":12}',
                      b'{"command":[]}', b'[]', b'\xff', b''):
            with self.assertRaises(ProtocolError):
                command(value)

    def test_fragmented_and_multiple_lines(self):
        decoder = CommandDecoder()
        self.assertEqual(decoder.feed(b'{"comm'), [])
        self.assertEqual(decoder.feed(b'and":"ping"}\r\n{"command":"stop"}\n'), ["ping", "stop"])

    def test_incomplete_input_is_bounded(self):
        decoder = CommandDecoder()
        decoder.feed(b" " * 4096)
        with self.assertRaises(ProtocolError):
            decoder.feed(b" ")

    def test_output_fields_and_size(self):
        self.assertEqual(encode({"type": "heartbeat"}), b'{"type":"heartbeat"}\n')
        for value in ({"type": "key", "key": [], "sequence": 1},
                      {"type": "key", "key": "back", "sequence": True},
                      {"type": "status", "phase": [], "detail": ""},
                      {"type": "status", "phase": "active", "detail": "x" * 257},
                      {"type": "heartbeat", "raw": "anything"}):
            with self.assertRaises(ProtocolError):
                encode(value)

    def test_cli_excludes_arbitrary_target_or_source(self):
        valid = ["--pipe", "MiVibeRemote.Keys." + "b" * 32, "--parent-pid", "123"]
        self.assertEqual(parse_arguments(valid).parent_pid, 123)
        for extra in (["--host-pid", "1"], ["--source", "code.js"], ["--parent", "4"]):
            with self.assertRaises(ValueError):
                parse_arguments(valid + extra)


class CalibrationTests(unittest.TestCase):
    def test_full_release_required_and_never_emits_calibration_keys(self):
        state = Calibration()
        result = arm(state)
        self.assertEqual([m.get("step") for m in result], [1, 2, None])
        self.assertTrue(all(m["type"] == "status" for m in result))
        self.assertEqual(state.stream, 1)
        self.assertEqual(state.sequence, 0)

    def test_three_keys_cannot_be_combined_from_other_streams(self):
        state = Calibration()
        state.observe(report("back", 1))
        state.observe(report(None, 1))
        arm(state, 2)
        self.assertIsNone(state.stream)
        self.assertEqual(state.step, 1)
        self.assertEqual(state.candidate, 1)

    def test_wrong_order_or_overlap_resets_calibration(self):
        state = Calibration()
        state.observe(report("back"))
        state.observe(report("volume_up"))
        self.assertIsNone(state.candidate)
        self.assertEqual(state.step, 0)
        state.observe(report(None))
        state.observe(report("back", other=True))
        self.assertIsNone(state.candidate)

    def test_armed_only_one_calibrated_stream_and_one_press(self):
        state = Calibration()
        arm(state)
        self.assertEqual(state.observe(report("back", 2)), [])
        self.assertEqual(state.observe(report("back")), [{"type": "key", "key": "back", "sequence": 1}])
        self.assertEqual(state.observe(report("back")), [])
        self.assertEqual(state.observe(report("back", other=True)), [])
        self.assertEqual(state.observe(report(None)), [])
        self.assertEqual(state.observe(report("volume_up"))[0]["sequence"], 2)

    def test_reset_reconnect_and_invalid_stream_revoke_arm(self):
        state = Calibration()
        arm(state)
        state.observe(report("back"))
        self.assertEqual(state.invalidate(2), [])
        self.assertEqual(state.stream, 1)
        self.assertEqual(state.invalidate(1)[0]["step"], 0)
        self.assertIsNone(state.stream)
        arm(state, 3)
        self.assertEqual(state.observe(report("volume_down", 3))[0]["sequence"], 2)
        state.reset()
        self.assertEqual(state.observe(report("volume_up", 3)), [])

    def test_malformed_reports_are_ignored(self):
        state = Calibration()
        arm(state)
        for value in ({"stream": True, "active": ["back"], "other": False},
                      {"stream": 1, "active": [0xf1], "other": False},
                      {"stream": 17, "active": ["back"], "other": False},
                      {"stream": 1, "active": ["back"], "other": 0}):
            self.assertEqual(state.observe(value), [])


class FakeToken:
    def __init__(self):
        self.cancelled = threading.Event()

    def cancel(self):
        self.cancelled.set()


class FakeScript:
    def __init__(self):
        self.callback = None
        self.unloaded = []
        self.renewed = []
        self.exports_sync = self
        self.fail_cleanup = False

    def on(self, name, callback):
        assert name == "message"
        self.callback = callback

    def load(self, cancellable):
        self.send({"kind": "ready"})

    def send(self, payload):
        self.callback({"type": "send", "payload": payload}, None)

    def renew_lease(self, cancellable):
        self.renewed.append(cancellable)
        return True

    def unload(self, cancellable):
        self.unloaded.append(cancellable)
        if self.fail_cleanup:
            raise RuntimeError("mock unload failure")


class FakeSession:
    def __init__(self, script):
        self.script = script
        self.detached = []
        self.callback = None
        self.fail_cleanup = False

    def on(self, name, callback):
        assert name == "detached"
        self.callback = callback

    def create_script(self, source, cancellable):
        return self.script

    def detach(self, cancellable):
        self.detached.append(cancellable)
        if self.fail_cleanup:
            raise RuntimeError("mock detach failure")


class FakeFrida:
    Cancellable = FakeToken
    type = "local"

    def __init__(self):
        self.sessions = []
        self.attach_tokens = []
        self.block_attach = False

    def get_device_manager(self):
        return self

    def get_device_matching(self, predicate, timeout, cancellable):
        assert predicate(self) and timeout == 0
        return self

    def attach(self, pid, persist_timeout, cancellable):
        assert pid in (123, 456) and persist_timeout == 0
        self.attach_tokens.append(cancellable)
        if self.block_attach:
            cancellable.cancelled.wait(2)
            raise RuntimeError("mock cancelled")
        session = FakeSession(FakeScript())
        self.sessions.append(session)
        return session


class LifecycleTests(unittest.TestCase):
    def wait_for(self, predicate):
        deadline = time.monotonic() + 1.5
        while time.monotonic() < deadline:
            if predicate():
                return
            time.sleep(0.005)
        self.fail("mock lifecycle condition timed out")

    def create_bridge(self, **options):
        self.frida = FakeFrida()
        self.messages = []
        self.host_pid = 123
        bridge = KeyBridge(self.frida, lambda: self.host_pid, lambda pid: None,
                           "mock source only", self.messages.append,
                           retry_delays=(0.01,), lease_interval=0.05,
                           operation_timeout=0.2, **options)
        self.addCleanup(bridge.close)
        return bridge

    def active_script(self, bridge):
        bridge.start()
        self.wait_for(lambda: any(m.get("phase") == "calibrating" for m in self.messages))
        return self.frida.sessions[-1].script

    def test_calibration_then_key_and_recalibrate_revoke(self):
        bridge = self.create_bridge()
        script = self.active_script(bridge)
        for key in ORDER:
            script.send(report(key))
            script.send(report())
        self.wait_for(lambda: any(m.get("phase") == "active" for m in self.messages))
        self.assertFalse(any(m["type"] == "key" for m in self.messages))
        script.send(report("back"))
        self.wait_for(lambda: any(m["type"] == "key" for m in self.messages))
        bridge.recalibrate()
        self.wait_for(lambda: bridge.calibration.stream is None)
        script.send(report("volume_up"))
        time.sleep(0.02)
        self.assertEqual(len([m for m in self.messages if m["type"] == "key"]), 1)
        self.wait_for(lambda: bool(script.renewed))

    def test_host_change_unloads_detaches_and_old_callbacks_ignored(self):
        bridge = self.create_bridge()
        old_script = self.active_script(bridge)
        old_session = self.frida.sessions[0]
        self.host_pid = 456
        self.wait_for(lambda: len(self.frida.sessions) == 2)
        self.assertEqual(len(old_script.unloaded), 1)
        self.assertEqual(len(old_session.detached), 1)
        self.assertIsNot(old_script.unloaded[0], old_session.detached[0])
        for key in ORDER:
            old_script.send(report(key))
            old_script.send(report())
        time.sleep(0.03)
        self.assertIsNone(bridge.calibration.stream)

    def test_stop_cancels_attach_and_never_retries(self):
        bridge = self.create_bridge()
        self.frida.block_attach = True
        bridge.start()
        self.wait_for(lambda: bool(self.frida.attach_tokens))
        self.assertTrue(bridge.close())
        self.assertTrue(self.frida.attach_tokens[0].cancelled.is_set())
        self.assertEqual(len(self.frida.attach_tokens), 1)

    def test_both_cleanup_failures_are_terminal_and_no_second_attach(self):
        bridge = self.create_bridge()
        script = self.active_script(bridge)
        script.fail_cleanup = self.frida.sessions[0].fail_cleanup = True
        script.send({"kind": "stopped"})
        self.wait_for(bridge.finished.is_set)
        self.assertEqual(bridge.exit_reason, "observer_cleanup_failed")
        self.assertEqual(len(self.frida.sessions), 1)

    def test_overflow_is_bounded_and_failed_closed(self):
        bridge = self.create_bridge()
        bridge._generation = 10
        for _ in range(1000):
            bridge._enqueue(10, report("back"))
        self.assertEqual(bridge._events.qsize(), 64)
        self.assertTrue(bridge._overflow.is_set())

    def test_parent_exit_and_pipe_disconnect_close_observer(self):
        for parent_alive, disconnected in ((False, False), (True, True)):
            parent = Mock()
            parent.alive.return_value = parent_alive
            pipe = Mock()
            pipe.closed = threading.Event()
            if disconnected:
                pipe.closed.set()
            bridge = Mock()
            bridge.finished = threading.Event()
            bridge.close.return_value = True
            serve(parent, pipe, bridge, queue.Queue())
            bridge.start.assert_called_once()
            bridge.close.assert_called_once()

    def test_commands_and_heartbeat_do_not_wait_for_attach(self):
        parent = Mock()
        parent.alive.return_value = True
        pipe = Mock()
        pipe.closed = threading.Event()
        bridge = Mock()
        bridge.finished = threading.Event()
        bridge.close.return_value = True
        commands = queue.Queue()
        for cmd in ("recalibrate", "ping", "stop"):
            commands.put(cmd)
        serve(parent, pipe, bridge, commands)
        bridge.recalibrate.assert_called_once()
        bridge.close.assert_called_once()
        self.assertEqual([c.args[0] for c in pipe.send.call_args_list].count({"type": "heartbeat"}), 2)


if __name__ == "__main__":
    unittest.main()
