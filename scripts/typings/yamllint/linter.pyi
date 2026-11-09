from collections.abc import Iterable

from yamllint.config import YamlLintConfig

class LintProblem:
    line: int
    column: int
    level: str
    desc: str
    rule: str | None

def run(
    input: str,  # noqa: A002 - external API parameter name.
    conf: YamlLintConfig,
    filepath: str | None = None,
) -> Iterable[LintProblem]: ...
