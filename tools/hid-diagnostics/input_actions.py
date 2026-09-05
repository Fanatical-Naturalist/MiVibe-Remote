"""Opt-in, calibrated action test for the finite RC003 report capture.

Importing this module sends no input. The caller must close the instance before
closing its log, and must stop it whenever the report tap disconnects or expires.
No keyboard hook, native-event suppression, or foreground activation is used.
"""
from __future__ import annotations

import ctypes
from ctypes import wintypes
import os
import queue
import re
import threading
import time


_MARKER = 0x4D69566962655431  # MiVibeT1; preserved on every injected event.
_CALIBRATION = ("back", "volume_up", "volume_down")
_KNOWN = frozenset((*_CALIBRATION, "menu", "home", "tv", "up", "down",
                    "left", "right", "ok", "power", "microphone"))
_MODIFIERS = (0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C)
_MAX_AGE = 0.35


class _MOUSEINPUT(ctypes.Structure):
    _fields_ = [("dx", wintypes.LONG), ("dy", wintypes.LONG),
                ("mouseData", wintypes.DWORD), ("dwFlags", wintypes.DWORD),
                ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.c_size_t)]


class _KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wintypes.WORD), ("wScan", wintypes.WORD),
                ("dwFlags", wintypes.DWORD), ("time", wintypes.DWORD),
                ("dwExtraInfo", ctypes.c_size_t)]


class _HARDWAREINPUT(ctypes.Structure):
    _fields_ = [("uMsg", wintypes.DWORD), ("wParamL", wintypes.WORD),
                ("wParamH", wintypes.WORD)]


class _INPUTUNION(ctypes.Union):
    _fields_ = [("mi", _MOUSEINPUT), ("ki", _KEYBDINPUT), ("hi", _HARDWAREINPUT)]


class _INPUT(ctypes.Structure):
    _anonymous_ = ("data",)
    _fields_ = [("type", wintypes.DWORD), ("data", _INPUTUNION)]


def _input(key: tuple[str, int, bool], pressed: bool) -> _INPUT:
    kind, code, extended = key
    item = _INPUT()
    if kind == "keyboard":
        item.type = 1
        item.ki = _KEYBDINPUT(0, code, 0x0008 | (1 if extended else 0)
                             | (0 if pressed else 0x0002), 0, _MARKER)
    else:
        item.type = 0
        item.mi = _MOUSEINPUT(0, 0, code, 0x0080 if pressed else 0x0100, 0, _MARKER)
    return item


class InputActionTest:
    """Calibrate one stream, then emit discrete actions from its press edges.

    navigation: 'none' sends only Delete; 'adjacent' adds Ctrl+PageUp/PageDown;
    'history' adds XBUTTON1/XBUTTON2. Volume actions require the Codex app.
    Delete is accepted only in Codex or notepad.exe. Every action is one-shot.
    """

    def __init__(self, record, navigation="none"):
        if os.name != "nt" or ctypes.sizeof(ctypes.c_void_p) != 8:
            raise RuntimeError("Input action test requires Windows x64.")
        if navigation not in {"none", "adjacent", "history"}:
            raise ValueError("navigation must be none, adjacent, or history")
        if ctypes.sizeof(_INPUT) != 40:
            raise RuntimeError("Unexpected x64 INPUT structure layout.")
        self._record_callback = record
        self._navigation = navigation
        self._queue = queue.Queue(maxsize=16)
        self._stop = threading.Event()
        self._overflow = threading.Event()
        self._candidate = None
        self._stream = None
        self._step = 0
        self._waiting_release = None
        self._active = set()
        self._pressed = []
        self._user = ctypes.WinDLL("user32", use_last_error=True)
        self._kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self._user.SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(_INPUT), ctypes.c_int]
        self._user.SendInput.restype = wintypes.UINT
        self._user.GetForegroundWindow.argtypes = []
        self._user.GetForegroundWindow.restype = wintypes.HWND
        self._user.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
        self._user.GetWindowThreadProcessId.restype = wintypes.DWORD
        self._user.GetAsyncKeyState.argtypes = [ctypes.c_int]
        self._user.GetAsyncKeyState.restype = ctypes.c_short
        self._kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        self._kernel.OpenProcess.restype = wintypes.HANDLE
        self._kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        self._kernel.CloseHandle.restype = wintypes.BOOL
        self._kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD,
            wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
        self._kernel.QueryFullProcessImageNameW.restype = wintypes.BOOL
        self._record("action_calibration", sequence=list(_CALIBRATION), navigation=navigation)
        self._thread = threading.Thread(target=self._run, name="rc003-action-test", daemon=True)
        self._thread.start()

    def _record(self, kind, **fields):
        try:
            self._record_callback(kind, **fields)
        except Exception:
            # Sending an unlogged action is inappropriate for a diagnostic.
            self._stop.set()

    def observe(self, payload):
        """Non-waiting report callback; all calibration/input work is queued."""
        if self._stop.is_set() or not isinstance(payload, dict):
            return
        if payload.get("kind") in {"stopped", "detached", "host_changed", "script_error"}:
            self._stop.set()
            return
        stream = payload.get("stream")
        if type(stream) is not int or not 1 <= stream <= 16:
            return
        fields = []
        for name in ("down", "up", "active"):
            value = payload.get(name)
            if not isinstance(value, list) or len(value) > 3:
                return
            if any(not isinstance(key, str) or key not in _KNOWN for key in value):
                return
            fields.append(frozenset(value))
        down, up, active = fields
        if down & up or not down <= active or up & active:
            return
        try:
            self._queue.put_nowait((time.monotonic(), self._user.GetForegroundWindow(),
                                    stream, down, up, active))
        except queue.Full:
            # Lost edges invalidate calibration and any queued press/release pair.
            self._overflow.set()

    def _reset(self):
        self._candidate = self._stream = self._waiting_release = None
        self._step = 0
        self._active.clear()

    def _discard_queue(self):
        while True:
            try:
                self._queue.get_nowait()
            except queue.Empty:
                return

    def _calibrate(self, stream, down, up, active):
        if self._candidate is None:
            if down == {"back"} and active == {"back"} and not up:
                self._candidate = stream
                self._waiting_release = "back"
            return
        if stream != self._candidate:
            return
        expected = _CALIBRATION[self._step]
        if self._waiting_release is not None:
            if not down and not active and up == {expected}:
                self._step += 1
                self._waiting_release = None
                if self._step == len(_CALIBRATION):
                    self._stream = stream
                    self._active.clear()
                    self._record("action_armed", stream=stream, navigation=self._navigation)
                else:
                    self._record("action_calibration_next", key=_CALIBRATION[self._step])
            elif down or up or active != {expected}:
                self._reset()
                self._record("action_calibration_reset", reason="unexpected_edge")
        elif down == {expected} and active == {expected} and not up:
            self._waiting_release = expected
        elif down or active:
            self._reset()
            self._record("action_calibration_reset", reason="unexpected_key")

    def _foreground(self):
        window = self._user.GetForegroundWindow()
        process_id = wintypes.DWORD()
        if not window or not self._user.GetWindowThreadProcessId(window, ctypes.byref(process_id)):
            return None
        handle = self._kernel.OpenProcess(0x1000, False, process_id.value)
        if not handle:
            return None
        try:
            length = wintypes.DWORD(32768)
            image = ctypes.create_unicode_buffer(length.value)
            if not self._kernel.QueryFullProcessImageNameW(handle, 0, image, ctypes.byref(length)):
                return None
            path = image.value.casefold()
            basename = os.path.basename(path)
            codex = bool(re.search(r"\\openai\.codex_[^\\]+\\app\\(?:chatgpt|codex)\.exe$", path))
            return window, process_id.value, basename, codex
        finally:
            self._kernel.CloseHandle(handle)

    def _send(self, events):
        array = (_INPUT * len(events))(*(_input(key, pressed) for key, pressed in events))
        ctypes.set_last_error(0)
        sent = int(self._user.SendInput(len(events), array, ctypes.sizeof(_INPUT)))
        error = ctypes.get_last_error()
        for key, pressed in events[:sent]:
            if pressed:
                if key not in self._pressed:
                    self._pressed.append(key)
            elif key in self._pressed:
                self._pressed.remove(key)
        return sent, error

    def _release_pending(self):
        # Only release keys whose down was accepted from this instance's batch.
        for _ in range(2):
            if not self._pressed:
                return True
            events = [(key, False) for key in reversed(self._pressed)]
            sent, error = self._send(events)
            if sent != len(events):
                self._record("action_release_error", accepted=sent, expected=len(events), errorCode=error)
        return not self._pressed

    def _act(self, key, received_at, original_window):
        if key == "back":
            keys = [("keyboard", 0x53, True)]
            action = "delete"
        elif key in {"volume_up", "volume_down"} and self._navigation != "none":
            previous = key == "volume_up"
            if self._navigation == "adjacent":
                keys = [("keyboard", 0x1D, False), ("keyboard", 0x49 if previous else 0x51, True)]
            else:
                keys = [("mouse", 1 if previous else 2, False)]
            action = self._navigation + ("_previous" if previous else "_next")
        else:
            return
        if self._stop.is_set() or time.monotonic() - received_at > _MAX_AGE:
            self._record("action_skipped", reason="expired_or_stopped", action=action)
            return
        foreground = self._foreground()
        if foreground is None or foreground[0] != original_window:
            self._record("action_skipped", reason="foreground_changed", action=action)
            return
        if not foreground[3] and not (key == "back" and foreground[2] == "notepad.exe"):
            self._record("action_skipped", reason="foreground_not_allowed", action=action,
                         process=foreground[2])
            return
        # Without an interception hook, GetAsyncKeyState cannot distinguish a
        # physical modifier from another app's injection: reject either kind.
        if any(self._user.GetAsyncKeyState(vk) & 0x8000 for vk in _MODIFIERS):
            self._record("action_skipped", reason="modifier_down", action=action)
            return
        events = [(item, True) for item in keys] + [(item, False) for item in reversed(keys)]
        if (self._stop.is_set() or time.monotonic() - received_at > _MAX_AGE
                or self._foreground() != foreground):
            self._record("action_skipped", reason="foreground_changed_or_expired", action=action)
            return
        sent, error = self._send(events)
        if sent != len(events):
            released = self._release_pending()
            self._record("action_send_failed", action=action, accepted=sent,
                         expected=len(events), errorCode=error, cleanupOk=released)
            self._stop.set()
            return
        self._record("action_sent", action=action, process=foreground[2], stream=self._stream)

    def _run(self):
        try:
            while not self._stop.is_set():
                if self._overflow.is_set():
                    self._discard_queue()
                    self._reset()
                    self._overflow.clear()
                    self._record("action_calibration_reset", reason="queue_overflow")
                try:
                    received_at, window, stream, down, up, active = self._queue.get(timeout=0.05)
                except queue.Empty:
                    continue
                if self._stop.is_set():
                    break
                if time.monotonic() - received_at > _MAX_AGE:
                    self._reset()
                    self._record("action_calibration_reset", reason="stale_event")
                    continue
                if self._stream is None:
                    self._calibrate(stream, down, up, active)
                    continue  # The final calibration release never emits input.
                if stream != self._stream:
                    continue
                previous = self._active
                self._active = set(active)
                if len(down) == 1 and active == down and not (down & previous):
                    self._act(next(iter(down)), received_at, window)
        except Exception as error:
            self._record("action_error", category=type(error).__name__)
            self._stop.set()
        finally:
            self._discard_queue()
            self._release_pending()

    def close(self):
        """Discard queued actions and wait briefly for a running batch to finish."""
        self._stop.set()
        self._discard_queue()
        if threading.current_thread() is self._thread:
            raise RuntimeError("Close the action test outside its worker callback.")
        self._thread.join(timeout=2.0)
        clean = not self._thread.is_alive() and not self._pressed
        self._record("action_closed", cleanupOk=clean)
        if not clean:
            raise RuntimeError("Action test cleanup did not complete; review diagnostic log.")
