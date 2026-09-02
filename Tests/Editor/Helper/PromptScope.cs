// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
#if UNITY_EDITOR
    using System;
    using WallstopStudios.UnityHelpers.Core.Helper;

    public readonly struct PromptScope : IDisposable
    {
        private readonly RestorableGlobal<bool>.Scope _scope;

        private PromptScope(RestorableGlobal<bool>.Scope scope)
        {
            _scope = scope;
        }

        public void Dispose()
        {
            _scope.Dispose();
        }

        public static PromptScope Suppress(Func<bool> getter, Action<bool> setter)
        {
            RestorableGlobal<bool> owner = new RestorableGlobal<bool>(getter, setter);
            return new PromptScope(owner.Borrow(true));
        }
    }
#endif
}
