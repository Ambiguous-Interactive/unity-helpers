// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using System;
using WallstopStudios.UnityHelpers.Core.Serialization;
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

// Assembly level for the same reason the collection marshals are: the formatters are generic, a
// registrar cannot register an open generic, and the closures a CONSUMER uses cannot appear in this
// package's sources. Declared here, the generator registers
// ValueTupleMarshalFormatter<their, types> for every closed ValueTuple it finds in their build.
[assembly: WProtoRootMarshal(typeof(ValueTuple<,>), typeof(ValueTupleMarshalFormatter<,>))]
[assembly: WProtoRootMarshal(typeof(ValueTuple<,,>), typeof(ValueTupleMarshalFormatter<,,>))]
