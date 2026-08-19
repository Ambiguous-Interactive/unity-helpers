// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>
    /// One field a developer asked Unity to serialize and Unity declined to.
    /// </summary>
    public readonly struct DroppedSerializedField
    {
        /// <summary>Initializes a new instance of the <see cref="DroppedSerializedField"/> struct.</summary>
        /// <param name="owner">The type declaring the field.</param>
        /// <param name="fieldName">The field's name.</param>
        /// <param name="fieldType">The field's declared type.</param>
        /// <param name="standIn">The package stand-in to use instead, or <c>null</c>.</param>
        public DroppedSerializedField(Type owner, string fieldName, Type fieldType, string standIn)
        {
            Owner = owner;
            FieldName = fieldName;
            FieldType = fieldType;
            StandIn = standIn;
        }

        /// <summary>The type declaring the field.</summary>
        public Type Owner { get; }

        /// <summary>The field's name.</summary>
        public string FieldName { get; }

        /// <summary>The field's declared type.</summary>
        public Type FieldType { get; }

        /// <summary>The package stand-in to use instead, or <c>null</c> when there is none.</summary>
        public string StandIn { get; }

        /// <summary>Renders the finding as one line, naming the fix where there is one.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            string described =
                $"{Owner?.FullName}.{FieldName} is declared as "
                + $"{UnitySerializationStandIns.Readable(FieldType)}, which Unity does not serialize. "
                + "Anything authored into it is gone on the next domain reload.";

            return StandIn == null
                ? described
                    + " Give it a [Serializable] type of your own, or mark it [NonSerialized]."
                : described + $" Use {StandIn} instead.";
        }
    }

    /// <summary>
    /// Reports fields Unity silently drops, by asking Unity rather than by modelling its rules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity declines every type out of the framework assemblies and it declines <b>silently</b>:
    /// <c>SerializedObject.FindProperty</c> returns <c>null</c>, <c>JsonUtility.ToJson</c> omits the
    /// field entirely, and no warning is written anywhere. Anything a designer authored into it is
    /// gone on the next domain reload, and the loss is usually discovered from a build.
    /// <c>[Serializable]</c> is not the discriminator either -- <c>ValueTuple&lt;int, float&gt;</c>
    /// carries it and is dropped anyway -- which is what makes the rule hard to reason about from
    /// the outside.
    /// </para>
    /// <para>
    /// So this does not reason about it. It constructs the type, wraps it in a
    /// <see cref="SerializedObject"/>, and asks which of the fields the developer marked for
    /// serialization actually arrived. That is Unity's own answer, current for the editor running
    /// it, and it cannot produce a false positive on the generic user types Unity has serialized
    /// since 2020 -- those come back as real properties.
    /// </para>
    /// <para>
    /// A field is only asked about when the developer asked for it: <c>public</c>, or
    /// <c>[SerializeField]</c>. A runtime-only cache is private, or carries
    /// <see cref="NonSerializedAttribute"/>, and is never reported.
    /// </para>
    /// </remarks>
    public static class SerializedFieldValidator
    {
        /// <summary>
        /// Reports every field of <paramref name="type"/> that Unity will not serialize.
        /// </summary>
        /// <param name="type">A concrete <c>MonoBehaviour</c> or <c>ScriptableObject</c> type.</param>
        /// <param name="findings">Receives one entry per dropped field.</param>
        /// <returns><c>false</c> when the type could not be inspected at all.</returns>
        public static bool TryValidate(Type type, List<DroppedSerializedField> findings)
        {
            if (findings == null)
            {
                return false;
            }

            findings.Clear();
            if (!IsInspectable(type))
            {
                return false;
            }

            Object instance = null;
            GameObject host = null;
            try
            {
                if (typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    instance = ScriptableObject.CreateInstance(type);
                }
                else
                {
                    host = new GameObject("WallstopStudios.SerializedFieldProbe")
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                    };

                    // Deactivated BEFORE the component is added, and this is not a tidiness
                    // measure: AddComponent on an ACTIVE GameObject runs Awake and OnEnable
                    // immediately, so a project-wide scan would run the startup half of every
                    // behaviour in the project -- registering singletons, opening files, starting
                    // work. Measured: the scan stopped responding until this was added.
                    host.SetActive(false);
                    instance = host.AddComponent(type);
                }

                if (instance == null)
                {
                    return false;
                }

                using SerializedObject serialized = new(instance);
                foreach (FieldInfo field in DeclaredSerializationCandidates(type))
                {
                    if (serialized.FindProperty(field.Name) != null)
                    {
                        continue;
                    }

                    UnitySerializationStandIns.TryGetStandIn(field.FieldType, out string standIn);
                    findings.Add(
                        new DroppedSerializedField(
                            field.DeclaringType,
                            field.Name,
                            field.FieldType,
                            standIn
                        )
                    );
                }

                return true;
            }
            catch (Exception)
            {
                // A type whose constructor or Awake throws tells us nothing about serialization, and
                // a validator that fails a project scan on one such type is a validator nobody runs.
                return false;
            }
            finally
            {
                if (host != null)
                {
                    Object.DestroyImmediate(host);
                }
                else if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        /// <summary>
        /// Reports whether a type can be probed by constructing one.
        /// </summary>
        /// <param name="type">The candidate type.</param>
        /// <returns><c>true</c> when an instance can be made and inspected.</returns>
        public static bool IsInspectable(Type type)
        {
            return type != null
                && !type.IsAbstract
                && !type.IsGenericTypeDefinition
                && !type.ContainsGenericParameters
                && (
                    typeof(ScriptableObject).IsAssignableFrom(type)
                    || typeof(MonoBehaviour).IsAssignableFrom(type)
                );
        }

        /// <summary>
        /// Enumerates the fields the developer asked Unity to serialize, base types included.
        /// </summary>
        /// <param name="type">The type to walk.</param>
        /// <returns>Each candidate field, most-derived first.</returns>
        /// <remarks>
        /// Walked explicitly rather than through <c>BindingFlags.FlattenHierarchy</c>, which does not
        /// return private fields of a base type -- and a <c>[SerializeField]</c> on a base is exactly
        /// as droppable as one declared here.
        /// </remarks>
        public static IEnumerable<FieldInfo> DeclaredSerializationCandidates(Type type)
        {
            HashSet<string> seen = new();
            for (
                Type current = type;
                current != null
                    && current != typeof(MonoBehaviour)
                    && current != typeof(ScriptableObject);
                current = current.BaseType
            )
            {
                FieldInfo[] fields = current.GetFields(
                    BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly
                );

                foreach (FieldInfo field in fields)
                {
                    if (!IsCandidate(field) || !seen.Add(field.Name))
                    {
                        continue;
                    }

                    yield return field;
                }
            }
        }

        private static bool IsCandidate(FieldInfo field)
        {
            if (field.IsStatic || field.IsInitOnly || field.IsLiteral)
            {
                return false;
            }

            // [NonSerialized] is the standard way to say "runtime only", Unity honours it, and
            // honouring it here is what keeps this from reporting a deliberate cache.
            if (field.IsDefined(typeof(NonSerializedAttribute), inherit: false))
            {
                return false;
            }

            return field.IsPublic || field.IsDefined(typeof(SerializeField), inherit: false);
        }
    }
#endif
}
