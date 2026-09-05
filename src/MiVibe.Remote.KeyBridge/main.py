"""MiVibe RC003 observation helper. Intended for a windowless x64 executable."""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import queue
import time

from bridge import KeyBridge
from protocol import pipe_path


class _Arguments(argparse.ArgumentParser):
    def error(self, message):
        raise ValueError("invalid_arguments")


def parse_arguments(argv=None):
    parser = _Arguments(add_help=False, allow_abbrev=False)
    parser.add_argument("--pipe", required=True)
    parser.add_argument("--parent-pid", required=True, type=int)
    args = parser.parse_args(argv)
    pipe_path(args.pipe)
    if not 0 < args.parent_pid <= 0xFFFFFFFF:
        raise ValueError("invalid_parent")
    return args


def serve(parent, pipe, bridge, commands, heartbeat_interval=2):
    """Keep transport/parent supervision responsive during attach and retries."""
    next_heartbeat = 0.0
    bridge.start()
    try:
        while parent.alive() and not pipe.closed.is_set() and not bridge.finished.is_set():
            now = time.monotonic()
            if now >= next_heartbeat:
                pipe.send({"type": "heartbeat"})
                next_heartbeat = now + heartbeat_interval
            try:
                value = commands.get(timeout=0.1)
            except queue.Empty:
                continue
            if value == "stop":
                break
            if value == "recalibrate":
                bridge.recalibrate()
            elif value == "ping":
                pipe.send({"type": "heartbeat"})
    finally:
        complete = bridge.close()
        if not pipe.closed.is_set():
            if not complete:
                pipe.send({"type": "status", "phase": "error", "detail": "observer_cleanup_timeout"})
            pipe.send({"type": "status", "phase": "stopped", "detail": "Remote keys stopped."})
            pipe.flush()


def main(argv=None):
    try:
        args = parse_arguments(argv)
    except (ValueError, TypeError):
        return 2
    if os.name != "nt":
        return 2
    # Keep platform/dependency initialization after argument validation. An
    # unavailable dependency is reported over the pipe, never in a disk log.
    from pipe_client import PipeClient
    from windows_host import (HostUnavailable, InstanceMutex, ParentProcess,
                              find_target, prepare_privilege, verify_target)
    parent = pipe = instance = None
    try:
        parent = ParentProcess(args.parent_pid)
        commands = queue.Queue(maxsize=8)
        pipe = PipeClient(args.pipe, parent.alive, commands, args.parent_pid)
        instance = InstanceMutex()
        prepare_privilege()
        try:
            import frida
        except ImportError:
            raise HostUnavailable("observer_dependency_unavailable") from None
        source = Path(__file__).with_name("hid_tap.js").read_text(encoding="utf-8")
        observer = KeyBridge(frida, find_target, verify_target, source, pipe.send)
        serve(parent, pipe, observer, commands)
        return 0 if observer.exit_reason == "stopped" else 1
    except HostUnavailable as error:
        if pipe is not None and not pipe.closed.is_set():
            try:
                pipe.send({"type": "status", "phase": "error", "detail": str(error)})
                pipe.flush()
            except Exception:
                pass
        return 1
    except Exception:
        if pipe is not None and not pipe.closed.is_set():
            try:
                pipe.send({"type": "status", "phase": "error", "detail": "bridge_unavailable"})
                pipe.flush()
            except Exception:
                pass
        return 1
    finally:
        if pipe is not None:
            pipe.close()
        if instance is not None:
            instance.close()
        if parent is not None:
            parent.close()


if __name__ == "__main__":
    raise SystemExit(main())
