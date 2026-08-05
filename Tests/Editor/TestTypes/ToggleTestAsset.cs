// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class ToggleTestAsset : ScriptableObject
    {
        [WEnumToggleButtons]
        public ExampleFlags flags = ExampleFlags.None;

        [WEnumToggleButtons]
        public ExampleEnum mode = ExampleEnum.First;

        [WEnumToggleButtons]
        [IntDropDown(30, 60, 90)]
        public int intSelection = 30;

        [WEnumToggleButtons]
        [StringInList("Idle", "Run", "Jump")]
        public string stateName = "Idle";

        [WEnumToggleButtons]
        [WValueDropDown(typeof(DropdownProvider), nameof(DropdownProvider.GetPriorityEntries))]
        public int priority = 1;

        [WEnumToggleButtons]
        [WValueDropDown(typeof(DropdownProvider), nameof(DropdownProvider.GetFloatEntries))]
        public float floatPriority = 0.5f;

        [WEnumToggleButtons(PageSize = 6)]
        [IntDropDown(0, 1, 2, 3, 4, 5, 6, 7, 8, 9)]
        public int paginatedInt;

        [WEnumToggleButtons(EnablePagination = false)]
        [StringInList("Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta")]
        public string noPaginationState = "Alpha";

        [WEnumToggleButtons]
        public SignedByteExampleEnum signedByteMode = SignedByteExampleEnum.MinusOne;

        [WEnumToggleButtons]
        public SignedShortExampleEnum signedShortMode = SignedShortExampleEnum.Minimum;

        [Flags]
        public enum ExampleFlags
        {
            None = 0,
            Move = 1 << 0,
            Jump = 1 << 1,
            Dash = 1 << 2,
        }

        public enum ExampleEnum
        {
            First,
            Second,
            Third,
        }

        // Convert.ToUInt64 throws OverflowException on every member below zero, which took the
        // whole inspector down while drawing either of these fields.
        public enum SignedByteExampleEnum : sbyte
        {
            MinusTwo = -2,
            MinusOne = -1,
            Zero = 0,
            One = 1,
        }

        public enum SignedShortExampleEnum : short
        {
            Minimum = short.MinValue,
            MinusOne = -1,
            Zero = 0,
            Maximum = short.MaxValue,
        }

        private static class DropdownProvider
        {
            internal static IEnumerable<int> GetPriorityEntries()
            {
                return new[] { 1, 2, 3 };
            }

            internal static IEnumerable<float> GetFloatEntries()
            {
                return new[] { 0.5f, 1.5f, 3f };
            }
        }
    }
}
