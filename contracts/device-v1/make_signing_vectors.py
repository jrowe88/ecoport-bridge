"""Reference implementation of the device request signature; writes signing-vectors.json.

Run from the repository root:
    python -I contracts/device-v1/make_signing_vectors.py > contracts/device-v1/signing-vectors.json
"""

import base64
import hashlib
import hmac
import json
from pathlib import Path

HERE = Path(__file__).parent
SECRET = bytes(range(32))


def sign(secret: bytes, method: str, path: str, timestamp: str, body: bytes) -> str:
    canonical = f"{method}\n{path}\n{timestamp}\n{hashlib.sha256(body).hexdigest()}"
    digest = hmac.new(secret, canonical.encode(), hashlib.sha256).digest()
    return base64.b64encode(digest).decode()


def main() -> None:
    body = (HERE / "upload-request.example.json").read_bytes()
    cases = [
        ("POST", "/device/v1/records", "2026-10-09T20:31:02Z", body),
        ("POST", "/device/v1/records", "2026-10-09T20:31:02Z", b""),
        ("GET", "/device/v1/commands", "2026-01-01T00:00:00Z", b""),
    ]
    vectors = [
        {
            "method": method,
            "path": path,
            "timestamp": ts,
            "bodyBase64": base64.b64encode(b).decode(),
            "signature": sign(SECRET, method, path, ts, b),
        }
        for method, path, ts, b in cases
    ]
    out = {"secretBase64": base64.b64encode(SECRET).decode(), "vectors": vectors}
    print(json.dumps(out, indent=2))


if __name__ == "__main__":
    main()
