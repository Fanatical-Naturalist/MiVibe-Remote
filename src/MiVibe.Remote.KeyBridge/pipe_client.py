"""Overlapped, bounded named-pipe transport. Never opens a network listener."""
from __future__ import annotations

import ctypes
from ctypes import wintypes
import queue
import threading
import time

from protocol import CommandDecoder, encode, pipe_path


class PipeDisconnected(RuntimeError):
    pass


class _OVERLAPPED(ctypes.Structure):
    _fields_ = [("Internal", ctypes.c_size_t), ("InternalHigh", ctypes.c_size_t),
                ("Offset", wintypes.DWORD), ("OffsetHigh", wintypes.DWORD),
                ("hEvent", wintypes.HANDLE)]


class PipeClient:
    def __init__(self, name, parent_alive, commands, parent_pid):
        path = pipe_path(name)
        self.closed = threading.Event()
        self._commands = commands
        self._outgoing = queue.Queue(maxsize=32)
        self._idle = threading.Event()
        self._idle.set()
        self._count_lock = threading.Lock()
        self._pending = 0
        self._kernel = dll = ctypes.WinDLL("kernel32", use_last_error=True)
        dll.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
            ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        dll.CreateFileW.restype = wintypes.HANDLE
        dll.ReadFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD,
            ctypes.POINTER(wintypes.DWORD), ctypes.POINTER(_OVERLAPPED)]
        dll.ReadFile.restype = wintypes.BOOL
        dll.WriteFile.argtypes = dll.ReadFile.argtypes
        dll.WriteFile.restype = wintypes.BOOL
        dll.CreateEventW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.BOOL, wintypes.LPCWSTR]
        dll.CreateEventW.restype = wintypes.HANDLE
        dll.CloseHandle.argtypes = [wintypes.HANDLE]
        dll.CloseHandle.restype = wintypes.BOOL
        dll.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        dll.WaitForSingleObject.restype = wintypes.DWORD
        dll.CancelIoEx.argtypes = [wintypes.HANDLE, ctypes.POINTER(_OVERLAPPED)]
        dll.CancelIoEx.restype = wintypes.BOOL
        dll.GetOverlappedResult.argtypes = [wintypes.HANDLE, ctypes.POINTER(_OVERLAPPED),
            ctypes.POINTER(wintypes.DWORD), wintypes.BOOL]
        dll.GetOverlappedResult.restype = wintypes.BOOL
        dll.GetNamedPipeServerProcessId.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
        dll.GetNamedPipeServerProcessId.restype = wintypes.BOOL
        invalid = ctypes.c_void_p(-1).value
        deadline = time.monotonic() + 10
        self._handle = None
        while time.monotonic() < deadline and parent_alive():
            handle = dll.CreateFileW(path, 0xC0000000, 0, None, 3,
                                     0x40000000 | 0x00100000, None)
            # FILE_FLAG_OVERLAPPED + SECURITY_SQOS_PRESENT (anonymous level):
            # the elevated client never lets the pipe server impersonate it.
            if handle and handle != invalid:
                self._handle = handle
                break
            if ctypes.get_last_error() not in {2, 231}:
                break
            time.sleep(0.1)
        if not self._handle:
            raise PipeDisconnected("pipe_unavailable")
        server_pid = wintypes.DWORD()
        if (not dll.GetNamedPipeServerProcessId(self._handle, ctypes.byref(server_pid))
                or server_pid.value != parent_pid):
            dll.CloseHandle(self._handle)
            self._handle = None
            raise PipeDisconnected("unexpected_pipe_server")
        self._reader = threading.Thread(target=self._read_loop, name="keybridge-pipe-read", daemon=True)
        self._writer = threading.Thread(target=self._write_loop, name="keybridge-pipe-write", daemon=True)
        self._reader.start()
        self._writer.start()

    def _io(self, data=None):
        handle = self._handle
        reading = data is None
        buffer = ctypes.create_string_buffer(1024 if reading else data, 1024 if reading else len(data))
        count = wintypes.DWORD()
        event = self._kernel.CreateEventW(None, True, False, None)
        if not event:
            raise PipeDisconnected("pipe_event_failed")
        overlapped = _OVERLAPPED(hEvent=event)
        pending = False
        try:
            call = self._kernel.ReadFile if reading else self._kernel.WriteFile
            ok = call(handle, buffer, len(buffer), ctypes.byref(count), ctypes.byref(overlapped))
            if not ok:
                if ctypes.get_last_error() != 997:
                    raise PipeDisconnected("pipe_io_failed")
                pending = True
                deadline = None if reading else time.monotonic() + 2
                while self._kernel.WaitForSingleObject(event, 50) == 0x102:
                    if self.closed.is_set() or deadline is not None and time.monotonic() >= deadline:
                        self._kernel.CancelIoEx(handle, ctypes.byref(overlapped))
                        # Keep the buffer and OVERLAPPED alive until cancellation
                        # completes; never free memory belonging to pending I/O.
                        self._kernel.GetOverlappedResult(handle, ctypes.byref(overlapped),
                                                         ctypes.byref(count), True)
                        pending = False
                        raise PipeDisconnected("pipe_closed_or_timeout")
                if not self._kernel.GetOverlappedResult(handle, ctypes.byref(overlapped),
                                                       ctypes.byref(count), False):
                    raise PipeDisconnected("pipe_io_failed")
                pending = False
            if count.value == 0 or not reading and count.value != len(data):
                raise PipeDisconnected("pipe_closed_or_partial_write")
            return buffer.raw[:count.value] if reading else None
        finally:
            if pending:
                self._kernel.CancelIoEx(handle, ctypes.byref(overlapped))
                self._kernel.GetOverlappedResult(handle, ctypes.byref(overlapped),
                                                 ctypes.byref(count), True)
            self._kernel.CloseHandle(event)

    def _read_loop(self):
        decoder = CommandDecoder()
        try:
            while not self.closed.is_set():
                for value in decoder.feed(self._io()):
                    self._commands.put_nowait(value)
        except Exception:
            self.closed.set()

    def _write_loop(self):
        try:
            while not self.closed.is_set():
                try:
                    line = self._outgoing.get(timeout=0.1)
                except queue.Empty:
                    continue
                try:
                    self._io(line)
                finally:
                    with self._count_lock:
                        self._pending -= 1
                        if self._pending == 0:
                            self._idle.set()
        except Exception:
            self.closed.set()

    def send(self, message):
        if self.closed.is_set():
            raise PipeDisconnected("pipe_closed")
        line = encode(message)
        with self._count_lock:
            self._pending += 1
            self._idle.clear()
            try:
                self._outgoing.put_nowait(line)
            except queue.Full as error:
                self._pending -= 1
                self.closed.set()
                raise PipeDisconnected("pipe_backpressure") from error

    def flush(self, timeout=0.5):
        return self._idle.wait(timeout)

    def close(self):
        self.closed.set()
        if self._handle:
            self._kernel.CancelIoEx(self._handle, None)
            self._reader.join(timeout=2)
            self._writer.join(timeout=2)
            self._kernel.CloseHandle(self._handle)
            self._handle = None
