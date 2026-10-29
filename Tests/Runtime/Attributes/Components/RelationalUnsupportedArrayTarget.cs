// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#pragma warning disable WUH025 // Deliberately invalid fields exercise runtime rejection for older assemblies.

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class RelationalUnsupportedArrayTarget : MonoBehaviour
    {
        [ParentComponent(Optional = true)]
        [SiblingComponent(Optional = true)]
        [ChildComponent(Optional = true)]
        public SpriteRenderer[,] rectangle = new SpriteRenderer[1, 1];

        [ParentComponent(Optional = true)]
        [SiblingComponent(Optional = true)]
        [ChildComponent(Optional = true)]
        public SpriteRenderer[,,] cube = new SpriteRenderer[1, 1, 1];

        [ParentComponent(Optional = true)]
        [SiblingComponent(Optional = true)]
        [ChildComponent(Optional = true)]
        [NonSerialized]
        public SpriteRenderer[][] jagged = new SpriteRenderer[1][];

        [ParentComponent(Optional = true)]
        [SiblingComponent(Optional = true)]
        [ChildComponent(Optional = true)]
        public int number = 37;

        [ParentComponent(Optional = true)]
        [SiblingComponent(Optional = true)]
        [ChildComponent(Optional = true)]
        public string text = "Authored";

        [ParentComponent(Optional = true)]
        [SiblingComponent(Optional = true)]
        [ChildComponent(Optional = true)]
        public List<int> numbers = new() { 37 };

        [ParentComponent(Optional = true)]
        [SiblingComponent(Optional = true)]
        [ChildComponent(Optional = true)]
        public HashSet<int> numberSet = new() { 37 };

        [ParentComponent(Optional = true)]
        [SiblingComponent(Optional = true)]
        [ChildComponent(Optional = true)]
        public SpriteRenderer valid;
    }
}

#pragma warning restore WUH025
