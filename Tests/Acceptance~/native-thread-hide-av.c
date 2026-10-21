// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

__declspec(dllimport) unsigned long __stdcall GetCurrentThreadId(void);
__declspec(dllimport) void __stdcall ExitThread(unsigned long);
__declspec(dllimport) void __stdcall Sleep(unsigned long);
__declspec(dllimport) void* __stdcall GetStdHandle(unsigned long);
__declspec(dllimport) int __stdcall WriteFile(void*, const void*, unsigned long, unsigned long*, void*);
__declspec(dllimport) void* __stdcall OpenThread(unsigned long, int, unsigned long);
__declspec(dllimport) void* __stdcall CreateThread(void*, unsigned long long, unsigned long (__stdcall*)(void*), void*, unsigned long, unsigned long*);
__declspec(dllimport) unsigned long __stdcall WaitForSingleObject(void*, unsigned long);
__declspec(dllimport) int __stdcall CloseHandle(void*);

static void WriteThreadId(void* stdoutHandle, const char* prefix, unsigned long threadId)
{
    char output[64];
    char digits[10];
    unsigned long count = 0;
    unsigned long length = 0;
    unsigned long written = 0;
    do
    {
        digits[count++] = (char)('0' + threadId % 10);
        threadId /= 10;
    } while (threadId != 0);
    while (length < sizeof(output) - sizeof(digits) - 2 && prefix[length] != 0)
    {
        output[length] = prefix[length];
        ++length;
    }
    while (count != 0)
    {
        output[length++] = digits[--count];
    }
    output[length++] = '\r';
    output[length++] = '\n';
    WriteFile(stdoutHandle, output, length, &written, 0);
}


__declspec(dllimport) unsigned long __stdcall SetErrorMode(unsigned long);
__declspec(dllimport) long __stdcall NtSetInformationThread(void*, unsigned long, void*, unsigned long);
#ifndef REQUEST_HIDE
#define REQUEST_HIDE 0
#endif
#ifndef HIDE_LATE
#define HIDE_LATE 0
#endif

int main(void)
{
    static const char telemetry[] = "##utp:{\"type\":\"MemoryLeaks\",\"controlled\":true}\r\n";
    unsigned long written = 0;
    void* stdoutHandle = GetStdHandle((unsigned long)-11);
    long status = 0;
    SetErrorMode(0x8003u);
    WriteThreadId(stdoutHandle, "WUH_CONTROL_MAIN_THREAD=", GetCurrentThreadId());
    if (REQUEST_HIDE && !HIDE_LATE)
    {
        status = NtSetInformationThread((void*)(long long)-2, 17, 0, 0);
    }
    WriteThreadId(stdoutHandle, "WUH_CONTROL_HIDE_STATUS=", (unsigned long)status);
    if (status != 0) { return 3; }
    Sleep(2000);
    WriteFile(stdoutHandle, telemetry, sizeof(telemetry) - 1, &written, 0);
    Sleep(6000);
    if (REQUEST_HIDE && HIDE_LATE)
    {
        status = NtSetInformationThread((void*)(long long)-2, 17, 0, 0);
        WriteThreadId(stdoutHandle, "WUH_CONTROL_LATE_HIDE_STATUS=", (unsigned long)status);
        if (status != 0) { return 4; }
    }
    *(volatile unsigned long*)0 = 0x12345678u;
    return 0;
}
