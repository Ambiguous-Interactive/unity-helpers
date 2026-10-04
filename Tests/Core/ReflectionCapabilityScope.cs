// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Helper;

    public sealed class ReflectionCapabilityScope : IDisposable
    {
        private readonly bool _previousExpressions;
        private readonly bool _previousDynamicIl;
        private bool _disposed;

        public ReflectionCapabilityScope(bool? expressions, bool? dynamicIl)
        {
            _previousExpressions = ReflectionHelpers.ExpressionsEnabled;
            _previousDynamicIl = ReflectionHelpers.DynamicIlEnabled;
            ReflectionHelpers.ExpressionsEnabled = expressions ?? _previousExpressions;
            ReflectionHelpers.DynamicIlEnabled = dynamicIl ?? _previousDynamicIl;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            ReflectionHelpers.ExpressionsEnabled = _previousExpressions;
            ReflectionHelpers.DynamicIlEnabled = _previousDynamicIl;
            _disposed = true;
        }
    }
}
