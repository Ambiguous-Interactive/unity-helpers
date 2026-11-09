// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using static WallstopStudios.UnityHelpers.Core.Attributes.RelationalComponentProcessor;

    internal static class ParentCollectionQuery
    {
        internal static FieldMetadata<ParentComponentAttribute>[] Create(
            Type ownerType,
            Type elementType,
            bool onlyAncestors,
            int maxDepth,
            int maxCount,
            bool includeInactive = true,
            string nameFilter = null,
            bool includeArrays = true
        )
        {
            List<FieldMetadata<ParentComponentAttribute>> selected = new();
            foreach (
                FieldMetadata<ParentComponentAttribute> field in GetFieldMetadata<ParentComponentAttribute>(
                    ownerType
                )
            )
            {
                if (
                    field.kind == FieldKind.Single
                    || !includeArrays && field.kind == FieldKind.Array
                    || elementType != null && field.elementType != elementType
                )
                {
                    continue;
                }
                ParentComponentAttribute attribute = new()
                {
                    Optional = true,
                    OnlyAncestors = onlyAncestors,
                    MaxDepth = maxDepth,
                    MaxCount = maxCount,
                    IncludeInactive = includeInactive,
                    NameFilter = nameFilter,
                };
                selected.Add(
                    new FieldMetadata<ParentComponentAttribute>(
                        field.field,
                        attribute,
                        new FilterParameters(attribute),
                        new RelationalFieldAccessor(field.field),
                        field.kind,
                        field.elementType,
                        field.arrayCreator,
                        field.listCreator,
                        field.hashSetCreator,
                        field.hashSetAdder,
                        field.hashSetClearer,
                        field.isInterface
                    )
                );
            }
            return selected.ToArray();
        }
    }
}
