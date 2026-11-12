// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    public sealed class ProtectedPathTestCase
    {
        public string Path { get; }
        public bool ExpectedIsProtected { get; }
        public bool ExpectedIsDuplicatePollution { get; }
        public string Description { get; }

        public ProtectedPathTestCase(
            string path,
            bool expectedIsProtected,
            bool expectedIsDuplicatePollution,
            string description
        )
        {
            Path = path;
            ExpectedIsProtected = expectedIsProtected;
            ExpectedIsDuplicatePollution = expectedIsDuplicatePollution;
            Description = description;
        }

        public override string ToString()
        {
            return $"{(Path ?? "(null)")} - {Description}";
        }
    }
#endif
}
