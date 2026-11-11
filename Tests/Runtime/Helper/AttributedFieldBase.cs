// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    public class AttributedFieldBase
    {
        public const string PrivateFieldName = nameof(_privateValue);
        public const string PrivateStaticFieldName = nameof(_privateStaticValue);
        public const string ProtectedFieldName = nameof(protectedValue);

        public static int PrivateStaticValue => _privateStaticValue;

        [RuntimeMarker]
        private static int _privateStaticValue = 2;

        public int PrivateValue => _privateValue;

        [RuntimeMarker]
        public int sharedValue;

        [RuntimeMarker]
        protected int protectedValue;

        [RuntimeMarker]
        private int _privateValue = 1;
    }
}
