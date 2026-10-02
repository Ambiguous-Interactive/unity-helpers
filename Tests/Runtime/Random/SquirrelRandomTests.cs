// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SquirrelRandomTests : RandomTestBase
    {
        protected override IRandom NewRandom()
        {
            return new SquirrelRandom(DeterministicSeedInt);
        }

        protected override double GetDeviationFor(string caller)
        {
            return 0.075;
        }
    }
}
