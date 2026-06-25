// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_2021 || UNITY_2022 || UNITY_2023
#define UNH_NEEDS_DOES_NOT_RETURN_ATTRIBUTE_POLYFILL
#endif

// Polyfill DoesNotReturnAttribute on targets older than .NET 5 (e.g. Unity 2021.3 with
// .NET Standard 2.1). The attribute is purely informational for static analyzers; declaring
// it locally lets us decorate the Throw* helpers on every supported target without conditional
// source changes elsewhere.
#if !NET5_0_OR_GREATER && UNH_NEEDS_DOES_NOT_RETURN_ATTRIBUTE_POLYFILL
namespace System.Diagnostics.CodeAnalysis
{
    using System;

    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class DoesNotReturnAttribute : Attribute { }
}
#endif

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;

    /// <summary>
    /// Identifies the wire format involved in a serialization failure.
    /// </summary>
    public enum SerializationFormat
    {
        /// <summary>Unknown / not-yet-classified format.</summary>
        Unknown = 0,

        /// <summary>protobuf-net binary format.</summary>
        Protobuf = 1,

        /// <summary>System.Text.Json (standard options).</summary>
        Json = 2,

        /// <summary>System.Text.Json (fast/strict options).</summary>
        JsonFast = 3,

        /// <summary>Legacy <c>BinaryFormatter</c>.</summary>
        Binary = 4,

        /// <summary>Generic dispatcher (<see cref="Serializer.Serialize{T}(T, SerializationType)"/> / <see cref="Serializer.Deserialize{T}(byte[], SerializationType)"/>).</summary>
        Dispatcher = 5,
    }

    /// <summary>
    /// Identifies the direction of the failed operation.
    /// </summary>
    public enum SerializationOperation
    {
        /// <summary>Decoding bytes/string into an instance.</summary>
        Deserialize = 0,

        /// <summary>Encoding an instance into bytes/string.</summary>
        Serialize = 1,
    }

    /// <summary>
    /// Identifies which stage of the pipeline rejected the operation. Used to make stack-trace
    /// triage trivial without parsing exception messages.
    /// </summary>
    public enum SerializationStage
    {
        /// <summary>Argument guards rejected the input (e.g. null or empty payload).</summary>
        InputValidation = 0,

        /// <summary>The unified dispatcher rejected an unknown <see cref="SerializationType"/>.</summary>
        Dispatch = 1,

        /// <summary>Polymorphic root resolution failed (e.g. missing <c>[ProtoInclude]</c> or registration).</summary>
        TypeResolution = 2,

        /// <summary>The wire-format decoder rejected the payload.</summary>
        Decode = 3,

        /// <summary>The wire-format encoder failed.</summary>
        Encode = 4,

        /// <summary>Post-decode processing failed (e.g. collection wrapper unpack).</summary>
        PostProcess = 5,
    }

    /// <summary>
    /// Single root exception for every failure surfaced by <see cref="Serializer"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All public <see cref="Serializer"/> entry points are contractually allowed to throw exactly one
    /// exception type: <see cref="SerializationFailureException"/> (or one of its sealed subclasses).
    /// Framework exceptions (<c>ProtoBuf.ProtoException</c>, <c>System.Text.Json.JsonException</c>,
    /// <c>ArgumentNullException</c>, etc.) are wrapped at the format boundary and exposed as
    /// <see cref="Exception.InnerException"/>. Callers that want flow-control rather than throwing should
    /// use the matching <c>TryXxx</c> overloads.
    /// </para>
    /// <para>
    /// Designed for zero allocation on the happy path: the <see cref="Message"/> property is composed
    /// lazily on first access, so callers that never log the message pay no string-formatting cost.
    /// The exception itself only ever materializes when the throw path is taken (a slow path by
    /// definition).
    /// </para>
    /// <para>
    /// Thread-safety: the cached message field is read and written via plain reference assignment.
    /// Concurrent first reads from multiple threads may compose the message twice (benign, since the
    /// inputs are immutable and produce identical strings); the cached field then stabilizes.
    /// </para>
    /// </remarks>
    [Serializable]
    public class SerializationFailureException : Exception
    {
        private const string PlaceholderReason = "operation failed";

        // Lazy cache for the composed message. Intentionally excluded from binary serialization —
        // after BinaryFormatter round-trip, Message will recompose from the immutable properties.
        // Reference assignment is atomic on all .NET runtimes; concurrent compose-twice is benign.
        [NonSerialized]
        private string _composedMessage;

        /// <summary>The wire format involved in the failure.</summary>
        public SerializationFormat Format { get; }

        /// <summary>The direction of the operation (serialize or deserialize).</summary>
        public SerializationOperation Operation { get; }

        /// <summary>
        /// The declared (generic) type that the caller requested. May be <see langword="null"/> after
        /// <c>[Serializable]</c> round-trip on platforms with type-trimming (IL2CPP/WebGL) if the
        /// stored <see cref="Type.AssemblyQualifiedName"/> cannot be resolved at deserialization time.
        /// Always non-null on the throw path.
        /// </summary>
        public Type DeclaredType { get; }

        /// <summary>
        /// The runtime/concrete type involved (e.g. a polymorphic protobuf root, or the runtime type
        /// of the input on the serialize path). May be <see langword="null"/> if not yet resolved, or
        /// after <c>[Serializable]</c> round-trip on trimmed runtimes.
        /// </summary>
        public Type ResolvedType { get; }

        /// <summary>
        /// A short, allocation-free description of the offending input (e.g. <c>"byte[256]"</c>,
        /// <c>"null byte[]"</c>, <c>"string(len=0)"</c>). Never contains payload bytes themselves —
        /// safe to log without leaking sensitive data.
        /// </summary>
        public string InputDescriptor { get; }

        /// <summary>The pipeline stage that rejected the operation.</summary>
        public SerializationStage Stage { get; }

        /// <summary>A short, human-readable reason supplied by the throw site.</summary>
        public string Reason { get; }

        /// <summary>
        /// Constructs a new instance. Prefer the static <c>Throw*</c> helpers on this type so that
        /// throw sites stay terse and the JIT can keep the hot path tight.
        /// </summary>
        public SerializationFailureException(
            SerializationFormat format,
            SerializationOperation operation,
            Type declaredType,
            string inputDescriptor,
            SerializationStage stage,
            string reason,
            Type resolvedType = null,
            Exception innerException = null
        )
            : base(null, innerException)
        {
            Format = format;
            Operation = operation;
            DeclaredType = declaredType;
            ResolvedType = resolvedType;
            InputDescriptor = inputDescriptor ?? "<unknown>";
            Stage = stage;
            Reason = string.IsNullOrEmpty(reason) ? PlaceholderReason : reason;
        }

        /// <summary>
        /// Serialization constructor for cross-AppDomain / legacy binary serialization scenarios.
        /// </summary>
        protected SerializationFailureException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            // info is non-null in every documented BinaryFormatter/ISerializable code path — the base
            // ctor already validates this. We never short-circuit because that would leave the object
            // in a half-initialized state.
            Format = (SerializationFormat)info.GetInt32(nameof(Format));
            Operation = (SerializationOperation)info.GetInt32(nameof(Operation));
            // Types are persisted as AssemblyQualifiedName strings so the exception survives platforms
            // (IL2CPP / WebGL) where BinaryFormatter cannot serialize a raw Type instance.
            DeclaredType = ResolveTypeOrNull(info.GetString(nameof(DeclaredType)));
            ResolvedType = ResolveTypeOrNull(info.GetString(nameof(ResolvedType)));
            InputDescriptor = info.GetString(nameof(InputDescriptor)) ?? "<unknown>";
            Stage = (SerializationStage)info.GetInt32(nameof(Stage));
            Reason = info.GetString(nameof(Reason)) ?? PlaceholderReason;
        }

        private static Type ResolveTypeOrNull(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName))
            {
                return null;
            }
            // Type.GetType(name, throwOnError: false) returns null instead of throwing when the
            // type cannot be located, so no try/catch is necessary.
            return Type.GetType(assemblyQualifiedName, throwOnError: false);
        }

        /// <inheritdoc />
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            // info is non-null per the BinaryFormatter contract; base.GetObjectData validates it.
            info.AddValue(nameof(Format), (int)Format);
            info.AddValue(nameof(Operation), (int)Operation);
            // Persist Type as AssemblyQualifiedName so the round-trip works on IL2CPP / WebGL where
            // BinaryFormatter cannot serialize raw Type references.
            info.AddValue(nameof(DeclaredType), DeclaredType?.AssemblyQualifiedName);
            info.AddValue(nameof(ResolvedType), ResolvedType?.AssemblyQualifiedName);
            info.AddValue(nameof(InputDescriptor), InputDescriptor);
            info.AddValue(nameof(Stage), (int)Stage);
            info.AddValue(nameof(Reason), Reason);
        }

        /// <inheritdoc />
        public override string Message => _composedMessage ??= ComposeMessage();

        private string ComposeMessage()
        {
            // Plain string concatenation is used here (rather than DefaultInterpolatedStringHandler)
            // for compatibility with .NET Standard 2.1 / Unity 2021.3 IL2CPP, where the handler type
            // is not available. The throw path is already slow — the resulting String.Concat call is
            // comfortably under the cost of the exception throw + stack-walk.
            string declaredName = DeclaredType?.FullName ?? "<unknown>";
            string resolvedSuffix =
                ResolvedType == null || ResolvedType == DeclaredType
                    ? string.Empty
                    : " (resolved as " + ResolvedType.FullName + ")";
            return "["
                + Format
                + "."
                + Operation
                + "] "
                + Stage
                + " failed for "
                + declaredName
                + resolvedSuffix
                + " (input: "
                + InputDescriptor
                + "): "
                + Reason;
        }

        // -----------------------------------------------------------------------------------
        // Throw helpers — keep call sites tiny and JIT-friendly.
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// Throws <see cref="SerializationInputException"/> for a null payload.
        /// </summary>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowNullInput<T>(
            SerializationFormat format,
            SerializationOperation operation,
            string parameterName = "data"
        )
        {
            throw new SerializationInputException(
                format,
                operation,
                typeof(T),
                DescribeNull(parameterName),
                $"{parameterName} is null."
            );
        }

        /// <summary>
        /// Throws <see cref="SerializationInputException"/> for an empty payload.
        /// </summary>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowEmptyInput<T>(
            SerializationFormat format,
            SerializationOperation operation,
            string parameterName = "data"
        )
        {
            throw new SerializationInputException(
                format,
                operation,
                typeof(T),
                DescribeEmpty(parameterName),
                $"{parameterName} is empty."
            );
        }

        /// <summary>
        /// Throws <see cref="SerializationCorruptDataException"/> wrapping a decoder/encoder failure.
        /// </summary>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowCorrupt<T>(
            SerializationFormat format,
            SerializationOperation operation,
            int inputLength,
            SerializationStage stage,
            Exception inner,
            string reason = null
        )
        {
            throw new SerializationCorruptDataException(
                format,
                operation,
                typeof(T),
                DescribeBytes(inputLength),
                stage,
                reason ?? "Underlying codec rejected the payload.",
                inner
            );
        }

        /// <summary>
        /// Throws <see cref="SerializationTypeException"/> for an unresolved polymorphic root.
        /// </summary>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowTypeResolution<T>(
            SerializationFormat format,
            SerializationOperation operation,
            string reason
        )
        {
            throw new SerializationTypeException(
                format,
                operation,
                typeof(T),
                "<unresolved>",
                reason
            );
        }

        /// <summary>
        /// Throws <see cref="SerializationConfigurationException"/> for an invalid configuration value
        /// (e.g. an unknown <see cref="SerializationType"/>).
        /// </summary>
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowConfiguration<T>(
            SerializationFormat format,
            SerializationOperation operation,
            string reason
        )
        {
            throw new SerializationConfigurationException(
                format,
                operation,
                typeof(T),
                "<n/a>",
                reason
            );
        }

        internal static string DescribeBytes(int length) =>
            length switch
            {
                < 0 => "byte[?]",
                0 => "byte[0]",
                _ => "byte["
                    + length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "]",
            };

        internal static string DescribeString(int length) =>
            length switch
            {
                < 0 => "string(len=?)",
                0 => "string(len=0)",
                _ => "string(len="
                    + length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ")",
            };

        internal static string DescribeNull(string parameterName) =>
            parameterName == "data" || string.IsNullOrEmpty(parameterName)
                ? "null"
                : "null " + parameterName;

        internal static string DescribeEmpty(string parameterName) =>
            parameterName == "data" || string.IsNullOrEmpty(parameterName)
                ? "empty"
                : "empty " + parameterName;
    }

    /// <summary>
    /// Raised when the input to a <see cref="Serializer"/> method violates the parameter contract
    /// (null, empty, wrong shape). The caller passed bad arguments — there is no
    /// <see cref="Exception.InnerException"/>.
    /// </summary>
    /// <remarks>
    /// Swallowed by <c>Serializer.TryXxx</c> overloads.
    /// </remarks>
    [Serializable]
    public sealed class SerializationInputException : SerializationFailureException
    {
        /// <inheritdoc />
        public SerializationInputException(
            SerializationFormat format,
            SerializationOperation operation,
            Type declaredType,
            string inputDescriptor,
            string reason
        )
            : base(
                format,
                operation,
                declaredType,
                inputDescriptor,
                SerializationStage.InputValidation,
                reason
            ) { }

        private SerializationInputException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }

    /// <summary>
    /// Raised when the underlying codec (protobuf-net, System.Text.Json, BinaryFormatter, ...)
    /// rejects a non-null payload. The original framework exception is preserved as
    /// <see cref="Exception.InnerException"/>.
    /// </summary>
    /// <remarks>
    /// Swallowed by <c>Serializer.TryXxx</c> overloads.
    /// </remarks>
    [Serializable]
    public sealed class SerializationCorruptDataException : SerializationFailureException
    {
        /// <inheritdoc />
        public SerializationCorruptDataException(
            SerializationFormat format,
            SerializationOperation operation,
            Type declaredType,
            string inputDescriptor,
            SerializationStage stage,
            string reason,
            Exception innerException
        )
            : base(
                format,
                operation,
                declaredType,
                inputDescriptor,
                stage,
                reason,
                innerException: innerException
            ) { }

        private SerializationCorruptDataException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }

    /// <summary>
    /// Raised when a polymorphic protobuf root cannot be resolved (e.g. the declared type is an
    /// interface and no <c>[ProtoInclude]</c> chain or <see cref="Serializer.RegisterProtobufRoot{TDeclared, TRoot}"/>
    /// registration was found). This indicates a programmer/config error, not corrupt data.
    /// </summary>
    /// <remarks>
    /// <strong>Not</strong> swallowed by <c>Serializer.TryXxx</c> overloads — it surfaces a developer
    /// mistake that should fail loudly.
    /// </remarks>
    [Serializable]
    public sealed class SerializationTypeException : SerializationFailureException
    {
        /// <inheritdoc />
        public SerializationTypeException(
            SerializationFormat format,
            SerializationOperation operation,
            Type declaredType,
            string inputDescriptor,
            string reason
        )
            : base(
                format,
                operation,
                declaredType,
                inputDescriptor,
                SerializationStage.TypeResolution,
                reason
            ) { }

        private SerializationTypeException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }

    /// <summary>
    /// Raised when a <see cref="Serializer"/> entry point is invoked with an invalid configuration
    /// value (e.g. an undefined <see cref="SerializationType"/>).
    /// </summary>
    /// <remarks>
    /// <strong>Not</strong> swallowed by <c>Serializer.TryXxx</c> overloads — it surfaces a developer
    /// mistake.
    /// </remarks>
    [Serializable]
    public sealed class SerializationConfigurationException : SerializationFailureException
    {
        /// <inheritdoc />
        public SerializationConfigurationException(
            SerializationFormat format,
            SerializationOperation operation,
            Type declaredType,
            string inputDescriptor,
            string reason
        )
            : base(
                format,
                operation,
                declaredType,
                inputDescriptor,
                SerializationStage.Dispatch,
                reason
            ) { }

        private SerializationConfigurationException(
            SerializationInfo info,
            StreamingContext context
        )
            : base(info, context) { }
    }
}
