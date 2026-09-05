"""Read the exact RC003 host identity and hold the parent process handle."""
from __future__ import annotations

import ctypes
from ctypes import wintypes
import os
import winreg


class HostUnavailable(RuntimeError):
    pass


class InstanceMutex:
    NAME = r"Local\MiVibe.Remote.KeyBridge-2717-32B8"

    def __init__(self):
        self._dll = _kernel()
        self._dll.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
        self._dll.CreateMutexW.restype = wintypes.HANDLE
        self._dll.ReleaseMutex.argtypes = [wintypes.HANDLE]
        self._dll.ReleaseMutex.restype = wintypes.BOOL
        self._handle = self._dll.CreateMutexW(None, True, self.NAME)
        if not self._handle:
            raise HostUnavailable("instance_lock_unavailable")
        if ctypes.get_last_error() == 183:
            self._dll.CloseHandle(self._handle)
            self._handle = None
            raise HostUnavailable("already_running")

    def close(self):
        if self._handle:
            self._dll.ReleaseMutex(self._handle)
            self._dll.CloseHandle(self._handle)
            self._handle = None


def _kernel():
    dll = ctypes.WinDLL("kernel32", use_last_error=True)
    dll.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    dll.OpenProcess.restype = wintypes.HANDLE
    dll.CloseHandle.argtypes = [wintypes.HANDLE]
    dll.CloseHandle.restype = wintypes.BOOL
    dll.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD,
        wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
    dll.QueryFullProcessImageNameW.restype = wintypes.BOOL
    dll.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    dll.WaitForSingleObject.restype = wintypes.DWORD
    return dll


class ParentProcess:
    def __init__(self, pid: int):
        if type(pid) is not int or not 0 < pid <= 0xFFFFFFFF or pid == os.getpid():
            raise HostUnavailable("invalid_parent")
        self._dll = _kernel()
        self._handle = self._dll.OpenProcess(0x00100000, False, pid)
        if not self._handle:
            raise HostUnavailable("parent_unavailable")

    def alive(self):
        return bool(self._handle) and self._dll.WaitForSingleObject(self._handle, 0) == 0x102

    def close(self):
        if self._handle:
            self._dll.CloseHandle(self._handle)
            self._handle = None


def prepare_privilege():
    if os.name != "nt" or ctypes.sizeof(ctypes.c_void_p) != 8:
        raise HostUnavailable("windows_x64_required")
    shell = ctypes.WinDLL("shell32", use_last_error=True)
    shell.IsUserAnAdmin.argtypes = []
    shell.IsUserAnAdmin.restype = wintypes.BOOL
    if not shell.IsUserAnAdmin():
        raise HostUnavailable("administrator_required")

    class LUID(ctypes.Structure):
        _fields_ = [("LowPart", wintypes.DWORD), ("HighPart", wintypes.LONG)]

    class PRIVILEGES(ctypes.Structure):
        _fields_ = [("PrivilegeCount", wintypes.DWORD), ("Luid", LUID),
                    ("Attributes", wintypes.DWORD)]

    kernel = _kernel()
    kernel.GetCurrentProcess.argtypes = []
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    advapi = ctypes.WinDLL("advapi32", use_last_error=True)
    advapi.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
    advapi.OpenProcessToken.restype = wintypes.BOOL
    advapi.LookupPrivilegeValueW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR, ctypes.POINTER(LUID)]
    advapi.LookupPrivilegeValueW.restype = wintypes.BOOL
    advapi.AdjustTokenPrivileges.argtypes = [wintypes.HANDLE, wintypes.BOOL,
        ctypes.POINTER(PRIVILEGES), wintypes.DWORD, ctypes.c_void_p, ctypes.c_void_p]
    advapi.AdjustTokenPrivileges.restype = wintypes.BOOL
    token = wintypes.HANDLE()
    if not advapi.OpenProcessToken(kernel.GetCurrentProcess(), 0x28, ctypes.byref(token)):
        raise HostUnavailable("debug_privilege_unavailable")
    try:
        value = PRIVILEGES(PrivilegeCount=1, Attributes=2)
        if not advapi.LookupPrivilegeValueW(None, "SeDebugPrivilege", ctypes.byref(value.Luid)):
            raise HostUnavailable("debug_privilege_unavailable")
        ctypes.set_last_error(0)
        if (not advapi.AdjustTokenPrivileges(token, False, ctypes.byref(value), 0, None, None)
                or ctypes.get_last_error() != 0):
            raise HostUnavailable("debug_privilege_unavailable")
    finally:
        kernel.CloseHandle(token)


def find_target() -> int:
    targets = []
    base = r"SYSTEM\CurrentControlSet\Enum\BTHLEDevice"
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, base) as root:
            for index in range(winreg.QueryInfoKey(root)[0]):
                family = winreg.EnumKey(root, index)
                folded = family.casefold()
                if not folded.startswith("{00001812-0000-1000-8000-00805f9b34fb}"):
                    continue
                if "dev_vid&012717_pid&32b8_rev&00a4" not in folded:
                    continue
                with winreg.OpenKey(root, family) as group:
                    for number in range(winreg.QueryInfoKey(group)[0]):
                        with winreg.OpenKey(group, winreg.EnumKey(group, number)) as device:
                            if str(winreg.QueryValueEx(device, "Service")[0]).casefold() != "mshidumdf":
                                raise HostUnavailable("unexpected_hid_service")
                            try:
                                with winreg.OpenKey(device, r"Device Parameters\WUDFDiagnosticInfo") as diag:
                                    pid = int(winreg.QueryValueEx(diag, "HostPid")[0])
                                if pid > 0:
                                    targets.append(pid)
                            except FileNotFoundError:
                                continue
    except OSError as error:
        raise HostUnavailable("remote_host_unavailable") from error
    if len(targets) != 1:
        raise HostUnavailable("remote_host_ambiguous" if targets else "remote_host_unavailable")
    return targets[0]


def verify_target(pid: int):
    kernel = _kernel()
    handle = kernel.OpenProcess(0x1000, False, pid)
    if not handle:
        raise HostUnavailable("remote_host_unavailable")
    try:
        length = wintypes.DWORD(32768)
        image = ctypes.create_unicode_buffer(length.value)
        if not kernel.QueryFullProcessImageNameW(handle, 0, image, ctypes.byref(length)):
            raise HostUnavailable("remote_host_unavailable")
        expected = os.path.join(os.environ["SystemRoot"], "System32", "WUDFHost.exe")
        if os.path.normcase(image.value) != os.path.normcase(expected):
            raise HostUnavailable("unexpected_host_image")
    finally:
        kernel.CloseHandle(handle)
