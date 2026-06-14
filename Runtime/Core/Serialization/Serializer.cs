// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using System.Buffers;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Reflection;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using JsonConverters;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using TypeConverter = JsonConverters.TypeConverter;

    internal static class SerializerEncoding
    {
        public static readonly Encoding Encoding;
        public static readonly JsonSerializerOptions NormalJsonOptions;
        public static readonly JsonSerializerOptions PrettyJsonOptions;
        public static readonly JsonSerializerOptions FastJsonOptions;
        public static readonly JsonSerializerOptions FastPocoJsonOptions;

        public static JsonSerializerOptions GetNormalJsonOptions()
        {
            return new JsonSerializerOptions
            {
                IgnoreReadOnlyFields = false,
                IgnoreReadOnlyProperties = false,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                IncludeFields = true,
                PropertyNameCaseInsensitive = true,
                NumberHandling =
                    JsonNumberHandling.AllowNamedFloatingPointLiterals
                    | JsonNumberHandling.AllowReadingFromString,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters =
                {
                    WGuidConverter.Instance,
                    RangeConverterFactory.Instance,
                    FastVector2IntConverter.Instance,
                    FastVector3IntConverter.Instance,
                    new JsonStringEnumConverter(),
                    Vector3Converter.Instance,
                    Vector2Converter.Instance,
                    Vector4Converter.Instance,
                    Vector2IntConverter.Instance,
                    Vector3IntConverter.Instance,
                    Matrix4x4Converter.Instance,
                    QuaternionConverter.Instance,
                    LayerMaskConverter.Instance,
                    ResolutionConverter.Instance,
                    RenderTextureDescriptorConverter.Instance,
                    MinMaxCurveConverter.Instance,
                    MinMaxGradientConverter.Instance,
                    ColorBlockConverter.Instance,
                    BoundingSphereConverter.Instance,
                    RaycastHitConverter.Instance,
                    TouchConverter.Instance,
                    SceneConverter.Instance,
                    PoseConverter.Instance,
                    PlaneConverter.Instance,
                    RayConverter.Instance,
                    Ray2DConverter.Instance,
                    RectOffsetConverter.Instance,
                    RangeIntConverter.Instance,
                    Hash128Converter.Instance,
                    AnimationCurveConverter.Instance,
                    GradientConverter.Instance,
                    SphericalHarmonicsL2Converter.Instance,
                    TypeConverter.Instance,
                    GameObjectConverter.Instance,
                    ColorConverter.Instance,
                    Color32Converter.Instance,
                    RectConverter.Instance,
                    RectIntConverter.Instance,
                    BoundsConverter.Instance,
                    BoundsIntConverter.Instance,
                    BitSetConverter.Instance,
                    ImmutableBitSetConverter.Instance,
                    DequeConverterFactory.Instance,
                    CyclicBufferConverterFactory.Instance,
                    SerializableSetConverterFactory.Instance,
                    SerializableDictionaryConverterFactory.Instance,
                    SerializableSortedDictionaryConverterFactory.Instance,
                },
            };
        }

        public static JsonSerializerOptions GetPrettyJsonOptions()
        {
            return new JsonSerializerOptions
            {
                IgnoreReadOnlyFields = false,
                IgnoreReadOnlyProperties = false,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                PropertyNameCaseInsensitive = true,
                IncludeFields = true,
                NumberHandling =
                    JsonNumberHandling.AllowNamedFloatingPointLiterals
                    | JsonNumberHandling.AllowReadingFromString,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters =
                {
                    WGuidConverter.Instance,
                    RangeConverterFactory.Instance,
                    FastVector2IntConverter.Instance,
                    FastVector3IntConverter.Instance,
                    new JsonStringEnumConverter(),
                    Vector3Converter.Instance,
                    Vector2Converter.Instance,
                    Vector4Converter.Instance,
                    Vector2IntConverter.Instance,
                    Vector3IntConverter.Instance,
                    Matrix4x4Converter.Instance,
                    QuaternionConverter.Instance,
                    LayerMaskConverter.Instance,
                    ResolutionConverter.Instance,
                    RenderTextureDescriptorConverter.Instance,
                    MinMaxCurveConverter.Instance,
                    MinMaxGradientConverter.Instance,
                    ColorBlockConverter.Instance,
                    BoundingSphereConverter.Instance,
                    RaycastHitConverter.Instance,
                    TouchConverter.Instance,
                    SceneConverter.Instance,
                    PoseConverter.Instance,
                    PlaneConverter.Instance,
                    RayConverter.Instance,
                    Ray2DConverter.Instance,
                    RectOffsetConverter.Instance,
                    RangeIntConverter.Instance,
                    Hash128Converter.Instance,
                    AnimationCurveConverter.Instance,
                    GradientConverter.Instance,
                    SphericalHarmonicsL2Converter.Instance,
                    TypeConverter.Instance,
                    GameObjectConverter.Instance,
                    ColorConverter.Instance,
                    Color32Converter.Instance,
                    RectConverter.Instance,
                    RectIntConverter.Instance,
                    BoundsConverter.Instance,
                    BoundsIntConverter.Instance,
                    BitSetConverter.Instance,
                    ImmutableBitSetConverter.Instance,
                    DequeConverterFactory.Instance,
                    CyclicBufferConverterFactory.Instance,
                    SerializableSetConverterFactory.Instance,
                    SerializableDictionaryConverterFactory.Instance,
                    SerializableSortedDictionaryConverterFactory.Instance,
                },
                WriteIndented = true,
            };
        }

        public static JsonSerializerOptions GetFastJsonOptions()
        {
            return new JsonSerializerOptions
            {
                IgnoreReadOnlyFields = false,
                IgnoreReadOnlyProperties = true,
                ReferenceHandler = null,
                PropertyNameCaseInsensitive = false,
                IncludeFields = false,
                NumberHandling = JsonNumberHandling.Strict,
                ReadCommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                Converters =
                {
                    WGuidConverter.Instance,
                    RangeConverterFactory.Instance,
                    FastVector2IntConverter.Instance,
                    FastVector3IntConverter.Instance,
                    Vector3Converter.Instance,
                    Vector2Converter.Instance,
                    Vector4Converter.Instance,
                    Vector2IntConverter.Instance,
                    Vector3IntConverter.Instance,
                    Matrix4x4Converter.Instance,
                    QuaternionConverter.Instance,
                    LayerMaskConverter.Instance,
                    ResolutionConverter.Instance,
                    RenderTextureDescriptorConverter.Instance,
                    MinMaxCurveConverter.Instance,
                    MinMaxGradientConverter.Instance,
                    ColorBlockConverter.Instance,
                    BoundingSphereConverter.Instance,
                    RaycastHitConverter.Instance,
                    TouchConverter.Instance,
                    SceneConverter.Instance,
                    PoseConverter.Instance,
                    PlaneConverter.Instance,
                    RayConverter.Instance,
                    Ray2DConverter.Instance,
                    RectOffsetConverter.Instance,
                    RangeIntConverter.Instance,
                    Hash128Converter.Instance,
                    AnimationCurveConverter.Instance,
                    GradientConverter.Instance,
                    SphericalHarmonicsL2Converter.Instance,
                    TypeConverter.Instance,
                    GameObjectConverter.Instance,
                    ColorConverter.Instance,
                    Color32Converter.Instance,
                    RectConverter.Instance,
                    RectIntConverter.Instance,
                    BoundsConverter.Instance,
                    BoundsIntConverter.Instance,
                    BitSetConverter.Instance,
                    ImmutableBitSetConverter.Instance,
                    DequeConverterFactory.Instance,
                    CyclicBufferConverterFactory.Instance,
                    SerializableSetConverterFactory.Instance,
                    SerializableDictionaryConverterFactory.Instance,
                    SerializableSortedDictionaryConverterFactory.Instance,
                },
            };
        }

        public static JsonSerializerOptions GetFastPocoJsonOptions()
        {
            return new JsonSerializerOptions
            {
                IgnoreReadOnlyFields = false,
                IgnoreReadOnlyProperties = false,
                ReferenceHandler = null,
                PropertyNameCaseInsensitive = false,
                IncludeFields = false,
                NumberHandling = JsonNumberHandling.Strict,
                ReadCommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                // No converters for POCO to minimize overhead
            };
        }

        static SerializerEncoding()
        {
            Encoding = Encoding.UTF8;
            NormalJsonOptions = GetNormalJsonOptions();
            PrettyJsonOptions = GetPrettyJsonOptions();
            FastJsonOptions = GetFastJsonOptions();
            FastPocoJsonOptions = GetFastPocoJsonOptions();
        }
    }

    /// <summary>
    /// Selects the wire format used by <see cref="Serializer"/>.
    /// </summary>
    /// <remarks>
    /// Choose a format based on your requirements:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Json"/> — Human‑readable and diff‑friendly. Uses System.Text.Json with Unity‑aware
    /// converters for common types (e.g., Vector2/3/4, Matrix4x4, Color, Type).
    /// Prefer for save files, configs, and tooling.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Protobuf"/> — Compact binary with great performance using protobuf‑net.
    /// Prefer for networking, large payloads, and memory‑sensitive scenarios.
    /// Requires opt‑in attributes like [ProtoContract]/[ProtoMember] or runtime models.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="SystemBinary"/> — .NET BinaryFormatter. Legacy and trusted‑only. Not
    /// cross‑version/portable and unsafe for untrusted input. Use only for ephemeral/dev data.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    public enum SerializationType
    {
        /// <summary>Unspecified format; not valid for read/write.</summary>
        [Obsolete("Please use a valid enum value")]
        None = 0,

        /// <summary>Legacy .NET BinaryFormatter. Trusted/ephemeral data only.</summary>
        [Obsolete(
            "BinaryFormatter is obsolete and unsafe for untrusted data. "
                + "Prefer Json or Protobuf for new code."
        )]
        SystemBinary = 1,

        /// <summary>protobuf-net compact binary. Best for networking and high-performance.</summary>
        Protobuf = 2,

        /// <summary>System.Text.Json text. Human-readable and diff-friendly.</summary>
        Json = 3,
    }

    /// <summary>
    /// Unified serialization helpers for JSON, protobuf‑net, and legacy BinaryFormatter.
    /// </summary>
    /// <remarks>
    /// Highlights
    /// <list type="bullet">
    /// <item><description>JSON: Uses pooled writers and Unity‑aware converters; supports pretty printing.</description></item>
    /// <item><description>Protobuf: Compact binary via protobuf‑net; supports interface/abstract types via root resolution or <see cref="RegisterProtobufRoot(Type, Type)"/>.</description></item>
    /// <item><description>Binary: Convenience for legacy only; do not feed untrusted data.</description></item>
    /// <item><description>Minimal allocations with ArrayPool-backed streams to reduce GC pressure.</description></item>
    /// </list>
    /// When to use what
    /// <list type="bullet">
    /// <item><description>Prefer <see cref="SerializationType.Json"/> for save systems, settings, and tools.</description></item>
    /// <item><description>Prefer <see cref="SerializationType.Protobuf"/> for networking, large or frequent messages.</description></item>
    /// <item><description>Reserve <see cref="SerializationType.SystemBinary"/> for trusted legacy scenarios only.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// JSON save/config
    /// <code>
    /// var save = new SaveData { Level = 3 };
    /// // To string
    /// string text = Serializer.JsonStringify(save, pretty: true);
    /// // File IO
    /// Serializer.WriteToJsonFile(save, "save.json", pretty: true);
    /// var loaded = Serializer.ReadFromJsonFile&lt;SaveData&gt;("save.json");
    /// </code>
    /// Protobuf networking
    /// <code>
    /// [ProtoContract]
    /// class NetworkMessage { [ProtoMember(1)] public int Id { get; set; } }
    /// byte[] bytes = Serializer.ProtoSerialize(new NetworkMessage { Id = 42 });
    /// NetworkMessage msg = Serializer.ProtoDeserialize&lt;NetworkMessage&gt;(bytes);
    /// </code>
    /// Legacy BinaryFormatter (trusted only)
    /// <code>
    /// byte[] blob = Serializer.BinarySerialize(obj);
    /// var roundtrip = Serializer.BinaryDeserialize&lt;SomeType&gt;(blob);
    /// </code>
    /// </example>
    public static class Serializer
    {
        /// <summary>
        /// Returns a copy of the package's Normal JSON options. The returned instance is independent
        /// of internal defaults, so modifying it won't affect global behavior. Cache and reuse the
        /// returned instance across calls to benefit from System.Text.Json metadata caches.
        /// </summary>
        public static JsonSerializerOptions CreateNormalJsonOptions() =>
            SerializerEncoding.GetNormalJsonOptions();

        /// <summary>
        /// Returns a copy of the package's Pretty (indented) JSON options.
        /// </summary>
        public static JsonSerializerOptions CreatePrettyJsonOptions() =>
            SerializerEncoding.GetPrettyJsonOptions();

        /// <summary>
        /// Returns a copy of the package's Fast JSON options, tuned for hot paths with reduced validation
        /// and features to minimize allocations and branching. See docs for trade-offs.
        /// </summary>
        public static JsonSerializerOptions CreateFastJsonOptions() =>
            SerializerEncoding.GetFastJsonOptions();

        /// <summary>
        /// Returns a copy of the package's Fast POCO JSON options.
        /// Strict, minimal, and with no Unity-specific converters.
        /// Use for pure POCO graphs when you want the fastest possible serialization/deserialization.
        /// Notes:
        /// - Case-sensitive property names (faster matching)
        /// - No comments/trailing commas; strict numbers only
        /// - IncludeFields = false (prefer properties for performance)
        /// - Returns a new instance each call; cache and reuse within your app to leverage STJ metadata caches
        /// </summary>
        public static JsonSerializerOptions CreateFastPocoJsonOptions() =>
            new(SerializerEncoding.FastPocoJsonOptions);

        // Small protobuf payloads benefit from protobuf-net's MemoryStream fast-path (TryGetBuffer).
        // Larger payloads see wins from our pooled read-only stream to avoid per-iteration allocations.
        private const int ProtobufMemoryStreamThreshold = 4096; // bytes

        // Optional zero-copy path if protobuf-net supports ReadOnlyMemory<byte>/ReadOnlySequence<byte> overloads
        private static readonly MethodInfo ProtoDeserializeTypeFromROM;
        private static readonly MethodInfo ProtoDeserializeTypeFromROS;
        private static readonly Func<
            Type,
            ReadOnlyMemory<byte>,
            object
        > ProtoDeserializeTypeFromROMFast;
        private static readonly Func<
            Type,
            ReadOnlySequence<byte>,
            object
        > ProtoDeserializeTypeFromROSFast;

        static Serializer()
        {
            // Initialize protobuf surrogates and any other serialization bootstrapping here
            // so initialization does not depend on JSON option access.
            ProtobufUnityModel.EnsureInitialized();
            try
            {
                MethodInfo[] methods = typeof(ProtoBuf.Serializer).GetMethods(
                    BindingFlags.Public | BindingFlags.Static
                );
                foreach (MethodInfo mi in methods)
                {
                    if (mi.Name != "Deserialize")
                    {
                        continue;
                    }

                    ParameterInfo[] pars = mi.GetParameters();
                    if (pars.Length != 2)
                    {
                        continue;
                    }

                    if (pars[0].ParameterType != typeof(Type))
                    {
                        continue;
                    }

                    Type p1 = pars[1].ParameterType;
                    switch (p1.IsGenericType)
                    {
                        case true when p1.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>):
                        {
                            Type genArg = p1.GetGenericArguments()[0];
                            if (genArg == typeof(byte))
                            {
                                ProtoDeserializeTypeFromROM ??= mi;
                                try
                                {
                                    ProtoDeserializeTypeFromROMFast =
                                        ReflectionHelpers.GetStaticMethodInvoker<
                                            Type,
                                            ReadOnlyMemory<byte>,
                                            object
                                        >(mi);
                                }
                                catch { }
                            }

                            break;
                        }
                        case true when p1.GetGenericTypeDefinition() == typeof(ReadOnlySequence<>):
                        {
                            Type genArg = p1.GetGenericArguments()[0];
                            if (genArg == typeof(byte))
                            {
                                ProtoDeserializeTypeFromROS ??= mi;
                                try
                                {
                                    ProtoDeserializeTypeFromROSFast =
                                        ReflectionHelpers.GetStaticMethodInvoker<
                                            Type,
                                            ReadOnlySequence<byte>,
                                            object
                                        >(mi);
                                }
                                catch { }
                            }

                            break;
                        }
                    }
                }
            }
            catch
            {
                // Reflection probing failed; keep nulls and fall back to streams
            }
        }

        private static readonly ConcurrentDictionary<Type, Type> ProtobufRootCache = new();
        private static readonly Type NoRootMarker = typeof(void);

        // Centralized decision logic for protobuf runtime vs declared handling
        internal static bool ShouldUseRuntimeTypeForProtobuf<T>(
            Type declared,
            T instance,
            bool forceRuntimeType
        )
        {
            if (forceRuntimeType)
            {
                return true;
            }

            if (declared == null)
            {
                return true;
            }

            if (declared.IsInterface || declared.IsAbstract || declared == typeof(object))
            {
                return true;
            }

            // Last resort: if the declared type is a reference type and the runtime type differs,
            // prefer using the runtime serializer to avoid protobuf-net subtype errors.
            if (!declared.IsValueType && instance != null && instance.GetType() != declared)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if the type is a serializable collection type that needs wrapper-based protobuf serialization.
        /// Returns true for SerializableHashSet, SerializableSortedSet, SerializableDictionary, SerializableSortedDictionary.
        /// </summary>
        private static bool IsSerializableCollectionType(Type type)
        {
            if (type == null || !type.IsGenericType)
            {
                return false;
            }

            Type genericDef = type.GetGenericTypeDefinition();
            return genericDef == typeof(SerializableHashSet<>)
                || genericDef == typeof(SerializableSortedSet<>)
                || genericDef == typeof(SerializableDictionary<,>)
                || genericDef == typeof(SerializableSortedDictionary<,>);
        }

        /// <summary>
        /// Cached reflection accessors for protobuf collection wrapper serialization.
        /// Uses ReflectionHelpers for cached delegate generation and nameof() for compile-time safety.
        /// </summary>
        private static class CollectionProtoAccessors
        {
            // Field names using nameof() for compile-time safety via internal access
            internal const string ItemsFieldName = SerializableHashSetSerializedPropertyNames.Items;
            internal const string KeysFieldName =
                SerializableDictionarySerializedPropertyNames.Keys;
            internal const string ValuesFieldName =
                SerializableDictionarySerializedPropertyNames.Values;

            // Use nameof() directly for fields accessible within this assembly
            internal const string PreserveSerializedEntriesFieldName = nameof(
                SerializableHashSet<int>._preserveSerializedEntries
            );
            internal const string OnBeforeSerializeMethodName = nameof(
                SerializableHashSet<int>.OnBeforeSerialize
            );
            internal const string OnAfterDeserializeMethodName = nameof(
                SerializableHashSet<int>.OnAfterDeserialize
            );

            // Wrapper field names (public fields, nameof() safe)
            internal const string WrapperItemsFieldName = nameof(
                SerializableHashSetProtoWrapper<int>.Items
            );
            internal const string WrapperKeysFieldName = nameof(
                SerializableDictionaryProtoWrapper<int, int>.Keys
            );
            internal const string WrapperValuesFieldName = nameof(
                SerializableDictionaryProtoWrapper<int, int>.Values
            );

            // Binding flags for field/method lookup
            private const BindingFlags InstanceFieldFlags =
                BindingFlags.NonPublic
                | BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.FlattenHierarchy;
            private const BindingFlags InstanceMethodFlags =
                BindingFlags.Public | BindingFlags.Instance;

            // Cached accessors per closed generic type
            private static readonly ConcurrentDictionary<
                Type,
                (
                    Func<object, object> GetItems,
                    Action<object, object> SetItems,
                    Func<object, object> GetKeys,
                    Action<object, object> SetKeys,
                    Func<object, object> GetValues,
                    Action<object, object> SetValues,
                    Action<object, object> SetPreserve,
                    Action<object> OnBeforeSerialize,
                    Action<object> OnAfterDeserialize
                )
            > TypeAccessors = new();

            /// <summary>
            /// Gets or creates cached accessors for the specified collection type.
            /// </summary>
            internal static (
                Func<object, object> GetItems,
                Action<object, object> SetItems,
                Func<object, object> GetKeys,
                Action<object, object> SetKeys,
                Func<object, object> GetValues,
                Action<object, object> SetValues,
                Action<object, object> SetPreserve,
                Action<object> OnBeforeSerialize,
                Action<object> OnAfterDeserialize
            ) GetAccessors(Type collectionType)
            {
                return TypeAccessors.GetOrAdd(collectionType, CreateAccessors);
            }

            private static (
                Func<object, object> GetItems,
                Action<object, object> SetItems,
                Func<object, object> GetKeys,
                Action<object, object> SetKeys,
                Func<object, object> GetValues,
                Action<object, object> SetValues,
                Action<object, object> SetPreserve,
                Action<object> OnBeforeSerialize,
                Action<object> OnAfterDeserialize
            ) CreateAccessors(Type type)
            {
                Type genericDef = type.GetGenericTypeDefinition();
                bool isSet =
                    genericDef == typeof(SerializableHashSet<>)
                    || genericDef == typeof(SerializableSortedSet<>);

                // Items field (for sets)
                Func<object, object> getItems = null;
                Action<object, object> setItems = null;
                if (isSet)
                {
                    FieldInfo itemsField = type.GetField(ItemsFieldName, InstanceFieldFlags);
                    if (itemsField != null)
                    {
                        getItems = ReflectionHelpers.GetFieldGetter(itemsField);
                        setItems = ReflectionHelpers.GetFieldSetter(itemsField);
                    }
                }

                // Keys/Values fields (for dictionaries)
                Func<object, object> getKeys = null;
                Action<object, object> setKeys = null;
                Func<object, object> getValues = null;
                Action<object, object> setValues = null;
                if (!isSet)
                {
                    FieldInfo keysField = type.GetField(KeysFieldName, InstanceFieldFlags);
                    FieldInfo valuesField = type.GetField(ValuesFieldName, InstanceFieldFlags);
                    if (keysField != null)
                    {
                        getKeys = ReflectionHelpers.GetFieldGetter(keysField);
                        setKeys = ReflectionHelpers.GetFieldSetter(keysField);
                    }
                    if (valuesField != null)
                    {
                        getValues = ReflectionHelpers.GetFieldGetter(valuesField);
                        setValues = ReflectionHelpers.GetFieldSetter(valuesField);
                    }
                }

                // PreserveSerializedEntries field
                Action<object, object> setPreserve = null;
                FieldInfo preserveField = type.GetField(
                    PreserveSerializedEntriesFieldName,
                    InstanceFieldFlags
                );
                if (preserveField != null)
                {
                    setPreserve = ReflectionHelpers.GetFieldSetter(preserveField);
                }

                // Lifecycle methods
                Action<object> onBeforeSerialize = null;
                Action<object> onAfterDeserialize = null;

                MethodInfo beforeMethod = type.GetMethod(
                    OnBeforeSerializeMethodName,
                    InstanceMethodFlags
                );
                if (beforeMethod != null)
                {
                    onBeforeSerialize = obj => beforeMethod.Invoke(obj, null);
                }

                MethodInfo afterMethod = type.GetMethod(
                    OnAfterDeserializeMethodName,
                    InstanceMethodFlags
                );
                if (afterMethod != null)
                {
                    onAfterDeserialize = obj => afterMethod.Invoke(obj, null);
                }

                return (
                    getItems,
                    setItems,
                    getKeys,
                    setKeys,
                    getValues,
                    setValues,
                    setPreserve,
                    onBeforeSerialize,
                    onAfterDeserialize
                );
            }

            /// <summary>
            /// Gets cached accessors for protobuf wrapper types.
            /// </summary>
            private static readonly ConcurrentDictionary<
                Type,
                (
                    Func<object, object> GetItems,
                    Action<object, object> SetItems,
                    Func<object, object> GetKeys,
                    Action<object, object> SetKeys,
                    Func<object, object> GetValues,
                    Action<object, object> SetValues
                )
            > WrapperAccessors = new();

            internal static (
                Func<object, object> GetItems,
                Action<object, object> SetItems,
                Func<object, object> GetKeys,
                Action<object, object> SetKeys,
                Func<object, object> GetValues,
                Action<object, object> SetValues
            ) GetWrapperAccessors(Type wrapperType, bool isSet)
            {
                return WrapperAccessors.GetOrAdd(
                    wrapperType,
                    t => CreateWrapperAccessors(t, isSet)
                );
            }

            private static (
                Func<object, object> GetItems,
                Action<object, object> SetItems,
                Func<object, object> GetKeys,
                Action<object, object> SetKeys,
                Func<object, object> GetValues,
                Action<object, object> SetValues
            ) CreateWrapperAccessors(Type wrapperType, bool isSet)
            {
                Func<object, object> getItems = null;
                Action<object, object> setItems = null;
                Func<object, object> getKeys = null;
                Action<object, object> setKeys = null;
                Func<object, object> getValues = null;
                Action<object, object> setValues = null;

                if (isSet)
                {
                    FieldInfo itemsField = wrapperType.GetField(WrapperItemsFieldName);
                    if (itemsField != null)
                    {
                        getItems = ReflectionHelpers.GetFieldGetter(itemsField);
                        setItems = ReflectionHelpers.GetFieldSetter(itemsField);
                    }
                }
                else
                {
                    FieldInfo keysField = wrapperType.GetField(WrapperKeysFieldName);
                    FieldInfo valuesField = wrapperType.GetField(WrapperValuesFieldName);
                    if (keysField != null)
                    {
                        getKeys = ReflectionHelpers.GetFieldGetter(keysField);
                        setKeys = ReflectionHelpers.GetFieldSetter(keysField);
                    }
                    if (valuesField != null)
                    {
                        getValues = ReflectionHelpers.GetFieldGetter(valuesField);
                        setValues = ReflectionHelpers.GetFieldSetter(valuesField);
                    }
                }

                return (getItems, setItems, getKeys, setKeys, getValues, setValues);
            }
        }

        /// <summary>
        /// Serializes a serializable collection to a protobuf wrapper and then to bytes.
        /// Uses cached reflection accessors for performance.
        /// </summary>
        private static byte[] SerializeCollectionWithWrapper<T>(T input)
        {
            Type type = typeof(T);
            Type genericDef = type.GetGenericTypeDefinition();
            bool isSet =
                genericDef == typeof(SerializableHashSet<>)
                || genericDef == typeof(SerializableSortedSet<>);

            // Get cached accessors for the collection type
            (
                Func<object, object> getItems,
                Action<object, object> _,
                Func<object, object> getKeys,
                Action<object, object> __,
                Func<object, object> getValues,
                Action<object, object> ___,
                Action<object, object> ____,
                Action<object> onBeforeSerialize,
                Action<object> _____
            ) = CollectionProtoAccessors.GetAccessors(type);

            // Determine wrapper type
            Type wrapperType;
            if (genericDef == typeof(SerializableHashSet<>))
            {
                wrapperType = typeof(SerializableHashSetProtoWrapper<>).MakeGenericType(
                    type.GetGenericArguments()
                );
            }
            else if (genericDef == typeof(SerializableSortedSet<>))
            {
                wrapperType = typeof(SerializableSortedSetProtoWrapper<>).MakeGenericType(
                    type.GetGenericArguments()
                );
            }
            else if (genericDef == typeof(SerializableDictionary<,>))
            {
                wrapperType = typeof(SerializableDictionaryProtoWrapper<,>).MakeGenericType(
                    type.GetGenericArguments()
                );
            }
            else if (genericDef == typeof(SerializableSortedDictionary<,>))
            {
                wrapperType = typeof(SerializableSortedDictionaryProtoWrapper<,>).MakeGenericType(
                    type.GetGenericArguments()
                );
            }
            else
            {
                throw new InvalidOperationException(
                    $"Type {type} is not a supported serializable collection type."
                );
            }

            // Get cached wrapper accessors
            (
                Func<object, object> _______,
                Action<object, object> setWrapperItems,
                Func<object, object> ________,
                Action<object, object> setWrapperKeys,
                Func<object, object> _________,
                Action<object, object> setWrapperValues
            ) = CollectionProtoAccessors.GetWrapperAccessors(wrapperType, isSet);

            // Call OnBeforeSerialize to ensure arrays are populated
            onBeforeSerialize?.Invoke(input);

            // Create wrapper and copy data
            object wrapper = Activator.CreateInstance(wrapperType);
            if (isSet)
            {
                object items = getItems?.Invoke(input);
                setWrapperItems?.Invoke(wrapper, items);
            }
            else
            {
                object keys = getKeys?.Invoke(input);
                object values = getValues?.Invoke(input);
                setWrapperKeys?.Invoke(wrapper, keys);
                setWrapperValues?.Invoke(wrapper, values);
            }

            // Serialize wrapper
            using Utils.PooledResource<PooledBufferStream> lease = PooledBufferStream.Rent(
                out PooledBufferStream stream
            );
            ProtoBuf.Serializer.NonGeneric.Serialize(stream, wrapper);

            byte[] buffer = null;
            stream.ToArrayExact(ref buffer);
            return buffer;
        }

        /// <summary>
        /// Deserializes a protobuf wrapper and constructs the serializable collection.
        /// Uses cached reflection accessors for performance.
        /// </summary>
        private static T DeserializeCollectionFromWrapper<T>(byte[] data)
        {
            Type type = typeof(T);
            Type genericDef = type.GetGenericTypeDefinition();
            bool isSet =
                genericDef == typeof(SerializableHashSet<>)
                || genericDef == typeof(SerializableSortedSet<>);

            // Get cached accessors for the collection type
            (
                Func<object, object> _,
                Action<object, object> setItems,
                Func<object, object> __,
                Action<object, object> setKeys,
                Func<object, object> ___,
                Action<object, object> setValues,
                Action<object, object> setPreserve,
                Action<object> ____,
                Action<object> onAfterDeserialize
            ) = CollectionProtoAccessors.GetAccessors(type);

            // Determine wrapper type
            Type wrapperType;
            if (genericDef == typeof(SerializableHashSet<>))
            {
                wrapperType = typeof(SerializableHashSetProtoWrapper<>).MakeGenericType(
                    type.GetGenericArguments()
                );
            }
            else if (genericDef == typeof(SerializableSortedSet<>))
            {
                wrapperType = typeof(SerializableSortedSetProtoWrapper<>).MakeGenericType(
                    type.GetGenericArguments()
                );
            }
            else if (genericDef == typeof(SerializableDictionary<,>))
            {
                wrapperType = typeof(SerializableDictionaryProtoWrapper<,>).MakeGenericType(
                    type.GetGenericArguments()
                );
            }
            else if (genericDef == typeof(SerializableSortedDictionary<,>))
            {
                wrapperType = typeof(SerializableSortedDictionaryProtoWrapper<,>).MakeGenericType(
                    type.GetGenericArguments()
                );
            }
            else
            {
                throw new InvalidOperationException(
                    $"Type {type} is not a supported serializable collection type."
                );
            }

            // Get cached wrapper accessors
            (
                Func<object, object> getWrapperItems,
                Action<object, object> _____,
                Func<object, object> getWrapperKeys,
                Action<object, object> ______,
                Func<object, object> getWrapperValues,
                Action<object, object> _______
            ) = CollectionProtoAccessors.GetWrapperAccessors(wrapperType, isSet);

            // Deserialize wrapper
            using MemoryStream ms = new(data, writable: false);
            object wrapper = ProtoBuf.Serializer.NonGeneric.Deserialize(wrapperType, ms);

            // Create result and copy data from wrapper
            object result = Activator.CreateInstance(type);
            if (isSet)
            {
                object items = getWrapperItems?.Invoke(wrapper);
                setItems?.Invoke(result, items);
            }
            else
            {
                object keys = getWrapperKeys?.Invoke(wrapper);
                object values = getWrapperValues?.Invoke(wrapper);
                setKeys?.Invoke(result, keys);
                setValues?.Invoke(result, values);
            }

            // Set preserve flag to prevent clearing during OnAfterDeserialize
            setPreserve?.Invoke(result, true);

            // Call OnAfterDeserialize to populate the backing collection
            onAfterDeserialize?.Invoke(result);

            return (T)result;
        }

        private static readonly Utils.WallstopGenericPool<BinaryFormatter> BinaryFormatterPool =
            new(() => new BinaryFormatter());

        private static readonly Utils.WallstopGenericPool<Utf8JsonWriter> JsonWriterPool = new(
            () => new Utf8JsonWriter(Stream.Null, new JsonWriterOptions { SkipValidation = true }),
            onRelease: writer =>
            {
                writer.Reset(Stream.Null);
            },
            onDisposal: stream => stream.Dispose()
        );

        /// <summary>
        /// Registers a concrete or abstract protobuf root type for a declared interface/abstract/object type.
        /// The root must be assignable to <paramref name="declared"/> and annotated with [ProtoContract].
        /// Subsequent deserializations to the declared type will use the registered root.
        /// </summary>
        /// <remarks>
        /// Use this when deserializing to an interface/abstract/object and you want deterministic root selection
        /// instead of relying on reflection inference.
        /// </remarks>
        /// <example>
        /// <code>
        /// // Given an interface and concrete implementation
        /// [ProtoContract] class PlayerJoined : IEvent { [ProtoMember(1)] public string Name { get; set; } }
        /// Serializer.RegisterProtobufRoot(typeof(IEvent), typeof(PlayerJoined));
        /// var evt = Serializer.ProtoDeserialize&lt;IEvent&gt;(bytes);
        /// </code>
        /// </example>
        /// <exception cref="ArgumentNullException">If declared or root is null.</exception>
        /// <exception cref="ArgumentException">If root is not assignable to declared or missing [ProtoContract].</exception>
        /// <exception cref="InvalidOperationException">If a conflicting root is already registered.</exception>
        public static void RegisterProtobufRoot(Type declared, Type root)
        {
            if (declared == null)
            {
                throw new ArgumentNullException(nameof(declared));
            }
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }
            if (!declared.IsAssignableFrom(root))
            {
                throw new ArgumentException(
                    $"Type {root.FullName} is not assignable to {declared.FullName}",
                    nameof(root)
                );
            }
            if (!ReflectionHelpers.HasAttributeSafe<ProtoContractAttribute>(root))
            {
                throw new ArgumentException(
                    $"Type {root.FullName} must be annotated with [ProtoContract]",
                    nameof(root)
                );
            }

            if (ProtobufRootCache.TryGetValue(declared, out Type existing))
            {
                if (existing != root && existing != NoRootMarker)
                {
                    throw new InvalidOperationException(
                        $"A different root {existing.FullName} is already registered for {declared.FullName}"
                    );
                }
            }

            ProtobufRootCache[declared] = root;
        }

        /// <summary>
        /// Generic convenience overload for registering a protobuf root type.
        /// </summary>
        /// <remarks>
        /// Useful for polymorphic APIs: map <typeparamref name="TDeclared"/> to <typeparamref name="TRoot"/> once,
        /// then call <see cref="ProtoDeserialize{T}(byte[])"/> for the declared type.
        /// </remarks>
        /// <example>
        /// <code>
        /// Serializer.RegisterProtobufRoot&lt;IEvent, PlayerJoined&gt;();
        /// IEvent evt = Serializer.ProtoDeserialize&lt;IEvent&gt;(bytes);
        /// </code>
        /// </example>
        public static void RegisterProtobufRoot<TDeclared, TRoot>()
            where TRoot : TDeclared
        {
            RegisterProtobufRoot(typeof(TDeclared), typeof(TRoot));
        }

        /// <summary>
        /// Deserializes a payload that was serialized with the specified <paramref name="serializationType"/>.
        /// </summary>
        /// <typeparam name="T">The target type.</typeparam>
        /// <param name="serialized">Payload bytes to decode.</param>
        /// <param name="serializationType">The format the payload is encoded with.</param>
        /// <returns>The decoded instance.</returns>
        /// <example>
        /// JSON
        /// <code>
        /// byte[] data = Serializer.JsonSerialize(save);
        /// SaveData loaded = Serializer.Deserialize&lt;SaveData&gt;(data, SerializationType.Json);
        /// </code>
        /// Protobuf
        /// <code>
        /// byte[] msg = Serializer.ProtoSerialize(message);
        /// NetworkMessage decoded = Serializer.Deserialize&lt;NetworkMessage&gt;(msg, SerializationType.Protobuf);
        /// </code>
        /// </example>
        public static T Deserialize<T>(byte[] serialized, SerializationType serializationType)
        {
            switch (serializationType)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                case SerializationType.SystemBinary:
#pragma warning restore CS0618 // Type or member is obsolete
                {
                    return BinaryDeserialize<T>(serialized);
                }
                case SerializationType.Protobuf:
                {
                    return ProtoDeserialize<T>(serialized);
                }
                case SerializationType.Json:
                {
                    return JsonDeserialize<T>(serialized);
                }
                default:
                {
                    SerializationFailureException.ThrowConfiguration<T>(
                        SerializationFormat.Dispatcher,
                        SerializationOperation.Deserialize,
                        $"Unknown SerializationType '{(int)serializationType}'."
                    );
                    return default;
                }
            }
        }

        /// <summary>
        /// Attempts to deserialize bytes using <paramref name="serializationType"/>. Returns <see langword="false"/>
        /// and sets <paramref name="value"/> to <see langword="default"/> if the payload is null/empty or the
        /// codec rejects it. Programmer errors (unknown <see cref="SerializationType"/>, unresolved polymorphic
        /// root) still throw <see cref="SerializationFailureException"/>.
        /// </summary>
        public static bool TryDeserialize<T>(
            byte[] serialized,
            SerializationType serializationType,
            out T value
        )
        {
            try
            {
                value = Deserialize<T>(serialized, serializationType);
                return true;
            }
            catch (SerializationInputException)
            {
                value = default;
                return false;
            }
            catch (SerializationCorruptDataException)
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Serializes an instance into bytes using the specified <paramref name="serializationType"/>.
        /// </summary>
        /// <typeparam name="T">The instance type.</typeparam>
        /// <param name="instance">The instance to encode.</param>
        /// <param name="serializationType">The target wire format.</param>
        /// <returns>Serialized bytes.</returns>
        /// <example>
        /// <code>
        /// // As bytes
        /// byte[] data = Serializer.Serialize(save, SerializationType.Json);
        /// // Later
        /// SaveData loaded = Serializer.Deserialize&lt;SaveData&gt;(data, SerializationType.Json);
        /// </code>
        /// </example>
        public static byte[] Serialize<T>(T instance, SerializationType serializationType)
        {
            switch (serializationType)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                case SerializationType.SystemBinary:
#pragma warning restore CS0618 // Type or member is obsolete
                {
                    return BinarySerialize(instance);
                }
                case SerializationType.Protobuf:
                {
                    return ProtoSerialize(instance);
                }
                case SerializationType.Json:
                {
                    return JsonSerialize(instance);
                }
                default:
                {
                    SerializationFailureException.ThrowConfiguration<T>(
                        SerializationFormat.Dispatcher,
                        SerializationOperation.Serialize,
                        $"Unknown SerializationType '{(int)serializationType}'."
                    );
                    return default;
                }
            }
        }

        /// <summary>
        /// Serializes into a caller-provided buffer to avoid an extra allocation.
        /// </summary>
        /// <typeparam name="T">The instance type.</typeparam>
        /// <param name="instance">The instance to encode.</param>
        /// <param name="serializationType">The target wire format.</param>
        /// <param name="buffer">Destination buffer reference. Resized if too small.</param>
        /// <returns>The number of valid bytes written to <paramref name="buffer"/>.</returns>
        public static int Serialize<T>(
            T instance,
            SerializationType serializationType,
            ref byte[] buffer
        )
        {
            switch (serializationType)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                case SerializationType.SystemBinary:
#pragma warning restore CS0618 // Type or member is obsolete
                {
                    return BinarySerialize(instance, ref buffer);
                }
                case SerializationType.Protobuf:
                {
                    return ProtoSerialize(instance, ref buffer);
                }
                case SerializationType.Json:
                {
                    return JsonSerialize(instance, ref buffer);
                }
                default:
                {
                    SerializationFailureException.ThrowConfiguration<T>(
                        SerializationFormat.Dispatcher,
                        SerializationOperation.Serialize,
                        $"Unknown SerializationType '{(int)serializationType}'."
                    );
                    return 0;
                }
            }
        }

        /// <summary>
        /// Deserializes bytes using legacy <c>BinaryFormatter</c>.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="data">Serialized bytes.</param>
        /// <remarks>
        /// Security: Never deserialize untrusted data with BinaryFormatter. It is obsolete and unsafe.
        /// Portability: Fragile across versions/platforms; avoid for long‑lived data.
        /// Prefer <see cref="JsonDeserialize{T}(string, System.Type, System.Text.Json.JsonSerializerOptions)"/> or <see cref="ProtoDeserialize{T}(byte[])"/> in production.
        /// </remarks>
        public static T BinaryDeserialize<T>(byte[] data)
        {
            if (data == null)
            {
                SerializationFailureException.ThrowNullInput<T>(
                    SerializationFormat.Binary,
                    SerializationOperation.Deserialize
                );
            }
            if (data.Length == 0)
            {
                SerializationFailureException.ThrowEmptyInput<T>(
                    SerializationFormat.Binary,
                    SerializationOperation.Deserialize
                );
            }

            try
            {
                using Utils.PooledResource<PooledReadOnlyMemoryStream> lease =
                    PooledReadOnlyMemoryStream.Rent(out PooledReadOnlyMemoryStream stream);
                stream.SetBuffer(data);
                using Utils.PooledResource<BinaryFormatter> fmtLease = BinaryFormatterPool.Get(
                    out BinaryFormatter binaryFormatter
                );
                return (T)binaryFormatter.Deserialize(stream);
            }
            catch (SerializationFailureException)
            {
                throw;
            }
            catch (Exception e)
            {
                SerializationFailureException.ThrowCorrupt<T>(
                    SerializationFormat.Binary,
                    SerializationOperation.Deserialize,
                    data.Length,
                    SerializationStage.Decode,
                    e,
                    "BinaryFormatter rejected the payload."
                );
                return default;
            }
        }

        /// <summary>
        /// Attempts to deserialize bytes with <c>BinaryFormatter</c>. Returns <see langword="false"/>
        /// for null/empty/corrupt payloads.
        /// </summary>
        public static bool TryBinaryDeserialize<T>(byte[] data, out T value)
        {
            try
            {
                value = BinaryDeserialize<T>(data);
                return true;
            }
            catch (SerializationInputException)
            {
                value = default;
                return false;
            }
            catch (SerializationCorruptDataException)
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Serializes an object using legacy <c>BinaryFormatter</c>.
        /// </summary>
        /// <typeparam name="T">Instance type.</typeparam>
        /// <param name="input">Object to serialize.</param>
        /// <returns>Serialized bytes.</returns>
        /// <remarks>
        /// Use for trusted, temporary data only. Not safe for untrusted input. Prefer JSON or protobuf.
        /// </remarks>
        public static byte[] BinarySerialize<T>(T input)
        {
            using Utils.PooledResource<PooledBufferStream> lease = PooledBufferStream.Rent(
                out PooledBufferStream stream
            );
            using Utils.PooledResource<BinaryFormatter> fmtLease = BinaryFormatterPool.Get(
                out BinaryFormatter binaryFormatter
            );
            binaryFormatter.Serialize(stream, input);
            byte[] buffer = null;
            stream.ToArrayExact(ref buffer);
            return buffer;
        }

        /// <summary>
        /// Serializes to a caller buffer using <c>BinaryFormatter</c>.
        /// </summary>
        /// <typeparam name="T">Instance type.</typeparam>
        /// <param name="input">Object to serialize.</param>
        /// <param name="buffer">Destination buffer reference. Resized if necessary.</param>
        /// <returns>Number of bytes written.</returns>
        public static int BinarySerialize<T>(T input, ref byte[] buffer)
        {
            using Utils.PooledResource<PooledBufferStream> lease = PooledBufferStream.Rent(
                out PooledBufferStream stream
            );
            using Utils.PooledResource<BinaryFormatter> fmtLease = BinaryFormatterPool.Get(
                out BinaryFormatter binaryFormatter
            );
            binaryFormatter.Serialize(stream, input);
            return stream.ToArrayExact(ref buffer);
        }

        /// <summary>
        /// Deserializes protobuf‑net bytes to <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="data">Encoded protobuf payload.</param>
        /// <returns>The decoded instance.</returns>
        /// <remarks>
        /// Polymorphism and interfaces:
        /// - If <typeparamref name="T"/> is an interface, abstract type, or <see cref="object"/>, deserialization
        ///   requires a concrete root type. We resolve this by either using an abstract base that is marked with
        ///   <c>[ProtoContract]</c> and <c>[ProtoInclude]</c> for all subtypes (e.g.,
        ///   <c>AbstractRandom</c> in the random package) or by a previously registered mapping via
        ///   <see cref="RegisterProtobufRoot{TDeclared, TRoot}()"/>. If no unique root is found, a
        ///   <see cref="ProtoException"/> is thrown to avoid ambiguous heuristics.
        ///
        /// Examples
        /// <code><![CDATA[
        /// // 1) Using an abstract base with [ProtoInclude]s
        /// [ProtoContract]
        /// abstract class Message { }
        /// [ProtoContract] class Ping : Message { [ProtoMember(1)] public int Id { get; set; } }
        /// // Deserialize to the abstract base; protobuf-net resolves to Ping
        /// Message m = Serializer.ProtoDeserialize<Message>(bytes);
        ///
        /// // 2) Using an interface by registering a root
        /// interface IEvent { }
        /// [ProtoContract] class PlayerJoined : IEvent { [ProtoMember(1)] public string Name { get; set; } }
        /// Serializer.RegisterProtobufRoot<IEvent, PlayerJoined>();
        /// IEvent evt = Serializer.ProtoDeserialize<IEvent>(bytes);
        ///
        /// // 3) Overload that specifies the concrete type explicitly
        /// IEvent evt2 = Serializer.ProtoDeserialize<IEvent>(bytes, typeof(PlayerJoined));
        /// ]]></code>
        /// </remarks>
        public static T ProtoDeserialize<T>(byte[] data)
        {
            if (data == null)
            {
                SerializationFailureException.ThrowNullInput<T>(
                    SerializationFormat.Protobuf,
                    SerializationOperation.Deserialize
                );
            }
            if (data.Length == 0)
            {
                SerializationFailureException.ThrowEmptyInput<T>(
                    SerializationFormat.Protobuf,
                    SerializationOperation.Deserialize
                );
            }

            // Intercept serializable collection types to use wrapper-based deserialization
            // This bypasses protobuf-net's collection detection which ignores IgnoreListHandling
            Type declared = typeof(T);
            if (IsSerializableCollectionType(declared))
            {
                try
                {
                    return DeserializeCollectionFromWrapper<T>(data);
                }
                catch (SerializationFailureException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    SerializationFailureException.ThrowCorrupt<T>(
                        SerializationFormat.Protobuf,
                        SerializationOperation.Deserialize,
                        data.Length,
                        SerializationStage.PostProcess,
                        e,
                        "Failed to unpack protobuf collection wrapper."
                    );
                    return default;
                }
            }

            try
            {
                // Prefer zero-copy ROM/ROS overloads when available
                if (ProtoDeserializeTypeFromROMFast != null)
                {
                    ReadOnlyMemory<byte> rom = new(data);
                    if (
                        ShouldUseRuntimeTypeForProtobuf<T>(
                            declared,
                            default,
                            forceRuntimeType: false
                        )
                    )
                    {
                        Type root = ResolveProtobufRootType(declared);
                        if (root != null)
                        {
                            return (T)ProtoDeserializeTypeFromROMFast(root, rom);
                        }

                        SerializationFailureException.ThrowTypeResolution<T>(
                            SerializationFormat.Protobuf,
                            SerializationOperation.Deserialize,
                            $"Unable to resolve a unique protobuf root for declared type {declared.FullName}. Register a root via RegisterProtobufRoot or annotate a shared abstract base with [ProtoInclude]s."
                        );
                    }

                    return (T)ProtoDeserializeTypeFromROMFast(declared, rom);
                }

                if (ProtoDeserializeTypeFromROSFast != null)
                {
                    ReadOnlySequence<byte> ros = new(data);
                    if (
                        ShouldUseRuntimeTypeForProtobuf<T>(
                            declared,
                            default,
                            forceRuntimeType: false
                        )
                    )
                    {
                        Type root = ResolveProtobufRootType(declared);
                        if (root != null)
                        {
                            return (T)ProtoDeserializeTypeFromROSFast(root, ros);
                        }

                        SerializationFailureException.ThrowTypeResolution<T>(
                            SerializationFormat.Protobuf,
                            SerializationOperation.Deserialize,
                            $"Unable to resolve a unique protobuf root for declared type {declared.FullName}. Register a root via RegisterProtobufRoot or annotate a shared abstract base with [ProtoInclude]s."
                        );
                    }

                    return (T)ProtoDeserializeTypeFromROSFast(declared, ros);
                }

                // For small payloads, allow protobuf-net to use MemoryStream's non-copy buffer access
                if (data.Length <= ProtobufMemoryStreamThreshold)
                {
                    using MemoryStream ms = new(data, writable: false);
                    if (
                        ShouldUseRuntimeTypeForProtobuf<T>(
                            declared,
                            default,
                            forceRuntimeType: false
                        )
                    )
                    {
                        Type root = ResolveProtobufRootType(declared);
                        if (root != null)
                        {
                            return (T)ProtoBuf.Serializer.Deserialize(root, ms);
                        }

                        SerializationFailureException.ThrowTypeResolution<T>(
                            SerializationFormat.Protobuf,
                            SerializationOperation.Deserialize,
                            $"Unable to resolve a unique protobuf root for declared type {declared.FullName}. Register a root via RegisterProtobufRoot or annotate a shared abstract base with [ProtoInclude]s."
                        );
                    }

                    return ProtoBuf.Serializer.Deserialize<T>(ms);
                }

                // For larger payloads, prefer pooled stream to avoid per-iteration allocations
                using Utils.PooledResource<PooledReadOnlyMemoryStream> lease =
                    PooledReadOnlyMemoryStream.Rent(out PooledReadOnlyMemoryStream stream);
                stream.SetBuffer(data);

                Type declaredLarge = typeof(T);
                if (
                    ShouldUseRuntimeTypeForProtobuf<T>(
                        declaredLarge,
                        default,
                        forceRuntimeType: false
                    )
                )
                {
                    Type root = ResolveProtobufRootType(declaredLarge);
                    if (root != null)
                    {
                        return (T)ProtoBuf.Serializer.Deserialize(root, stream);
                    }

                    SerializationFailureException.ThrowTypeResolution<T>(
                        SerializationFormat.Protobuf,
                        SerializationOperation.Deserialize,
                        $"Unable to resolve a unique protobuf root for declared type {declaredLarge.FullName}. Register a root via RegisterProtobufRoot or annotate a shared abstract base with [ProtoInclude]s."
                    );
                }

                return ProtoBuf.Serializer.Deserialize<T>(stream);
            }
            catch (SerializationFailureException)
            {
                throw;
            }
            catch (Exception e)
            {
                SerializationFailureException.ThrowCorrupt<T>(
                    SerializationFormat.Protobuf,
                    SerializationOperation.Deserialize,
                    data.Length,
                    SerializationStage.Decode,
                    e,
                    "protobuf-net rejected the payload."
                );
                return default;
            }
        }

        /// <summary>
        /// Attempts to deserialize a protobuf payload. Returns <see langword="false"/> and sets
        /// <paramref name="value"/> to <see langword="default"/> for null/empty/corrupt input.
        /// Polymorphic-root resolution failures still throw (programmer error).
        /// </summary>
        public static bool TryProtoDeserialize<T>(byte[] data, out T value)
        {
            try
            {
                value = ProtoDeserialize<T>(data);
                return true;
            }
            catch (SerializationInputException)
            {
                value = default;
                return false;
            }
            catch (SerializationCorruptDataException)
            {
                value = default;
                return false;
            }
        }

        // Attempts to resolve a concrete root type for protobuf-net when the declared generic type
        // is interface/abstract/object.
        // Rules:
        // - If a root is explicitly registered, use it.
        // - If the declared type itself is an abstract [ProtoContract] (with [ProtoInclude]s), return the declared type
        //   to allow protobuf-net to handle subtypes via includes.
        // - Do not auto-pick implementations based on reflection heuristics; require registration instead to avoid
        //   ambiguity and brittle ordering of loaded types.
        private static Type ResolveProtobufRootType(Type declared)
        {
            if (declared == null)
            {
                return null;
            }

            // If declared is already a usable concrete type, just return it
            if (!declared.IsInterface && !declared.IsAbstract && declared != typeof(object))
            {
                return declared;
            }

            // If declared itself is an abstract [ProtoContract] base with [ProtoInclude]s, prefer it
            if (
                declared.IsAbstract
                && ReflectionHelpers.HasAttributeSafe<ProtoContractAttribute>(declared)
            )
            {
                return declared;
            }

            if (ProtobufRootCache.TryGetValue(declared, out Type cached))
            {
                return cached == NoRootMarker ? null : cached;
            }

            // Try to resolve a unique abstract [ProtoContract] base that implements the declared interface.
            // This allows scenarios like: IRandom -> AbstractRandom (annotated with [ProtoContract] + [ProtoInclude]).
            // We deliberately keep the search local to the declaring assembly to avoid brittle cross-assembly heuristics.
            if (declared.IsInterface && declared != typeof(object))
            {
                try
                {
                    Type[] types = ReflectionHelpers.GetTypesFromAssembly(declared.Assembly);
                    using PooledResource<List<Type>> candidatesLease = Buffers<Type>.List.Get(
                        out List<Type> candidates
                    );
                    for (int i = 0; i < types.Length; i++)
                    {
                        Type t = types[i];
                        if (
                            t.IsClass
                            && t.IsAbstract
                            && declared.IsAssignableFrom(t)
                            && ReflectionHelpers.HasAttributeSafe<ProtoContractAttribute>(t)
                        )
                        {
                            candidates.Add(t);
                        }
                    }

                    switch (candidates.Count)
                    {
                        case 1:
                        {
                            Type root = candidates[0];
                            ProtobufRootCache[declared] = root;
                            return root;
                        }
                        case > 1:
                        {
                            // Prefer a candidate that explicitly declares [ProtoInclude]s if this disambiguates
                            using PooledResource<List<Type>> includeCandidatesLease =
                                Buffers<Type>.List.Get(out List<Type> includeCandidates);
                            for (int i = 0; i < candidates.Count; i++)
                            {
                                Type t = candidates[i];
                                if (ReflectionHelpers.HasAttributeSafe<ProtoIncludeAttribute>(t))
                                {
                                    includeCandidates.Add(t);
                                }
                            }

                            if (includeCandidates.Count == 1)
                            {
                                Type root = includeCandidates[0];
                                ProtobufRootCache[declared] = root;
                                return root;
                            }

                            break;
                        }
                    }
                }
                catch
                {
                    // Reflection may fail in some restricted environments; fall through to marker/null
                }
            }

            ProtobufRootCache[declared] = NoRootMarker;
            return null;
        }

        /// <summary>
        /// Deserializes protobuf‑net bytes into the provided <paramref name="type"/>.
        /// </summary>
        /// <typeparam name="T">Expected return type after cast.</typeparam>
        /// <param name="data">Encoded protobuf payload.</param>
        /// <param name="type">Concrete type to deserialize to.</param>
        /// <returns>The decoded instance cast to <typeparamref name="T"/>.</returns>
        public static T ProtoDeserialize<T>(byte[] data, Type type)
        {
            if (data == null)
            {
                SerializationFailureException.ThrowNullInput<T>(
                    SerializationFormat.Protobuf,
                    SerializationOperation.Deserialize
                );
            }
            if (data.Length == 0)
            {
                SerializationFailureException.ThrowEmptyInput<T>(
                    SerializationFormat.Protobuf,
                    SerializationOperation.Deserialize
                );
            }
            if (type == null)
            {
                SerializationFailureException.ThrowConfiguration<T>(
                    SerializationFormat.Protobuf,
                    SerializationOperation.Deserialize,
                    "Target Type argument is null."
                );
            }

            try
            {
                // Prefer zero-copy ROM/ROS overloads when available
                if (ProtoDeserializeTypeFromROMFast != null)
                {
                    ReadOnlyMemory<byte> rom = new(data);
                    return (T)ProtoDeserializeTypeFromROMFast(type, rom);
                }
                if (ProtoDeserializeTypeFromROSFast != null)
                {
                    ReadOnlySequence<byte> ros = new(data);
                    return (T)ProtoDeserializeTypeFromROSFast(type, ros);
                }

                if (data.Length <= ProtobufMemoryStreamThreshold)
                {
                    using MemoryStream ms = new(data, writable: false);
                    return (T)ProtoBuf.Serializer.Deserialize(type, ms);
                }

                using Utils.PooledResource<PooledReadOnlyMemoryStream> lease =
                    PooledReadOnlyMemoryStream.Rent(out PooledReadOnlyMemoryStream stream);
                stream.SetBuffer(data);
                return (T)ProtoBuf.Serializer.Deserialize(type, stream);
            }
            catch (SerializationFailureException)
            {
                throw;
            }
            catch (Exception e)
            {
                SerializationFailureException.ThrowCorrupt<T>(
                    SerializationFormat.Protobuf,
                    SerializationOperation.Deserialize,
                    data.Length,
                    SerializationStage.Decode,
                    e,
                    "protobuf-net rejected the payload."
                );
                return default;
            }
        }

        /// <summary>
        /// Attempts to deserialize a protobuf payload into the supplied <paramref name="type"/>.
        /// Returns <see langword="false"/> on null/empty/corrupt input. A null
        /// <paramref name="type"/> still throws (programmer error).
        /// </summary>
        public static bool TryProtoDeserialize<T>(byte[] data, Type type, out T value)
        {
            try
            {
                value = ProtoDeserialize<T>(data, type);
                return true;
            }
            catch (SerializationInputException)
            {
                value = default;
                return false;
            }
            catch (SerializationCorruptDataException)
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Serializes an instance to protobuf‑net bytes.
        /// </summary>
        /// <typeparam name="T">Declared type.</typeparam>
        /// <param name="input">The instance to serialize.</param>
        /// <param name="forceRuntimeType">When true, always serialize as the runtime type; otherwise uses declared type unless it is interface/abstract/object.</param>
        /// <returns>Serialized bytes.</returns>
        /// <example>
        /// <code>
        /// [ProtoContract]
        /// class NetworkMessage { [ProtoMember(1)] public int Id { get; set; } }
        /// var bytes = Serializer.ProtoSerialize(new NetworkMessage { Id = 5 });
        /// var msg = Serializer.ProtoDeserialize&lt;NetworkMessage&gt;(bytes);
        /// </code>
        /// </example>
        public static byte[] ProtoSerialize<T>(T input, bool forceRuntimeType = false)
        {
            Type declared = typeof(T);

            // Intercept serializable collection types to use wrapper-based serialization
            // This bypasses protobuf-net's collection detection which ignores IgnoreListHandling
            if (IsSerializableCollectionType(declared))
            {
                return SerializeCollectionWithWrapper(input);
            }

            using Utils.PooledResource<PooledBufferStream> lease = PooledBufferStream.Rent(
                out PooledBufferStream stream
            );
            bool useRuntime = ShouldUseRuntimeTypeForProtobuf(declared, input, forceRuntimeType);

            if (useRuntime)
            {
                ProtoBuf.Serializer.NonGeneric.Serialize(stream, input);
            }
            else
            {
                ProtoBuf.Serializer.Serialize(stream, input);
            }

            byte[] buffer = null;
            stream.ToArrayExact(ref buffer);
            return buffer;
        }

        /// <summary>
        /// Serializes an instance to protobuf‑net bytes into a caller-provided buffer.
        /// </summary>
        /// <typeparam name="T">Declared type.</typeparam>
        /// <param name="input">The instance to serialize.</param>
        /// <param name="buffer">Destination buffer reference. Resized if necessary.</param>
        /// <param name="forceRuntimeType">When true, always serialize as the runtime type.</param>
        /// <returns>Number of bytes written.</returns>
        public static int ProtoSerialize<T>(
            T input,
            ref byte[] buffer,
            bool forceRuntimeType = false
        )
        {
            Type declared = typeof(T);

            // Intercept serializable collection types to use wrapper-based serialization
            if (IsSerializableCollectionType(declared))
            {
                byte[] result = SerializeCollectionWithWrapper(input);
                if (buffer == null || buffer.Length < result.Length)
                {
                    buffer = new byte[result.Length];
                }
                Array.Copy(result, buffer, result.Length);
                return result.Length;
            }

            using Utils.PooledResource<PooledBufferStream> lease = PooledBufferStream.Rent(
                out PooledBufferStream stream
            );
            bool useRuntime = ShouldUseRuntimeTypeForProtobuf(declared, input, forceRuntimeType);

            if (useRuntime)
            {
                ProtoBuf.Serializer.NonGeneric.Serialize(stream, input);
            }
            else
            {
                ProtoBuf.Serializer.Serialize(stream, input);
            }
            return stream.ToArrayExact(ref buffer);
        }

        /// <summary>
        /// Deserializes JSON text to <typeparamref name="T"/> using Unity‑aware converters.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="data">JSON string.</param>
        /// <param name="type">Optional concrete target type (defaults to <typeparamref name="T"/>).</param>
        /// <param name="options">Serializer options; defaults include converters for Unity types and ReferenceHandler.IgnoreCycles.</param>
        /// <returns>The decoded instance.</returns>
        public static T JsonDeserialize<T>(
            string data,
            Type type = null,
            JsonSerializerOptions options = null
        )
        {
            if (data == null)
            {
                SerializationFailureException.ThrowNullInput<T>(
                    SerializationFormat.Json,
                    SerializationOperation.Deserialize
                );
            }
            if (data.Length == 0)
            {
                SerializationFailureException.ThrowEmptyInput<T>(
                    SerializationFormat.Json,
                    SerializationOperation.Deserialize
                );
            }

            try
            {
                return (T)
                    JsonSerializer.Deserialize(
                        data,
                        type ?? typeof(T),
                        options ?? SerializerEncoding.NormalJsonOptions
                    );
            }
            catch (SerializationFailureException)
            {
                throw;
            }
            catch (Exception e)
            {
                SerializationFailureException.ThrowCorrupt<T>(
                    SerializationFormat.Json,
                    SerializationOperation.Deserialize,
                    data.Length,
                    SerializationStage.Decode,
                    e,
                    "System.Text.Json rejected the payload."
                );
                return default;
            }
        }

        /// <summary>
        /// Attempts to deserialize a JSON string. Returns <see langword="false"/> for null/empty/corrupt input.
        /// </summary>
        public static bool TryJsonDeserialize<T>(
            string data,
            out T value,
            Type type = null,
            JsonSerializerOptions options = null
        )
        {
            try
            {
                value = JsonDeserialize<T>(data, type, options);
                return true;
            }
            catch (SerializationInputException)
            {
                value = default;
                return false;
            }
            catch (SerializationCorruptDataException)
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Deserializes JSON bytes (UTF-8) to <typeparamref name="T"/> using Unity-aware converters.
        /// Avoids intermediate string allocation by using span-based System.Text.Json APIs.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="data">UTF-8 JSON bytes.</param>
        /// <param name="type">Optional concrete target type (defaults to <typeparamref name="T"/>).</param>
        /// <param name="options">Serializer options; defaults include Unity converters.</param>
        /// <returns>The decoded instance.</returns>
        public static T JsonDeserialize<T>(
            byte[] data,
            Type type = null,
            JsonSerializerOptions options = null
        )
        {
            if (data == null)
            {
                SerializationFailureException.ThrowNullInput<T>(
                    SerializationFormat.Json,
                    SerializationOperation.Deserialize
                );
            }
            if (data.Length == 0)
            {
                SerializationFailureException.ThrowEmptyInput<T>(
                    SerializationFormat.Json,
                    SerializationOperation.Deserialize
                );
            }

            try
            {
                ReadOnlySpan<byte> span = new(data);
                return (T)
                    JsonSerializer.Deserialize(
                        span,
                        type ?? typeof(T),
                        options ?? SerializerEncoding.NormalJsonOptions
                    );
            }
            catch (SerializationFailureException)
            {
                throw;
            }
            catch (Exception e)
            {
                SerializationFailureException.ThrowCorrupt<T>(
                    SerializationFormat.Json,
                    SerializationOperation.Deserialize,
                    data.Length,
                    SerializationStage.Decode,
                    e,
                    "System.Text.Json rejected the payload."
                );
                return default;
            }
        }

        /// <summary>
        /// Attempts to deserialize JSON bytes. Returns <see langword="false"/> for null/empty/corrupt input.
        /// </summary>
        public static bool TryJsonDeserialize<T>(
            byte[] data,
            out T value,
            Type type = null,
            JsonSerializerOptions options = null
        )
        {
            try
            {
                value = JsonDeserialize<T>(data, type, options);
                return true;
            }
            catch (SerializationInputException)
            {
                value = default;
                return false;
            }
            catch (SerializationCorruptDataException)
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Fast-path JSON deserialize using strict, allocation-lean options.
        /// </summary>
        public static T JsonDeserializeFast<T>(byte[] data)
        {
            if (data == null)
            {
                SerializationFailureException.ThrowNullInput<T>(
                    SerializationFormat.JsonFast,
                    SerializationOperation.Deserialize
                );
            }
            if (data.Length == 0)
            {
                SerializationFailureException.ThrowEmptyInput<T>(
                    SerializationFormat.JsonFast,
                    SerializationOperation.Deserialize
                );
            }

            try
            {
                ReadOnlySpan<byte> span = new(data);
                return JsonSerializer.Deserialize<T>(span, SerializerEncoding.FastJsonOptions);
            }
            catch (SerializationFailureException)
            {
                throw;
            }
            catch (Exception e)
            {
                SerializationFailureException.ThrowCorrupt<T>(
                    SerializationFormat.JsonFast,
                    SerializationOperation.Deserialize,
                    data.Length,
                    SerializationStage.Decode,
                    e,
                    "System.Text.Json (fast options) rejected the payload."
                );
                return default;
            }
        }

        /// <summary>
        /// Attempts a fast-path JSON deserialize. Returns <see langword="false"/> for null/empty/corrupt input.
        /// </summary>
        public static bool TryJsonDeserializeFast<T>(byte[] data, out T value)
        {
            try
            {
                value = JsonDeserializeFast<T>(data);
                return true;
            }
            catch (SerializationInputException)
            {
                value = default;
                return false;
            }
            catch (SerializationCorruptDataException)
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Serializes an instance to JSON bytes (UTF‑8) using Unity‑aware converters.
        /// </summary>
        /// <typeparam name="T">Instance type.</typeparam>
        /// <param name="input">The instance to serialize.</param>
        /// <returns>UTF‑8 JSON bytes.</returns>
        public static byte[] JsonSerialize<T>(T input)
        {
            using Utils.PooledResource<PooledArrayBufferWriter> lease =
                PooledArrayBufferWriter.Rent(out PooledArrayBufferWriter bufferWriter);
            WriteJsonToBuffer(input, SerializerEncoding.NormalJsonOptions, bufferWriter);
            byte[] buffer = null;
            bufferWriter.ToArrayExact(ref buffer);
            return buffer;
        }

        /// <summary>
        /// Serializes an instance to JSON bytes (UTF-8) using caller-provided options.
        /// Tip: Reuse the same options instance across calls to benefit from metadata caches.
        /// </summary>
        public static byte[] JsonSerialize<T>(T input, JsonSerializerOptions options)
        {
            using Utils.PooledResource<PooledArrayBufferWriter> lease =
                PooledArrayBufferWriter.Rent(out PooledArrayBufferWriter bufferWriter);
            WriteJsonToBuffer(input, options ?? SerializerEncoding.NormalJsonOptions, bufferWriter);
            byte[] buffer = null;
            bufferWriter.ToArrayExact(ref buffer);
            return buffer;
        }

        /// <summary>
        /// Serializes an instance to JSON bytes (UTF‑8) into a caller-provided buffer.
        /// </summary>
        /// <typeparam name="T">Instance type.</typeparam>
        /// <param name="input">The instance to serialize.</param>
        /// <param name="buffer">Destination buffer reference. Resized if necessary.</param>
        /// <returns>Number of bytes written.</returns>
        public static int JsonSerialize<T>(T input, ref byte[] buffer)
        {
            using Utils.PooledResource<PooledArrayBufferWriter> lease =
                PooledArrayBufferWriter.Rent(out PooledArrayBufferWriter bufferWriter);
            WriteJsonToBuffer(input, SerializerEncoding.NormalJsonOptions, bufferWriter);
            return bufferWriter.ToArrayExact(ref buffer);
        }

        /// <summary>
        /// Serializes into a caller-provided buffer using caller options.
        /// Reuses the provided buffer when large enough to avoid allocations; resizes if necessary.
        /// </summary>
        public static int JsonSerialize<T>(
            T input,
            JsonSerializerOptions options,
            ref byte[] buffer
        )
        {
            using Utils.PooledResource<PooledArrayBufferWriter> lease =
                PooledArrayBufferWriter.Rent(out PooledArrayBufferWriter bufferWriter);
            WriteJsonToBuffer(input, options ?? SerializerEncoding.NormalJsonOptions, bufferWriter);
            return bufferWriter.ToArrayExact(ref buffer);
        }

        /// <summary>
        /// Serializes into a caller-provided buffer using caller options and a size hint to reduce growth copies.
        /// Provide an approximate size of the final payload to minimize buffer growth/copy churn for large outputs.
        /// Example: for large int[] payloads, estimate (count * 12) + overhead.
        /// </summary>
        public static int JsonSerialize<T>(
            T input,
            JsonSerializerOptions options,
            int sizeHint,
            ref byte[] buffer
        )
        {
            using Utils.PooledResource<PooledArrayBufferWriter> lease =
                PooledArrayBufferWriter.Rent(out PooledArrayBufferWriter bufferWriter);
            if (sizeHint > 0)
            {
                bufferWriter.Preallocate(sizeHint);
            }
            WriteJsonToBuffer(input, options ?? SerializerEncoding.NormalJsonOptions, bufferWriter);
            return bufferWriter.ToArrayExact(ref buffer);
        }

        /// <summary>
        /// Fast-path JSON serialize using strict, allocation-lean options.
        /// </summary>
        public static byte[] JsonSerializeFast<T>(T input)
        {
            using Utils.PooledResource<PooledArrayBufferWriter> lease =
                PooledArrayBufferWriter.Rent(out PooledArrayBufferWriter bufferWriter);
            WriteJsonToBuffer(input, SerializerEncoding.FastJsonOptions, bufferWriter);
            byte[] buffer = null;
            bufferWriter.ToArrayExact(ref buffer);
            return buffer;
        }

        /// <summary>
        /// Fast-path JSON serialize into a caller-provided buffer.
        /// </summary>
        public static int JsonSerializeFast<T>(T input, ref byte[] buffer)
        {
            using Utils.PooledResource<PooledArrayBufferWriter> lease =
                PooledArrayBufferWriter.Rent(out PooledArrayBufferWriter bufferWriter);
            WriteJsonToBuffer(input, SerializerEncoding.FastJsonOptions, bufferWriter);
            return bufferWriter.ToArrayExact(ref buffer);
        }

        private static void WriteJsonToStream<T>(
            T input,
            JsonSerializerOptions options,
            Stream stream
        )
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using (JsonWriterPool.Get(out Utf8JsonWriter writer))
            {
                writer.Reset(stream);
                Type parameterType = typeof(T);
                if (
                    parameterType.IsAbstract
                    || parameterType.IsInterface
                    || parameterType == typeof(object)
                )
                {
                    object data = input;
                    if (data == null)
                    {
                        writer.WriteStartObject();
                        writer.WriteEndObject();
                        writer.Flush();
                        return;
                    }

                    Type type = data.GetType();
                    JsonSerializer.Serialize(writer, data, type, options);
                }
                else
                {
                    JsonSerializer.Serialize(writer, input, options);
                }
                writer.Flush();
            }
        }

        /// <summary>
        /// Serializes an instance to a JSON string.
        /// </summary>
        /// <typeparam name="T">Instance type.</typeparam>
        /// <param name="input">The instance to serialize.</param>
        /// <param name="pretty">Write indented output when true.</param>
        /// <returns>JSON text.</returns>
        /// <example>
        /// <code>
        /// var json = Serializer.JsonStringify(save, pretty: true);
        /// var roundtrip = Serializer.JsonDeserialize&lt;SaveData&gt;(json);
        /// </code>
        /// </example>
        public static string JsonStringify<T>(T input, bool pretty = false)
        {
            JsonSerializerOptions options = pretty
                ? SerializerEncoding.PrettyJsonOptions
                : SerializerEncoding.NormalJsonOptions;

            return JsonStringify(input, options);
        }

        /// <summary>
        /// Serializes an instance to a JSON string using the provided <paramref name="options"/>.
        /// </summary>
        /// <typeparam name="T">Instance type.</typeparam>
        /// <param name="input">The instance to serialize.</param>
        /// <param name="options">Serializer options.</param>
        /// <returns>JSON text.</returns>
        public static string JsonStringify<T>(T input, JsonSerializerOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Type parameterType = typeof(T);
            if (
                parameterType.IsAbstract
                || parameterType.IsInterface
                || parameterType == typeof(object)
            )
            {
                object data = input;
                if (data == null)
                {
                    return "{}";
                }

                Type type = data.GetType();
                return JsonSerializer.Serialize(data, type, options);
            }

            return JsonSerializer.Serialize(input, options);
        }

        /// <summary>
        /// Reads JSON text from a file (UTF‑8) and deserializes to <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="path">File path.</param>
        /// <returns>Decoded instance.</returns>
        public static T ReadFromJsonFile<T>(string path)
        {
            byte[] settingsAsBytes = File.ReadAllBytes(path);
            return JsonDeserialize<T>(settingsAsBytes);
        }

        private static void WriteJsonToBuffer<T>(
            T input,
            JsonSerializerOptions options,
            PooledArrayBufferWriter buffer
        )
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            using (
                Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { SkipValidation = true })
            )
            {
                Type parameterType = typeof(T);
                if (
                    parameterType.IsAbstract
                    || parameterType.IsInterface
                    || parameterType == typeof(object)
                )
                {
                    object data = input;
                    if (data == null)
                    {
                        writer.WriteStartObject();
                        writer.WriteEndObject();
                        writer.Flush();
                        return;
                    }

                    Type type = data.GetType();
                    JsonSerializer.Serialize(writer, data, type, options);
                }
                else
                {
                    JsonSerializer.Serialize(writer, input, options);
                }
                writer.Flush();
            }
        }

        /// <summary>
        /// Asynchronously reads JSON text from a file (UTF‑8) and deserializes to <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="path">File path.</param>
        /// <returns>Decoded instance.</returns>
        public static async Task<T> ReadFromJsonFileAsync<T>(string path)
        {
            byte[] settingsAsBytes = await File.ReadAllBytesAsync(path);
            return JsonDeserialize<T>(settingsAsBytes);
        }

        /// <summary>
        /// Asynchronously reads JSON with cancellation.
        /// </summary>
        public static async Task<T> ReadFromJsonFileAsync<T>(
            string path,
            System.Threading.CancellationToken cancellationToken
        )
        {
            using FileStream fs = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                useAsync: true
            );
            using Utils.PooledResource<PooledBufferStream> lease = PooledBufferStream.Rent(
                out PooledBufferStream stream
            );
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await fs.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                stream.Write(buffer, 0, read);
            }
            ArraySegment<byte> seg = stream.GetWrittenSegment();
            return JsonDeserialize<T>(seg.Array.AsSpan(0, seg.Count).ToArray());
        }

        /// <summary>
        /// Writes an instance to a JSON file (UTF‑8).
        /// </summary>
        /// <typeparam name="T">Instance type.</typeparam>
        /// <param name="input">The instance to serialize.</param>
        /// <param name="path">Destination file path.</param>
        /// <param name="pretty">Write indented output when true.</param>
        public static void WriteToJsonFile<T>(T input, string path, bool pretty = true)
        {
            string jsonAsText = JsonStringify(input, pretty);
            File.WriteAllText(path, jsonAsText);
        }

        /// <summary>
        /// Asynchronously writes an instance to a JSON file (UTF‑8).
        /// </summary>
        /// <typeparam name="T">Instance type.</typeparam>
        /// <param name="input">The instance to serialize.</param>
        /// <param name="path">Destination file path.</param>
        /// <param name="pretty">Write indented output when true.</param>
        public static async Task WriteToJsonFileAsync<T>(T input, string path, bool pretty = true)
        {
            string jsonAsText = JsonStringify(input, pretty);
            await File.WriteAllTextAsync(path, jsonAsText);
        }

        /// <summary>
        /// Asynchronously writes an instance to a JSON file (UTF‑8) with cancellation.
        /// </summary>
        public static async Task WriteToJsonFileAsync<T>(
            T input,
            string path,
            bool pretty,
            System.Threading.CancellationToken cancellationToken
        )
        {
            string jsonAsText = JsonStringify(input, pretty);
            byte[] bytes = SerializerEncoding.Encoding.GetBytes(jsonAsText);
            using FileStream fs = new(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true
            );
            await fs.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        }

        /// <summary>
        /// Writes an instance to a JSON file (UTF‑8) using the provided <paramref name="options"/>.
        /// </summary>
        /// <typeparam name="T">Instance type.</typeparam>
        /// <param name="input">The instance to serialize.</param>
        /// <param name="path">Destination file path.</param>
        /// <param name="options">Serializer options.</param>
        public static void WriteToJsonFile<T>(T input, string path, JsonSerializerOptions options)
        {
            string jsonAsText = JsonStringify(input, options);
            File.WriteAllText(path, jsonAsText);
        }

        /// <summary>
        /// Asynchronously writes an instance to a JSON file (UTF‑8) using the provided <paramref name="options"/>.
        /// </summary>
        /// <typeparam name="T">Instance type.</typeparam>
        /// <param name="input">The instance to serialize.</param>
        /// <param name="path">Destination file path.</param>
        /// <param name="options">Serializer options.</param>
        public static async Task WriteToJsonFileAsync<T>(
            T input,
            string path,
            JsonSerializerOptions options
        )
        {
            string jsonAsText = JsonStringify(input, options);
            await File.WriteAllTextAsync(path, jsonAsText);
        }

        /// <summary>
        /// Attempts to read JSON into an instance, returns false if file missing or invalid.
        /// </summary>
        public static bool TryReadFromJsonFile<T>(string path, out T value)
        {
            try
            {
                if (!File.Exists(path))
                {
                    value = default;
                    return false;
                }
                string json = File.ReadAllText(path);
                value = JsonDeserialize<T>(json);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Attempts to write JSON to a file, returns false on failure.
        /// </summary>
        public static bool TryWriteToJsonFile<T>(T input, string path, bool pretty = true)
        {
            try
            {
                WriteToJsonFile(input, path, pretty);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // Internal pooled, growable write stream backed by ArrayPool<byte> to reduce allocations
    internal sealed class PooledBufferStream : Stream
    {
        private const int DefaultInitialCapacity = 256;

        private byte[] _buffer;
        private int _length;
        private int _position;
        private bool _disposed;

        private static readonly Utils.WallstopGenericPool<PooledBufferStream> Pool = new(
            producer: () => new PooledBufferStream(),
            onRelease: stream => stream.ResetForReuse(),
            onDisposal: stream => stream.Dispose()
        );

        public static Utils.PooledResource<PooledBufferStream> Rent(
            out PooledBufferStream stream
        ) => Pool.Get(out stream);

        private PooledBufferStream(int initialCapacity = DefaultInitialCapacity)
        {
            if (initialCapacity < 1)
            {
                initialCapacity = DefaultInitialCapacity;
            }

            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            _length = 0;
            _position = 0;
        }

        internal ArraySegment<byte> GetWrittenSegment()
        {
            return new ArraySegment<byte>(_buffer, 0, _length);
        }

        private void ResetForReuse()
        {
            _length = 0;
            _position = 0;
            _disposed = false;
        }

        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            int basePos = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => _position,
                SeekOrigin.End => _length,
                _ => 0,
            };
            long newPos = basePos + offset;
            if (newPos is < 0 or > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            _position = (int)newPos;
            return _position;
        }

        public override void SetLength(long value)
        {
            if (value is < 0 or > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            int newLen = (int)value;
            EnsureCapacity(newLen);
            _length = newLen;
            if (_position > _length)
            {
                _position = _length;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            int endPos = _position + count;
            EnsureCapacity(endPos);
            Array.Copy(buffer, offset, _buffer, _position, count);
            _position = endPos;
            if (endPos > _length)
            {
                _length = endPos;
            }
        }

        public override void WriteByte(byte value)
        {
            int endPos = _position + 1;
            EnsureCapacity(endPos);
            _buffer[_position] = value;
            _position = endPos;
            if (endPos > _length)
            {
                _length = endPos;
            }
        }

        private void EnsureCapacity(int required)
        {
            if (_buffer.Length >= required)
            {
                return;
            }

            int newSize = _buffer.Length;
            if (newSize < DefaultInitialCapacity)
            {
                newSize = DefaultInitialCapacity;
            }

            while (newSize < required)
            {
                newSize = newSize < 1024 ? newSize * 2 : newSize + (newSize >> 1);
            }
            byte[] newBuf = ArrayPool<byte>.Shared.Rent(newSize);
            if (_length > 0)
            {
                Array.Copy(_buffer, newBuf, _length);
            }
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuf;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (_buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(_buffer);
                    _buffer = Array.Empty<byte>();
                }
                _length = 0;
                _position = 0;
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        public int ToArrayExact(ref byte[] buffer)
        {
            if (buffer == null || buffer.Length < _length)
            {
                buffer = new byte[_length];
            }

            if (_length > 0)
            {
                Array.Copy(_buffer, buffer, _length);
            }

            return _length;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            int count = buffer.Length;
            int endPos = _position + count;
            EnsureCapacity(endPos);
            buffer.CopyTo(new Span<byte>(_buffer, _position, count));
            _position = endPos;
            if (endPos > _length)
            {
                _length = endPos;
            }
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> source,
            System.Threading.CancellationToken cancellationToken = default
        )
        {
            // Delegate to synchronous span-based path; callers expect a fast in-memory stream
            Write(source.Span);
            return new ValueTask();
        }
    }

    // Internal pooled ArrayBufferWriter-like implementation to enable zero-copy JSON writing via IBufferWriter<byte>
    internal sealed class PooledArrayBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private const int DefaultInitialCapacity = 256;
        private byte[] _buffer;
        private int _written;
        private bool _disposed;

        private static readonly Utils.WallstopGenericPool<PooledArrayBufferWriter> Pool = new(
            producer: () => new PooledArrayBufferWriter(),
            onRelease: w =>
            {
                w.Reset();
            }
        );

        public static Utils.PooledResource<PooledArrayBufferWriter> Rent(
            out PooledArrayBufferWriter writer
        ) => Pool.Get(out writer);

        private PooledArrayBufferWriter(int initialCapacity = DefaultInitialCapacity)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
            _written = 0;
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint <= 0)
            {
                sizeHint = 1;
            }
            int required = _written + sizeHint;
            if (_buffer.Length >= required)
            {
                return;
            }

            int newSize = _buffer.Length;
            while (newSize < required)
            {
                newSize = newSize < 1024 ? newSize * 2 : newSize + (newSize >> 1);
            }

            byte[] newBuf = ArrayPool<byte>.Shared.Rent(newSize);
            if (_written > 0)
            {
                Buffer.BlockCopy(_buffer, 0, newBuf, 0, _written);
            }
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuf;
        }

        public void Advance(int count)
        {
            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_written);
        }

        public int WrittenCount => _written;

        public void Preallocate(int sizeHint)
        {
            EnsureCapacity(sizeHint);
        }

        public int ToArrayExact(ref byte[] buffer)
        {
            if (buffer == null || buffer.Length < _written)
            {
                buffer = new byte[_written];
            }
            if (_written > 0)
            {
                Buffer.BlockCopy(_buffer, 0, buffer, 0, _written);
            }
            return _written;
        }

        private void Reset()
        {
            // Keep the rented buffer to avoid churn; just reset write cursor.
            if (_buffer == null || _buffer.Length == 0)
            {
                _buffer = ArrayPool<byte>.Shared.Rent(DefaultInitialCapacity);
            }
            _written = 0;
            _disposed = false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(_buffer);
                }
                _buffer = Array.Empty<byte>();
                _written = 0;
                _disposed = true;
            }
        }
    }

    // Internal pooled read-only stream over an existing byte[] to avoid MemoryStream allocation in deserialization paths
    internal sealed class PooledReadOnlyMemoryStream : Stream
    {
        private byte[] _buffer = Array.Empty<byte>();
        private int _position;
        private int _length;

        private static readonly Utils.WallstopGenericPool<PooledReadOnlyMemoryStream> Pool = new(
            producer: () => new PooledReadOnlyMemoryStream(),
            onRelease: s =>
            {
                s.ResetForReuse();
            }
        );

        public static Utils.PooledResource<PooledReadOnlyMemoryStream> Rent(
            out PooledReadOnlyMemoryStream stream
        ) => Pool.Get(out stream);

        public void SetBuffer(byte[] buffer)
        {
            _buffer = buffer ?? Array.Empty<byte>();
            _position = 0;
            _length = _buffer.Length;
        }

        private void ResetForReuse()
        {
            SetBuffer(Array.Empty<byte>());
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set
            {
                if (value is < 0 or > int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                _position = (int)value;
            }
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if ((uint)offset > buffer.Length || (uint)count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException();
            }
            int remaining = _length - _position;
            if (remaining <= 0)
            {
                return 0;
            }
            if (count > remaining)
            {
                count = remaining;
            }

            Array.Copy(_buffer, _position, buffer, offset, count);
            _position += count;
            return count;
        }

        // Span-based fast-path used by modern callers (e.g., protobuf-net)
        public override int Read(Span<byte> destination)
        {
            int remaining = _length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            int toCopy = destination.Length;
            if (toCopy > remaining)
            {
                toCopy = remaining;
            }

            new ReadOnlySpan<byte>(_buffer, _position, toCopy).CopyTo(destination);
            _position += toCopy;
            return toCopy;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> destination,
            System.Threading.CancellationToken cancellationToken = default
        )
        {
            // Delegate to synchronous span-based path; this stream is purely in-memory
            int read = Read(destination.Span);
            return new ValueTask<int>(read);
        }

        public override int ReadByte()
        {
            if (_position >= _length)
            {
                return -1;
            }

            return _buffer[_position++];
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            int basePos = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => _position,
                SeekOrigin.End => _length,
                _ => 0,
            };
            long newPos = basePos + offset;
            if (newPos is < 0 or > int.MaxValue)
            {
                throw new IOException("Attempted to seek outside the stream bounds.");
            }
            _position = (int)newPos;
            return _position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void WriteByte(byte value)
        {
            throw new NotSupportedException();
        }
    }
}
