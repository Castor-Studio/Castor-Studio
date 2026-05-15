"""RTMP demuxing via ffmpeg subprocess pipes.

Spawns two ffmpeg processes per source: one for video (raw BGR frames),
one for audio (PCM s16le mono). Both processes target the same RTMP URL.
Frames and audio chunks are pushed into bounded queues; consumers read
the most recent. Latency is bounded by queue size (max 2 video / 4 audio).

Notes:
- ffmpeg must be in PATH (Windows: bundle ffmpeg.exe, Mac: brew install ffmpeg).
- We use two processes (not one with -map) for portability: Windows lacks
  named pipes on stdout cleanly, and stderr-separated multi-output is fragile.
- For local MediaMTX the double TCP connection cost is negligible.
"""
from __future__ import annotations

import logging
import queue
import shutil
import subprocess
import threading
from dataclasses import dataclass

import numpy as np

logger = logging.getLogger("ai-hub.preprocessing")


@dataclass(frozen=True)
class StreamConfig:
    width: int = 640
    height: int = 360
    fps: int = 15
    # 16 kHz mono, 512-sample chunks → matches Silero VAD's expected frame size.
    audio_sample_rate: int = 16_000
    audio_channels: int = 1
    audio_chunk_samples: int = 512
    ffmpeg_loglevel: str = "error"


def _ffmpeg_bin() -> str:
    path = shutil.which("ffmpeg")
    if not path:
        raise RuntimeError(
            "ffmpeg not found in PATH. Install it (mac: brew install ffmpeg, "
            "win: choco install ffmpeg or bundle ffmpeg.exe next to mediamtx)."
        )
    return path


_STREAM_SCHEMES = ("rtmp://", "rtsp://", "http://", "https://", "udp://", "tcp://", "srt://")


def is_stream_url(src: str) -> bool:
    return src.lower().startswith(_STREAM_SCHEMES)


def _input_args(src: str) -> list[str]:
    """ffmpeg input flags differ between live streams and local files.

    Local files: play at real-time rate (-re) and loop forever
    (-stream_loop -1) so a 60s clip drives the pipeline indefinitely.
    Live streams: low-latency flags, no looping.
    """
    if is_stream_url(src):
        return [
            "-fflags", "nobuffer",
            "-flags", "low_delay",
            "-rtmp_live", "live",
            "-i", src,
        ]
    return [
        "-re",
        "-stream_loop", "-1",
        "-i", src,
    ]


class RtmpVideoReader(threading.Thread):
    """Pulls raw BGR frames from an RTMP URL via ffmpeg."""

    def __init__(self, url: str, cfg: StreamConfig, source_id: str):
        super().__init__(daemon=True, name=f"video-{source_id}")
        self.url = url
        self.cfg = cfg
        self.source_id = source_id
        self.frame_q: "queue.Queue[np.ndarray]" = queue.Queue(maxsize=2)
        self._proc: subprocess.Popen | None = None
        self._running = False
        self._frame_bytes = cfg.width * cfg.height * 3

    def run(self) -> None:
        self._running = True
        ffmpeg = _ffmpeg_bin()
        cmd = [
            ffmpeg,
            "-loglevel", self.cfg.ffmpeg_loglevel,
            *_input_args(self.url),
            "-an",
            "-vf", f"scale={self.cfg.width}:{self.cfg.height}",
            "-r", str(self.cfg.fps),
            "-f", "rawvideo",
            "-pix_fmt", "bgr24",
            "pipe:1",
        ]
        try:
            self._proc = subprocess.Popen(
                cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0
            )
        except Exception as e:
            logger.error("Failed to spawn ffmpeg for %s: %s", self.url, e)
            self._running = False
            return

        logger.info("Video reader started: %s", self.url)
        try:
            while self._running and self._proc.poll() is None:
                buf = self._read_exact(self._proc.stdout, self._frame_bytes)
                if buf is None:
                    break
                frame = np.frombuffer(buf, dtype=np.uint8).reshape(
                    (self.cfg.height, self.cfg.width, 3)
                )
                self._push(frame)
        finally:
            self._terminate()
            logger.info("Video reader stopped: %s", self.url)

    @staticmethod
    def _read_exact(pipe, n: int) -> bytes | None:
        chunks = []
        remaining = n
        while remaining > 0:
            chunk = pipe.read(remaining)
            if not chunk:
                return None
            chunks.append(chunk)
            remaining -= len(chunk)
        return b"".join(chunks)

    def _push(self, frame: np.ndarray) -> None:
        if self.frame_q.full():
            try:
                self.frame_q.get_nowait()
            except queue.Empty:
                pass
        try:
            self.frame_q.put_nowait(frame)
        except queue.Full:
            pass

    def get_latest(self) -> np.ndarray | None:
        latest = None
        while True:
            try:
                latest = self.frame_q.get_nowait()
            except queue.Empty:
                break
        return latest

    def stop(self) -> None:
        self._running = False
        self._terminate()

    def _terminate(self) -> None:
        if self._proc and self._proc.poll() is None:
            try:
                self._proc.terminate()
                self._proc.wait(timeout=1.0)
            except Exception:
                try:
                    self._proc.kill()
                except Exception:
                    pass


class RtmpAudioReader(threading.Thread):
    """Pulls mono float32 audio chunks from an RTMP URL via ffmpeg."""

    def __init__(self, url: str, cfg: StreamConfig, source_id: str):
        super().__init__(daemon=True, name=f"audio-{source_id}")
        self.url = url
        self.cfg = cfg
        self.source_id = source_id
        self.audio_q: "queue.Queue[np.ndarray]" = queue.Queue(maxsize=8)
        self._proc: subprocess.Popen | None = None
        self._running = False
        self._chunk_bytes = cfg.audio_chunk_samples * 2 * cfg.audio_channels  # s16le

    def run(self) -> None:
        self._running = True
        ffmpeg = _ffmpeg_bin()
        cmd = [
            ffmpeg,
            "-loglevel", self.cfg.ffmpeg_loglevel,
            *_input_args(self.url),
            "-vn",
            "-ar", str(self.cfg.audio_sample_rate),
            "-ac", str(self.cfg.audio_channels),
            "-f", "s16le",
            "pipe:1",
        ]
        try:
            self._proc = subprocess.Popen(
                cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0
            )
        except Exception as e:
            logger.error("Failed to spawn ffmpeg audio for %s: %s", self.url, e)
            self._running = False
            return

        logger.info("Audio reader started: %s", self.url)
        try:
            while self._running and self._proc.poll() is None:
                buf = RtmpVideoReader._read_exact(self._proc.stdout, self._chunk_bytes)
                if buf is None:
                    break
                samples = np.frombuffer(buf, dtype=np.int16).astype(np.float32) / 32768.0
                self._push(samples)
        finally:
            self._terminate()
            logger.info("Audio reader stopped: %s", self.url)

    def _push(self, samples: np.ndarray) -> None:
        if self.audio_q.full():
            try:
                self.audio_q.get_nowait()
            except queue.Empty:
                pass
        try:
            self.audio_q.put_nowait(samples)
        except queue.Full:
            pass

    def drain(self) -> np.ndarray | None:
        """Return concatenation of all queued chunks, or None."""
        chunks = self.get_pending()
        if not chunks:
            return None
        return np.concatenate(chunks)

    def get_pending(self) -> list[np.ndarray]:
        """Drain all queued chunks individually (preserves chunk boundaries
        for frame-based VAD)."""
        chunks: list[np.ndarray] = []
        while True:
            try:
                chunks.append(self.audio_q.get_nowait())
            except queue.Empty:
                break
        return chunks

    def stop(self) -> None:
        self._running = False
        self._terminate()

    def _terminate(self) -> None:
        if self._proc and self._proc.poll() is None:
            try:
                self._proc.terminate()
                self._proc.wait(timeout=1.0)
            except Exception:
                try:
                    self._proc.kill()
                except Exception:
                    pass


class RtmpSource:
    """One RTMP URL → (latest_frame, latest_audio_chunk).

    Owns a video reader and (optionally) an audio reader. Audio can be
    disabled per source if MediaMTX has no audio track for it.
    """

    def __init__(
        self,
        url: str,
        source_id: str,
        cfg: StreamConfig | None = None,
        with_audio: bool = True,
    ):
        self.url = url
        self.source_id = source_id
        self.cfg = cfg or StreamConfig()
        self.video = RtmpVideoReader(url, self.cfg, source_id)
        self.audio = RtmpAudioReader(url, self.cfg, source_id) if with_audio else None

    def start(self) -> None:
        self.video.start()
        if self.audio:
            self.audio.start()

    def stop(self) -> None:
        self.video.stop()
        if self.audio:
            self.audio.stop()

    @property
    def is_alive(self) -> bool:
        v = self.video.is_alive()
        a = (not self.audio) or self.audio.is_alive()
        return v and a


def compute_rms(samples: np.ndarray) -> float:
    if samples is None or samples.size == 0:
        return 0.0
    return float(np.sqrt(np.mean(samples.astype(np.float32) ** 2)))
