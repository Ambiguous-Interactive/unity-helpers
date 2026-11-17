"""Stream native-report observations without predicting test or cleanup results."""

from __future__ import annotations

import argparse
import io
import json
import sys
from pathlib import Path
from typing import TYPE_CHECKING, Literal, TypedDict

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from cli_encoding import configure_output

if TYPE_CHECKING:
    from collections.abc import Iterator

ASCII_LOWER = str.maketrans("ABCDEFGHIJKLMNOPQRSTUVWXYZ", "abcdefghijklmnopqrstuvwxyz")
WHITESPACE = "\t\n\v\f\r \u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000"


class Summary(TypedDict):
    State: Literal["missing-log", "observed", "unreadable-log"]
    NativeReportHeaderCount: int
    UnknownNativeConditionCount: int
    NativeReportHeaderLines: list[int]
    UnknownNativeConditionLines: list[int]
    LocationsTruncated: bool


def read_lines(path: Path) -> Iterator[str]:
    with path.open("rb") as stream:
        prefix = stream.read(4)
        stream.seek(0)
        encoding = "utf-8-sig"
        for marker, candidate in [
            (b"\xff\xfe\x00\x00", "utf-32"),
            (b"\x00\x00\xfe\xff", "utf-32"),
            (b"\xff\xfe", "utf-16"),
            (b"\xfe\xff", "utf-16"),
        ]:
            if prefix.startswith(marker):
                encoding = candidate
                break
        with io.TextIOWrapper(stream, encoding=encoding, errors="replace", newline=None) as reader:
            yield from reader


def summarize(log_path: str) -> Summary:
    result: Summary = {
        "State": "missing-log",
        "NativeReportHeaderCount": 0,
        "UnknownNativeConditionCount": 0,
        "NativeReportHeaderLines": [],
        "UnknownNativeConditionLines": [],
        "LocationsTruncated": False,
    }
    if not log_path.strip(WHITESPACE) or not Path(log_path).is_file():
        return result
    try:
        for number, line in enumerate(read_lines(Path(log_path)), 1):
            folded = line.lower() if line.isascii() else line.translate(ASCII_LOWER)
            if "native crash reporting" in folded:
                result["NativeReportHeaderCount"] += 1
                if len(result["NativeReportHeaderLines"]) < 8:
                    result["NativeReportHeaderLines"].append(number)
                else:
                    result["LocationsTruncated"] = True
            if "got a unknown while executing native code" in folded:
                result["UnknownNativeConditionCount"] += 1
                if len(result["UnknownNativeConditionLines"]) < 8:
                    result["UnknownNativeConditionLines"].append(number)
                else:
                    result["LocationsTruncated"] = True
        result["State"] = "observed"
    except OSError:
        result["State"] = "unreadable-log"
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log-path", default="")
    arguments = parser.parse_args()
    print(json.dumps(summarize(arguments.log_path), ensure_ascii=True))
    return 0


if __name__ == "__main__":
    configure_output()
    sys.exit(main())
