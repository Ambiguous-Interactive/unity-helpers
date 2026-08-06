// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Extension;
    using Helper;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Specifies the type of message displayed in the inspector when a field fails validation.
    /// </summary>
    public enum ValidateAssignmentMessageType
    {
        /// <summary>
        /// Displays as a warning (yellow) in the inspector.
        /// </summary>
        Warning = 0,

        /// <summary>
        /// Displays as an error (red) in the inspector.
        /// </summary>
        Error = 1,
    }

    /// <summary>
    /// Validates that a field is properly assigned at edit time. Displays a warning or error in the inspector
    /// when the field is null, empty (for strings), or has no elements (for collections).
    /// Use <see cref="ValidateAssignmentExtensions.ValidateAssignments"/> to log warnings for all invalid fields,
    /// or <see cref="ValidateAssignmentExtensions.AreAnyAssignmentsInvalid"/> to check validity programmatically.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="WNotNullAttribute"/>, this attribute validates:
    /// <list type="bullet">
    /// <item>Object references (null check)</item>
    /// <item>Strings (empty or whitespace check)</item>
    /// <item>Lists and Collections (empty check)</item>
    /// <item>Enumerables (empty check)</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// public class EnemySpawner : MonoBehaviour
    /// {
    ///     [ValidateAssignment]
    ///     public GameObject enemyPrefab;
    ///
    ///     [ValidateAssignment(ValidateAssignmentMessageType.Error, "Spawn points list cannot be empty")]
    ///     public List&lt;Transform&gt; spawnPoints;
    ///
    ///     private void Start()
    ///     {
    ///         this.ValidateAssignments();
    ///     }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ValidateAssignmentAttribute : PropertyAttribute
    {
        /// <summary>
        /// The type of message to display in the inspector when the field is invalid.
        /// </summary>
        public ValidateAssignmentMessageType MessageType { get; }

        /// <summary>
        /// An optional custom message to display in the inspector when the field is invalid.
        /// If null or empty, a default message will be generated based on the field name.
        /// </summary>
        public string CustomMessage { get; }

        /// <summary>
        /// Creates a new ValidateAssignment attribute with default settings (warning message type, auto-generated message).
        /// </summary>
        public ValidateAssignmentAttribute()
            : this(ValidateAssignmentMessageType.Warning, null) { }

        /// <summary>
        /// Creates a new ValidateAssignment attribute with the specified message type and auto-generated message.
        /// </summary>
        /// <param name="messageType">The type of message to display when invalid.</param>
        public ValidateAssignmentAttribute(ValidateAssignmentMessageType messageType)
            : this(messageType, null) { }

        /// <summary>
        /// Creates a new ValidateAssignment attribute with default message type (warning) and a custom message.
        /// </summary>
        /// <param name="customMessage">The custom message to display when invalid.</param>
        public ValidateAssignmentAttribute(string customMessage)
            : this(ValidateAssignmentMessageType.Warning, customMessage) { }

        /// <summary>
        /// Creates a new ValidateAssignment attribute with the specified message type and custom message.
        /// </summary>
        /// <param name="messageType">The type of message to display when invalid.</param>
        /// <param name="customMessage">The custom message to display when invalid.</param>
        public ValidateAssignmentAttribute(
            ValidateAssignmentMessageType messageType,
            string customMessage
        )
        {
            MessageType = messageType;
            CustomMessage = customMessage;
        }
    }

    public static class ValidateAssignmentExtensions
    {
        private static readonly Dictionary<Type, FieldInfo[]> FieldsByType = new();

        private static FieldInfo[] GetOrAdd(Type objectType)
        {
            return FieldsByType.GetOrAdd(
                objectType,
                type =>
                {
                    FieldInfo[] allFields = type.GetFields(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    );

                    if (allFields.Length == 0)
                    {
                        return Array.Empty<FieldInfo>();
                    }

                    using PooledResource<List<FieldInfo>> bufferResource =
                        Buffers<FieldInfo>.List.Get(out List<FieldInfo> result);
                    for (int i = 0; i < allFields.Length; i++)
                    {
                        FieldInfo field = allFields[i];
                        if (
                            field.IsAttributeDefined<ValidateAssignmentAttribute>(
                                out _,
                                inherit: false
                            )
                        )
                        {
                            result.Add(field);
                        }
                    }

                    if (result.Count == 0)
                    {
                        return Array.Empty<FieldInfo>();
                    }

                    return result.ToArray();
                }
            );
        }

        /// <summary>
        /// Logs a warning for every <see cref="ValidateAssignmentAttribute"/> field on
        /// <paramref name="o"/> that is null, empty, or has no elements.
        /// </summary>
        /// <param name="o">The Unity Object to validate. A null object is a no-op.</param>
        /// <remarks>
        /// Thread-safe: Yes.
        /// Performance: O(n) in the attributed field count; the field set is cached per type.
        /// Allocations: None when logging is disabled -- this method is
        /// <see cref="System.Diagnostics.ConditionalAttribute"/> on the same symbol set as
        /// <see cref="WallstopStudiosLogger.LogWarn"/>, its only observable effect. The compiler
        /// removes the entire call site, so the reflection walk and its
        /// <see cref="FieldInfo.GetValue(object)"/> boxing never happen in a release build.
        /// Use <see cref="AreAnyAssignmentsInvalid"/> when the answer is needed regardless of
        /// build configuration.
        /// </remarks>
        // NOT gated on UNITY_EDITOR alone: the symbol set below includes DEVELOPMENT_BUILD, which
        // keeps this alive for IL2CPP standalone test players, where the warning test expects the
        // log. The body is reflection metadata + FieldInfo.GetValue + a log call, all AOT-safe.
        [System.Diagnostics.Conditional(WallstopStudiosLogger.EnableUberLoggingSymbol)]
        [System.Diagnostics.Conditional(WallstopStudiosLogger.DevelopmentBuildSymbol)]
        [System.Diagnostics.Conditional(WallstopStudiosLogger.DebugSymbol)]
        [System.Diagnostics.Conditional(WallstopStudiosLogger.UnityEditorSymbol)]
        [System.Diagnostics.Conditional(WallstopStudiosLogger.WarnLoggingSymbol)]
        public static void ValidateAssignments(this Object o)
        {
            if (o == null)
            {
                return;
            }

            Type objectType = o.GetType();
            FieldInfo[] fields = GetOrAdd(objectType);

            foreach (FieldInfo field in fields)
            {
                bool logNotAssigned = IsFieldInvalid(field, o);

                if (logNotAssigned)
                {
                    o.LogNotAssigned(field.Name);
                }
            }
        }

        public static bool AreAnyAssignmentsInvalid(this Object o)
        {
            Type objectType = o.GetType();
            FieldInfo[] fields = GetOrAdd(objectType);

            foreach (FieldInfo field in fields)
            {
                if (IsFieldInvalid(field, o))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInvalid(IEnumerable enumerable)
        {
            try
            {
                IEnumerator enumerator = enumerable.GetEnumerator();
                try
                {
                    return !enumerator.MoveNext();
                }
                finally
                {
                    if (enumerator is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
            catch
            {
                return true;
            }
        }

        private static bool IsFieldInvalid(FieldInfo field, Object o)
        {
            object fieldValue = field.GetValue(o);
            return IsValueInvalid(fieldValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsValueInvalid(object value)
        {
            return value switch
            {
                Object unityObject => unityObject == null,
                string stringValue => string.IsNullOrWhiteSpace(stringValue),
                IList list => list.Count <= 0,
                ICollection collection => collection.Count <= 0,
                IEnumerable enumerable => IsInvalid(enumerable),
                _ => value == null,
            };
        }
    }
}
