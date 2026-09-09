// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    internal sealed class SerializedPropertyExtensionsTestContainer : ScriptableObject
    {
        public int publicInt = 5;

        public int[] intArray = new[] { 10, 20, 30 };
        public List<int> intList = new() { 1, 2, 3 };

        public Nested nested = new();

        [SerializeField]
        internal string privateString = "hello";

        public string GetPrivateString() => privateString;

        [Serializable]
        public class Inner
        {
            public int x = 7;
        }

        [Serializable]
        public class Nested
        {
            public float f = 3.14f;

            [SerializeField]
            internal Inner inner = new();

            public Inner GetInner() => inner;
        }
    }
}
