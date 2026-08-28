// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only shim for the one UnityEngine.Scripting type the runtime sources declare.
//
// The oracle harness compiles the real Runtime serialization sources under net9.0, where no
// UnityEngine exists; [Preserve] on the reflected attributes (WProtoContractAttribute,
// WProtoMemberAttribute, WProtoIncludeAttribute) would otherwise fail this build. The real
// attribute is a marker whose only consumer is IL2CPP's linker, so an empty derivation here is a
// faithful stand-in: declaring members the real type does not have would let a genuine error
// through. The desktop suite reads the attribute's PRESENCE, never its behaviour.
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
