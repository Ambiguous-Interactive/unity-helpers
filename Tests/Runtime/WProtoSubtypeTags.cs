// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// WallstopProto subtype tag manifest for WallstopStudios.UnityHelpers.Tests.Runtime.
// Written by Tools > Wallstop Studios > Unity Helpers > Assign WallstopProto
// Subtype Tags. Commit it: these numbers are the wire contract for every
// [WProtoSubtype] declared without one, so a payload saved today is read back by
// this file. Do not renumber an entry, and do not delete a retired one -- a
// retired number is held so a later subtype cannot be given a number old saves
// already mean something else by.
//
// The editor rewrites this file automatically after an assembly reload that finds a
// [WProtoSubtype] with no number and no entry here, so adding a subtype is one
// attribute and a recompile.

[assembly: WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoSubtypeTag(
    "WallstopStudios.UnityHelpers.Tests.Serialization.WProtoManifestAlpha",
    typeof(WallstopStudios.UnityHelpers.Tests.Serialization.WProtoManifestBase),
    3
)]
[assembly: WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoSubtypeTag(
    "WallstopStudios.UnityHelpers.Tests.Serialization.WProtoManifestBeta",
    typeof(WallstopStudios.UnityHelpers.Tests.Serialization.WProtoManifestBase),
    4
)]
[assembly: WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoSubtypeTag(
    "WallstopStudios.UnityHelpers.Tests.Serialization.WProtoManifestGamma",
    typeof(WallstopStudios.UnityHelpers.Tests.Serialization.WProtoManifestBeta),
    2
)]
