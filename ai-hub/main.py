"""ai-hub entrypoint.

Usage:
    python main.py --module podcast --sources rtmp://localhost/cam1 rtmp://localhost/cam2
    python main.py --module sport   --sources rtmp://localhost/cam1 rtmp://localhost/cam2

This is the standalone dev runner. In production the Castor backend
(CameraSelectorDebug/Server) imports `modules.podcast_ai.PodcastSelector`
directly via the gRPC adapter and this entrypoint isn't used.
"""
from __future__ import annotations

import argparse
import sys

from core.utils import setup_logging


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Castor AI Hub")
    parser.add_argument("--module", choices=["podcast", "sport"], default="podcast")
    parser.add_argument("--sources", nargs="+", required=True,
                        help="RTMP URLs or local video files")
    parser.add_argument("--no-preview", action="store_true",
                        help="Run headless (no OpenCV window)")
    parser.add_argument("--debug", action="store_true",
                        help="Draw score overlay on preview (off by default — clean for presentation)")
    parser.add_argument("--json-events", action="store_true",
                        help="Emit newline-delimited JSON events on stdout (for host apps like Castor)")
    parser.add_argument("--log-level", default="INFO")
    args = parser.parse_args(argv)

    # When emitting JSON events on stdout, route logs to stderr so they don't
    # corrupt the event stream consumed by a parent process.
    if args.json_events:
        import logging as _logging
        _logging.basicConfig(
            level=getattr(_logging, args.log_level.upper(), _logging.INFO),
            format="[%(asctime)s] %(levelname)s %(name)s - %(message)s",
            datefmt="%H:%M:%S",
            stream=sys.stderr,
            force=True,
        )
    else:
        setup_logging(args.log_level)

    if args.module == "podcast":
        from modules.podcast_ai.inference import PodcastEngine
        engine = PodcastEngine(
            urls=args.sources,
            preview=not args.no_preview,
            debug_overlay=args.debug,
            json_events=args.json_events,
        )
        engine.run()
        return 0

    if args.module == "sport":
        print("sport_ai stub — owned by Guillian", file=sys.stderr)
        return 1

    return 2


if __name__ == "__main__":
    sys.exit(main())
