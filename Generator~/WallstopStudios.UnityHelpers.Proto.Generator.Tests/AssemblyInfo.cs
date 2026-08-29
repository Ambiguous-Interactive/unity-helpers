// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
using WallstopStudios.UnityHelpers.Proto.Generator.Tests;

// Declared at assembly level because the real type is usually in an assembly that cannot reference
// this one, and because assembly attributes are the one thing a generator can enumerate cheaply
// across every reference.
[assembly: WProtoSurrogate(typeof(ForeignVector3), typeof(ForeignVector3Surrogate))]

// The one-way pair the WPROTO016 test needs is declared inside that test's own synthetic
// compilation, so nothing is registered here for it.

// Root marshals, declared here for the same reason and read the same way. A consumer compilation
// that only REFERENCES this assembly still has to find them, which is what the registrar tests
// drive through a second, real compilation.
[assembly: WProtoRootMarshal(typeof(StandInRing<>), typeof(StandInRingMarshalFormatter<>))]
[assembly: WProtoRootMarshal(typeof(StandInBag), typeof(StandInBagMarshalFormatter))]

// The declared root, in the same shape the package ships for IRandom: an interface with no members,
// served by the contract at the head of an include chain. Read only from the compilation's own
// assembly, so declaring it here is what makes the generated registration exist at all.
[assembly: WProtoDeclaredRoot(typeof(IIncludeThing), typeof(IncludeBase))]

// The subtype tag manifest, in the shape the assignment tool writes it. Nothing in the
// ManifestForm hierarchy states a field number, so these three lines are the whole wire contract
// for it -- and they are what the byte-equivalence tests compare against the hand-numbered twins.
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

// A number that belonged to a subtype since deleted. It names no live type on purpose: that is
// exactly what a retired entry is, and the compilation has to accept one, because an assembly that
// has ever removed a subtype carries these forever.
[assembly: WProtoRetiredSubtypeTag(
    "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormDeleted",
    typeof(ManifestFormRoot),
    102
)]

// An ORPHANED assignment: a live entry naming a subtype that no longer exists. This is the state a
// deletion leaves behind, and the reason the subtype half of an entry is a string -- a typeof here
// would not compile, and the obvious repair (delete the line) would free 103 for the next subtype
// added, which is exactly the reuse retirement forbids. Left in place on purpose so both halves are
// proven against a REAL compile: the generator must accept it, and the assignment planner must
// retire it rather than lose it. Nothing may ever be given 103.
[assembly: WProtoSubtypeTag(
    "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormOrphaned",
    typeof(ManifestFormRoot),
    103
)]
