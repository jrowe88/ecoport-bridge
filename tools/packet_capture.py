"""Write raw serial captures and timestamped receive events to disk.

This module intentionally has no serial-port code. It is used by passive
capture tools now and can be reused by future active tools without changing
the capture format.
"""

from __future__ import annotations

import base64
import json
import re
from datetime import datetime
from pathlib import Path
from typing import Any, Self


class CaptureWriter:
    """Persist a binary capture with a JSON Lines receive-event sidecar."""

    def __init__(
        self,
        output_root: Path,
        scenario: str,
        metadata: dict[str, Any],
        started_at: datetime | None = None,
    ) -> None:
        self._started_at = started_at or datetime.now().astimezone()
        safe_scenario = re.sub(r"[^a-z0-9-]+", "-", scenario.lower()).strip("-")
        if not safe_scenario:
            raise ValueError("scenario must include at least one letter or number")

        directory_name = f"{self._started_at:%Y-%m-%dT%H%M%S}-{safe_scenario}"
        self.directory = output_root / directory_name
        self.directory.mkdir(parents=True, exist_ok=False)
        self.binary_path = self.directory / "capture.bin"
        self.events_path = self.directory / "capture.jsonl"
        self.notes_path = self.directory / "notes.md"
        self._binary_file = self.binary_path.open("wb")
        self._events_file = self.events_path.open("x", encoding="utf-8")
        self._offset = 0

        self._write_notes(metadata)

    def record(self, data: bytes, received_at: datetime | None = None) -> None:
        """Append bytes exactly as received and describe the receive event."""
        if not data:
            return

        timestamp = received_at or datetime.now().astimezone()
        event = {
            "timestamp": timestamp.isoformat(timespec="milliseconds"),
            "offset": self._offset,
            "length": len(data),
            "hex": data.hex(" "),
            "base64": base64.b64encode(data).decode("ascii"),
        }
        self._binary_file.write(data)
        self._binary_file.flush()
        self._events_file.write(json.dumps(event, separators=(",", ":")) + "\n")
        self._events_file.flush()
        self._offset += len(data)

    def close(self) -> None:
        """Close the capture files. Safe to call more than once."""
        if not self._binary_file.closed:
            self._binary_file.close()
        if not self._events_file.closed:
            self._events_file.close()

    def _write_notes(self, metadata: dict[str, Any]) -> None:
        rendered_metadata = "\n".join(
            f"- **{key.replace('_', ' ').title()}:** {value}"
            for key, value in metadata.items()
        )
        self.notes_path.write_text(
            f"""# {self.directory.name}

## Capture metadata

{rendered_metadata}
- **Started:** {self._started_at.isoformat(timespec="seconds")}

## Operator timeline

Record each physical/control-panel action here using a local timestamp.
Do not infer protocol meanings until the raw traffic has been decoded.

| Time | Appliance display / condition | Manual action | Notes |
|------|-------------------------------|---------------|-------|
|      |                               |               |       |

## Observations

-
""",
            encoding="utf-8",
        )

    def __enter__(self) -> Self:
        return self

    def __exit__(self, *_: object) -> None:
        self.close()
