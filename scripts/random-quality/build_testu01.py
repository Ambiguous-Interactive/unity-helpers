"""Build the TestU01 stream driver and prove its negative control still fails."""

from __future__ import annotations

import os
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from tempfile import gettempdir

from testu01_report import check_control

SOURCE = Path(__file__).resolve().parent
ROOT = SOURCE.parents[1]
DEFAULT_URL = "http://www.iro.umontreal.ca/~simardr/testu01/TestU01.zip"
HOST_PROJECT = "WallstopStudios.UnityHelpers.RandomQuality"


@dataclass(frozen=True)
class Settings:
    build: Path
    reports: Path
    download: str
    generator: str
    battery: str
    size: str

    @property
    def driver(self) -> Path:
        return self.build / "wallstop-testu01"

    @property
    def host_directory(self) -> Path:
        return ROOT / "Generator~" / HOST_PROJECT

    @property
    def host(self) -> Path:
        return self.host_directory / "bin/Release/net9.0" / HOST_PROJECT


def settings_from_environment() -> Settings:
    temporary = Path(os.environ.get("TMPDIR") or gettempdir())
    build = Path(os.environ.get("TESTU01_BUILD_ROOT") or temporary / "testu01-build")
    return Settings(
        build=build,
        reports=Path(os.environ.get("TESTU01_CONTROL_REPORT_DIRECTORY") or build / "self-test"),
        download=os.environ.get("TESTU01_URL") or DEFAULT_URL,
        generator=os.environ.get("TESTU01_CONTROL_GENERATOR") or "XorShiftRandom",
        battery=os.environ.get("TESTU01_CONTROL_BATTERY") or "SmallCrush",
        size=os.environ.get("TESTU01_CONTROL_BYTES") or "2000000000",
    )


def command_output(command: list[str]) -> str:
    result = subprocess.run(  # noqa: S603 - fixed native build tools and argument lists.
        command, check=True, stdout=subprocess.PIPE, encoding="utf-8"
    )
    return result.stdout.rstrip("\n")


def run_command(command: list[str], cwd: Path | None = None, log: Path | None = None) -> None:
    if log is None:
        subprocess.run(command, cwd=cwd, check=True)  # noqa: S603 - fixed native tools and paths.
        return
    with log.open("wb") as stream:
        subprocess.run(  # noqa: S603 - fixed native tools and paths.
            command, cwd=cwd, check=True, stdout=stream, stderr=subprocess.STDOUT
        )


def install_testu01(settings: Settings, install: Path) -> bool:
    build = settings.build
    archive = build / "TestU01.zip"
    print(f"[testu01] Fetching TestU01 into {build}", flush=True)
    run_command(["curl", "-sSL", "-o", str(archive), settings.download])
    run_command(["unzip", "-q", "-o", str(archive), "-d", str(build)])
    source_tree = next(
        (
            path
            for path in sorted(build.iterdir())
            if path.name.startswith("TestU01-") and path.is_dir() and not path.is_symlink()
        ),
        None,
    )
    if source_tree is None:
        print("[testu01] The archive did not contain a TestU01-* directory. Refusing to guess.")
        return False
    gcc_version = command_output(["gcc", "-dumpversion"])
    print(f"[testu01] Configuring and compiling (gcc {gcc_version})", flush=True)
    run_command(
        ["./configure", f"--prefix={install.resolve()}"], source_tree, build / "configure.log"
    )
    jobs = command_output(["nproc"])
    run_command(["make", f"-j{jobs}"], source_tree, build / "make.log")
    run_command(["make", "install"], source_tree, build / "install.log")
    return True


def build_driver(settings: Settings) -> bool:
    if os.access(settings.driver, os.X_OK):
        return True
    install = settings.build / "install"
    include = install / "include"
    if not include.is_dir() and not install_testu01(settings, install):
        return False
    print("[testu01] Linking the stream driver", flush=True)
    # Static archives keep cached drivers independent of the host library search path.
    library = install / "lib"
    run_command(
        [
            "gcc",
            "-O2",
            f"-I{include}",
            "-o",
            str(settings.driver),
            str(SOURCE / "testu01~/testu01-driver.c"),
            str(library / "libtestu01.a"),
            str(library / "libprobdist.a"),
            str(library / "libmylib.a"),
            "-lm",
        ]
    )
    return True


def shell_status(returncode: int) -> int:
    return returncode if returncode >= 0 else 128 - returncode


def run_control(settings: Settings) -> int:
    reports = settings.reports
    reports.mkdir(parents=True, exist_ok=True)
    report = reports / "report.txt"
    errors = reports / "driver.stderr.log"
    print(
        f"[testu01] Self-test: {settings.generator} must fail {settings.battery}, "
        "or this binary proves nothing",
        flush=True,
    )
    producer = [
        str(settings.host),
        "--generator",
        settings.generator,
        "--width",
        "32",
        "--bytes",
        settings.size,
    ]
    with (
        (reports / "host.stderr.log").open("wb") as producer_errors,
        report.open("wb") as output,
        errors.open("wb") as driver_errors,
        subprocess.Popen(  # noqa: S603 - invoke the owned raw byte stream host.
            producer, stdout=subprocess.PIPE, stderr=producer_errors
        ) as host,
    ):
        stream = host.stdout
        if stream is None:
            raise RuntimeError("The stream host did not expose its stdout pipe.")
        try:
            driver = subprocess.run(  # noqa: S603 - invoke the built driver with a battery name.
                [str(settings.driver.resolve()), settings.battery],
                stdin=stream,
                stdout=output,
                stderr=driver_errors,
                check=False,
            )
        finally:
            stream.close()
    # Finishing the bounded battery can SIGPIPE the host. The driver's exit controls the verdict.
    if driver.returncode:
        print(
            f"[testu01] SELF-TEST INCONCLUSIVE: the driver exited {shell_status(driver.returncode)}.",
            flush=True,
        )
        sys.stdout.buffer.write(errors.read_bytes())
        return 1
    with errors.open("rb") as stream:
        exhausted = any(b"input stream exhausted" in line for line in stream)
    if exhausted:
        print(
            f"[testu01] SELF-TEST INCONCLUSIVE: {settings.battery} wanted more than {settings.size} bytes."
        )
        print(
            "[testu01] Raise TESTU01_CONTROL_BYTES. Reporting this as a harness fault, not a result."
        )
        return 1
    return check_control(report)


def main() -> int:
    try:
        settings = settings_from_environment()
        settings.build.mkdir(parents=True, exist_ok=True)
        if not build_driver(settings):
            return 1
        if not os.access(settings.host, os.X_OK):
            print("[testu01] Building the deterministic stream host", flush=True)
            run_command(
                [
                    "dotnet",
                    "build",
                    str(settings.host_directory),
                    "-c",
                    "Release",
                    "--nologo",
                    "-v",
                    "quiet",
                ]
            )
        result = run_control(settings)
        if result == 0:
            print(f"[testu01] driver: {settings.driver}")
        return result
    except subprocess.CalledProcessError as error:
        return shell_status(error.returncode)
    except FileNotFoundError as error:
        print(f"[testu01] {error}", file=sys.stderr)
        return 127
    except OSError as error:
        print(f"[testu01] {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
