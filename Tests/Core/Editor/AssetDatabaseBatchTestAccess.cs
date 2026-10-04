// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Tests.Core
{
#if UNITY_EDITOR
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using static WallstopStudios.UnityHelpers.Editor.Utils.AssetDatabaseBatchHelper;

    public static class AssetDatabaseBatchTestAccess
    {
        /// <summary>
        ///     Increments the batch depth counter and returns whether this is the outermost scope.
        ///     This method ONLY increments the counter - it does NOT call Unity's AssetDatabase APIs.
        /// </summary>
        /// <remarks>
        ///     <strong>Warning:</strong> Using this method directly creates a mismatch between the
        ///     tracked counter and Unity's actual state. Prefer using <see cref="BeginBatch"/> instead,
        ///     which properly manages both the counter and Unity's state.
        ///     This method exists primarily for testing the counter logic in isolation.
        /// </remarks>
        /// <returns><c>true</c> if this is the outermost (first) scope; otherwise, <c>false</c>.</returns>
        public static bool IncrementBatchDepth()
        {
            lock (Lock)
            {
                int previousDepth = _batchDepth;
                ++_batchDepth;
                return previousDepth == 0;
            }
        }

        /// <summary>
        ///     Decrements the batch depth counter and returns whether this was the outermost scope.
        ///     This method ONLY decrements the counter - use <see cref="DecrementBatchDepthWithUnityCleanup"/>
        ///     when Unity cleanup will be performed.
        /// </summary>
        /// <returns><c>true</c> if this was the outermost scope (depth is now 0); otherwise, <c>false</c>.</returns>
        public static bool DecrementBatchDepth()
        {
            lock (Lock)
            {
                _batchDepth--;

                if (_batchDepth < 0)
                {
                    _batchDepth = 0;
                    return false;
                }

                return _batchDepth == 0;
            }
        }
    }
#endif
}
