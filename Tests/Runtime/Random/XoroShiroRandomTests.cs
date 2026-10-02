// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class XoroShiroRandomTests : RandomTestBase
    {
        protected override IRandom NewRandom()
        {
            return new XoroShiroRandom(DeterministicSeed64, DeterministicSeed64B);
        }
    }
}
