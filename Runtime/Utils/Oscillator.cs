// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using UnityEngine;

    [DisallowMultipleComponent]
    public sealed class Oscillator : MonoBehaviour
    {
        public float speed = 1f;
        public float width = 1f;
        public float height = 1f;

        internal Vector3 _initialLocalPosition;

        private void Awake()
        {
            _initialLocalPosition = transform.localPosition;
        }

        private void Update()
        {
            float time = Time.time;
            transform.localPosition = CalculateLocalPosition(
                _initialLocalPosition,
                time,
                speed,
                width,
                height
            );
        }

        internal static Vector3 CalculateLocalPosition(
            Vector3 initialLocalPosition,
            float time,
            float speed,
            float width,
            float height
        )
        {
            return initialLocalPosition
                + new Vector3(Mathf.Cos(time * speed) * width, Mathf.Sin(time * speed) * height);
        }
    }
}
