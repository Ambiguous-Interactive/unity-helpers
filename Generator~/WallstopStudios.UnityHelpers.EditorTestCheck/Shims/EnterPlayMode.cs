// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace UnityEngine.TestTools
{
    /// <summary>Provides the type-check declaration for Unity's PlayMode entry instruction.</summary>
    public sealed class EnterPlayMode
    {
        /// <summary>Declares PlayMode entry with the expected domain reload behavior.</summary>
        public EnterPlayMode(bool expectDomainReload = true) { }
    }
}
