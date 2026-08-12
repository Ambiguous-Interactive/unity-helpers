// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using WallstopStudios.UnityHelpers.Core.Random;
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

// IRandom is the declared type this package's own documentation recommends -- a generator is almost
// never held as its concrete type -- and an interface has no members to encode, so without this the
// facade declines and every such call takes the protobuf-net path. Naming AbstractRandom is not a
// choice: Serializer.ResolveProtobufRootType already resolves IRandom to it, by scanning the
// interface's assembly for a unique abstract [ProtoContract] base. This states the same answer
// without the scan.
//
// Assembly level so a CONSUMER's build finds it, matching the surrogate and marshal registrations.
[assembly: WProtoDeclaredRoot(typeof(IRandom), typeof(AbstractRandom))]
