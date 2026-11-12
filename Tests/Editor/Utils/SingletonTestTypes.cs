// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    internal static class SingletonTestTypes
    {
        internal static readonly Type[] WithExclusion =
        {
            typeof(TestSingleton),
            typeof(EmptyPathSingleton),
            typeof(CustomPathSingleton),
            typeof(MultipleInstancesSingleton),
            typeof(ResourceBackedSingleton),
            typeof(DeepPathResourceSingleton),
            typeof(WrongPathFallbackSingleton),
            typeof(MultiAssetScriptableSingleton),
            typeof(LifecycleScriptableSingleton),
            typeof(MissingResourceSingleton),
            typeof(AutoScriptableSingleton),
            typeof(ScriptableMismatchSingleton),
            typeof(SingleLevelPathSingleton),
        };
        internal static readonly Type[] WithoutExclusion =
        {
            typeof(CreatorPathSingleton),
            typeof(NestedDiskSingleton),
            typeof(CaseMismatch),
            typeof(Duplicate),
        };
        internal static readonly Type[] All = Combine();

        private static Type[] Combine()
        {
            Type[] result = new Type[WithExclusion.Length + WithoutExclusion.Length];
            Array.Copy(WithExclusion, 0, result, 0, WithExclusion.Length);
            Array.Copy(WithoutExclusion, 0, result, WithExclusion.Length, WithoutExclusion.Length);
            return result;
        }
    }
}
