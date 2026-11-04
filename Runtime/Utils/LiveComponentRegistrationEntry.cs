// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using UnityEngine;

    internal struct LiveComponentRegistrationEntry
    {
        internal Component component;
        internal LiveComponentRegistration owner;
        internal bool manual;
    }
}
