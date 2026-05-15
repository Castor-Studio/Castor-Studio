"""PodcastSelector: gabin-inspired audio-led switching, enriched with
YOLO person presence and MediaPipe lip movement.

Decision pipeline per tick:
1. For each source, feed the latest audio chunks through Silero VAD,
   update a frame-counter hysteresis (gabin-style: +5 to flip ON, -20
   to flip OFF, capped at ±30).
2. Loudest-speaker arbitration: if multiple sources are "speaking",
   only the one with the highest dB level keeps that flag.
3. If no source has been speaking for >silence_grace_s → illustration
   mode (audio weight is dropped, switches driven by lip/person only).
4. Hysteresis on the switch itself (min_hold_time + switch_margin).
"""
from __future__ import annotations

import logging
import threading
import time
from collections import deque
from dataclasses import dataclass, field, replace

import cv2
import numpy as np

from core.model_loader import (
    LIP_LOWER_INNER,
    LIP_UPPER_INNER,
    LIP_LEFT_CORNER,
    LIP_RIGHT_CORNER,
    load_facemesh,
    load_yolo,
)
from core.preprocessing import RtmpSource
from core.vad import SpeakingState, VoiceActivityDetector, compute_db

logger = logging.getLogger("ai-hub.podcast_ai")


@dataclass
class SourceScores:
    speaking: bool = False
    audio_db: float = -80.0
    person_present: bool = False
    face_count: int = 0
    lip_movement: float = 0.0
    last_update: float = field(default_factory=time.time)


class PerSourceAnalyzer(threading.Thread):
    """Pulls frames+audio from one RtmpSource, updates SourceScores."""

    def __init__(
        self,
        source: RtmpSource,
        yolo_model,
        vad: VoiceActivityDetector,
        yolo_skip_frames: int = 3,
        yolo_conf: float = 0.4,
        yolo_imgsz: int = 480,
        lip_buffer_size: int = 12,
        loop_hz: float = 15.0,
        vad_threshold: float = 0.5,
        speak_threshold: int = 5,
        silence_threshold: int = 20,
        min_db: float = -45.0,
    ):
        super().__init__(daemon=True, name=f"analyzer-{source.source_id}")
        self.source = source
        self.yolo = yolo_model
        self.vad = vad
        self.yolo_skip = max(1, yolo_skip_frames)
        self.yolo_conf = yolo_conf
        self.yolo_imgsz = yolo_imgsz
        self._facemesh = load_facemesh(max_num_faces=2)
        self._lip_buf: deque[float] = deque(maxlen=lip_buffer_size)
        self._loop_period = 1.0 / loop_hz
        self._running = False
        self._frame_idx = 0
        self._state = SpeakingState(
            speak_threshold=speak_threshold,
            silence_threshold=silence_threshold,
            vad_threshold=vad_threshold,
            min_db=min_db,
        )
        self.scores = SourceScores()
        self._lock = threading.Lock()

    def run(self) -> None:
        self._running = True
        logger.info("Analyzer started: %s", self.source.source_id)
        while self._running:
            t0 = time.time()
            self._tick()
            elapsed = time.time() - t0
            time.sleep(max(0.0, self._loop_period - elapsed))
        try:
            self._facemesh.close()
        except Exception:
            pass
        logger.info("Analyzer stopped: %s", self.source.source_id)

    def stop(self) -> None:
        self._running = False

    def get_scores(self) -> SourceScores:
        with self._lock:
            return replace(self.scores)

    def _tick(self) -> None:
        self._process_audio()
        self._process_video()

    def _process_audio(self) -> None:
        if self.source.audio is None:
            return
        chunks = self.source.audio.get_pending()
        if not chunks:
            return
        # Feed each ffmpeg chunk to the VAD individually so the
        # frame-counter advances at the natural audio rate.
        last_db = self._state.last_db
        for chunk in chunks:
            db = compute_db(chunk)
            prob = self.vad.probability(chunk)
            self._state.feed(prob, db)
            last_db = db
        with self._lock:
            self.scores.speaking = self._state.speaking
            self.scores.audio_db = last_db

    def _process_video(self) -> None:
        frame = self.source.video.get_latest()
        if frame is None:
            return
        self._frame_idx += 1
        run_yolo = (self._frame_idx % self.yolo_skip) == 0

        person_present = self.scores.person_present
        if run_yolo:
            person_present = self._detect_person(frame)
        lip_movement = self._update_lip_score(frame)
        face_count = 1 if lip_movement > 0.0 else 0

        with self._lock:
            self.scores.person_present = person_present
            self.scores.face_count = face_count
            self.scores.lip_movement = lip_movement
            self.scores.last_update = time.time()

    def _detect_person(self, frame: np.ndarray) -> bool:
        try:
            results = self.yolo.predict(
                frame,
                imgsz=self.yolo_imgsz,
                conf=self.yolo_conf,
                classes=[0],  # COCO 'person'
                verbose=False,
            )
        except Exception as e:
            logger.debug("YOLO inference error: %s", e)
            return self.scores.person_present
        if not results:
            return False
        boxes = results[0].boxes
        return boxes is not None and len(boxes) > 0

    def _update_lip_score(self, frame: np.ndarray) -> float:
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        try:
            res = self._facemesh.process(rgb)
        except Exception as e:
            logger.debug("FaceMesh error: %s", e)
            return self.scores.lip_movement
        if not res.multi_face_landmarks:
            self._lip_buf.append(0.0)
            return self._compute_lip_movement()
        lm = res.multi_face_landmarks[0].landmark
        upper = lm[LIP_UPPER_INNER]
        lower = lm[LIP_LOWER_INNER]
        left = lm[LIP_LEFT_CORNER]
        right = lm[LIP_RIGHT_CORNER]
        mouth_h = abs(upper.y - lower.y)
        mouth_w = abs(left.x - right.x) or 1e-6
        ratio = mouth_h / mouth_w
        self._lip_buf.append(ratio)
        return self._compute_lip_movement()

    def _compute_lip_movement(self) -> float:
        if len(self._lip_buf) < 4:
            return 0.0
        arr = np.fromiter(self._lip_buf, dtype=np.float32)
        std = float(np.std(arr))
        return min(1.0, std / 0.05)


@dataclass
class SelectorConfig:
    # Base weights for focus mode when audio differs between cameras.
    # These are overridden automatically when audio is uniform across
    # cameras (typical case: 2 angles sharing one mic bus) → lips lead.
    weight_audio: float = 0.55
    weight_lips: float = 0.30
    weight_person: float = 0.15
    # Switching hysteresis (time-based, on top of audio frame-hysteresis).
    min_hold_time: float = 3.0           # Don't switch faster than this.
    max_hold_time: float = 12.0          # Force a switch after this long.
    switch_margin: float = 0.20          # New score must beat current by this.
    # Threshold (dB) below which audio levels are considered "indistinguishable"
    # across cameras — flips the algorithm to lips-dominant scoring.
    audio_uniform_db: float = 3.0
    # A camera must have either a visible person OR lip-movement above this
    # threshold to be eligible. Prevents switching to an empty/static frame.
    eligibility_lip_threshold: float = 0.05
    # Lip-movement above this gets a 30% score boost — strong "speaking now" signal.
    lip_strong_threshold: float = 0.30
    # Silence handling.
    silence_grace_s: float = 2.5         # No speech for this long → illustration.
    illustration_camera_id: str | None = None  # If set, prefer this source in idle.


class PodcastSelector:
    """Stateful selector. Lifecycle: start(sources) → select() → stop()."""

    def __init__(
        self,
        cfg: SelectorConfig | None = None,
        yolo_model_name: str | None = None,
    ):
        self.cfg = cfg or SelectorConfig()
        self._yolo_name = yolo_model_name
        self._yolo = None
        self._yolo_device = "cpu"
        self._vad: VoiceActivityDetector | None = None
        self._analyzers: list[PerSourceAnalyzer] = []
        self._rtmp_sources: list[RtmpSource] = []
        self._source_ids: list[str] = []
        self._current_index: int = -1
        self._last_switch_time: float = 0.0
        self._last_speech_time: float = 0.0
        self._mode: str = "focus"  # "focus" or "illustration"
        self._lock = threading.Lock()

    # --- Lifecycle -------------------------------------------------------

    def start(self, sources: list) -> None:
        self.stop()
        if not sources:
            return
        if self._yolo is None:
            self._yolo, self._yolo_device = load_yolo(self._yolo_name)
        if self._vad is None:
            self._vad = VoiceActivityDetector()
        for s in sources:
            url = getattr(s, "rtmp_url", None) or getattr(s, "url", None)
            sid = getattr(s, "id", None) or getattr(s, "label", None) or url
            if not url:
                continue
            rtmp = RtmpSource(url=url, source_id=str(sid))
            rtmp.start()
            self._rtmp_sources.append(rtmp)
            self._source_ids.append(str(sid))
            analyzer = PerSourceAnalyzer(rtmp, self._yolo, self._vad)
            analyzer.start()
            self._analyzers.append(analyzer)
        self._current_index = 0 if self._analyzers else -1
        now = time.time()
        self._last_switch_time = now
        self._last_speech_time = now  # Start in focus mode for first window.
        self._mode = "focus"
        logger.info("PodcastSelector started: %d sources", len(self._analyzers))

    def stop(self) -> None:
        for a in self._analyzers:
            a.stop()
        for s in self._rtmp_sources:
            s.stop()
        self._analyzers.clear()
        self._rtmp_sources.clear()
        self._source_ids.clear()
        self._current_index = -1

    # --- Selection -------------------------------------------------------

    def select(self, sources) -> int:
        if not self._analyzers:
            self.start(list(sources))
            return self._current_index

        now = time.time()
        scores = [a.get_scores() for a in self._analyzers]
        scores = self._arbitrate(scores)
        self._update_mode(scores, now)

        combined = self._combine(scores)
        best_idx = int(np.argmax(combined))
        held = now - self._last_switch_time

        if best_idx == self._current_index:
            if held > self.cfg.max_hold_time:
                alt = self._pick_alternative(combined, scores)
                if alt is not None and alt != self._current_index:
                    self._do_switch(alt, combined, scores, reason="max_hold")
            return self._current_index

        if held < self.cfg.min_hold_time:
            return self._current_index

        current_score = combined[self._current_index] if 0 <= self._current_index < len(combined) else -1.0
        if combined[best_idx] - current_score < self.cfg.switch_margin:
            return self._current_index

        self._do_switch(best_idx, combined, scores, reason=self._mode)
        return self._current_index

    # --- Internals -------------------------------------------------------

    def _arbitrate(self, scores: list[SourceScores]) -> list[SourceScores]:
        """Loudest-speaker wins (gabin-style). At most one source is 'speaking'."""
        speaking = [i for i, s in enumerate(scores) if s.speaking]
        if len(speaking) <= 1:
            return scores
        loudest = max(speaking, key=lambda i: scores[i].audio_db)
        out = []
        for i, s in enumerate(scores):
            if i in speaking and i != loudest:
                out.append(replace(s, speaking=False))
            else:
                out.append(s)
        return out

    def _update_mode(self, scores: list[SourceScores], now: float) -> None:
        any_speaking = any(s.speaking for s in scores)
        if any_speaking:
            self._last_speech_time = now
            if self._mode != "focus":
                logger.info("Mode: illustration → focus")
            self._mode = "focus"
        else:
            if now - self._last_speech_time > self.cfg.silence_grace_s:
                if self._mode != "illustration":
                    logger.info("Mode: focus → illustration (%.1fs silence)",
                                now - self._last_speech_time)
                self._mode = "illustration"

    def _combine(self, scores: list[SourceScores]) -> np.ndarray:
        """Score each source for the active-camera decision.

        Strategy:
        1. **Eligibility gate** — a camera must show a person OR have visible
           lip movement to be considered. Otherwise it's locked out (-inf-ish
           score) so we never switch to an empty/static frame.
        2. **Audio-uniformity detection** — if the speaking cameras' dB levels
           are within a few dB of each other, audio can't differentiate them.
           We auto-flip to a LIPS-DOMINANT weighting (0.10/0.70/0.20) so the
           visually-talking person wins. This is the common case for 2-angle
           podcast recordings sharing a single mic bus.
        3. **Strong lip boost** — a clear "lips moving" reading gets a +30%
           score multiplier, since it's the most reliable real-time speaker cue.
        4. **Illustration mode** — when nobody speaks, audio is dropped and we
           pick on lips+person only, with a nudge toward the configured wide cam.
        """
        w = self.cfg
        n = len(scores)
        out = np.full(n, -1.0, dtype=np.float32)  # ineligible = -1 (never beats 0)

        eligible = [
            i for i, s in enumerate(scores)
            if s.person_present or s.lip_movement > w.eligibility_lip_threshold
        ]
        if not eligible:
            return out

        if self._mode == "illustration":
            for i in eligible:
                s = scores[i]
                lip = float(np.clip(s.lip_movement, 0.0, 1.0))
                person = 1.0 if s.person_present else 0.0
                out[i] = 0.6 * lip + 0.4 * person
                if w.illustration_camera_id and self._source_ids[i] == w.illustration_camera_id:
                    out[i] += 0.5
            return out

        # Focus mode: someone is speaking. Decide whether audio differentiates.
        speaking_dbs = [scores[i].audio_db for i in eligible if scores[i].speaking]
        audio_uniform = (
            len(speaking_dbs) >= 2
            and (max(speaking_dbs) - min(speaking_dbs)) < w.audio_uniform_db
        )

        if audio_uniform:
            w_audio, w_lips, w_person = 0.10, 0.70, 0.20
        else:
            w_audio, w_lips, w_person = w.weight_audio, w.weight_lips, w.weight_person

        for i in eligible:
            s = scores[i]
            audio_signal = 1.0 if s.speaking else 0.0
            lip = float(np.clip(s.lip_movement, 0.0, 1.0))
            person = 1.0 if s.person_present else 0.0
            score = w_audio * audio_signal + w_lips * lip + w_person * person
            if lip > w.lip_strong_threshold:
                score *= 1.3
            out[i] = score
        return out

    def _pick_alternative(self, combined: np.ndarray, scores: list[SourceScores]) -> int | None:
        if combined.size <= 1:
            return None
        ranked = np.argsort(combined)[::-1]
        for idx in ranked:
            i = int(idx)
            if i != self._current_index and (scores[i].person_present or scores[i].lip_movement > 0.1):
                return i
        return None

    def _do_switch(self, new_idx: int, combined, scores, reason: str) -> None:
        with self._lock:
            old = self._current_index
            self._current_index = new_idx
            self._last_switch_time = time.time()
        logger.info(
            "Switch %d → %d (%s) scores=%s speaking=%s db=%s lips=%s",
            old, new_idx, reason,
            [round(float(c), 3) for c in combined],
            [s.speaking for s in scores],
            [round(s.audio_db, 1) for s in scores],
            [round(s.lip_movement, 3) for s in scores],
        )
