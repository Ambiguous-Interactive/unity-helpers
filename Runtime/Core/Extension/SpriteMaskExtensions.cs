// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using UnityEngine;

    /// <summary>Limits sprite masks to a sorting layer and queries their layer coverage.</summary>
    public static class SpriteMaskExtensions
    {
        /// <summary>Limits a mask to every sorting order on one valid sorting layer.</summary>
        /// <remarks>A missing mask or unknown layer leaves the mask unchanged.</remarks>
        public static void RangeTo(this SpriteMask mask, int sortingLayerId)
        {
            if (mask == null || !SortingLayer.IsValid(sortingLayerId))
            {
                return;
            }

            mask.frontSortingLayerID = sortingLayerId;
            mask.backSortingLayerID = sortingLayerId;
            mask.frontSortingOrder = short.MaxValue;
            mask.backSortingOrder = short.MinValue;
            mask.isCustomRangeActive = true;
        }

        /// <summary>Limits a mask to the sorting layer assigned to that mask.</summary>
        public static void RangeToItsOwnLayer(this SpriteMask mask)
        {
            if (mask != null)
            {
                mask.RangeTo(mask.sortingLayerID);
            }
        }

        /// <summary>Reports whether a mask can affect any sorting order on a valid layer.</summary>
        /// <remarks>Order bounds and SortingGroup isolation are not part of this layer-only query.</remarks>
        public static bool Reaches(this SpriteMask mask, int sortingLayerId)
        {
            return mask != null
                && Reaches(
                    mask.isCustomRangeActive,
                    mask.frontSortingLayerID,
                    mask.backSortingLayerID,
                    sortingLayerId
                );
        }

        /// <summary>Queries mask layer coverage from saved sorting layer identifiers.</summary>
        /// <remarks>Unknown identifiers return false; range comparisons use layer order, not identifiers.</remarks>
        public static bool Reaches(
            bool customRangeActive,
            int frontSortingLayerId,
            int backSortingLayerId,
            int sortingLayerId
        )
        {
            if (!SortingLayer.IsValid(sortingLayerId))
            {
                return false;
            }

            if (!customRangeActive)
            {
                return true;
            }

            if (
                !SortingLayer.IsValid(frontSortingLayerId)
                || !SortingLayer.IsValid(backSortingLayerId)
            )
            {
                return false;
            }

            int depth = SortingLayer.GetLayerValueFromID(sortingLayerId);
            return SortingLayer.GetLayerValueFromID(backSortingLayerId) <= depth
                && depth <= SortingLayer.GetLayerValueFromID(frontSortingLayerId);
        }
    }
}
