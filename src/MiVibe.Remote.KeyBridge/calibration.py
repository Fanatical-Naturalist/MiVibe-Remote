"""Pure state machine: no shared WUDFHost stream is trusted before calibration."""
from __future__ import annotations

from protocol import KEYS

ORDER = ("back", "volume_up", "volume_down")


class Calibration:
    def __init__(self):
        self.sequence = 0
        self.reset()

    def reset(self):
        self.stream = None
        self.candidate = None
        self.step = 0
        self.waiting_release = False
        self.snapshots = {}

    def status(self):
        if self.stream is not None:
            return {"type": "status", "phase": "active", "detail": "Remote keys ready."}
        return {"type": "status", "phase": "calibrating",
                "detail": "Press and release " + ORDER[self.step] + ".", "step": self.step}

    def observe(self, report: dict) -> list[dict]:
        stream = report.get("stream")
        active_list = report.get("active")
        other = report.get("other")
        if (type(stream) is not int or not 1 <= stream <= 16
                or not isinstance(active_list, list) or len(active_list) > 3
                or any(not isinstance(key, str) or key not in KEYS for key in active_list)
                or type(other) is not bool):
            return []
        active = frozenset(active_list)
        previous, previous_other = self.snapshots.get(stream, (frozenset(), False))
        self.snapshots[stream] = (active, other)
        if active == previous and other == previous_other:
            return []

        if self.stream is not None:
            if stream != self.stream or other or len(active) != 1:
                return []
            key = next(iter(active))
            if key in previous:
                return []
            self.sequence += 1
            return [{"type": "key", "key": key, "sequence": self.sequence}]

        if self.candidate is None:
            if active == {ORDER[0]} and not other and not previous and not previous_other:
                self.candidate = stream
                self.waiting_release = True
            return []
        if stream != self.candidate:
            return []
        expected = ORDER[self.step]
        if self.waiting_release:
            if not active and not other and previous == {expected} and not previous_other:
                self.step += 1
                self.waiting_release = False
                if self.step == len(ORDER):
                    self.stream = stream
                return [self.status()]
            if active != {expected} or other:
                self.reset()
                return [self.status()]
        elif active == {expected} and not other and not previous and not previous_other:
            self.waiting_release = True
        elif active or other:
            self.reset()
            return [self.status()]
        return []

    def invalidate(self, stream: int) -> list[dict]:
        if stream in {self.stream, self.candidate}:
            self.reset()
            return [self.status()]
        self.snapshots.pop(stream, None)
        return []
