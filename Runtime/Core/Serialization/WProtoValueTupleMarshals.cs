// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    // A ValueTuple looks serializable and is not. protobuf-net reaches it through
    // StructValueChecker<ValueTuple<T1, T2>> and System.Text.Json through
    // ObjectDefaultConverter<ValueTuple<T1, T2>>, both instantiated reflectively over the closed
    // type -- so both work in the editor and both throw ExecutionEngineException on an IL2CPP
    // player, which is the failure mode this whole serializer exists to remove. Measured on Unity
    // 2021.3, where it took nine test failures on the gated standalone leg to surface.
    //
    // These marshals give the tuple the same treatment the seven collections get: the bytes are
    // SerializableValueTuple's generated formatter's, by construction, so the tuple and its
    // Unity-serializable stand-in cannot drift apart -- and nothing on the path reflects.

    /// <summary>
    /// Serializes a <see cref="ValueTuple{T1, T2}"/> root through its serializable stand-in.
    /// </summary>
    /// <typeparam name="T1">The first component's type.</typeparam>
    /// <typeparam name="T2">The second component's type.</typeparam>
    public sealed class ValueTupleMarshalFormatter<T1, T2>
        : IWProtoFormatter<ValueTuple<T1, T2>>,
            IWProtoConditionalFormatter
    {
        /// <inheritdoc />
        public bool CanServe()
        {
            return WProtoGeneric<T1>.CanEncode && WProtoGeneric<T2>.CanEncode;
        }

        /// <inheritdoc />
        public int Measure(in ValueTuple<T1, T2> value)
        {
            return SerializableValueTuple<T1, T2>.WProtoFormatter.Instance.Measure(Wrap(value));
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in ValueTuple<T1, T2> value)
        {
            return SerializableValueTuple<T1, T2>.WProtoFormatter.Instance.Write(
                ref writer,
                Wrap(value)
            );
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out ValueTuple<T1, T2> value)
        {
            if (
                !SerializableValueTuple<T1, T2>.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out SerializableValueTuple<T1, T2> wrapper
                )
            )
            {
                value = default;
                return false;
            }

            value = new ValueTuple<T1, T2>(wrapper.Item1, wrapper.Item2);
            return true;
        }

        private static SerializableValueTuple<T1, T2> Wrap(in ValueTuple<T1, T2> value)
        {
            return new SerializableValueTuple<T1, T2>(value.Item1, value.Item2);
        }
    }

    /// <summary>
    /// Serializes a <see cref="ValueTuple{T1, T2, T3}"/> root through its serializable stand-in.
    /// </summary>
    /// <typeparam name="T1">The first component's type.</typeparam>
    /// <typeparam name="T2">The second component's type.</typeparam>
    /// <typeparam name="T3">The third component's type.</typeparam>
    public sealed class ValueTupleMarshalFormatter<T1, T2, T3>
        : IWProtoFormatter<ValueTuple<T1, T2, T3>>,
            IWProtoConditionalFormatter
    {
        /// <inheritdoc />
        public bool CanServe()
        {
            return WProtoGeneric<T1>.CanEncode
                && WProtoGeneric<T2>.CanEncode
                && WProtoGeneric<T3>.CanEncode;
        }

        /// <inheritdoc />
        public int Measure(in ValueTuple<T1, T2, T3> value)
        {
            return SerializableValueTuple<T1, T2, T3>.WProtoFormatter.Instance.Measure(Wrap(value));
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in ValueTuple<T1, T2, T3> value)
        {
            return SerializableValueTuple<T1, T2, T3>.WProtoFormatter.Instance.Write(
                ref writer,
                Wrap(value)
            );
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out ValueTuple<T1, T2, T3> value)
        {
            if (
                !SerializableValueTuple<T1, T2, T3>.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out SerializableValueTuple<T1, T2, T3> wrapper
                )
            )
            {
                value = default;
                return false;
            }

            value = new ValueTuple<T1, T2, T3>(wrapper.Item1, wrapper.Item2, wrapper.Item3);
            return true;
        }

        private static SerializableValueTuple<T1, T2, T3> Wrap(in ValueTuple<T1, T2, T3> value)
        {
            return new SerializableValueTuple<T1, T2, T3>(value.Item1, value.Item2, value.Item3);
        }
    }
}
