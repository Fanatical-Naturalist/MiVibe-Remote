"""Finite, opt-in RC003 driver-host diagnostic. Does not remap or suppress keys."""
from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
from datetime import datetime
import json
import os
from pathlib import Path
import sys
import threading
import time
import winreg

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / '.local' / 'hid-probe-deps'))


def find_target() -> int:
    targets = []
    base = r'SYSTEM\CurrentControlSet\Enum\BTHLEDevice'
    with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, base) as root:
        for index in range(winreg.QueryInfoKey(root)[0]):
            family = winreg.EnumKey(root, index)
            folded = family.lower()
            if '00001812-' not in folded or 'vid&012717_pid&32b8_rev&00a4' not in folded:
                continue
            with winreg.OpenKey(root, family) as group:
                for number in range(winreg.QueryInfoKey(group)[0]):
                    instance = winreg.EnumKey(group, number)
                    with winreg.OpenKey(group, instance) as device:
                        service = winreg.QueryValueEx(device, 'Service')[0]
                        if service.lower() != 'mshidumdf':
                            raise RuntimeError('Unexpected HID service; refusing attachment.')
                        try:
                            with winreg.OpenKey(device, r'Device Parameters\WUDFDiagnosticInfo') as diag:
                                host_pid = winreg.QueryValueEx(diag, 'HostPid')[0]
                                if host_pid:
                                    targets.append(int(host_pid))
                        except FileNotFoundError:
                            pass
    if len(targets) != 1:
        raise RuntimeError(f'Expected one RC003 HID instance; found {len(targets)}.')
    return targets[0]


def verify_image(host_pid: int) -> None:
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD,
                                               wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
    handle = kernel.OpenProcess(0x1000, False, host_pid)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        length = wintypes.DWORD(32768)
        image = ctypes.create_unicode_buffer(length.value)
        if not kernel.QueryFullProcessImageNameW(handle, 0, image, ctypes.byref(length)):
            raise ctypes.WinError(ctypes.get_last_error())
        expected = Path(os.environ['SystemRoot']) / 'System32' / 'WUDFHost.exe'
        if os.path.normcase(image.value) != os.path.normcase(str(expected)):
            raise RuntimeError('HostPid does not point to the system WUDFHost image.')
    finally:
        kernel.CloseHandle(handle)


def enable_debug_privilege() -> None:
    class LUID(ctypes.Structure):
        _fields_ = [('LowPart', wintypes.DWORD), ('HighPart', wintypes.LONG)]
    class PRIVILEGES(ctypes.Structure):
        _fields_ = [('PrivilegeCount', wintypes.DWORD), ('Luid', LUID),
                    ('Attributes', wintypes.DWORD)]
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    advapi = ctypes.WinDLL('advapi32', use_last_error=True)
    kernel.GetCurrentProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    advapi.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD,
                                      ctypes.POINTER(wintypes.HANDLE)]
    advapi.LookupPrivilegeValueW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR,
                                          ctypes.POINTER(LUID)]
    advapi.AdjustTokenPrivileges.argtypes = [wintypes.HANDLE, wintypes.BOOL,
        ctypes.POINTER(PRIVILEGES), wintypes.DWORD, ctypes.c_void_p, ctypes.c_void_p]
    token = wintypes.HANDLE()
    if not advapi.OpenProcessToken(kernel.GetCurrentProcess(), 0x28, ctypes.byref(token)):
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        value = PRIVILEGES(PrivilegeCount=1, Attributes=2)
        if not advapi.LookupPrivilegeValueW(None, 'SeDebugPrivilege', ctypes.byref(value.Luid)):
            raise ctypes.WinError(ctypes.get_last_error())
        ctypes.set_last_error(0)
        if not advapi.AdjustTokenPrivileges(token, False, ctypes.byref(value), 0, None, None):
            raise ctypes.WinError(ctypes.get_last_error())
        if ctypes.get_last_error():
            raise ctypes.WinError(ctypes.get_last_error())
    finally:
        kernel.CloseHandle(token)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--seconds', type=int, default=120)
    parser.add_argument('--preflight', action='store_true')
    parser.add_argument('--actions', action='store_true',
                        help='Explicitly enable a calibrated, foreground-limited action test.')
    parser.add_argument('--navigation', choices=['none', 'adjacent', 'history'], default='none')
    args = parser.parse_args()
    if not 5 <= args.seconds <= 180:
        parser.error('--seconds must be between 5 and 180')
    directory = ROOT / 'logs' / 'hid-diagnostics'
    directory.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now().strftime('%Y%m%d-%H%M%S-%f')
    log_path = directory / f'capture-{timestamp}.jsonl'
    finished = threading.Event()
    ready = threading.Event()
    failed = threading.Event()
    shutting_down = threading.Event()
    lock = threading.Lock()
    with log_path.open('x', encoding='utf-8', buffering=1) as log:
        def record(kind: str, **fields) -> None:
            line = json.dumps({'time': datetime.now().astimezone().isoformat(),
                               'kind': kind, **fields}, ensure_ascii=True)
            with lock:
                log.write(line + '\n')
                print(line, flush=True)

        def bounded(frida, operation, seconds=12):
            token = frida.Cancellable()
            timer = threading.Timer(seconds, token.cancel)
            timer.daemon = True
            timer.start()
            try:
                with token:
                    return operation(token)
            finally:
                timer.cancel()

        script = None
        session = None
        actions = None
        clean = True
        result = 0
        record('starting', log=str(log_path), observationOnly=not args.actions)
        try:
            if ctypes.sizeof(ctypes.c_void_p) != 8:
                raise RuntimeError('Run using x64 Python.')
            host_pid = find_target()
            elevated = bool(ctypes.windll.shell32.IsUserAnAdmin())
            record('target', family='2717/32B8/00A4', hostPid=host_pid, elevated=elevated)
            if not elevated:
                raise RuntimeError('Administrator permission is required for this diagnostic.')
            enable_debug_privilege()
            verify_image(host_pid)
            import frida
            record('preflight_ok', fridaVersion=frida.__version__, hostImage='WUDFHost.exe')
            if args.preflight:
                return 0
            if args.actions:
                from input_actions import InputActionTest
                actions = InputActionTest(record, args.navigation)
            if find_target() != host_pid:
                raise RuntimeError('Target host changed during preflight; start a fresh capture.')
            session = bounded(frida, lambda token: frida.get_local_device().attach(
                host_pid, persist_timeout=0, cancellable=token))
            def detached(reason, crash=None):
                record('detached', reason=str(reason), crashed=crash is not None)
                if actions is not None:
                    actions.observe({'kind': 'detached'})
                if not shutting_down.is_set():
                    failed.set()
                finished.set()
            session.on('detached', detached)
            source = Path(__file__).with_name('capture.js').read_text(encoding='utf-8')
            source = source.replace('__DURATION_MS__', str(args.seconds * 1000))
            script = bounded(frida, lambda token: session.create_script(source, cancellable=token))
            def message_received(message, data):
                if message.get('type') == 'send':
                    payload = dict(message['payload'])
                    kind = payload.pop('kind')
                    if kind == 'ready':
                        payload['observationOnly'] = not args.actions
                    record(kind, **payload)
                    if kind == 'keys' and actions is not None:
                        actions.observe(payload)
                    if kind == 'ready':
                        ready.set()
                    elif kind == 'stopped':
                        if actions is not None:
                            actions.observe({'kind': 'stopped'})
                        finished.set()
                elif message.get('type') == 'error':
                    record('script_error', description=message.get('description', 'Unknown'))
                    if actions is not None:
                        actions.observe({'kind': 'script_error'})
                    failed.set()
                    finished.set()
            script.on('message', message_received)
            bounded(frida, lambda token: script.load(cancellable=token))
            if not ready.wait(3):
                raise RuntimeError('Hook did not report ready.')
            deadline = time.monotonic() + args.seconds + 3
            next_check = time.monotonic() + 5
            while not finished.wait(0.25) and time.monotonic() < deadline:
                if time.monotonic() >= next_check:
                    if find_target() != host_pid:
                        record('host_changed')
                        failed.set()
                        break
                    next_check = time.monotonic() + 5
            if failed.is_set():
                result = 1
        except KeyboardInterrupt:
            record('interrupted')
        except Exception as error:
            record('error', category=type(error).__name__, message=str(error))
            result = 1
        finally:
            shutting_down.set()
            if actions is not None:
                try:
                    actions.close()
                except Exception as error:
                    clean = False
                    record('cleanup_error', step='actions', category=type(error).__name__)
            if script is not None:
                try:
                    bounded(frida, lambda token: script.unload(cancellable=token), 5)
                    record('script_unloaded')
                except Exception as error:
                    clean = False
                    record('cleanup_error', step='unload', category=type(error).__name__)
            if session is not None and not session.is_detached:
                try:
                    bounded(frida, lambda token: session.detach(cancellable=token), 5)
                except Exception as error:
                    clean = False
                    record('cleanup_error', step='detach', category=type(error).__name__)
            record('finished', cleanupOk=clean, exitCode=result)
        return result if clean else 2


if __name__ == '__main__':
    raise SystemExit(main())
