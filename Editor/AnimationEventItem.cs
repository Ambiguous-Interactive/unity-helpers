// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEngine;

    /// <summary>
    /// Represents a single animation event row in the editor along with cached UI state.
    /// </summary>
    internal sealed class AnimationEventItem
    {
        public AnimationEventItem(AnimationEvent animationEvent)
        {
            this.animationEvent = animationEvent;
            search = string.Empty;
            typeSearch = string.Empty;
        }

        public Type selectedType;
        public MethodInfo selectedMethod;
        public string search;
        public string typeSearch;
        public readonly AnimationEvent animationEvent;
        public bool isTextureReadable;
        public bool isInvalidTextureRect;
        public Sprite sprite;
        public float resolvedSpriteTime;
        public int? originalIndex;
        public bool overrideEnumValues;
        public bool isValid = true;
        public string validationMessage = string.Empty;

        public IReadOnlyDictionary<Type, IReadOnlyList<MethodInfo>> cachedLookup;
        public string lastSearchForCache;
    }
#endif
}
