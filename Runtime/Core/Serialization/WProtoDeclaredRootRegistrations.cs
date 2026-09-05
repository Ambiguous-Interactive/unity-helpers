// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using WallstopStudios.UnityHelpers.Core.Random;
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

// Map the documented IRandom root to the same abstract contract as the legacy resolver.

// Assembly scope makes this mapping visible to consumer builds.
[assembly: WProtoDeclaredRoot(typeof(IRandom), typeof(AbstractRandom))]
