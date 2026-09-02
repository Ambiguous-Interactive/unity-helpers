// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters
{
    using System;
    using System.Collections.Concurrent;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Holds one already-constructed <see cref="JsonConverter"/> per closed generic type, so no
    /// factory has to build one reflectively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Populated by generated code. The source generator finds each closed construction of a type
    /// declared through <see cref="WJsonConverterAttribute"/> and emits
    /// <c>WJsonConverterRegistry.TryRegister(typeof(Deque&lt;TheirStruct&gt;), new
    /// DequeConverterFactory.DequeConverter&lt;TheirStruct&gt;())</c> into a module initializer in
    /// the assembly that wrote the closure. Naming the closure is the whole point: IL2CPP compiles a
    /// generic closure when something references it, and a registration is a reference.
    /// </para>
    /// <para>
    /// <b>First registration wins</b>, matching <c>WProtoRootMarshalProvider</c>. Generated
    /// registrars run in Unity's unordered startup phase, so a consumer registering the same closure
    /// this package already registered would otherwise make load order decide which converter serves
    /// it. To replace one deliberately, put your converter in the
    /// <see cref="System.Text.Json.JsonSerializerOptions.Converters"/> list, which System.Text.Json
    /// consults before any type-level attribute.
    /// </para>
    /// <para>
    /// Concurrent because module initializers are not ordered and Unity may run more than one at
    /// once; reads outnumber writes by orders of magnitude and happen long after startup.
    /// </para>
    /// </remarks>
    public static class WJsonConverterRegistry
    {
        private static readonly ConcurrentDictionary<Type, JsonConverter> Converters = new();

        /// <summary>The number of closed constructions registered so far.</summary>
        /// <remarks>
        /// Exposed so a build can assert that generation happened at all. A registry that is empty
        /// in a player is the failure this whole mechanism exists to prevent, and it is otherwise
        /// invisible until the first serialization throws.
        /// </remarks>
        public static int Count => Converters.Count;

        /// <summary>
        /// Registers <paramref name="converter"/> as the converter for
        /// <paramref name="serializedType"/>.
        /// </summary>
        /// <param name="serializedType">The closed generic type being serialized.</param>
        /// <param name="converter">A converter for it.</param>
        /// <returns>
        /// <c>true</c> when this call registered the converter, <c>false</c> when the arguments were
        /// unusable or something had already claimed the type.
        /// </returns>
        public static bool TryRegister(Type serializedType, JsonConverter converter)
        {
            if (serializedType == null || converter == null)
            {
                return false;
            }

            /*
                A converter that cannot serve the type it is registered for would fail inside
                System.Text.Json with a message naming neither, long after the registration that
                caused it. Generated code cannot produce this pair, but a hand-written registration
                can.
            */
            if (!converter.CanConvert(serializedType))
            {
                return false;
            }

            return Converters.TryAdd(serializedType, converter);
        }

        /// <summary>
        /// Finds the registered converter for <paramref name="serializedType"/>.
        /// </summary>
        /// <param name="serializedType">The closed generic type being serialized.</param>
        /// <param name="converter">Receives the converter, or <c>null</c>.</param>
        /// <returns><c>true</c> when one is registered.</returns>
        public static bool TryGet(Type serializedType, out JsonConverter converter)
        {
            if (serializedType == null)
            {
                converter = null;
                return false;
            }

            return Converters.TryGetValue(serializedType, out converter);
        }
    }
}
