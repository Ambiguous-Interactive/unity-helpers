// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Attributes
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalExactBindingTests : RelationalExactBindingTestBase
    {
        [Test]
        public void ExactTypeAssignmentMatchesDirectQueriesAcrossShapesAndCacheStates(
            [Values(1, 4)] int depth,
            [Values(false, true)] bool inactive,
            [Values(0, 1, 2)] int capability
        )
        {
            VerifyExactTypeAssignment(depth, inactive, capability);
        }
    }
}
