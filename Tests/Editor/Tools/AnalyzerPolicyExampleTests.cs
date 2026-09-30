// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Tools;

    [TestFixture]
    public sealed class AnalyzerPolicyExampleTests
    {
        [Test]
        public void EveryPolicyHasDistinctExplainedExamples()
        {
            foreach (AnalyzerPolicy policy in AnalyzerPolicyAPI.Policies)
            {
                Assert.IsTrue(
                    AnalyzerPolicyExamples.TryGet(
                        policy.Id,
                        out string bad,
                        out string good,
                        out string why
                    ),
                    policy.Id
                );
                Assert.IsFalse(string.IsNullOrWhiteSpace(bad), policy.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(good), policy.Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(why), policy.Id);
                Assert.AreNotEqual(bad, good, policy.Id);
                Assert.That(bad, Does.Not.Contain("\u00A0"), policy.Id);
                Assert.That(good, Does.Not.Contain("\u00A0"), policy.Id);
                Assert.AreNotEqual(bad, AnalyzerPolicyCodeHighlighting.Format(bad), policy.Id);
                Assert.AreNotEqual(good, AnalyzerPolicyCodeHighlighting.Format(good), policy.Id);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("WUH020")]
        [TestCase("WPROTO001")]
        public void UnknownPoliciesReturnEmptyExamples(string id)
        {
            Assert.IsFalse(
                AnalyzerPolicyExamples.TryGet(id, out string bad, out string good, out string why)
            );
            Assert.IsEmpty(bad);
            Assert.IsEmpty(good);
            Assert.IsEmpty(why);
        }
    }
}
