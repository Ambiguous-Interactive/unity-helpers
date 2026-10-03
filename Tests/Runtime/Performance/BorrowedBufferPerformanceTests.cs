// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tests.Extensions;

    [TestFixture]
    [Category("Performance")]
    public sealed class BorrowedBufferPerformanceTests
    {
        private static IEnumerable<TestCaseData> CampaignCases()
        {
            return BorrowedBase64Tests.GetCampaignTestCases(
                nameof(Base64WholeCallComparedWithBorrowedBuffer)
            );
        }

        [TestCaseSource(nameof(CampaignCases))]
        [Timeout(600_000)]
        public void Base64WholeCallComparedWithBorrowedBuffer(string shape, int length)
        {
            new BorrowedBase64Tests().CompareWholeCall(shape, length);
        }
    }
}
