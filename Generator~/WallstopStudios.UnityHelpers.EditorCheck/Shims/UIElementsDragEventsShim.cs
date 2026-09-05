// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// These UIElements drag events live in an editor module absent from both community reference packages.
namespace UnityEngine.UIElements
{
    public class DragUpdatedEvent : MouseEventBase<DragUpdatedEvent> { }

    public class DragPerformEvent : MouseEventBase<DragPerformEvent> { }

    public class DragLeaveEvent : MouseEventBase<DragLeaveEvent> { }
}
