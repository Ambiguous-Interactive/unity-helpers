// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
using WallstopStudios.UnityHelpers.Tests.Serialization;

// Assembly level, because UnityEngine.Vector3 is not ours to annotate and because an assembly
// attribute is what a generator can enumerate cheaply across every referenced assembly.
[assembly: WProtoSurrogate(typeof(Vector3), typeof(WProtoVector3Surrogate))]
