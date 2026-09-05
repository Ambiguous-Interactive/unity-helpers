// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * CoreCLR tests need a signature-only PreserveAttribute shim; its real behavior belongs to the IL2CPP

 * linker.

 */
namespace UnityEngine.Scripting
{
    using System;

    /// <summary>Stand-in for UnityEngine.Scripting.PreserveAttribute on Unity-free builds.</summary>
    [AttributeUsage(
        AttributeTargets.Assembly
            | AttributeTargets.Class
            | AttributeTargets.Struct
            | AttributeTargets.Method
            | AttributeTargets.Property
            | AttributeTargets.Field
            | AttributeTargets.Interface
            | AttributeTargets.Enum
            | AttributeTargets.Delegate
            | AttributeTargets.Parameter
            | AttributeTargets.Event
    )]
    public sealed class PreserveAttribute : Attribute { }
}
