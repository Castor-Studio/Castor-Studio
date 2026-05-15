"""Voice Activity Detection wrapper (Silero VAD, ONNX).

Inspired by gabin's audioActivity.ts: per-source frame counter capped at
±30, with separate thresholds to flip ON (sustained speech) and OFF
(sustained silence). Gives much more stable speaking states than raw RMS.
"""
from __future__ import annotations

import logging
import threading

import numpy as np

logger = logging.getLogger("ai-hub.vad")


VAD_SAMPLE_RATE = 16_000
VAD_FRAME_SAMPLES = 512  # Silero requires exactly 512 @ 16k (or 256 @ 8k).


class VoiceActivityDetector:
    """Thin wrapper around Silero VAD. Thread-safe (lock around inference).

    The ONNX backend keeps RAM tiny and avoids the GIL-heavy torch path.
    """

    def __init__(self):
        self._model = None
        self._lock = threading.Lock()
        self._init_model()

    def _init_model(self) -> None:
        try:
            from silero_vad import load_silero_vad
        except ImportError as e:
            raise RuntimeError(
                "silero-vad not installed. Run: uv pip install silero-vad onnxruntime"
            ) from e
        # onnx=True → onnxruntime backend, no torch needed at inference time.
        self._model = load_silero_vad(onnx=True)
        logger.info("Silero VAD loaded (ONNX backend)")

    def probability(self, chunk: np.ndarray) -> float:
        """Return P(speech) for a 512-sample float32 chunk at 16kHz."""
        if chunk.size != VAD_FRAME_SAMPLES:
            return 0.0
        if chunk.dtype != np.float32:
            chunk = chunk.astype(np.float32)
        with self._lock:
            try:
                # silero-vad accepts numpy arrays directly with the onnx backend
                # since v5.x; older versions need a torch tensor.
                prob = self._model(chunk, VAD_SAMPLE_RATE)
                if hasattr(prob, "item"):
                    return float(prob.item())
                return float(prob)
            except Exception as e:
                logger.debug("VAD inference error: %s", e)
                return 0.0

    def reset(self) -> None:
        """Reset the model state (e.g. between streams)."""
        if self._model is not None and hasattr(self._model, "reset_states"):
            with self._lock:
                self._model.reset_states()


class SpeakingState:
    """Per-source frame-counter hysteresis, gabin-style.

    consecutive ∈ [-silence_cap, +speak_cap]
      > +speak_threshold → speaking = True
      < -silence_threshold → speaking = False
    """

    __slots__ = (
        "consecutive",
        "speaking",
        "speak_threshold",
        "silence_threshold",
        "speak_cap",
        "silence_cap",
        "vad_threshold",
        "min_db",
        "last_db",
    )

    def __init__(
        self,
        speak_threshold: int = 5,
        silence_threshold: int = 20,
        speak_cap: int = 30,
        silence_cap: int = 30,
        vad_threshold: float = 0.5,
        min_db: float = -45.0,
    ):
        self.consecutive: int = 0
        self.speaking: bool = False
        self.speak_threshold = speak_threshold
        self.silence_threshold = silence_threshold
        self.speak_cap = speak_cap
        self.silence_cap = silence_cap
        self.vad_threshold = vad_threshold
        self.min_db = min_db
        self.last_db: float = -80.0

    def feed(self, vad_prob: float, db: float) -> None:
        speaking_frame = (vad_prob >= self.vad_threshold) and (db >= self.min_db)
        if speaking_frame:
            # Cap on positive side, snap to +1 if we were negative.
            self.consecutive = max(self.consecutive + 1, 1)
            if self.consecutive > self.speak_cap:
                self.consecutive = self.speak_cap
        else:
            self.consecutive = min(self.consecutive - 1, -1)
            if self.consecutive < -self.silence_cap:
                self.consecutive = -self.silence_cap

        if self.consecutive >= self.speak_threshold:
            self.speaking = True
        elif self.consecutive <= -self.silence_threshold:
            self.speaking = False

        self.last_db = db


def compute_db(samples: np.ndarray) -> float:
    """Return RMS level in dBFS (full-scale = 0 dB)."""
    if samples is None or samples.size == 0:
        return -80.0
    rms = float(np.sqrt(np.mean(samples.astype(np.float32) ** 2)))
    if rms <= 1e-7:
        return -80.0
    return 20.0 * float(np.log10(rms))
