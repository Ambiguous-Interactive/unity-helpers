// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*

 * Odin has no NuGet equivalent. These signature-only runtime bases check optional aliases; the separate

 * EditorCheck shim covers editor APIs.

 */
namespace Sirenix.OdinInspector
{
    using UnityEngine;

    public abstract class SerializedMonoBehaviour : MonoBehaviour { }

    public abstract class SerializedScriptableObject : ScriptableObject { }
}
