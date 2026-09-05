// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using System;
using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
using WallstopStudios.UnityHelpers.Core.Serialization;
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

// Disable tuple serialization to avoid generated closures when build size matters more than tuple support.

// Assembly registrations let consumer generators discover and close tuple marshals and surrogates.

#if !WALLSTOP_DISABLE_VALUE_TUPLE_SERIALIZATION
[assembly: WProtoSurrogate(typeof(ValueTuple<,>), typeof(SerializableValueTuple<,>))]
[assembly: WProtoSurrogate(typeof(ValueTuple<,,>), typeof(SerializableValueTuple<,,>))]
[assembly: WProtoRootMarshal(typeof(ValueTuple<,>), typeof(ValueTupleMarshalFormatter<,>))]
[assembly: WProtoRootMarshal(typeof(ValueTuple<,,>), typeof(ValueTupleMarshalFormatter<,,>))]
#endif
