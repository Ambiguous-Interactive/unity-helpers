// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only shim for the two Odin Inspector base classes the RUNTIME assembly aliases.
//
// Odin (com.sirenix.odininspector) is a paid asset with no NuGet equivalent, so the sources behind
// WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR compiled NOWHERE in automation (#347) -- not here, not in
// any Unity leg, not in the MCP editor. `RuntimeSingleton<T>` and `ScriptableObjectSingleton<T>` are
// public API whose base class CHANGES when a consumer installs Odin, and that branch had never once
// been compiled. #275 is that blind spot shipping a break to consumers.
//
// This shim compiles only under the `Odin` typecheck configuration, and only these two types are
// declared, because the runtime assembly reaches Odin exclusively through `using` aliases:
//
//     Runtime/Tags/AttributeEffect.cs           -> SerializedScriptableObject
//     Runtime/Utils/RuntimeSingleton.cs         -> SerializedMonoBehaviour
//     Runtime/Utils/ScriptableObjectSingleton.cs -> SerializedScriptableObject
//
// The EDITOR-side Odin surface (OdinAttributeDrawer, InspectorProperty, OdinEditor) is deliberately
// NOT shimmed, and it is STILL COMPILED BY NOTHING -- do not read this file as covering it.
// Compiling those drawers means compiling the whole Editor assembly, because WButtonOdinInspectorHelper
// pulls in Editor/Utils/WButton/**, and that needs a UnityEditor reference the community reference
// assemblies do not carry. Shimming UnityEditor to reach it would be a large fabricated surface
// guarding a small real one. The vehicle that would work is a real editor with a stub `odininspector`
// UPM package injected into the generated test project, compile-only. Neither exists yet; both are
// tracked on #347.
//
// Mirroring the real base classes matters: declaring a member the real type does not have would let
// a genuine error through. Both are plain derivations in Odin -- they exist to route serialization
// through Odin's serializer, which is behaviour rather than surface, and behaviour is not what a
// type-checker asserts.
namespace Sirenix.OdinInspector
{
    using UnityEngine;

    public abstract class SerializedMonoBehaviour : MonoBehaviour { }

    public abstract class SerializedScriptableObject : ScriptableObject { }
}
