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
// NOT shimmed here -- do not read this file as covering it. It lives one project over, in
// `Generator~/WallstopStudios.UnityHelpers.EditorCheck/ShimsOdin/OdinEditorShim.cs`, because
// compiling those drawers means compiling the whole Editor assembly (WButtonOdinInspectorHelper
// pulls in Editor/Utils/WButton/**) against the community `Unity3D.SDK` UnityEditor assembly that
// this project does not reference. Both halves of #347 are now covered; this one is the runtime
// half.
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
