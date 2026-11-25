"""Run one PowerShell worker on a private Windows desktop and own its process tree."""

from __future__ import annotations

import argparse
import base64
import ctypes
import re
import subprocess
import sys
import time
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
            ctypes.POINTER(BasicJobAccounting),
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
                raise TimeoutError(
                    f"Private Unity worker job still has {accounting.active_processes} active "
                    "processes after termination"
                )
            time.sleep(0.05)
        raise ctypes.WinError(ctypes.get_last_error())

    class WindowsWorkerJob:
        """Own the shared worker job until the mode controller finishes."""

        def __init__(self) -> None:
            self._kernel = ctypes.WinDLL("kernel32", use_last_error=True)
            configure_close_handle(self._kernel)
            self.handle = worker_job(self._kernel)

        def close(self) -> None:
            try:
                reap_job(self._kernel, self.handle)
            finally:
                self._kernel.CloseHandle(self.handle)

    def configure_close_handle(kernel: ctypes.WinDLL) -> None:
        kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
        kernel.CloseHandle.restype = wintypes.BOOL

    def assign_launcher_to_job(kernel: ctypes.WinDLL, job: int) -> None:
        kernel.GetCurrentProcess.argtypes = ()
        kernel.GetCurrentProcess.restype = wintypes.HANDLE
        kernel.AssignProcessToJobObject.argtypes = (wintypes.HANDLE, wintypes.HANDLE)
        kernel.AssignProcessToJobObject.restype = wintypes.BOOL
        if not kernel.AssignProcessToJobObject(job, kernel.GetCurrentProcess()):
            raise ctypes.WinError(ctypes.get_last_error())

    def create_worker_process(
        kernel: ctypes.WinDLL, startup: StartupInfo, shell: str, encoded: str, *, extended: bool
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
        process = ProcessInfo()
        flags = 0x08000000  # CREATE_NO_WINDOW
        if extended:
            flags |= 0x00080000  # EXTENDED_STARTUPINFO_PRESENT
        if not kernel.CreateProcessW(
            shell,
            command,
            None,
            None,
            True,
            flags,
            None,
            None,
            ctypes.byref(startup),
            ctypes.byref(process),
        ):
            raise ctypes.WinError(ctypes.get_last_error())
        return process

    def start_independent_worker(
        kernel: ctypes.WinDLL, startup: StartupInfoEx, job: int, shell: str, encoded: str
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
        kernel.InitializeProcThreadAttributeList(None, 1, 0, ctypes.byref(size))
        if not size.value:
            raise ctypes.WinError(ctypes.get_last_error())
        attributes = ctypes.create_string_buffer(size.value)
        startup.attributes = ctypes.cast(attributes, ctypes.c_void_p)
        if not kernel.InitializeProcThreadAttributeList(
            startup.attributes, 1, 0, ctypes.byref(size)
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
            startup.startup.size = ctypes.sizeof(startup)
            return create_worker_process(kernel, startup.startup, shell, encoded, extended=True)
        finally:
            kernel.DeleteProcThreadAttributeList(startup.attributes)

    def run(shell: str, encoded: str, inherited_job: int | None = None) -> int:
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
        configure_close_handle(kernel)
        kernel.GetStdHandle.argtypes = (wintypes.DWORD,)
        kernel.GetStdHandle.restype = wintypes.HANDLE
        kernel.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
        kernel.WaitForSingleObject.restype = wintypes.DWORD
        kernel.GetExitCodeProcess.argtypes = (
            wintypes.HANDLE,
            ctypes.POINTER(wintypes.DWORD),
        )
        kernel.GetExitCodeProcess.restype = wintypes.BOOL
        name = "uh-mode-" + uuid.uuid4().hex
        desktop: int | None = user.CreateDesktopW(name, None, None, 0, 0x01FF, None)
        if not desktop:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            startup = StartupInfoEx()
            startup.startup.size = ctypes.sizeof(StartupInfo)
            startup.startup.desktop = name
            startup.startup.flags = 0x0100
            startup.startup.stdin = kernel.GetStdHandle(-10)
            startup.startup.stdout = kernel.GetStdHandle(-11)
            startup.startup.stderr = kernel.GetStdHandle(-12)
            job = None
            if inherited_job is not None:
                try:
                    # Include the launcher and its log handles. PowerShell
                    # inherits the job from the launcher when it starts.
                    assign_launcher_to_job(kernel, inherited_job)
                finally:
                    kernel.CloseHandle(inherited_job)
            else:
                job = worker_job(kernel)
            try:
                process = (
                    start_independent_worker(kernel, startup, job, shell, encoded)
                    if job is not None
                    else create_worker_process(
                        kernel, startup.startup, shell, encoded, extended=False
                    )
                )
                try:
                    if kernel.WaitForSingleObject(process.process, 0xFFFFFFFF) != 0:
                        raise ctypes.WinError(ctypes.get_last_error())
                    code = wintypes.DWORD()
                    if not kernel.GetExitCodeProcess(process.process, ctypes.byref(code)):
                        raise ctypes.WinError(ctypes.get_last_error())
                    return ctypes.c_int32(code.value).value
                finally:
                    kernel.CloseHandle(process.thread)
                    kernel.CloseHandle(process.process)
                    if job is not None:
                        reap_job(kernel, job)
            finally:
                if job is not None:
                    kernel.CloseHandle(job)
        finally:
            if not user.CloseDesktop(desktop):
                raise ctypes.WinError(ctypes.get_last_error())

else:

    def run(_shell: str, _encoded: str, _inherited_job: int | None = None) -> int:
        raise OSError("Private Unity worker desktops require Windows")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--shell", required=True)
    parser.add_argument("--encoded-worker", required=True)
    parser.add_argument("--inherited-job", type=int)
    arguments = parser.parse_args()
    try:
        return run(arguments.shell, arguments.encoded_worker, arguments.inherited_job)
    except (OSError, ValueError) as error:
        print(f"Cannot launch private Unity worker: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
