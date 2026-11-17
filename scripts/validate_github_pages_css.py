"""Validate the repository's fixed GitHub Pages layout and advisory CSS heuristics."""

from __future__ import annotations

import os
import re
import stat
import sys
from pathlib import Path

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
RED = "\033[0;31m"
GREEN = "\033[0;32m"
YELLOW = "\033[1;33m"
BLUE = "\033[0;34m"
RESET = "\033[0m"


class Report:
    def __init__(self, content: str) -> None:
        self.content = content
        self.lines = content.split("\n")
        self.warnings = 0

    def has(self, pattern: str, text: str | None = None) -> bool:
        return any(
            re.search(pattern, line) is not None
            for line in (self.content if text is None else text).split("\n")
        )

    def good(self, message: str) -> None:
        print(f"  {GREEN}✓{RESET} {message}")

    def warn(self, message: str) -> None:
        self.warnings += 1
        print(f"  {YELLOW}⚠{RESET} {message}")

    def balanced(self, opening: str, closing: str, label: str, failure: str) -> bool:
        left, right = self.content.count(opening), self.content.count(closing)
        counts = f"{left} open, {right} close"
        if left != right:
            print(f"  {RED}✗{RESET} {failure}: {counts}")
            return False
        self.good(f"Balanced {label}: {counts}")
        return True

    def blocks(self, selector: str) -> str:
        selected: list[str] = []
        inside = False
        depth = 0
        for line in self.lines:
            if not inside and self.has(selector, line):
                inside = True
                depth = 0
            if inside:
                selected.append(line)
                depth += line.count("{") - line.count("}")
                if depth <= 0:
                    inside = False
        return "\n".join(selected)

    def media(self, pattern: str) -> bool:
        for line in self.lines:
            for part in line.split("{"):
                position = part.find("@media")
                if position >= 0 and re.search(pattern, part[position + 6 :]) is not None:
                    return True
        return False

    def missing_semicolon(self, line: str) -> bool:
        if self.has(r"^\s*/\*", line):
            return False
        for block in line.split("}")[:-1]:
            tail = re.split(r"[;{]", block)[-1]
            if re.search(r"[a-z-]:.", tail) is not None:
                return True
        return False

    def required(
        self, description: str, pattern: str, selector: str | None = None, *, media: bool = False
    ) -> bool:
        blocks = self.blocks(selector) if selector is not None else self.content
        found = self.media(pattern) if media else self.has(pattern, blocks)
        if found:
            self.good(description)
            return True
        print(f"  {RED}✗{RESET} MISSING: {description}")
        if selector is not None:
            if not blocks:
                print("      (selector not found)")
            print("      (property not found)")
        return False

    def validate(self, size: int) -> int:
        section("1. CSS Syntax Validation")
        if not self.balanced("{", "}", "braces", "Unbalanced braces"):
            return 1
        if not self.balanced("/*", "*/", "comments", "Unclosed comments"):
            return 1
        if self.has(r"\{[^\S\n]*\}"):
            self.warn("Empty rule blocks detected (may be intentional)")
        else:
            self.good("No empty rule blocks")
        if any(self.missing_semicolon(line) for line in self.lines):
            self.warn("Possible missing semicolons detected")
        else:
            self.good("No obvious missing semicolons")
        if self.has(
            r"#[0-9a-fA-F]{1,2}[^0-9a-fA-F;,\\s)}]|#[0-9a-fA-F]{5}[^0-9a-fA-F;,\\s)}]|#[0-9a-fA-F]{7}[^0-9a-fA-F;,\\s)}]"
        ):
            self.warn("Possible invalid hex color values")
        else:
            self.good("Hex color values appear valid")
        print()
        section("2. Required Layout Rules")
        print("\n  Layout Structure:")
        for description, selector, pattern in [
            (".wrapper max-width: 1400px", r"^\.wrapper\s*\{", r"max-width:\s*1400px"),
            ("header float: none (banner layout)", r"^header\s*\{", r"float:\s*none"),
            ("section float: none", r"^section\s*\{", r"float:\s*none"),
            ("section max-width: 1200px", r"^section\s*\{", r"max-width:\s*1200px"),
        ]:
            if not self.required(description, pattern, selector):
                return 1
        print("\n  Responsive Breakpoints:")
        for direction, width in [("max", 768), ("max", 480), ("min", 1400), ("min", 2000)]:
            if not self.required(
                f"@media {direction}-width: {width}px breakpoint",
                rf"{direction}-width:[^\S\n]*{width}px",
                media=True,
            ):
                return 1
        print("\n  Code Block Styling:")
        if not self.required("pre overflow-x: auto", r"overflow-x:\s*auto", r"^pre\s*\{"):
            return 1
        print("\n  Accessibility Rules:")
        if not self.required(
            "@media prefers-reduced-motion", r"prefers-reduced-motion", media=True
        ):
            return 1
        if not self.required(
            "Focus styles (:focus or :focus-visible)", r":focus-visible|:focus[^-\n]"
        ):
            return 1
        print()
        section("3. Vendor Prefix Validation")
        if self.has(r"display:\s*flex"):
            webkit = self.has(r"display:\s*-webkit-flex")
            microsoft = self.has(r"display:\s*-ms-flexbox")
            if webkit and microsoft:
                self.good("Flex display has vendor prefixes (-webkit-flex, -ms-flexbox)")
            elif webkit:
                self.warn("Flex display missing -ms-flexbox prefix")
            elif microsoft:
                self.warn("Flex display missing -webkit-flex prefix")
            else:
                self.warn("Flex display missing vendor prefixes")
        else:
            self.good("No flex display rules to check")
        if self.has(r"box-sizing:\s*border-box"):
            if self.has(r"-webkit-box-sizing:\s*border-box") and self.has(
                r"-moz-box-sizing:\s*border-box"
            ):
                self.good("box-sizing has vendor prefixes (-webkit, -moz)")
            else:
                self.warn("box-sizing may need vendor prefixes for older browsers")
        else:
            self.good("No box-sizing rules to check")
        if self.has(r"[^-]transition:"):
            if self.has(r"-webkit-transition:"):
                self.good("Transitions have -webkit- prefix")
            else:
                self.warn("Transitions may need -webkit- prefix for Safari")
        else:
            self.good("No transition rules to check")
        print()
        section("4. Additional Quality Checks")
        if self.has(r"--[a-z]"):
            variables = len(set(re.findall(r"--[a-z][a-z0-9-]*", self.content)))
            self.good(f"CSS custom properties defined: {variables} unique variables")
        else:
            self.warn("No CSS custom properties found")
        for pattern, success, warning in [
            (
                r"\[data-theme=",
                "Theme switching support (data-theme attribute)",
                "No data-theme attribute selectors found",
            ),
            (r"@media\s*print", "Print stylesheet included", "No print styles found"),
        ]:
            if self.has(pattern):
                self.good(success)
            else:
                self.warn(warning)
        kilobytes = size // 1024
        if kilobytes < 100:
            self.good(f"File size: {kilobytes} KB (under 100KB limit)")
        else:
            self.warn(f"File size: {kilobytes} KB (consider minification)")
        rules = sum("}" in line for line in self.lines)
        print(f"  {BLUE}\u2139{RESET} Approximate rule count: {rules}\n")
        print(
            "========================================\nValidation Summary\n========================================"
        )
        print(f"{GREEN}✓ All required checks passed{RESET}")
        if self.warnings:
            print(f"{YELLOW}⚠ {self.warnings} warning(s){RESET}")
        print(f"\n{GREEN}VALIDATION PASSED{RESET}")
        return 0


def section(title: str) -> None:
    print(
        f"----------------------------------------\n{title}\n----------------------------------------"
    )


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    arguments = sys.argv[1:] if arguments is None else arguments
    display = arguments[0] if arguments and arguments[0] else str(ROOT / "assets/css/theme.css")
    path = Path(display)
    print(
        "========================================\nGitHub Pages CSS Validator\n========================================\n"
    )
    print(f"CSS File: {BLUE}{display}{RESET}\n")
    try:
        if display.endswith(("/", os.sep)) or not stat.S_ISREG(path.stat().st_mode):
            raise ValueError(f"CSS file not found: {display}")
        payload = path.read_bytes()
        content = payload.decode("utf-8")
    except (FileNotFoundError, NotADirectoryError):
        print(f"{RED}ERROR: CSS file not found: {display}{RESET}")
        return 1
    except (OSError, ValueError) as error:
        print(f"{RED}ERROR: {error}{RESET}")
        return 1
    return Report(content).validate(len(payload))


if __name__ == "__main__":
    raise SystemExit(main())
