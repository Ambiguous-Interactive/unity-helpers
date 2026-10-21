// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    public static class WProtoConstructionBudgetEvents
    {
        public static int Initializations;
        public static int Constructors;
        public static int BeforeReads;
        public static int AfterReads;
        public static int Assignments;
        public static bool ThrowInConstructor;

        public static int Initialize()
        {
            ++Initializations;
            return 7;
        }

        public static void Reset()
        {
            Initializations = 0;
            Constructors = 0;
            BeforeReads = 0;
            AfterReads = 0;
            Assignments = 0;
            ThrowInConstructor = false;
        }
    }
}
