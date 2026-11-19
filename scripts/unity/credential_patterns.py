"""Credential patterns for Unity artifacts, in redaction precedence order."""

import re

# Match JavaScript's whitespace set while keeping word boundaries ASCII.
SPACE = r"[\u0009-\u000d\u0020\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]"
# Keep the fixed prefix ahead of the boundary check so the regex engine can skip ordinary text.
PATTERNS: tuple[tuple[str, re.Pattern[str]], ...] = (
    (
        "pem-private-key",
        re.compile(
            r"-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----"
            r"(?:.*?-----END (?:[A-Z ]+ )?PRIVATE KEY-----|.*)",
            re.DOTALL,
        ),
    ),
    (
        "unity-access-token",
        re.compile(
            r"""(\b(?:successfully[ \t]+updated[ \t]+the[ \t]+access[ \t]+token[ \t]+"""
            r"""|access[ \t]+token[ \t]*[:=][ \t]*)["']?)[A-Za-z0-9._~+/=-]+""",
            re.IGNORECASE | re.ASCII,
        ),
    ),
    ("unity-license-id", re.compile(r'(<License\b[^>]*\bid=")[^"<]+', re.ASCII)),
    (
        "unity-serial",
        re.compile(r"S(?<!\wS)[CBP]-[0-9A-Z]{4}(?:-[0-9A-Z]{4}){4}\b", re.ASCII),
    ),
    (
        "github-token",
        re.compile(
            r"g(?<!\wg)(?:h[pousr]_[A-Za-z0-9]{36,}|ithub_pat_[A-Za-z0-9_]{40,})\b",
            re.ASCII,
        ),
    ),
    ("aws-access-key-id", re.compile(r"AKIA(?<!\wAKIA)[0-9A-Z]{16}\b", re.ASCII)),
    (
        "http-bearer-token",
        re.compile(rf"(Bearer(?<!\wBearer){SPACE}+)[A-Za-z0-9._~+/=-]{{20,}}", re.ASCII),
    ),
    (
        "credential-assignment",
        re.compile(
            rf"""(\b(?:UNITY_(?:SERIAL|EMAIL|PASSWORD)|[A-Z][A-Z0-9_]*"""
            rf"""(?:TOKEN|SECRET|PASSWORD|API_KEY))["']?{SPACE}*[=:]{SPACE}*["']?)"""
            r"[A-Za-z0-9._~+/=@-]{12,}",
            re.ASCII,
        ),
    ),
)


def redact_credentials(text: str) -> tuple[str, dict[str, int]]:
    counts: dict[str, int] = {}
    for name, pattern in PATTERNS:
        prefix = r"\g<1>" if pattern.groups else ""
        text, count = pattern.subn(f"{prefix}<redacted:{name}>", text)
        if count:
            counts[name] = count
    return text, counts
