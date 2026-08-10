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
