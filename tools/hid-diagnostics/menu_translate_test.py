"""Temporary Menu -> Right Shift + T test; never starts automatically.

Run on Windows x64 with Python: menu_translate_test.py --seconds 60
The existing voice bridge may remain running. This hook must be installed after
its Menu hook; a bridge reconnect can change that order. Physical keyboard Menu
keys are also intercepted during the test. SendInput success only confirms input
submission, never that Typeless opened Translate. All diagnostics stay in the
project's logs/hid-diagnostics directory; no typed text or audio is recorded.
"""

from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import queue
import signal
import sys
import threading
import time


WH_KEYBOARD_LL = 13
WM_KEYDOWN = 0x0100
WM_KEYUP = 0x0101
WM_SYSKEYDOWN = 0x0104
WM_SYSKEYUP = 0x0105
WM_QUIT = 0x0012
VK_APPS = 0x5D
VK_RSHIFT = 0xA1
VK_T = 0x54
# Left/right Shift, Control, Alt and Windows keys; never combine with held keys.
MODIFIER_VKS = (0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C)
LLKHF_INJECTED = 0x10
LLKHF_LOWER_IL_INJECTED = 0x02
INPUT_KEYBOARD = 1
KEYEVENTF_SCANCODE = 0x0008
KEYEVENTF_KEYUP = 0x0002
SCAN_RSHIFT = 0x36
SCAN_T = 0x14
INJECTION_TAG = 0x4D6956695472616E  # "MiViTran", scoped to this test helper.
ULONG_PTR = ctypes.c_size_t
LRESULT = ctypes.c_ssize_t


class KBDLLHOOKSTRUCT(ctypes.Structure):
    _fields_ = [
        ("vkCode", wintypes.DWORD),
        ("scanCode", wintypes.DWORD),
        ("flags", wintypes.DWORD),
        ("time", wintypes.DWORD),
        ("dwExtraInfo", ULONG_PTR),
    ]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [
        ("wVk", wintypes.WORD),
        ("wScan", wintypes.WORD),
        ("dwFlags", wintypes.DWORD),
        ("time", wintypes.DWORD),
        ("dwExtraInfo", ULONG_PTR),
    ]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [
        ("dx", wintypes.LONG),
        ("dy", wintypes.LONG),
        ("mouseData", wintypes.DWORD),
        ("dwFlags", wintypes.DWORD),
        ("time", wintypes.DWORD),
        ("dwExtraInfo", ULONG_PTR),
    ]


class HARDWAREINPUT(ctypes.Structure):
    _fields_ = [
        ("uMsg", wintypes.DWORD),
        ("wParamL", wintypes.WORD),
        ("wParamH", wintypes.WORD),
    ]


class INPUT_UNION(ctypes.Union):
    _fields_ = [("ki", KEYBDINPUT), ("mi", MOUSEINPUT), ("hi", HARDWAREINPUT)]


class INPUT(ctypes.Structure):
    _anonymous_ = ("payload",)
    _fields_ = [("type", wintypes.DWORD), ("payload", INPUT_UNION)]


class MSG(ctypes.Structure):
    _fields_ = [
        ("hwnd", wintypes.HWND),
        ("message", wintypes.UINT),
        ("wParam", wintypes.WPARAM),
        ("lParam", wintypes.LPARAM),
        ("time", wintypes.DWORD),
        ("pt", wintypes.POINT),
        ("lPrivate", wintypes.DWORD),
    ]


class JsonLog:
    def __init__(self) -> None:
        directory = Path(__file__).resolve().parents[2] / "logs" / "hid-diagnostics"
        directory.mkdir(parents=True, exist_ok=True)
        stamp = datetime.now().strftime("%Y%m%d-%H%M%S-%f")
        self.path = directory / f"menu-translate-{stamp}.jsonl"
        self.file = self.path.open("x", encoding="utf-8")
        self.lock = threading.Lock()

    def write(self, event: str, **fields: object) -> None:
        record = {"time": datetime.now(timezone.utc).isoformat(), "event": event, **fields}
        with self.lock:
            self.file.write(json.dumps(record, ensure_ascii=False) + "\n")
            self.file.flush()

    def close(self) -> None:
        self.file.close()


class WinApi:
    def __init__(self) -> None:
        self.user32 = ctypes.WinDLL("user32", use_last_error=True)
        self.kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self.HOOKPROC = ctypes.WINFUNCTYPE(
            LRESULT, ctypes.c_int, wintypes.WPARAM, wintypes.LPARAM
        )
        self.user32.SetWindowsHookExW.argtypes = [
            ctypes.c_int, self.HOOKPROC, wintypes.HINSTANCE, wintypes.DWORD
        ]
        self.user32.SetWindowsHookExW.restype = wintypes.HANDLE
        self.user32.UnhookWindowsHookEx.argtypes = [wintypes.HANDLE]
        self.user32.UnhookWindowsHookEx.restype = wintypes.BOOL
        self.user32.CallNextHookEx.argtypes = [
            wintypes.HANDLE, ctypes.c_int, wintypes.WPARAM, wintypes.LPARAM
        ]
        self.user32.CallNextHookEx.restype = LRESULT
        self.user32.GetAsyncKeyState.argtypes = [ctypes.c_int]
        self.user32.GetAsyncKeyState.restype = ctypes.c_short
        self.user32.SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
        self.user32.SendInput.restype = wintypes.UINT
        self.user32.PeekMessageW.argtypes = [
            ctypes.POINTER(MSG), wintypes.HWND, wintypes.UINT, wintypes.UINT, wintypes.UINT
        ]
        self.user32.PeekMessageW.restype = wintypes.BOOL
        self.user32.GetMessageW.argtypes = [
            ctypes.POINTER(MSG), wintypes.HWND, wintypes.UINT, wintypes.UINT
        ]
        self.user32.GetMessageW.restype = ctypes.c_int
        self.user32.TranslateMessage.argtypes = [ctypes.POINTER(MSG)]
        self.user32.TranslateMessage.restype = wintypes.BOOL
        self.user32.DispatchMessageW.argtypes = [ctypes.POINTER(MSG)]
        self.user32.DispatchMessageW.restype = LRESULT
        self.user32.PostThreadMessageW.argtypes = [
            wintypes.DWORD, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM
        ]
        self.user32.PostThreadMessageW.restype = wintypes.BOOL
        self.kernel32.GetCurrentThreadId.argtypes = []
        self.kernel32.GetCurrentThreadId.restype = wintypes.DWORD
        self.kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]
        self.kernel32.GetModuleHandleW.restype = wintypes.HMODULE

    def is_down(self, key: int) -> bool:
        return bool(self.user32.GetAsyncKeyState(key) & 0x8000)

    def send(self, scans: list[int], *, key_up: bool = False) -> tuple[int, int]:
        items = (INPUT * len(scans))()
        for index, scan in enumerate(scans):
            items[index].type = INPUT_KEYBOARD
            items[index].ki = KEYBDINPUT(
                0, scan, KEYEVENTF_SCANCODE | (KEYEVENTF_KEYUP if key_up else 0),
                0, INJECTION_TAG,
            )
        ctypes.set_last_error(0)
        sent = int(self.user32.SendInput(len(items), items, ctypes.sizeof(INPUT)))
        return sent, ctypes.get_last_error() if sent != len(items) else 0


class MenuTranslateTest:
    def __init__(self, seconds: int, log: JsonLog) -> None:
        self.seconds = seconds
        self.log = log
        self.api = WinApi()
        self.stop = threading.Event()
        self.actions: queue.Queue[float] = queue.Queue(maxsize=1)
        self.hook = None
        self.thread_id = 0
        self.menu_down = False
        self.callback_error: str | None = None
        self.worker_failed = False
        self.stop_reason: str | None = None
        self.quit_post_error = 0
        self.unreleased_scans: list[int] = []
        self.stats = {"menu_down": 0, "menu_up": 0, "repeats": 0, "queued": 0, "queue_full": 0}
        self.callback = self.api.HOOKPROC(self.on_keyboard_event)

    def on_keyboard_event(self, code: int, message: int, pointer: int) -> int:
        # Keep this callback nonblocking: no file I/O, sleeps, or SendInput calls.
        if code >= 0 and message in (WM_KEYDOWN, WM_KEYUP, WM_SYSKEYDOWN, WM_SYSKEYUP):
            try:
                event = ctypes.cast(pointer, ctypes.POINTER(KBDLLHOOKSTRUCT)).contents
                if event.vkCode == VK_APPS and not (
                    event.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)
                ) and event.dwExtraInfo != INJECTION_TAG:
                    if message in (WM_KEYUP, WM_SYSKEYUP):
                        self.menu_down = False
                        self.stats["menu_up"] += 1
                    elif self.menu_down:
                        self.stats["repeats"] += 1
                    else:
                        self.menu_down = True
                        self.stats["menu_down"] += 1
                        if not self.stop.is_set():
                            try:
                                self.actions.put_nowait(time.monotonic())
                                self.stats["queued"] += 1
                            except queue.Full:
                                self.stats["queue_full"] += 1
                    return 1
            except Exception as error:
                self.callback_error = type(error).__name__
        return int(self.api.user32.CallNextHookEx(self.hook, code, message, pointer))

    def request_stop(self, reason: str) -> None:
        # Signal handlers must not acquire the log lock or perform file I/O.
        if self.stop_reason is None:
            self.stop_reason = reason
        self.stop.set()
        if self.thread_id:
            posted = bool(self.api.user32.PostThreadMessageW(self.thread_id, WM_QUIT, 0, 0))
            if not posted:
                self.quit_post_error = ctypes.get_last_error()

    def release_pending(self, pending: list[int]) -> list[int]:
        remaining = list(reversed(pending))  # Release T before its modifier.
        for attempt in range(3):
            failed = []
            for scan in remaining:
                sent, error = self.api.send([scan], key_up=True)
                if sent != 1:
                    failed.append(scan)
                    self.log.write("release_retry", scan=scan, attempt=attempt + 1,
                                   win32_error=error)
            remaining = failed
            if not remaining:
                break
        return remaining

    def perform_action(self, queued_at: float) -> None:
        if self.stop.is_set():
            return
        # Avoid adding to an existing chord or releasing a key held by the user.
        if any(self.api.is_down(key) for key in (*MODIFIER_VKS, VK_T)):
            self.log.write("action_skipped", reason="modifier_or_shortcut_key_already_down")
            return
        pending: list[int] = []
        try:
            sent, error = self.api.send([SCAN_RSHIFT, SCAN_T])
            pending = [SCAN_RSHIFT, SCAN_T][:sent]
            self.log.write("shortcut_submitted", requested_events=2, sent_events=sent,
                           win32_error=error, queue_delay_ms=round((time.monotonic() - queued_at) * 1000, 1),
                           shortcut="RightShift+T", translate_success="not_verified")
            if sent == 2:
                self.stop.wait(0.08)
        finally:
            self.unreleased_scans = self.release_pending(pending)
            self.log.write("shortcut_released", released=not self.unreleased_scans,
                           pending_scans=self.unreleased_scans,
                           translate_success="not_verified")
            if self.unreleased_scans:
                self.worker_failed = True
                self.request_stop("key_release_failed")

    def worker_main(self) -> None:
        try:
            while not self.stop.is_set():
                try:
                    queued_at = self.actions.get(timeout=0.1)
                except queue.Empty:
                    continue
                try:
                    self.perform_action(queued_at)
                finally:
                    self.actions.task_done()
        except Exception as error:
            self.worker_failed = True
            self.log.write("worker_error", error_type=type(error).__name__, error=str(error))
            self.request_stop("worker_error")
        finally:
            if self.unreleased_scans:
                self.unreleased_scans = self.release_pending(list(reversed(self.unreleased_scans)))

    def run(self) -> int:
        if ctypes.sizeof(INPUT) != 40 or ctypes.sizeof(KBDLLHOOKSTRUCT) != 24:
            raise RuntimeError("Unexpected Windows x64 input structure sizes.")
        if self.api.is_down(VK_APPS):
            self.log.write("startup_refused", reason="menu_already_down")
            print(f"NOT STARTED: release Menu first. Diagnostic file: {self.log.path}", flush=True)
            return 2
        message = MSG()
        self.thread_id = int(self.api.kernel32.GetCurrentThreadId())
        # Create this thread's message queue before the timer can post WM_QUIT.
        self.api.user32.PeekMessageW(ctypes.byref(message), None, 0, 0, 0)
        worker = threading.Thread(target=self.worker_main, name="MiVibe Translate sender")
        timer = threading.Timer(self.seconds, self.request_stop, args=("duration_elapsed",))
        old_handlers = {}
        unhooked = True
        try:
            self.hook = self.api.user32.SetWindowsHookExW(
                WH_KEYBOARD_LL, self.callback, self.api.kernel32.GetModuleHandleW(None), 0
            )
            if not self.hook:
                raise ctypes.WinError(ctypes.get_last_error())
            for number in (signal.SIGINT, getattr(signal, "SIGBREAK", signal.SIGINT)):
                if number not in old_handlers:
                    old_handlers[number] = signal.signal(
                        number, lambda signum, frame: self.request_stop("console_cancel")
                    )
            worker.start()
            timer.start()
            self.log.write("ready", seconds=self.seconds, pid=os.getpid(),
                           input_size=ctypes.sizeof(INPUT), injection_tag=f"0x{INJECTION_TAG:X}",
                           action="physical Menu -> RightShift+T", hold_ms=80,
                           translate_success="not_verified")
            print(f"READY for {self.seconds}s: press Menu to test Translate. Log: {self.log.path}", flush=True)
            while not self.stop.is_set():
                result = self.api.user32.GetMessageW(ctypes.byref(message), None, 0, 0)
                if result == -1:
                    raise ctypes.WinError(ctypes.get_last_error())
                if result == 0:
                    break
                self.api.user32.TranslateMessage(ctypes.byref(message))
                self.api.user32.DispatchMessageW(ctypes.byref(message))
        finally:
            self.stop.set()
            timer.cancel()
            # Uninstall on the same thread that owns the hook and message loop.
            if self.hook:
                unhooked = bool(self.api.user32.UnhookWindowsHookEx(self.hook))
                error = 0 if unhooked else ctypes.get_last_error()
                self.hook = None
                self.log.write("hook_removed", success=unhooked, win32_error=error)
            if worker.ident is not None:
                worker.join()
            if timer.ident is not None:
                timer.join()
            for number, old_handler in old_handlers.items():
                signal.signal(number, old_handler)
            self.log.write("finished", **self.stats, hook_removed=unhooked,
                           callback_error=self.callback_error, worker_failed=self.worker_failed,
                           pending_scans=self.unreleased_scans, stop_reason=self.stop_reason,
                           quit_post_error=self.quit_post_error, translate_success="not_verified")
            print(f"STOPPED: temporary Menu test ended. Log: {self.log.path}", flush=True)
        return 0 if unhooked and not self.worker_failed and not self.callback_error else 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seconds", type=int, default=60,
                        help="Temporary hook duration, from 30 to 90 seconds (default: 60).")
    args = parser.parse_args()
    if not 30 <= args.seconds <= 90:
        parser.error("--seconds must be between 30 and 90")
    if sys.platform != "win32" or ctypes.sizeof(ctypes.c_void_p) != 8:
        parser.error("This diagnostic requires Windows and 64-bit Python.")
    log = JsonLog()
    try:
        log.write("starting", seconds=args.seconds)
        return MenuTranslateTest(args.seconds, log).run()
    except Exception as error:
        log.write("fatal_error", error_type=type(error).__name__, error=str(error))
        print(f"FAILED: see diagnostic file {log.path}", file=sys.stderr, flush=True)
        return 1
    finally:
        log.close()


if __name__ == "__main__":
    raise SystemExit(main())
