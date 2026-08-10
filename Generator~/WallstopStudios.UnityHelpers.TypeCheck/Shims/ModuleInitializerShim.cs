// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Unity's BCL exposes ModuleInitializerAttribute; the netstandard2.1 REFERENCE assemblies this
// harness compiles against do not. Declaring it here is the documented polyfill -- the compiler
// binds [ModuleInitializer] to whatever type has this name, so the generated registrar compiles
// exactly as it does in the editor. This is a harness-only gap, not a package one: the same
// registrar is green on all four Unity versions in CI, including 2021.3 IL2CPP standalone.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}
