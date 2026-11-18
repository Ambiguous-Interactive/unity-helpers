"""Read the JSON syntax and object keys accepted by PowerShell ConvertFrom-Json."""

from __future__ import annotations

import re
from typing import TYPE_CHECKING

import json5

if TYPE_CHECKING:
    from collections.abc import Iterable

JSON_TOKENS = re.compile(
    r""""(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|//[^\r\n]*|/\*[\s\S]*?\*/"""
    r"|(?P<special>[+-]?(?:Infinity|NaN)\b)"
    r"|(?P<number>[+-]?(?:0[xX][0-9a-fA-F]+|(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?))"
    r"|(?P<identifier>[A-Za-z_$][A-Za-z0-9_$]*)|[\s\S]"
)


def json_object(pairs: Iterable[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    spelling: dict[str, str] = {}
    for key, value in pairs:
        if not key:
            raise ValueError("PowerShell objects require nonempty property names.")
        folded_parts: list[str] = []
        for character in key:
            upper = character.upper()
            folded_parts.append(
                upper
                if len(upper) == 1 and (character.isascii() or not upper.isascii())
                else character
            )
        folded = "".join(folded_parts)
        if folded in spelling and spelling[folded] != key:
            raise ValueError("PowerShell object properties cannot differ only by case.")
        spelling[folded] = key
        result[key] = value
    return result


def normalize_json_tokens(text: str) -> str:
    normalized: list[str] = []
    for match in JSON_TOKENS.finditer(text):
        lexeme = match[0]
        if match.lastgroup == "special" and lexeme.startswith(("+", "-NaN")):
            raise ValueError("ConvertFrom-Json refuses this signed constant.")
        if match.lastgroup == "number":
            if lexeme.startswith("+"):
                raise ValueError("ConvertFrom-Json refuses explicit positive numeric signs.")
            if re.fullmatch(r"-?0\d+", lexeme):
                lexeme = str(int(lexeme, 8))
        if lexeme == "undefined" and re.match(r"\s*:", text[match.end() :]) is None:
            lexeme = "null"
        normalized.append(lexeme)
    return "".join(normalized)


def loads(text: str) -> object:
    return json5.loads(normalize_json_tokens(text), object_pairs_hook=json_object)
