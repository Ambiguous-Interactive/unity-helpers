// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    internal interface INullableDictionaryWrapper
    {
        void PrepareNullableValues();

        bool TryRestoreNullableValues();
    }
}
