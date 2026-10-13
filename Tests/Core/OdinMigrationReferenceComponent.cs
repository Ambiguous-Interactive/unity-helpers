// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using UnityEngine;

    /// <summary>Stores a scene reference for authored file identifier checks.</summary>
    public sealed class OdinMigrationReferenceComponent : MonoBehaviour
    {
        /// <summary>The object written into the scene's reference table.</summary>
        public Object Reference;
    }
}
