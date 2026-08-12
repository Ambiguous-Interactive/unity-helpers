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
