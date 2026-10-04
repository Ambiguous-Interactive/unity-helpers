// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopProto = WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    internal static class ProtoRootTestUtilities
    {
        internal static void ClearRootCaches(params Type[] declaredTypes)
        {
            if (declaredTypes == null || declaredTypes.Length == 0)
            {
                Serializer.ProtobufRootCache.Clear();
                Serializer.ExplicitProtobufRootCache.Clear();
                WallstopProto.WProtoDeclaredRootProvider.ReleaseAllClaims();
                return;
            }

            foreach (Type declaredType in declaredTypes)
            {
                if (declaredType == null)
                {
                    continue;
                }

                Serializer.ProtobufRootCache.TryRemove(declaredType, out _);
                Serializer.ExplicitProtobufRootCache.TryRemove(declaredType, out _);
                WallstopProto.WProtoDeclaredRootProvider.ReleaseClaim(declaredType);
            }
        }
    }
}
