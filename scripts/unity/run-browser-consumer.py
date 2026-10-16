# /// script
# requires-python = ">=3.11"
# dependencies = ["playwright==1.56.0"]
# ///
"""Build and execute retained-map checks in a fresh WebGL consumer."""

from __future__ import annotations

import argparse
import functools
import hashlib
import http.server
import json
import os
import re
import shutil
import signal
import subprocess
import threading
import uuid
from pathlib import Path
from urllib.parse import urlencode


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def inventory(root: Path) -> dict[str, str]:
    return {
        path.relative_to(root).as_posix(): digest(path)
        for path in sorted(root.rglob("*"))
        if path.is_file()
    }


def require_inventory(root: Path, expected: dict[str, str]) -> None:
    if not expected or inventory(root) != expected:
        raise RuntimeError(f"Player inventory changed: {root}")


def cases() -> list[tuple[str, str]]:
    result = [
        (mode, shape)
        for shape in ("null", "empty", "populated")
        for mode in (
            "typed-hash",
            "object-hash",
            "typed-equality",
            "object-equality",
            "serialize-read",
        )
    ]
    result.append(("merge", "populated"))
    result.append(("safety", "populated"))
    result.extend(
        (f"presence-{mode}", "presence")
        for mode in (
            "typed-hash",
            "object-hash",
            "typed-equality",
            "object-equality",
            "serialize-read",
            "foreign",
        )
    )
    return result


def validate_result(actual: str, expected: str, errors: list[str]) -> None:
    if errors or actual != expected:
        raise RuntimeError(f"Browser result mismatch: {actual!r}; errors={errors!r}")


def write_json(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def generated_contract(source: str, expected_tags: list[int]) -> str:
    tags = [int(value) for value in re.findall(r"\[ProtoMember\((\d+)\)\]", source)]
    if source.count("[ProtoContract]") != 1 or tags != expected_tags:
        raise ValueError(
            "Consumer contract shape changed; review the generated profile"
        )
    namespace = "global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto."
    source, declarations = re.subn(
        r"\b(?:partial\s+)?(class|struct)\s+", r"partial \1 ", source
    )
    if declarations != 1:
        raise ValueError("Generated profile requires one reviewed contract declaration")
    source = source.replace(
        "[ProtoContract]", f"[ProtoContract, {namespace}WProtoContract]"
    )
    source = re.sub(
        r"\[ProtoMember\((\d+)\)\]",
        lambda match: f"[ProtoMember({match[1]}), {namespace}WProtoMember({match[1]})]",
        source,
    )
    return re.sub(
        r"^\s*#pragma warning (?:disable|restore) WPROTO030[^\n]*\n",
        "",
        source,
        flags=re.MULTILINE,
    )


def run_editor(
    editor: Path, project: Path, method: str, log: Path, output: Path
) -> None:
    environment = dict(os.environ, CONSUMER_OUTPUT=str(output))
    command = [
        str(editor),
        "-batchmode",
        "-nographics",
        "-quit",
        "-buildTarget",
        "WebGL",
        "-projectPath",
        str(project),
        "-executeMethod",
        method,
        "-logFile",
        str(log),
    ]
    with subprocess.Popen(
        command, env=environment, start_new_session=os.name != "nt"
    ) as process:
        interrupted = False
        try:
            code = process.wait(timeout=3600)
        except BaseException:
            interrupted = True
            if process.poll() is None:
                if os.name == "nt":
                    subprocess.run(
                        ["taskkill", "/PID", str(process.pid), "/T", "/F"], check=False
                    )
                else:
                    os.killpg(process.pid, signal.SIGKILL)
                process.wait(timeout=30)
            raise
        finally:
            write_json(
                log.with_suffix(".process.json"),
                {
                    "pid": process.pid,
                    "exitCode": process.poll(),
                    "command": command,
                    "interrupted": interrupted,
                },
            )
    if code != 0:
        raise RuntimeError(f"Unity exited {code}; retained log: {log}")


def prepare(repository: Path, root: Path, editor: Path, version: str) -> dict[str, str]:
    if root.exists():
        raise ValueError("Artifacts directory must be fresh")
    root.mkdir(parents=True)
    project = root / "project"
    template = repository / "Tests/Acceptance~/NullableMapConsumer"
    for folder in ("Assets", "Packages", "ProjectSettings"):
        shutil.copytree(template / folder, project / folder)
    declarations = {
        "Doc": [1, 7, 11, 17, 23],
        "OwnedMessage": [1, 3, 5],
        "PresenceValue": [1],
        "PresenceDoc": list(range(1, 25)),
    }
    for name, tags in declarations.items():
        path = project / "Assets/Consumer" / f"{name}.cs"
        path.write_text(
            generated_contract(path.read_text(encoding="utf-8"), tags), encoding="utf-8"
        )
    package = project / "Packages/com.wallstop-studios.unity-helpers"
    package.mkdir()
    for folder in ("Runtime", "Editor"):
        shutil.copytree(repository / folder, package / folder)
    for name in (
        "package.json",
        "link.xml",
        "link.xml.meta",
        "Runtime.meta",
        "Editor.meta",
    ):
        shutil.copy2(repository / name, package / name)
    modules = json.loads(
        (repository / ".github/unity-test-project-modules.json").read_text()
    )["modules"]
    modules["com.wallstop-studios.unity-helpers"] = (
        "file:com.wallstop-studios.unity-helpers"
    )
    write_json(project / "Packages/manifest.json", {"dependencies": modules})
    (project / "ProjectSettings/ProjectVersion.txt").write_text(
        f"m_EditorVersion: {version}\n"
    )
    goldens = project / "Assets/Consumer/Resources/BrowserGoldens"
    goldens.mkdir(parents=True)
    for golden in (template / "Goldens~").glob("*.base64"):
        shutil.copy2(golden, goldens / (golden.stem + ".txt"))
    binding = project / "Assets/Consumer/ConsumerBinding.cs"
    source = {f"package/{name}": value for name, value in inventory(package).items()}
    source.update(
        {
            f"consumer/{name}": value
            for name, value in inventory(project / "Assets").items()
            if name != "Consumer/ConsumerBinding.cs"
        }
    )
    source["tools/run-browser-consumer.py"] = digest(Path(__file__))
    write_json(root / "source-manifest.json", source)
    commit = subprocess.check_output(
        ["git", "-C", str(repository), "rev-parse", "HEAD"], text=True
    ).strip()
    identity = {
        "commit": commit,
        "source": digest(root / "source-manifest.json"),
        "token": uuid.uuid4().hex,
        "unity": version,
    }
    text = binding.read_text(encoding="utf-8")
    for marker, value in (
        ("COMMIT", commit),
        ("SOURCE", identity["source"]),
        ("TOKEN", identity["token"]),
        ("UNITY", version),
    ):
        text = text.replace(f"__{marker}__", value)
    binding.write_text(text, encoding="utf-8")
    binding_hash = digest(binding)
    shutil.copy2(binding, root / "generated-ConsumerBinding.cs")
    write_json(root / "binding-manifest.json", {"ConsumerBinding.cs": binding_hash})
    write_json(root / "identity.json", identity)
    run_editor(
        editor,
        project,
        "NestedOnlyConsumer.Editor.BrowserConsumerBuild.Prepare",
        root / "prepare.log",
        root / "player",
    )
    run_editor(
        editor,
        project,
        "NestedOnlyConsumer.Editor.BrowserConsumerBuild.Build",
        root / "build.log",
        root / "player",
    )
    if digest(binding) != binding_hash:
        raise RuntimeError("Generated consumer binding changed during compilation")
    for name, expected in source.items():
        base, relative = name.split("/", 1)
        if base == "tools":
            path = Path(__file__)
        else:
            path = (package if base == "package" else project / "Assets") / relative
        if digest(path) != expected:
            raise RuntimeError(f"Source changed during build: {name}")
    settings = dict(
        line.split("=", 1)
        for line in (root / "player/settings.txt").read_text().splitlines()
    )
    required = {
        "unity": version,
        "target": "WebGL",
        "backend": "IL2CPP",
        "stripping": "High",
        "compiler": "Release",
        "defines": "SINGLE_THREADED",
        "development": "False",
        "result": "Succeeded",
        "errors": "0",
    }
    if any(settings.get(key) != value for key, value in required.items()):
        raise RuntimeError(f"Build settings mismatch: {settings}")
    player = inventory(root / "player")
    if not any(name.endswith(".wasm") for name in player) or "index.html" not in player:
        raise RuntimeError("WebGL player is incomplete")
    write_json(root / "player-manifest.json", player)
    return identity


def new_browser_run(root: Path) -> Path:
    run = root / "browser-runs" / uuid.uuid4().hex
    run.mkdir(parents=True)
    record_browser_status(root, run, "running")
    return run


def record_browser_status(
    root: Path, run: Path, status: str, error: str | None = None
) -> None:
    record = {"run": run.relative_to(root).as_posix(), "status": status, "error": error}
    write_json(run / "status.json", record)
    write_json(root / "latest-run.json", record)


def run_browser(root: Path, executable: str | None = None) -> None:
    run = new_browser_run(root)
    server = None
    worker = None
    receipts: list[dict[str, object]] = []
    try:
        from importlib.metadata import version

        from playwright.sync_api import sync_playwright

        identity = json.loads((root / "identity.json").read_text())
        player = json.loads((root / "player-manifest.json").read_text())
        require_inventory(root / "player", player)
        handler = functools.partial(
            http.server.SimpleHTTPRequestHandler, directory=str(root / "player")
        )
        server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        with sync_playwright() as playwright:
            browser = playwright.chromium.launch(
                executable_path=executable,
                headless=True,
                args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader"],
            )
            write_json(
                run / "executor.json",
                {
                    "runnerSHA256": digest(Path(__file__)),
                    "playwright": version("playwright"),
                    "browser": browser.version,
                },
            )
            try:
                selections = [(mode, shape, "none") for mode, shape in cases()]
                selections.extend(
                    [
                        ("typed-hash", "populated", "corrupt-golden"),
                        ("serialize-read", "populated", "wrong-source"),
                    ]
                )
                for mode, shape, control in selections:
                    require_inventory(root / "player", player)
                    context = browser.new_context()
                    page = context.new_page()
                    errors: list[str] = []
                    page.on(
                        "pageerror",
                        lambda error, captured=errors: captured.append(str(error)),
                    )
                    page.on(
                        "console",
                        lambda message, captured=errors: (
                            captured.append(message.text)
                            if message.type == "error"
                            else None
                        ),
                    )
                    query = dict(identity, mode=mode, shape=shape, control=control)
                    if control == "wrong-source":
                        query["source"] = "deliberately-wrong"
                    record: dict[str, object] = {
                        "mode": mode,
                        "shape": shape,
                        "control": control,
                        "playerManifestSHA256": digest(root / "player-manifest.json"),
                        "identity": identity,
                    }
                    try:
                        page.goto(
                            f"http://127.0.0.1:{server.server_port}/index.html?{urlencode(query)}"
                        )
                        page.wait_for_function(
                            "typeof window.browserConsumerResult === 'string'",
                            timeout=180000,
                        )
                        result = page.evaluate("window.browserConsumerResult")
                        record.update(result=result, errors=errors)
                        if control == "none":
                            expected = f"Passed|{mode}|{shape}|{identity['unity']}|IL2CPP=True|SINGLE_THREADED=True|commit={identity['commit']}|source={identity['source']}|run={identity['token']}"
                            validate_result(result, expected, errors)
                        elif (
                            errors
                            or not result.startswith("Failed|")
                            or (
                                control == "wrong-source"
                                and "Source binding changed" not in result
                            )
                            or (
                                control == "corrupt-golden"
                                and "First typed hash changed" not in result
                            )
                        ):
                            raise RuntimeError(
                                f"Negative control did not fail correctly: {record}"
                            )
                        receipts.append(record)
                    except BaseException as failure:
                        record.update(error=str(failure), errors=errors)
                        raise
                    finally:
                        write_json(run / f"{mode}-{shape}-{control}.json", record)
                        context.close()
                    require_inventory(root / "player", player)
            finally:
                browser.close()
        if len(receipts) != len(cases()) + 2:
            raise RuntimeError("Incomplete browser matrix")
        write_json(
            run / "results.json",
            {"passed": len(cases()), "negativeControls": 2, "cases": receipts},
        )
        record_browser_status(root, run, "passed")
    except BaseException as failure:
        record_browser_status(root, run, "failed", str(failure))
        raise
    finally:
        if server is not None:
            if worker is not None and worker.is_alive():
                server.shutdown()
                worker.join(timeout=10)
            server.server_close()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--repository", type=Path, default=Path(__file__).resolve().parents[2]
    )
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--editor", type=Path)
    parser.add_argument("--unity")
    parser.add_argument("--browser-executable")
    parser.add_argument("--browser-only", action="store_true")
    args = parser.parse_args()
    root = args.artifacts.resolve()
    if not args.browser_only:
        if not args.editor or not args.editor.is_file() or not args.unity:
            parser.error("Build requires --editor and --unity")
        prepare(args.repository.resolve(), root, args.editor.resolve(), args.unity)
    run_browser(root, args.browser_executable)


if __name__ == "__main__":
    main()
