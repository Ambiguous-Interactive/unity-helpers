"""Run one PowerShell worker on a private Windows desktop and own its process tree."""

from __future__ import annotations

import argparse
import base64
import ctypes
import os
import re
import subprocess
import sys
import time
import uuid
from ctypes import wintypes
from pathlib import Path
from typing import TYPE_CHECKING, Protocol

if TYPE_CHECKING:
    from typing import BinaryIO


class ModeProcess(Protocol):
    pid: int
    returncode: int | None

    def poll(self) -> int | None: ...

    def wait(self, timeout: float | None = None) -> int: ...


class WorkerJob(Protocol):
    def launch(
        self, shell: str, encoded: str, environment: dict[str, str], stream: BinaryIO
    ) -> ModeProcess: ...

    def close(self) -> None: ...


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


class StartupInfoEx(ctypes.Structure):
    _fields_ = (
        ("startup", StartupInfo),
        ("attributes", ctypes.c_void_p),
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
    import math
    import msvcrt

    class BasicJobLimits(ctypes.Structure):
        _fields_ = (
            ("process_time_limit", ctypes.c_longlong),
            ("job_time_limit", ctypes.c_longlong),
            ("limit_flags", wintypes.DWORD),
            ("minimum_working_set_size", ctypes.c_size_t),
            ("maximum_working_set_size", ctypes.c_size_t),
            ("active_process_limit", wintypes.DWORD),
            ("affinity", ctypes.c_size_t),
            ("priority_class", wintypes.DWORD),
            ("scheduling_class", wintypes.DWORD),
        )

    class ExtendedJobLimits(ctypes.Structure):
        _fields_ = (
            ("basic_limits", BasicJobLimits),
            ("io_counters", ctypes.c_ulonglong * 6),
            ("process_memory_limit", ctypes.c_size_t),
            ("job_memory_limit", ctypes.c_size_t),
            ("peak_process_memory_used", ctypes.c_size_t),
            ("peak_job_memory_used", ctypes.c_size_t),
        )

    class BasicJobAccounting(ctypes.Structure):
        _fields_ = (
            ("total_user_time", ctypes.c_longlong),
            ("total_kernel_time", ctypes.c_longlong),
            ("period_user_time", ctypes.c_longlong),
            ("period_kernel_time", ctypes.c_longlong),
            ("total_page_fault_count", wintypes.DWORD),
            ("total_processes", wintypes.DWORD),
            ("active_processes", wintypes.DWORD),
            ("total_terminated_processes", wintypes.DWORD),
        )

    class JobProcessIds(ctypes.Structure):
        _fields_ = (
            ("assigned", wintypes.DWORD),
            ("listed", wintypes.DWORD),
            ("pids", ctypes.c_size_t * 16),
        )

    def worker_job(kernel: ctypes.WinDLL) -> int:
        kernel.CreateJobObjectW.argtypes = (ctypes.c_void_p, wintypes.LPCWSTR)
        kernel.CreateJobObjectW.restype = wintypes.HANDLE
        kernel.SetInformationJobObject.argtypes = (
            wintypes.HANDLE,
            ctypes.c_int,
            ctypes.POINTER(ExtendedJobLimits),
            wintypes.DWORD,
        )
        kernel.SetInformationJobObject.restype = wintypes.BOOL
        # The owner holds the final handle, so Windows reaps the job if the
        # owner exits before explicit cleanup.
        job: int | None = kernel.CreateJobObjectW(None, None)
        if not job:
            raise ctypes.WinError(ctypes.get_last_error())
        limits = ExtendedJobLimits()
        limits.basic_limits.limit_flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        extended_limit_information = 9
        if not kernel.SetInformationJobObject(
            job, extended_limit_information, ctypes.byref(limits), ctypes.sizeof(limits)
        ):
            error = ctypes.get_last_error()
            kernel.CloseHandle(job)
            raise ctypes.WinError(error)
        return job

    def reap_job(kernel: ctypes.WinDLL, job: int) -> None:
        kernel.QueryInformationJobObject.argtypes = (
            wintypes.HANDLE,
            ctypes.c_int,
            ctypes.c_void_p,
            wintypes.DWORD,
            ctypes.c_void_p,
        )
        kernel.QueryInformationJobObject.restype = wintypes.BOOL
        kernel.TerminateJobObject.argtypes = (wintypes.HANDLE, wintypes.UINT)
        kernel.TerminateJobObject.restype = wintypes.BOOL
        accounting = BasicJobAccounting()
        basic_accounting_information = 1
        deadline = time.monotonic() + 15
        terminating = False
        while kernel.QueryInformationJobObject(
            job,
            basic_accounting_information,
            ctypes.byref(accounting),
            ctypes.sizeof(accounting),
            None,
        ):
            if accounting.active_processes == 0:
                return
            if not terminating:
                if not kernel.TerminateJobObject(job, 1):
                    raise ctypes.WinError(ctypes.get_last_error())
                terminating = True
            if time.monotonic() >= deadline:
                snapshot = JobProcessIds()
                process_id_list = 3
                listed = kernel.QueryInformationJobObject(
                    job, process_id_list, ctypes.byref(snapshot), ctypes.sizeof(snapshot), None
                )
                if listed or (ctypes.get_last_error() == 234 and snapshot.listed):
                    pids = ", ".join(str(pid) for pid in snapshot.pids[: snapshot.listed])
                    remaining = f"remaining job PIDs: {pids}"
                    if snapshot.assigned > snapshot.listed:
                        remaining += f" (first {snapshot.listed} of {snapshot.assigned})"
                else:
                    remaining = f"job PIDs unavailable (WinError {ctypes.get_last_error()})"
                raise TimeoutError(
                    f"Private Unity worker job still has {accounting.active_processes} active "
                    f"processes after termination, {remaining}"
                )
            time.sleep(0.05)
        raise ctypes.WinError(ctypes.get_last_error())

    class WindowsWorkerProcess:
        def __init__(
            self, kernel: ctypes.WinDLL, user: ctypes.WinDLL, desktop: int, process: ProcessInfo
        ) -> None:
            self._kernel = kernel
            self._user = user
            self._desktop = desktop
            self._process = process
            self.pid = process.process_id
            self.returncode: int | None = None
            kernel.CloseHandle(process.thread)

        def poll(self) -> int | None:
            try:
                return self.wait(0)
            except subprocess.TimeoutExpired:
                return None

        def wait(self, timeout: float | None = None) -> int:
            if self.returncode is not None:
                return self.returncode
            duration = (
                0xFFFFFFFF
                if timeout is None
                else min(0xFFFFFFFE, max(0, math.ceil(timeout * 1000)))
            )
            status = self._kernel.WaitForSingleObject(self._process.process, duration)
            if status == 258 and timeout is not None:
                raise subprocess.TimeoutExpired("private Unity worker", timeout)
            if status != 0:
                raise ctypes.WinError(ctypes.get_last_error())
            try:
                code = wintypes.DWORD()
                if not self._kernel.GetExitCodeProcess(self._process.process, ctypes.byref(code)):
                    raise ctypes.WinError(ctypes.get_last_error())
                self.returncode = ctypes.c_int32(code.value).value
                return self.returncode
            finally:
                self._kernel.CloseHandle(self._process.process)
                close_desktop(self._user, self._desktop)

    class WindowsWorkerJob:
        """Run PowerShell workers with a private desktop and one shared job."""

        def __init__(self) -> None:
            self._kernel = ctypes.WinDLL("kernel32", use_last_error=True)
            self._user = ctypes.WinDLL("user32", use_last_error=True)
            configure_native_process(self._kernel, self._user)
            self._handle = worker_job(self._kernel)

        def launch(
            self, shell: str, encoded: str, environment: dict[str, str], stream: BinaryIO
        ) -> ModeProcess:
            # The CI worker is noninteractive. Child processes inherit only the
            # input and combined log handles selected for this worker.
            kernel = self._kernel
            with Path(os.devnull).open("rb") as stdin:
                input_handle = inheritable_stream_handle(kernel, stdin)
                try:
                    output_handle = inheritable_stream_handle(kernel, stream)
                    try:
                        return self._start(
                            shell,
                            encoded,
                            (input_handle, output_handle, output_handle),
                            environment=environment,
                            handles=(input_handle, output_handle),
                        )
                    finally:
                        kernel.CloseHandle(output_handle)
                finally:
                    kernel.CloseHandle(input_handle)

        def launch_console(self, shell: str, encoded: str) -> ModeProcess:
            kernel = self._kernel
            handles = (kernel.GetStdHandle(-10), kernel.GetStdHandle(-11), kernel.GetStdHandle(-12))
            return self._start(shell, encoded, handles)

        def _start(
            self,
            shell: str,
            encoded: str,
            stdio: tuple[int, int, int],
            *,
            environment: dict[str, str] | None = None,
            handles: tuple[int, ...] = (),
        ) -> WindowsWorkerProcess:
            validate_payload(encoded)
            name = "uh-mode-" + uuid.uuid4().hex
            desktop: int | None = self._user.CreateDesktopW(name, None, None, 0, 0x01FF, None)
            if not desktop:
                raise ctypes.WinError(ctypes.get_last_error())
            try:
                startup = StartupInfoEx()
                startup.startup.size = ctypes.sizeof(startup)
                startup.startup.desktop = name
                startup.startup.flags = 0x0100  # STARTF_USESTDHANDLES
                startup.startup.stdin, startup.startup.stdout, startup.startup.stderr = stdio
                process = start_worker(
                    self._kernel, startup, self._handle, shell, encoded, environment, handles
                )
                return WindowsWorkerProcess(self._kernel, self._user, desktop, process)
            except BaseException:
                close_desktop(self._user, desktop)
                raise

        def close(self) -> None:
            try:
                reap_job(self._kernel, self._handle)
            finally:
                self._kernel.CloseHandle(self._handle)

    def inheritable_stream_handle(kernel: ctypes.WinDLL, stream: BinaryIO) -> int:
        owner = kernel.GetCurrentProcess()
        handle = wintypes.HANDLE()
        duplicate_same_access = 2
        if not kernel.DuplicateHandle(
            owner,
            msvcrt.get_osfhandle(stream.fileno()),
            owner,
            ctypes.byref(handle),
            0,
            True,
            duplicate_same_access,
        ):
            raise ctypes.WinError(ctypes.get_last_error())
        if handle.value is None:
            raise OSError("Windows did not duplicate the worker stream handle")
        return handle.value

    def close_desktop(user: ctypes.WinDLL, desktop: int) -> None:
        if not user.CloseDesktop(desktop):
            raise ctypes.WinError(ctypes.get_last_error())

    def create_worker_process(
        kernel: ctypes.WinDLL,
        startup: StartupInfo,
        shell: str,
        encoded: str,
        environment: dict[str, str] | None,
    ) -> ProcessInfo:
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
        environment_buffer = None
        if environment is not None:
            entries = sorted(environment, key=str.upper)
            environment_buffer = ctypes.create_unicode_buffer(
                "\0".join(f"{name}={environment[name]}" for name in entries) + "\0"
            )
        process = ProcessInfo()
        # CREATE_NO_WINDOW, EXTENDED_STARTUPINFO_PRESENT, CREATE_UNICODE_ENVIRONMENT
        flags = 0x08080400
        if not kernel.CreateProcessW(
            shell,
            command,
            None,
            None,
            True,
            flags,
            environment_buffer,
            None,
            ctypes.byref(startup),
            ctypes.byref(process),
        ):
            raise ctypes.WinError(ctypes.get_last_error())
        return process

    def start_worker(
        kernel: ctypes.WinDLL,
        startup: StartupInfoEx,
        job: int,
        shell: str,
        encoded: str,
        environment: dict[str, str] | None,
        handles: tuple[int, ...],
    ) -> ProcessInfo:
        kernel.InitializeProcThreadAttributeList.argtypes = (
            ctypes.c_void_p,
            wintypes.DWORD,
            wintypes.DWORD,
            ctypes.POINTER(ctypes.c_size_t),
        )
        kernel.InitializeProcThreadAttributeList.restype = wintypes.BOOL
        kernel.UpdateProcThreadAttribute.argtypes = (
            ctypes.c_void_p,
            wintypes.DWORD,
            ctypes.c_size_t,
            ctypes.c_void_p,
            ctypes.c_size_t,
            ctypes.c_void_p,
            ctypes.c_void_p,
        )
        kernel.UpdateProcThreadAttribute.restype = wintypes.BOOL
        kernel.DeleteProcThreadAttributeList.argtypes = (ctypes.c_void_p,)
        kernel.DeleteProcThreadAttributeList.restype = None
        size = ctypes.c_size_t()
        count = 2 if handles else 1
        kernel.InitializeProcThreadAttributeList(None, count, 0, ctypes.byref(size))
        if not size.value:
            raise ctypes.WinError(ctypes.get_last_error())
        attributes = ctypes.create_string_buffer(size.value)
        startup.attributes = ctypes.cast(attributes, ctypes.c_void_p)
        if not kernel.InitializeProcThreadAttributeList(
            startup.attributes, count, 0, ctypes.byref(size)
        ):
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            # Attach at creation so even a canceled launch cannot leave an unowned worker.
            job_list = wintypes.HANDLE(job)
            job_list_attribute = 0x0002000D  # PROC_THREAD_ATTRIBUTE_JOB_LIST
            if not kernel.UpdateProcThreadAttribute(
                startup.attributes,
                0,
                job_list_attribute,
                ctypes.byref(job_list),
                ctypes.sizeof(job_list),
                None,
                None,
            ):
                raise ctypes.WinError(ctypes.get_last_error())
            inherited = (wintypes.HANDLE * len(handles))(*handles)
            handle_list_attribute = 0x00020002  # PROC_THREAD_ATTRIBUTE_HANDLE_LIST
            if handles and not kernel.UpdateProcThreadAttribute(
                startup.attributes,
                0,
                handle_list_attribute,
                ctypes.byref(inherited),
                ctypes.sizeof(inherited),
                None,
                None,
            ):
                raise ctypes.WinError(ctypes.get_last_error())
            return create_worker_process(kernel, startup.startup, shell, encoded, environment)
        finally:
            kernel.DeleteProcThreadAttributeList(startup.attributes)

    def configure_native_process(kernel: ctypes.WinDLL, user: ctypes.WinDLL) -> None:
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
        kernel.GetCurrentProcess.argtypes = ()
        kernel.GetCurrentProcess.restype = wintypes.HANDLE
        kernel.DuplicateHandle.argtypes = (
            wintypes.HANDLE,
            wintypes.HANDLE,
            wintypes.HANDLE,
            ctypes.POINTER(wintypes.HANDLE),
            wintypes.DWORD,
            wintypes.BOOL,
            wintypes.DWORD,
        )
        kernel.DuplicateHandle.restype = wintypes.BOOL
        kernel.GetStdHandle.argtypes = (wintypes.DWORD,)
        kernel.GetStdHandle.restype = wintypes.HANDLE
        kernel.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
        kernel.WaitForSingleObject.restype = wintypes.DWORD
        kernel.GetExitCodeProcess.argtypes = (
            wintypes.HANDLE,
            ctypes.POINTER(wintypes.DWORD),
        )
        kernel.GetExitCodeProcess.restype = wintypes.BOOL

    def run(shell: str, encoded: str) -> int:
        validate_payload(encoded)
        job = WindowsWorkerJob()
        try:
            return job.launch_console(shell, encoded).wait()
        finally:
            job.close()

    def create_worker_job() -> WorkerJob | None:
        return WindowsWorkerJob()

else:

    def run(_shell: str, _encoded: str) -> int:
        raise OSError("Private Unity worker desktops require Windows")

    def create_worker_job() -> WorkerJob | None:
        return None


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
