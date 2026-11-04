"""Run one PowerShell worker on an owned Windows desktop without compiling a launcher."""

from __future__ import annotations

import argparse
import base64
import ctypes
import re
import subprocess
import sys
import uuid
from ctypes import wintypes


class StartupInfo(ctypes.Structure):
    _fields_ = (
        ("size", wintypes.DWORD),
        ("reserved", wintypes.LPWSTR),
        ("desktop", wintypes.LPWSTR),
        ("title", wintypes.LPWSTR),
        ("x", wintypes.DWORD),
        ("y", wintypes.DWORD),
        ("width", wintypes.DWORD),
        ("height", wintypes.DWORD),
        ("x_chars", wintypes.DWORD),
        ("y_chars", wintypes.DWORD),
        ("fill", wintypes.DWORD),
        ("flags", wintypes.DWORD),
        ("show", wintypes.WORD),
        ("reserved_size", wintypes.WORD),
        ("reserved_bytes", ctypes.c_void_p),
        ("stdin", wintypes.HANDLE),
        ("stdout", wintypes.HANDLE),
        ("stderr", wintypes.HANDLE),
    )


class ProcessInfo(ctypes.Structure):
    _fields_ = (
        ("process", wintypes.HANDLE),
        ("thread", wintypes.HANDLE),
        ("process_id", wintypes.DWORD),
        ("thread_id", wintypes.DWORD),
    )


def validate_payload(encoded: str) -> None:
    if not encoded or len(encoded) > 32768 or re.fullmatch(r"[A-Za-z0-9+/]+=*", encoded) is None:
        raise ValueError("Worker payload must be bounded base64")
    try:
        base64.b64decode(encoded, validate=True).decode("utf-16-le")
    except (ValueError, UnicodeError) as error:
        raise ValueError("Worker payload must encode UTF-16 PowerShell") from error


if sys.platform == "win32":

    def run(shell: str, encoded: str) -> int:
        validate_payload(encoded)
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        user = ctypes.WinDLL("user32", use_last_error=True)
        user.CreateDesktopW.argtypes = (
            wintypes.LPCWSTR,
            ctypes.c_void_p,
            ctypes.c_void_p,
            wintypes.DWORD,
            wintypes.DWORD,
            ctypes.c_void_p,
        )
        user.CreateDesktopW.restype = wintypes.HANDLE
        user.CloseDesktop.argtypes = (wintypes.HANDLE,)
        user.CloseDesktop.restype = wintypes.BOOL
        kernel.CreateProcessW.argtypes = (
            wintypes.LPCWSTR,
            wintypes.LPWSTR,
            ctypes.c_void_p,
            ctypes.c_void_p,
            wintypes.BOOL,
            wintypes.DWORD,
            ctypes.c_void_p,
            wintypes.LPCWSTR,
            ctypes.POINTER(StartupInfo),
            ctypes.POINTER(ProcessInfo),
        )
        kernel.CreateProcessW.restype = wintypes.BOOL
        kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
        kernel.CloseHandle.restype = wintypes.BOOL
        kernel.GetStdHandle.argtypes = (wintypes.DWORD,)
        kernel.GetStdHandle.restype = wintypes.HANDLE
        kernel.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
        kernel.WaitForSingleObject.restype = wintypes.DWORD
        kernel.GetExitCodeProcess.argtypes = (
            wintypes.HANDLE,
            ctypes.POINTER(wintypes.DWORD),
        )
        kernel.GetExitCodeProcess.restype = wintypes.BOOL
        kernel.TerminateProcess.argtypes = (wintypes.HANDLE, wintypes.UINT)
        kernel.TerminateProcess.restype = wintypes.BOOL
        name = "uh-mode-" + uuid.uuid4().hex
        desktop: int | None = user.CreateDesktopW(name, None, None, 0, 0x01FF, None)
        if not desktop:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            startup = StartupInfo()
            startup.size = ctypes.sizeof(StartupInfo)
            startup.desktop = name
            startup.flags = 0x0100
            startup.stdin = kernel.GetStdHandle(-10)
            startup.stdout = kernel.GetStdHandle(-11)
            startup.stderr = kernel.GetStdHandle(-12)
            command = ctypes.create_unicode_buffer(
                subprocess.list2cmdline(
                    [
                        shell,
                        "-NoProfile",
                        "-NonInteractive",
                        "-OutputFormat",
                        "Text",
                        "-EncodedCommand",
                        encoded,
                    ]
                )
            )
            process = ProcessInfo()
            if not kernel.CreateProcessW(
                shell,
                command,
                None,
                None,
                True,
                0x08000000,
                None,
                None,
                ctypes.byref(startup),
                ctypes.byref(process),
            ):
                raise ctypes.WinError(ctypes.get_last_error())
            try:
                if kernel.WaitForSingleObject(process.process, 0xFFFFFFFF) != 0:
                    error = ctypes.WinError(ctypes.get_last_error())
                    kernel.TerminateProcess(process.process, 1)
                    kernel.WaitForSingleObject(process.process, 0xFFFFFFFF)
                    raise error
                code = wintypes.DWORD()
                if not kernel.GetExitCodeProcess(process.process, ctypes.byref(code)):
                    raise ctypes.WinError(ctypes.get_last_error())
                return ctypes.c_int32(code.value).value
            finally:
                kernel.CloseHandle(process.thread)
                kernel.CloseHandle(process.process)
        finally:
            if not user.CloseDesktop(desktop):
                raise ctypes.WinError(ctypes.get_last_error())

else:

    def run(_shell: str, _encoded: str) -> int:
        raise OSError("Private Unity worker desktops require Windows")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--shell", required=True)
    parser.add_argument("--encoded-worker", required=True)
    arguments = parser.parse_args()
    try:
        return run(arguments.shell, arguments.encoded_worker)
    except (OSError, ValueError) as error:
        print(f"Cannot launch private Unity worker: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
