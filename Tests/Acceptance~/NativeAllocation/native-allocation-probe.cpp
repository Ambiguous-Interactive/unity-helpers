// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#include <windows.h>
#include <stdint.h>
#include <atomic>

#ifndef UH_NATIVE_PROBE_SOURCE_SHA256
#define UH_NATIVE_PROBE_SOURCE_SHA256 "unverified"
#endif

static std::atomic<DWORD> activeThread(0);
static uint64_t allocationCount = 0;
static uint64_t logicalObjectBytes = 0;
static uint32_t (__cdecl *objectSize)(void*) = nullptr;
static void* profilerHandle = nullptr;
static void (__cdecl *setMonoCallback)(void*, void (__cdecl *)(void*, void*)) = nullptr;
static int backend = 0;

static void Capture(void* object)
{
    if (activeThread.load(std::memory_order_acquire) != GetCurrentThreadId())
    {
        return;
    }
    ++allocationCount;
    logicalObjectBytes += objectSize(object);
}

static void __cdecl MonoAllocation(void*, void* object)
{
    Capture(object);
}

static void __cdecl ClassicAllocation(void*, void* object, void*)
{
    Capture(object);
}

extern "C" __declspec(dllexport) int __cdecl ProbeInitialize()
{
    if (backend != 0)
    {
        return backend;
    }
    HMODULE pinned = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
            reinterpret_cast<LPCWSTR>(&ProbeInitialize), &pinned))
    {
        return 0;
    }
    HMODULE module = GetModuleHandleW(L"GameAssembly.dll");
    bool il2cpp = module != nullptr;
    if (module == nullptr)
    {
        module = GetModuleHandleW(L"mono-2.0-bdwgc.dll");
    }
    if (module == nullptr)
    {
        return 0;
    }
    objectSize = reinterpret_cast<uint32_t (__cdecl *)(void*)>(
        GetProcAddress(module, il2cpp ? "il2cpp_object_get_size" : "mono_object_get_size"));
    if (objectSize == nullptr)
    {
        return 0;
    }
    if (!il2cpp)
    {
        auto create = reinterpret_cast<void* (__cdecl *)(void*)>(
            GetProcAddress(module, "mono_profiler_create"));
        auto enable = reinterpret_cast<int (__cdecl *)()>(
            GetProcAddress(module, "mono_profiler_enable_allocations"));
        setMonoCallback = reinterpret_cast<void (__cdecl *)(void*, void (__cdecl *)(void*, void*))>(
            GetProcAddress(module, "mono_profiler_set_gc_allocation_callback"));
        if (create != nullptr && enable != nullptr && setMonoCallback != nullptr)
        {
            if (enable() == 0)
            {
                return 0;
            }
            profilerHandle = create(nullptr);
            if (profilerHandle == nullptr)
            {
                return 0;
            }
            backend = 1;
        }
    }
    if (backend == 0)
    {
        auto install = reinterpret_cast<void (__cdecl *)(void*, void*)>(
            GetProcAddress(module, il2cpp ? "il2cpp_profiler_install" : "mono_profiler_install"));
        auto setAllocation = reinterpret_cast<void (__cdecl *)(void (__cdecl *)(void*, void*, void*))>(
            GetProcAddress(module, il2cpp ? "il2cpp_profiler_install_allocation" : "mono_profiler_install_allocation"));
        auto setEvents = reinterpret_cast<void (__cdecl *)(int)>(
            GetProcAddress(module, il2cpp ? "il2cpp_profiler_set_events" : "mono_profiler_set_events"));
        if (install == nullptr || setAllocation == nullptr || setEvents == nullptr)
        {
            return 0;
        }
        install(nullptr, nullptr);
        setAllocation(&ClassicAllocation);
        setEvents(1 << 7);
        // The classic setters target the last descriptor. Configure ours once.
        // Keep its native callback and pinned module alive until this diagnostic player exits.
        backend = il2cpp ? 3 : 2;
    }
    return backend;
}

extern "C" __declspec(dllexport) int __cdecl ProbeBegin()
{
    if (backend == 0)
    {
        return 0;
    }
    DWORD expected = 0;
    if (!activeThread.compare_exchange_strong(expected, MAXDWORD, std::memory_order_acq_rel))
    {
        return 0;
    }
    allocationCount = 0;
    logicalObjectBytes = 0;
    if (backend == 1)
    {
        setMonoCallback(profilerHandle, &MonoAllocation);
    }
    activeThread.store(GetCurrentThreadId(), std::memory_order_release);
    return 1;
}

extern "C" __declspec(dllexport) void __cdecl ProbeEnd()
{
    if (activeThread.load(std::memory_order_acquire) != GetCurrentThreadId())
    {
        return;
    }
    activeThread.store(0, std::memory_order_release);
    if (backend == 1)
    {
        setMonoCallback(profilerHandle, nullptr);
    }
}

extern "C" __declspec(dllexport) uint64_t __cdecl ProbeCount()
{
    return allocationCount;
}

extern "C" __declspec(dllexport) uint64_t __cdecl ProbeLogicalObjectBytes()
{
    return logicalObjectBytes;
}

extern "C" __declspec(dllexport) const char* __cdecl ProbeSourceSha256()
{
    return UH_NATIVE_PROBE_SOURCE_SHA256;
}
