// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    internal sealed class RelationalCollectionCallbackComparer<T> : IEqualityComparer<T>
        where T : Component
    {
        private readonly Action _callback;
        private bool _invoking;

        internal RelationalCollectionCallbackComparer(Action callback)
        {
            _callback = callback;
        }

        public bool Equals(T left, T right)
        {
            return EqualityComparer<T>.Default.Equals(left, right);
        }

        public int GetHashCode(T component)
        {
            if (!_invoking)
            {
                _invoking = true;
                try
                {
                    _callback?.Invoke();
                }
                finally
                {
                    _invoking = false;
                }
            }
            return EqualityComparer<T>.Default.GetHashCode(component);
        }
    }
}
