// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Runs <see cref="SerializedFieldValidator"/> over whatever is selected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Selection-scoped, and that is the design rather than a limitation. Validating a type means
    /// <b>constructing</b> one, which is the only way to get Unity's own answer instead of a guess
    /// about its rules -- and constructing every type in a project runs the startup half of the
    /// whole project. Measured: a first draft walked every loaded assembly and stopped the editor
    /// responding. <see cref="SerializedFieldValidator"/> deactivates its probe before adding a
    /// component, so no <c>Awake</c> runs now, but a <c>ScriptableObject</c>'s <c>OnEnable</c> still
    /// does and always will.
    /// </para>
    /// <para>
    /// Selecting the script you just wrote is also where the answer is wanted. Select one or more
    /// <c>MonoScript</c> assets, prefabs, scene objects or <c>ScriptableObject</c> assets and run
    /// the command; every type behind the selection is inspected.
    /// </para>
    /// </remarks>
    public static class SerializedFieldValidatorMenu
    {
        private const string MenuPath =
            "Tools/Wallstop Studios/Unity Helpers/Validate Serialized Fields In Selection";

        [MenuItem(MenuPath, priority = 99)]
        public static void ValidateSelection()
        {
            List<Type> types = new();
            foreach (Object selected in Selection.objects)
            {
                Collect(selected, types);
            }

            if (types.Count == 0)
            {
                Debug.LogWarning(
                    "[Unity Helpers] Nothing in the selection is a MonoBehaviour or ScriptableObject. "
                        + "Select a script, prefab, scene object or asset and run the command again."
                );
                return;
            }

            List<DroppedSerializedField> findings = new();
            List<DroppedSerializedField> all = new();
            List<Type> skipped = new();
            int inspected = 0;
            foreach (Type type in types)
            {
                if (!SerializedFieldValidator.TryValidate(type, findings))
                {
                    // A type that would not construct was not measured, and reporting it inside the
                    // "everything survives" count would say the opposite of what happened -- loudest
                    // when every selected type fails and the command cheerfully reports zero
                    // problems across zero types.
                    skipped.Add(type);
                    continue;
                }

                inspected++;
                all.AddRange(findings);
            }

            StringBuilder report = new();
            if (all.Count == 0)
            {
                report.AppendLine(
                    $"[Unity Helpers] Every serialized field on {inspected} selected type(s) survives Unity's serializer."
                );
            }
            else
            {
                report.AppendLine(
                    $"[Unity Helpers] {all.Count} serialized field(s) on {inspected} selected type(s) are silently dropped by Unity:"
                );
                foreach (DroppedSerializedField finding in all)
                {
                    report.Append("  - ").AppendLine(finding.ToString());
                }
            }

            if (0 < skipped.Count)
            {
                report.AppendLine(
                    $"{skipped.Count} selected type(s) could not be constructed and were NOT checked:"
                );
                foreach (Type type in skipped)
                {
                    report.Append("  - ").AppendLine(type.FullName);
                }
            }

            // A warning rather than an error. Every dropped field compiles and runs, and one may be
            // deliberately unpersisted -- in which case [NonSerialized] says so and silences it.
            if (0 < all.Count || 0 < skipped.Count)
            {
                Debug.LogWarning(report.ToString());
                return;
            }

            Debug.Log(report.ToString().TrimEnd());
        }

        /// <summary>
        /// Resolves the inspectable types a selected object stands for.
        /// </summary>
        /// <param name="selected">One selected object.</param>
        /// <param name="types">Receives each distinct inspectable type.</param>
        /// <remarks>
        /// A prefab or scene object contributes every component it carries, because that is what a
        /// developer means by "check this prefab", and the field that will be dropped is rarely on
        /// the component they were looking at.
        /// </remarks>
        public static void Collect(Object selected, List<Type> types)
        {
            if (selected == null || types == null)
            {
                return;
            }

            if (selected is MonoScript script)
            {
                Add(script.GetClass(), types);
                return;
            }

            if (selected is GameObject gameObject)
            {
                foreach (
                    MonoBehaviour behaviour in gameObject.GetComponentsInChildren<MonoBehaviour>(
                        true
                    )
                )
                {
                    if (behaviour != null)
                    {
                        Add(behaviour.GetType(), types);
                    }
                }

                return;
            }

            Add(selected.GetType(), types);
        }

        private static void Add(Type type, List<Type> types)
        {
            if (SerializedFieldValidator.IsInspectable(type) && !types.Contains(type))
            {
                types.Add(type);
            }
        }
    }
#endif
}
