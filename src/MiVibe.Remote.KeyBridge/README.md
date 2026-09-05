# MiVibe Remote KeyBridge

Windowless Windows x64 observation helper for the paired Xiaomi RC003. The tray
starts it elevated and owns a `CurrentUserOnly` duplex named-pipe server. The
helper observes the verified synchronous HID report format; it never sends input,
suppresses native keys, modifies report memory, opens a network listener, or writes
device reports to disk.

## Entry point and packaging

`main.py --pipe MiVibeRemote.Keys.<32 hexadecimal characters> --parent-pid <tray PID>`

The only external Python dependency is ordinary `import frida`. Package with
PyInstaller `--onedir --windowed`, include `hid_tap.js` beside the bundled entry
point (`--add-data "src/MiVibe.Remote.KeyBridge/hid_tap.js;."`). No Gadget or
eternal script is used. The runtime imports from its bundle, not probe dependency
folders. Keep the complete one-directory output together.

The helper verifies the pipe server PID equals its parent PID; the tray verifies
the connected client PID equals the process it launched. The client requests
anonymous security impersonation level. A parent process handle prevents PID reuse
from extending the helper's lifetime. The fixed single-instance mutex is
`Local\MiVibe.Remote.KeyBridge-2717-32B8`; an existing instance reports
`status/error/already_running` and exits before attaching.

## Pipe contract

UTF-8 JSON, one object per LF-terminated line, maximum 4096 bytes including LF.
Client output queues hold at most 32 lines, incoming commands 8, and observer
events 64. Overflow or a two-second blocked write fails closed.

* `{"type":"status","phase":"connecting","detail":"..."}`. Phases are
  `connecting`, `calibrating`, `active`, `reconnecting`, `error`, `stopped`.
* Calibration status adds `"step":0`, `1`, or `2`, meaning the next key to press
  and fully release is **back**, **volume_up**, or **volume_down** respectively.
* `{"type":"key","key":"back","sequence":1}`. Keys are only `back`,
  `volume_up`, and `volume_down`. Sequence increases throughout this helper process,
  including after reconnection and recalibration; it starts at one.
* `{"type":"heartbeat"}` every two seconds, including attach/retry periods.

Server commands are exactly `{"command":"stop"}`, `{"command":"recalibrate"}`,
and `{"command":"ping"}`. Extra fields and all other commands are rejected.
Ping requests an immediate heartbeat. Status detail contains short fixed messages
or error categories, never device addresses, host PIDs, paths, or raw report data.

## Calibration and lifetime

Device discovery requires exactly one HID service (`1812`) matching vendor
`2717`, product `32b8`, revision `00a4`, the `mshidumdf` service, and a HostPid
whose executable is the Windows system `WUDFHost.exe`. A shared driver host is
**not** treated as device identity. Each attach requires all three calibration
keys on the same report stream, in order, with a complete release after each.
Calibration never emits key actions. Only new singleton key presses from the
calibrated stream produce key messages; held repeats are ignored.

Host identity is rechecked and the JavaScript lease renewed every three seconds.
The script detaches both observation hooks if the lease expires after ten seconds.
It accepts only IOCTL `0x80018483`, success, capacity nine, the verified header,
and Information `0` or `9`. Asynchronous pending reports are ignored. Unknown
usages are counted locally; their values are never sent. Closing a known native
file handle revokes its stream identity before numeric handle reuse is possible.
Any invalid report on the calibrated stream requires recalibration.

Host changes and observer errors reset calibration and retry after 2, 5, 10, then
30 seconds. A pipe disconnect or parent exit stops the helper instead of
reconnecting the pipe. Stop cancels an in-progress Frida operation. Script unload
and session detach each use a fresh Cancellable with a five-second limit; if both
fail, no new attach is attempted and the host lease remains the final safeguard.
Recalibration intentionally requires user input after every host restart.

## Tests without hardware access

From the repository root:

```powershell
python -B -m unittest discover -s src/MiVibe.Remote.KeyBridge/tests -p "test_*.py"
node src/MiVibe.Remote.KeyBridge/tests/test_hid_tap.cjs
node --check src/MiVibe.Remote.KeyBridge/hid_tap.js
```

Python lifecycle tests use fake Frida objects; JavaScript tests use fake native
pointers in a VM. These tests do not start the helper, attach to any process, or
send keyboard/mouse input. Real driver behavior and packaged elevation still need
an explicit integration run through the tray.
