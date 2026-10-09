// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace UnityEngine
{
    /// <summary>
    /// Supplies the type marker needed by shared traits in Unity-free serialization tests.
    /// </summary>
    /// <remarks>
    /// This shim has no Unity identity, lifetime, or null behavior. Native Unity tests verify
    /// classification against Unity's actual object hierarchy.
    /// </remarks>
    public class Object { }
}
