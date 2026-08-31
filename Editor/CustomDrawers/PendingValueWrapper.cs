// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// A transient host that gives an arbitrary value a <see cref="SerializedProperty"/> to be drawn
    /// through, for a collection element that has no serialized slot of its own yet.
    /// </summary>
    /// <remarks>
    /// This lives in its own file rather than nested in the drawer that uses it because a nested
    /// type carries no <c>MonoScript</c>, and a <c>ScriptableObject</c> with no <c>MonoScript</c>
    /// cannot be authored onto anything -- the shape
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/624">#624</see>
    /// is about. The dictionary and set drawers previously each declared their own copy, and the
    /// two had drifted: one named its property with <c>nameof</c> and the other with a literal.
    /// </remarks>
    internal sealed class PendingValueWrapper : ScriptableObject
    {
        /// <summary>The serialized key the pending value is written under.</summary>
        internal const string PropertyName = nameof(boxedValue);

        [SerializeReference]
        private object boxedValue;

        /// <summary>Reads the value being edited.</summary>
        /// <returns>The pending value, which may be <c>null</c>.</returns>
        public object GetValue()
        {
            return boxedValue;
        }

        /// <summary>Replaces the value being edited.</summary>
        /// <param name="incoming">The new pending value.</param>
        public void SetValue(object incoming)
        {
            boxedValue = incoming;
        }

        /// <summary>Resolves the property the value is drawn through.</summary>
        /// <param name="serializedObject">The serialized view of this wrapper.</param>
        /// <returns>The property, or <c>null</c> when the view is not of this wrapper.</returns>
        public SerializedProperty FindValueProperty(SerializedObject serializedObject)
        {
            if (serializedObject == null)
            {
                return null;
            }

            return serializedObject.FindProperty(PropertyName);
        }
    }
#endif
}
