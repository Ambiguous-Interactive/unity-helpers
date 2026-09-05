// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;

    [Flags]
    public enum MatchTransformMode
    {
        [Obsolete]
        None = 0,
        Update = 1 << 0,
        FixedUpdate = 1 << 1,
        LateUpdate = 1 << 2,
        Awake = 1 << 3,
        Start = 1 << 4,
    }

    [DisallowMultipleComponent]
    public sealed class MatchTransform : MonoBehaviour
    {
        public Transform toMatch;
        public Vector3 localOffset;

        public MatchTransformMode mode = MatchTransformMode.Update;

        [SiblingComponent]
        internal Transform _transform;

        // Remember the original position when matching self so offsets do not accumulate each frame.
        private bool _hasSelfBasePosition;
        private Vector3 _selfBasePosition;

        private void Awake()
        {
            this.AssignRelationalComponents();
            if (mode.HasFlagNoAlloc(MatchTransformMode.Awake))
            {
                Match();
            }
        }

        private void Start()
        {
            if (mode.HasFlagNoAlloc(MatchTransformMode.Start))
            {
                Match();
            }
        }

        private void Update()
        {
            if (mode.HasFlagNoAlloc(MatchTransformMode.Update))
            {
                Match();
            }
        }

        private void FixedUpdate()
        {
            if (mode.HasFlagNoAlloc(MatchTransformMode.FixedUpdate))
            {
                Match();
            }
        }

        private void LateUpdate()
        {
            if (mode.HasFlagNoAlloc(MatchTransformMode.LateUpdate))
            {
                Match();
            }
        }

        private void Match()
        {
            if (toMatch == null)
            {
                return;
            }

            if (toMatch == _transform)
            {
                if (!_hasSelfBasePosition)
                {
                    _selfBasePosition = _transform.position;
                    _hasSelfBasePosition = true;
                }

                _transform.position = _selfBasePosition + localOffset;
                return;
            }

            _transform.position = toMatch.position + localOffset;
        }
    }
}
