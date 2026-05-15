"""Lazy loaders for YOLO + MediaPipe FaceLandmarker."""
from __future__ import annotations

import logging
import urllib.request
from pathlib import Path
from types import SimpleNamespace

from .utils import detect_device, repo_root

logger = logging.getLogger("ai-hub.model_loader")


_YOLO_FALLBACK_CHAIN = ["yolo26n.pt", "yolo11n.pt", "yolov8n.pt"]

_FACE_LANDMARKER_URL = (
    "https://storage.googleapis.com/mediapipe-models/face_landmarker/"
    "face_landmarker/float16/1/face_landmarker.task"
)
_FACE_LANDMARKER_FILENAME = "face_landmarker.task"


def load_yolo(model_name: str | None = None, device: str | None = None):
    """Load an Ultralytics YOLO model.

    Tries the requested name first, then a fallback chain. Models are
    auto-downloaded by Ultralytics on first use if the name matches a
    known release; otherwise we look in ai-hub/weights/.
    """
    from ultralytics import YOLO

    device = device or detect_device()
    candidates = [model_name] if model_name else []
    candidates.extend(c for c in _YOLO_FALLBACK_CHAIN if c not in candidates)

    weights_dir = repo_root() / "weights"
    last_err: Exception | None = None
    for name in candidates:
        if not name:
            continue
        local = weights_dir / name
        target = str(local) if local.exists() else name
        try:
            model = YOLO(target)
            logger.info("Loaded YOLO '%s' on device=%s", target, device)
            return model, device
        except Exception as e:
            logger.warning("YOLO load failed for '%s': %s", target, e)
            last_err = e

    raise RuntimeError(f"Could not load any YOLO model: {last_err}")


def _ensure_face_landmarker_task() -> Path:
    weights = repo_root() / "weights"
    weights.mkdir(parents=True, exist_ok=True)
    target = weights / _FACE_LANDMARKER_FILENAME
    if target.exists() and target.stat().st_size > 0:
        return target
    logger.info("Downloading %s ...", _FACE_LANDMARKER_FILENAME)
    tmp = target.with_suffix(target.suffix + ".part")
    try:
        urllib.request.urlretrieve(_FACE_LANDMARKER_URL, tmp)
        tmp.rename(target)
    except Exception:
        if tmp.exists():
            tmp.unlink(missing_ok=True)
        raise
    logger.info("Saved %s (%d bytes)", target, target.stat().st_size)
    return target


class _FaceLandmarkerAdapter:
    """Drop-in replacement exposing the old `.process(rgb)` /
    `.close()` surface, backed by the new mediapipe Tasks API.

    `.process(rgb_frame)` returns an object with `.multi_face_landmarks`
    being a list of objects with `.landmark[i].x/.y/.z`, matching the
    legacy `mp.solutions.face_mesh.FaceMesh` shape.
    """

    def __init__(self, landmarker):
        self._landmarker = landmarker
        self._timestamp_ms = 0

    def process(self, rgb_frame):
        import mediapipe as mp

        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)
        # detect_for_video requires monotonically-increasing timestamps.
        self._timestamp_ms += 33
        result = self._landmarker.detect_for_video(mp_image, self._timestamp_ms)
        faces = result.face_landmarks or []
        multi = [SimpleNamespace(landmark=face) for face in faces] if faces else None
        return SimpleNamespace(multi_face_landmarks=multi)

    def close(self):
        try:
            self._landmarker.close()
        except Exception:
            pass


def load_facemesh(max_num_faces: int = 2, refine_landmarks: bool = True):
    """Build a FaceLandmarker (new Tasks API) and return the adapter."""
    from mediapipe.tasks import python as mp_python
    from mediapipe.tasks.python import vision

    task_path = _ensure_face_landmarker_task()
    base_options = mp_python.BaseOptions(model_asset_path=str(task_path))
    options = vision.FaceLandmarkerOptions(
        base_options=base_options,
        running_mode=vision.RunningMode.VIDEO,
        num_faces=max_num_faces,
        output_face_blendshapes=False,
        output_facial_transformation_matrixes=False,
    )
    landmarker = vision.FaceLandmarker.create_from_options(options)
    logger.info("FaceLandmarker ready (max_faces=%d)", max_num_faces)
    return _FaceLandmarkerAdapter(landmarker)


# Lip landmark indices (MediaPipe canonical 468/478-pt mesh — unchanged
# between the legacy FaceMesh and the new FaceLandmarker output).
LIP_UPPER_INNER = 13
LIP_LOWER_INNER = 14
LIP_LEFT_CORNER = 78
LIP_RIGHT_CORNER = 308
