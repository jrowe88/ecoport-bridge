"""CTA-2045 link-layer framing (start/end delimiters, escaping, length)."""


def frame(payload: bytes) -> bytes:
    """Wrap a payload in CTA-2045 link-layer framing.

    TODO: implement per confirmed protocol details in docs/protocol.md.
    """
    raise NotImplementedError


def unframe(data: bytes) -> bytes:
    """Extract a payload from CTA-2045 link-layer framing.

    TODO: implement per confirmed protocol details in docs/protocol.md.
    """
    raise NotImplementedError
