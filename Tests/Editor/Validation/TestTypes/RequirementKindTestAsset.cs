// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Serialization;

    internal sealed class RequirementKindTestAsset : ScriptableObject
    {
        public static int shared;

        [NonEmptyRequirementTest]
        public int[] numbers;

        [NonEmptyRequirementTest]
        public List<string> names;

        [NonEmptyRequirementTest]
        public string text;

        [NonDefaultRequirementTest]
        [FormerlySerializedAs("oldCount")]
        public int count;

        [NonDefaultRequirementTest]
        public bool enabledFlag;

        [NonDefaultRequirementTest]
        public float amount;

        [NonDefaultRequirementTest]
        public Material material;

        [NonDefaultRequirementTest]
        public Vector3 direction;

        [NonDefaultRequirementTest]
        public Color tint;

        [NonDefaultRequirementTest]
        public Bounds bounds;

        [NonDefaultRequirementTest]
        public HideFlags flags;

        [NonSerialized]
        public int nonSerialized;

        public readonly int fixedValue;
    }
}
