"""PodcastEngine: standalone runner for local dev / debugging.

When the gRPC server in CameraSelectorDebug is used, the PodcastSelector
is wrapped directly there (no engine needed). This file exists so
`python main.py --module podcast --sources rtmp://... rtmp://...`
runs a preview loop with OpenCV.
"""
from __future__ import annotations

import json
import logging
import sys
import time
from dataclasses import dataclass

import cv2

from .model import PodcastSelector, SelectorConfig

logger = logging.getLogger("ai-hub.podcast_ai.inference")


@dataclass
class _FakeSource:
    id: str
    rtmp_url: str


class PodcastEngine:
    def __init__(self, urls: list[str], cfg: SelectorConfig | None = None,
                 yolo_model_name: str | None = None, preview: bool = True,
                 debug_overlay: bool = False, json_events: bool = False):
        # `urls` may mix RTMP URLs and local file paths; preprocessing
        # auto-detects and chooses ffmpeg flags accordingly.
        import os
        self.urls = [
            u if u.lower().startswith(("rtmp://", "rtsp://", "http://", "https://", "udp://", "tcp://", "srt://"))
            else os.path.abspath(u)
            for u in urls
        ]
        self.preview = preview
        self.debug_overlay = debug_overlay
        self.json_events = json_events
        self.selector = PodcastSelector(cfg=cfg, yolo_model_name=yolo_model_name)

    def run(self) -> None:
        sources = [_FakeSource(id=f"cam{i+1}", rtmp_url=u) for i, u in enumerate(self.urls)]
        self.selector.start(sources)
        logger.info("PodcastEngine running on %d sources", len(sources))
        if self.json_events:
            self._emit({"event": "started", "sources": list(self.urls)})
        try:
            last_log = 0.0
            last_idx = -2
            while True:
                idx = self.selector.select(sources)
                if idx != last_idx and self.json_events:
                    self._emit({
                        "event": "switch",
                        "index": int(idx),
                        "source": self.urls[idx] if 0 <= idx < len(self.urls) else None,
                        "mode": self.selector._mode,
                        "ts": time.time(),
                    })
                    last_idx = idx
                if self.preview and self.selector._rtmp_sources:
                    self._draw_preview(idx)
                    key = cv2.waitKey(30) & 0xFF
                    if key in (ord("q"), 27):
                        break
                else:
                    time.sleep(0.1)
                now = time.time()
                if now - last_log > 2.0:
                    logger.info("active=%d", idx)
                    last_log = now
        except KeyboardInterrupt:
            logger.info("Interrupted")
        finally:
            if self.json_events:
                self._emit({"event": "stopped"})
            self.selector.stop()
            if self.preview:
                cv2.destroyAllWindows()

    @staticmethod
    def _emit(payload: dict) -> None:
        # Newline-delimited JSON on stdout. Each line is self-contained
        # so a host process can split on '\n' and json.loads each.
        sys.stdout.write(json.dumps(payload, separators=(",", ":")) + "\n")
        sys.stdout.flush()

    def _draw_preview(self, idx: int) -> None:
        if not (0 <= idx < len(self.selector._rtmp_sources)):
            return
        rtmp = self.selector._rtmp_sources[idx]
        frame = rtmp.video.get_latest()
        if frame is None:
            return
        if self.debug_overlay:
            # ffmpeg buffer is read-only; copy before drawing.
            frame = frame.copy()
            scores = self.selector._analyzers[idx].get_scores()
            mode = self.selector._mode
            label = (
                f"cam{idx+1} [{mode}] | speak={scores.speaking} "
                f"db={scores.audio_db:.1f} lip={scores.lip_movement:.2f} "
                f"person={scores.person_present}"
            )
            cv2.putText(frame, label, (12, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
        cv2.imshow("Castor — Podcast AI preview", frame)
