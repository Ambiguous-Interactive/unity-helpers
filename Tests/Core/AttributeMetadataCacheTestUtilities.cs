// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tags;

    public static class AttributeMetadataCacheTestUtilities
    {
        /// <summary>Rebuilds cached metadata after fixture inputs change.</summary>
        public static void Rebuild(AttributeMetadataCache cache)
        {
            Assert.IsTrue(cache != null);
            lock (cache._lookupLock)
            {
                cache._typeFieldsLookup = null;
                cache._relationalFieldsLookup = null;
                cache._resolvedRelationalFieldsLookup = null;
                cache._elementTypeLookup = null;
                cache.BuildLookup();
            }
        }
    }
}
