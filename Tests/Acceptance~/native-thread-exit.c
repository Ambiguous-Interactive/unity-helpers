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

static unsigned long __stdcall WaitForMainThread(void* mainThread)
{
    WaitForSingleObject(mainThread, 0xffffffffu);
    CloseHandle(mainThread);
    return 0;
}

int main(void)
{
    static const char telemetry[] = "##utp:{\"type\":\"MemoryLeaks\",\"controlled\":true}\r\n";
    unsigned long mainThreadId = GetCurrentThreadId();
    unsigned long waiterThreadId = 0;
    unsigned long written = 0;
    void* stdoutHandle = GetStdHandle((unsigned long)-11);
    void* mainThread = OpenThread(0x00100000u, 0, mainThreadId);
    void* waiterThread;
    if (mainThread == 0)
    {
        return 1;
    }
    waiterThread = CreateThread(0, 0, WaitForMainThread, mainThread, 0, &waiterThreadId);
    if (waiterThread == 0)
    {
        CloseHandle(mainThread);
        return 2;
    }
    CloseHandle(waiterThread);
    WriteThreadId(stdoutHandle, "WUH_CONTROL_MAIN_THREAD=", mainThreadId);
    WriteThreadId(stdoutHandle, "WUH_CONTROL_WAITER_THREAD=", waiterThreadId);
    Sleep(2000);
    WriteFile(stdoutHandle, telemetry, sizeof(telemetry) - 1, &written, 0);
    Sleep(6000);
    ExitThread(0xc0000005u);
    return 0;
}
