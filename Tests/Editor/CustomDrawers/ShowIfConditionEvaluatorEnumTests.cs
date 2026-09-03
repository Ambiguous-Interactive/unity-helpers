// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ShowIfConditionEvaluatorEnumTests
    {
        [Test]
        public void ValuesEqualMatchesEveryMemberOfAUnsignedBackedEnum(
            [Values(
                UnsignedExampleEnum.None,
                UnsignedExampleEnum.One,
                UnsignedExampleEnum.AboveSignedMaximum,
                UnsignedExampleEnum.Highest
            )]
                UnsignedExampleEnum member
        )
        {
            Assert.IsTrue(ShowIfConditionEvaluator.ValuesEqual(member, member));
        }

        [Test]
        public void ValuesEqualSeparatesTwoMembersAboveTheSignedMaximum()
        {
            Assert.IsFalse(
                ShowIfConditionEvaluator.ValuesEqual(
                    UnsignedExampleEnum.AboveSignedMaximum,
                    UnsignedExampleEnum.Highest
                )
            );
        }

        [Test]
        public void ValuesEqualMatchesAFlagWhoseBitIsTheSignBit()
        {
            Assert.IsTrue(
                ShowIfConditionEvaluator.ValuesEqual(
                    UnsignedExampleFlags.TopBit | UnsignedExampleFlags.Low,
                    UnsignedExampleFlags.TopBit
                )
            );
            Assert.IsFalse(
                ShowIfConditionEvaluator.ValuesEqual(
                    UnsignedExampleFlags.Low | UnsignedExampleFlags.Mid,
                    UnsignedExampleFlags.TopBit
                )
            );
        }

        [Test]
        public void ValuesEqualStillMatchesEveryMemberOfASignedBackedEnum(
            [Values(
                SignedExampleEnum.None,
                SignedExampleEnum.Minimum,
                SignedExampleEnum.MinusOne,
                SignedExampleEnum.Maximum
            )]
                SignedExampleEnum member
        )
        {
            Assert.IsTrue(ShowIfConditionEvaluator.ValuesEqual(member, member));
        }

        [Test]
        public void ValuesEqualStillComparesAnEnumAgainstThePlainNumberAnAttributeCarries()
        {
            Assert.IsTrue(ShowIfConditionEvaluator.ValuesEqual(UnsignedExampleEnum.One, 1));
            Assert.IsTrue(ShowIfConditionEvaluator.ValuesEqual(UnsignedExampleEnum.One, 1.0));
            Assert.IsTrue(ShowIfConditionEvaluator.ValuesEqual(SignedExampleEnum.MinusOne, -1));
            Assert.IsFalse(ShowIfConditionEvaluator.ValuesEqual(UnsignedExampleEnum.One, 2));
        }

        /*
            Convert.ToInt64 throws OverflowException on any of these above long.MaxValue, and the
            evaluator's catch turned that into a silent "does not match" -- so a WShowIf naming
            Highest hid the field it was supposed to reveal.
        */
        public enum UnsignedExampleEnum : ulong
        {
            None = 0,
            One = 1,
            AboveSignedMaximum = (ulong)long.MaxValue + 1UL,
            Highest = ulong.MaxValue,
        }

        [Flags]
        public enum UnsignedExampleFlags : ulong
        {
            None = 0,
            Low = 1UL << 0,
            Mid = 1UL << 1,
            TopBit = 1UL << 63,
        }

        public enum SignedExampleEnum : long
        {
            None = 0,
            Minimum = long.MinValue,
            MinusOne = -1,
            Maximum = long.MaxValue,
        }
    }
}
