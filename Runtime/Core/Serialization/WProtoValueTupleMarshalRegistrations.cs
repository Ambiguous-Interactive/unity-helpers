// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using System;
using WallstopStudios.UnityHelpers.Core.Serialization;
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

// OPT-OUT: define WALLSTOP_DISABLE_VALUE_TUPLE_SERIALIZATION to remove these registrations. They
// are on by default because a tuple that throws only in a player is the worst failure this package
// has to offer, but they are not free: the generator emits one formatter per closed ValueTuple your
// build uses, and a tuple is a common local aggregate rather than a deliberate container. Measured
// on this package alone, 41 registrations of which 11 close over types that can never serialize
// (Type, ConstructorInfo, ...). Those decline at run time through CanServe(), but under IL2CPP each
// closure is still compiled code. Turn them off if your build cares more about size than about
// tuples serializing.
//
// Assembly level for the same reason the collection marshals are: the formatters are generic, a
// registrar cannot register an open generic, and the closures a CONSUMER uses cannot appear in this
// package's sources. Declared here, the generator registers
// ValueTupleMarshalFormatter<their, types> for every closed ValueTuple it finds in their build.
#if !WALLSTOP_DISABLE_VALUE_TUPLE_SERIALIZATION
[assembly: WProtoRootMarshal(typeof(ValueTuple<,>), typeof(ValueTupleMarshalFormatter<,>))]
[assembly: WProtoRootMarshal(typeof(ValueTuple<,,>), typeof(ValueTupleMarshalFormatter<,,>))]
#endif
