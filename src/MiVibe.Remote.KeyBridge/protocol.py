"""Small, bounded protocol for the current user's MiVibe named pipe."""
from __future__ import annotations

import json
import re

MAX_LINE_BYTES = 4096
PIPE_PATTERN = re.compile(r"MiVibeRemote\.Keys\.[0-9a-fA-F]{32}\Z")
KEYS = frozenset({"back", "volume_up", "volume_down"})
PHASES = frozenset({"connecting", "calibrating", "active", "reconnecting", "error", "stopped"})
COMMANDS = frozenset({"stop", "recalibrate", "ping"})


class ProtocolError(ValueError):
    pass


def pipe_path(name: str) -> str:
    if not isinstance(name, str) or PIPE_PATTERN.fullmatch(name) is None:
        raise ProtocolError("Invalid pipe name")
    return "\\\\.\\pipe\\" + name


def command(line: bytes) -> str:
    if not line or len(line) > MAX_LINE_BYTES:
        raise ProtocolError("Invalid command length")
    try:
        value = json.loads(line.decode("utf-8"))
    except (ValueError, UnicodeError) as error:
        raise ProtocolError("Invalid command JSON") from error
    if not isinstance(value, dict) or set(value) != {"command"}:
        raise ProtocolError("Invalid command fields")
    result = value["command"]
    if not isinstance(result, str) or result not in COMMANDS:
        raise ProtocolError("Unsupported command")
    return result


def encode(message: dict) -> bytes:
    if not isinstance(message, dict):
        raise ProtocolError("Invalid response")
    kind = message.get("type")
    if kind == "heartbeat":
        valid = set(message) == {"type"}
    elif kind == "key":
        valid = (set(message) == {"type", "key", "sequence"}
                 and isinstance(message.get("key"), str)
                 and message.get("key") in KEYS
                 and type(message.get("sequence")) is int and message["sequence"] > 0)
    elif kind == "status":
        valid = (set(message) <= {"type", "phase", "detail", "step"}
                 and set(message) >= {"type", "phase", "detail"}
                 and isinstance(message.get("phase"), str)
                 and message.get("phase") in PHASES
                 and isinstance(message.get("detail"), str)
                 and len(message["detail"]) <= 256
                 and ("step" not in message or type(message["step"]) is int
                      and message["step"] in {0, 1, 2}))
    else:
        valid = False
    if not valid:
        raise ProtocolError("Invalid response fields")
    line = json.dumps(message, ensure_ascii=False, separators=(",", ":")).encode("utf-8") + b"\n"
    if len(line) > MAX_LINE_BYTES:
        raise ProtocolError("Response too long")
    return line


class CommandDecoder:
    def __init__(self):
        self._buffer = bytearray()

    def feed(self, data: bytes) -> list[str]:
        result = []
        for part in data.splitlines(keepends=True):
            self._buffer.extend(part)
            if len(self._buffer) > MAX_LINE_BYTES:
                raise ProtocolError("Command too long")
            if self._buffer.endswith(b"\n"):
                result.append(command(bytes(self._buffer).rstrip(b"\r\n")))
                self._buffer.clear()
        return result
