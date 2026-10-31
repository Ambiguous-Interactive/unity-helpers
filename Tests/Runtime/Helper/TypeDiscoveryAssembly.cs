// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Reflection;

    internal sealed class TypeDiscoveryAssembly : Assembly
    {
        private readonly Type[] _types;
        private readonly Exception _failure;

        internal TypeDiscoveryAssembly(Type[] types, Exception failure = null)
        {
            _types = types;
            _failure = failure;
        }

        public override Type[] GetTypes()
        {
            if (_failure != null)
            {
                throw _failure;
            }
            return _types;
        }
    }
}
