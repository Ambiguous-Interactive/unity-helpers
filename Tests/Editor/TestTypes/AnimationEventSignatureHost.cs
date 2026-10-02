// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public enum AnimationEventSignal
    {
        Ready,
        Done,
    }

    public sealed class AnimationEventSignatureHost : MonoBehaviour
    {
        [AnimationEvent]
        public void NoArgs() { }

        [AnimationEvent]
        public void WithInt(int value) { }

        [AnimationEvent]
        public void WithEnum(AnimationEventSignal signal) { }

        [AnimationEvent]
        public void WithFloat(float value) { }

        [AnimationEvent]
        public void WithString(string text) { }

        [AnimationEvent]
        public void WithUnityObject(Object target) { }

        [AnimationEvent]
        public void TwoParameters(int value, string text) { }

        [AnimationEvent]
        public int NonVoidReturn()
        {
            return 0;
        }
    }
}
