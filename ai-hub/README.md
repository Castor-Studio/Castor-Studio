# ai-hub

Castor's AI selectors. Two modules:

| Module | Owner | Purpose |
|---|---|---|
| `modules/podcast_ai` | André | Pick the active camera in a podcast by combining Silero VAD (frame-counter hysteresis, gabin-style), person presence (YOLO), and lip movement (MediaPipe FaceMesh). Loudest-speaker arbitration + illustration mode on prolonged silence. |
| `modules/sport_ai` | Guillian | Football scenarios — ball/player tracking driven switching. Stub for now. |

Shared building blocks live in `core/`:

- `core/preprocessing.py` — `RtmpSource`: pulls video+audio from an RTMP URL via two ffmpeg subprocesses. 16 kHz mono / 512-sample chunks (Silero VAD frame size). Thread-safe queues, latest-wins.
- `core/vad.py` — `VoiceActivityDetector` (Silero ONNX) + `SpeakingState` (gabin-style frame counter: +5 to flip ON, −20 to flip OFF, capped at ±30).
- `core/model_loader.py` — Ultralytics YOLO loader with fallback chain (`yolo26n.pt` → `yolo11n.pt` → `yolov8n.pt`) and a MediaPipe FaceMesh factory.
- `core/utils.py` — logging, yaml, device autodetect.

## Layout

```
ai-hub/
├── main.py                 # standalone CLI for local dev
├── core/
├── modules/podcast_ai/
├── modules/sport_ai/
└── configs/
```

## Requirements

- Python 3.10–3.12
- `ffmpeg` on PATH (mac: `brew install ffmpeg`, win: bundle next to mediamtx)
- For YOLOv26 weights: drop `yolo26n.pt` in `weights/`. If absent the loader falls back to `yolo11n.pt`, then `yolov8n.pt` (Ultralytics auto-downloads those).

## Run standalone

```bash
uv sync
uv run python main.py --module podcast \
    --sources rtmp://localhost:1935/cam1 rtmp://localhost:1935/cam2
```

Preview window shows the active camera with audio/lip/person scores. `q` or `Esc` to quit.

## How Castor backend consumes it

`CameraSelectorDebug/Server` imports `modules.podcast_ai.PodcastSelector` through the adapter at `Server/src/ai/podcast_selector.py`. The gRPC service calls `selector.start(sources)` on `StartAnalysis` and reads `selector.select(sources)` from the camera loop. State (ffmpeg readers, models, scoring threads) is owned by the selector — the gRPC layer is just a stateless transport.

## Switching selector

The server reads `$CASTOR_SELECTOR` (default `podcast`):

```bash
CASTOR_SELECTOR=random uv run ./src/main.py   # for smoke tests
CASTOR_SELECTOR=podcast uv run ./src/main.py  # real AI
```
