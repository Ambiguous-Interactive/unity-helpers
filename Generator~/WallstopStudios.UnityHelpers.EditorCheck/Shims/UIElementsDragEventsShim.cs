// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only shim for the UI Toolkit DRAG events.
//
// These live in the `UnityEngine.UIElements` NAMESPACE but Unity compiles them into its editor-side
// `UnityEditor.UIElementsModule.dll`, which neither community package ships: `UnityEngine.Modules`
// carries the runtime modules only, and `Unity3D.SDK` carries the monolithic `UnityEditor.dll` from
// a build where the UIElements module was already split out.
//
// Mirrors the real shape: each is `MouseEventBase<TSelf>` with a public parameterless constructor,
// which is what `RegisterCallback<TEventType>` constrains on. Declaring a member the real type does
// not have would let a genuine error through, so nothing beyond the derivation is declared.
namespace UnityEngine.UIElements
{
    public class DragUpdatedEvent : MouseEventBase<DragUpdatedEvent> { }

    public class DragPerformEvent : MouseEventBase<DragPerformEvent> { }

    public class DragLeaveEvent : MouseEventBase<DragLeaveEvent> { }
}
