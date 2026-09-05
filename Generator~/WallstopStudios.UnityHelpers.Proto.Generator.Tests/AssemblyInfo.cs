// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
using WallstopStudios.UnityHelpers.Proto.Generator.Tests;

// Assembly attributes allow registration when the type's assembly cannot reference this one.
[assembly: WProtoSurrogate(typeof(ForeignVector3), typeof(ForeignVector3Surrogate))]

[assembly: WProtoRootMarshal(typeof(StandInRing<>), typeof(StandInRingMarshalFormatter<>))]
[assembly: WProtoRootMarshal(typeof(StandInBag), typeof(StandInBagMarshalFormatter))]

[assembly: WProtoDeclaredRoot(typeof(IIncludeThing), typeof(IncludeBase))]

[assembly: WProtoSubtypeTag(
    "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormAlpha",
    typeof(ManifestFormRoot),
    100
)]
[assembly: WProtoSubtypeTag(
    "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormBeta",
    typeof(ManifestFormRoot),
    101
)]
[assembly: WProtoSubtypeTag(
    "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormGamma",
    typeof(ManifestFormBeta),
    200
)]

// A retired tag deliberately names no live type and must remain reserved.
[assembly: WProtoRetiredSubtypeTag(
    "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormDeleted",
    typeof(ManifestFormRoot),
    102
)]

// This orphaned assignment must retire tag 103 rather than free it for reuse.
[assembly: WProtoSubtypeTag(
    "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormOrphaned",
    typeof(ManifestFormRoot),
    103
)]
