"""Stub for sport_ai — owned by Guillian.

Should expose a class with the same lifecycle as PodcastSelector:
    start(sources) / select(sources) -> int / stop()
"""
from __future__ import annotations


class SportSelector:
    def __init__(self, cfg=None):
        self.cfg = cfg
        self._current = -1

    def start(self, sources) -> None:
        self._current = 0 if sources else -1

    def select(self, sources) -> int:
        return self._current

    def stop(self) -> None:
        self._current = -1
