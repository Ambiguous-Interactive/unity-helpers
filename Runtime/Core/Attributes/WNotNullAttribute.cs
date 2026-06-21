// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEngine;

    /// <summary>
    /// Specifies the type of message displayed in the inspector when a field is null.
    /// </summary>
    public enum WNotNullMessageType
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
    /// Validates that a field is not null. Displays a warning or error in the inspector when the field is null,
    /// and throws an <see cref="ArgumentNullException"/> when <see cref="WNotNullAttributeExtensions.CheckForNulls"/>
    /// is called on an object containing a null field marked with this attribute.
    /// </summary>
    /// <example>
    /// <code>
    /// public class PlayerController : MonoBehaviour
    /// {
    ///     [WNotNull]
    ///     public Rigidbody2D rb;
    ///
    ///     [WNotNull(WNotNullMessageType.Error, "Audio source is required for sound effects")]
    ///     public AudioSource audioSource;
    ///
    ///     private void Awake()
    ///     {
    ///         this.CheckForNulls();
    ///     }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class WNotNullAttribute : PropertyAttribute
    {
        /// <summary>
        /// The type of message to display in the inspector when the field is null.
        /// </summary>
        public WNotNullMessageType MessageType { get; }

        /// <summary>
        /// An optional custom message to display in the inspector when the field is null.
        /// If null or empty, a default message will be generated based on the field name.
        /// </summary>
        public string CustomMessage { get; }

        /// <summary>
        /// Creates a new WNotNull attribute with default settings (warning message type, auto-generated message).
        /// </summary>
        public WNotNullAttribute()
            : this(WNotNullMessageType.Warning, null) { }

        /// <summary>
        /// Creates a new WNotNull attribute with the specified message type and auto-generated message.
        /// </summary>
        /// <param name="messageType">The type of message to display when null.</param>
        public WNotNullAttribute(WNotNullMessageType messageType)
            : this(messageType, null) { }

        /// <summary>
        /// Creates a new WNotNull attribute with default message type (warning) and a custom message.
        /// </summary>
        /// <param name="customMessage">The custom message to display when null.</param>
        public WNotNullAttribute(string customMessage)
            : this(WNotNullMessageType.Warning, customMessage) { }

        /// <summary>
        /// Creates a new WNotNull attribute with the specified message type and custom message.
        /// </summary>
        /// <param name="messageType">The type of message to display when null.</param>
        /// <param name="customMessage">The custom message to display when null.</param>
        public WNotNullAttribute(WNotNullMessageType messageType, string customMessage)
        {
            MessageType = messageType;
            CustomMessage = customMessage;
        }
    }

    public static class WNotNullAttributeExtensions
    {
        public static void CheckForNulls(this object o)
        {
            // Intentionally NOT gated on UNITY_EDITOR: the documented contract is that this throws
            // at runtime when a [WNotNull] field is null, and callers invoke it from Awake/Start to
            // fail fast in real builds. The body uses only GetFieldsWithAttribute + FieldInfo.GetValue,
            // both AOT-safe, so it works under IL2CPP. (The inspector visualization is editor-only and
            // lives in the property drawer, not here.)
            if (o == null || (o is UnityEngine.Object unityObj && unityObj == null))
            {
                return;
            }

            IEnumerable<FieldInfo> properties =
                Helper.ReflectionHelpers.GetFieldsWithAttribute<WNotNullAttribute>(o.GetType());

            foreach (FieldInfo field in properties)
            {
                object fieldValue = field.GetValue(o);

                switch (fieldValue)
                {
                    case UnityEngine.Object unityObject when unityObject == null:
                        throw new ArgumentNullException(field.Name);
                    case null:
                        throw new ArgumentNullException(field.Name);
                }
            }
        }
    }
}
