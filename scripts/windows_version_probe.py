"""Run one Windows version command in a job and return a bounded result."""

from __future__ import annotations

import subprocess
import sys

MAXIMUM_VERSION_BYTES = 64 * 1024
STATUS_BYTES = 4


if sys.platform == "win32":
    import ctypes
    from ctypes import wintypes

    class _BasicJobLimits(ctypes.Structure):
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

    class _ExtendedJobLimits(ctypes.Structure):
        _fields_ = (
            ("basic_limits", _BasicJobLimits),
            ("io_counters", ctypes.c_ulonglong * 6),
            ("process_memory_limit", ctypes.c_size_t),
            ("job_memory_limit", ctypes.c_size_t),
            ("peak_process_memory_used", ctypes.c_size_t),
            ("peak_job_memory_used", ctypes.c_size_t),
        )

    def _enter_job() -> None:
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateJobObjectW.argtypes = (ctypes.c_void_p, wintypes.LPCWSTR)
        kernel.CreateJobObjectW.restype = wintypes.HANDLE
        kernel.SetInformationJobObject.argtypes = (
            wintypes.HANDLE,
            ctypes.c_int,
            ctypes.POINTER(_ExtendedJobLimits),
            wintypes.DWORD,
        )
        kernel.SetInformationJobObject.restype = wintypes.BOOL
        kernel.AssignProcessToJobObject.argtypes = (wintypes.HANDLE, wintypes.HANDLE)
        kernel.AssignProcessToJobObject.restype = wintypes.BOOL
        kernel.GetCurrentProcess.argtypes = ()
        kernel.GetCurrentProcess.restype = wintypes.HANDLE
        kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
        kernel.CloseHandle.restype = wintypes.BOOL

        job: int | None = kernel.CreateJobObjectW(None, None)
        if not job:
            raise ctypes.WinError(ctypes.get_last_error())
        limits = _ExtendedJobLimits()
        limits.basic_limits.limit_flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        extended_limit_information = 9
        if not kernel.SetInformationJobObject(
            job, extended_limit_information, ctypes.byref(limits), ctypes.sizeof(limits)
        ) or not kernel.AssignProcessToJobObject(job, kernel.GetCurrentProcess()):
            error = ctypes.get_last_error()
            kernel.CloseHandle(job)
            raise ctypes.WinError(error)
        # The worker holds the only job handle so exiting it also stops remaining descendants.

    def _run_probe(command: list[str]) -> tuple[int, bytes]:
        _enter_job()
        with subprocess.Popen(  # noqa: S603 - this worker receives a resolved executable and its arguments.
            command,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
        ) as process:
            pipe = process.stdout
            if pipe is None:
                raise OSError("Version probe output pipe is unavailable")
            prefix = pipe.readline(MAXIMUM_VERSION_BYTES + 2)
            while pipe.read(MAXIMUM_VERSION_BYTES):
                pass
            return process.wait(), prefix

    def main() -> int:
        if len(sys.argv) < 2:
            return 2
        try:
            status, prefix = _run_probe(sys.argv[1:])
        except OSError as error:
            print(error, file=sys.stderr)
            return 1
        sys.stdout.buffer.write(status.to_bytes(STATUS_BYTES, "little") + prefix)
        return 0

    if __name__ == "__main__":
        raise SystemExit(main())
