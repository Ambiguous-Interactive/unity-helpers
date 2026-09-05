// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WGroup
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Utils;

    internal static class WGroupLayoutBuilder
    {
        private static readonly WGroupDrawOperation[] EmptyOperations =
            Array.Empty<WGroupDrawOperation>();
        private static readonly WGroupDefinition[] EmptyDefinitions =
            Array.Empty<WGroupDefinition>();
        private static readonly HashSet<string> EmptyGroupedPaths = new(0);
        private static readonly HashSet<string> EmptyHiddenPaths = new(0);
        private static readonly Dictionary<
            string,
            IReadOnlyList<WGroupDefinition>
        > EmptyAnchorToGroups = new(0);

        private static readonly Dictionary<Type, WGroupLayout> LayoutCache = new();
        private static readonly Dictionary<Type, TypePropertyMetadata> PropertyMetadataCache =
            new();
        private static readonly List<WGroupAttribute> EmptyGroupAttributes = new(0);
        private static readonly List<WGroupEndAttribute> EmptyEndAttributes = new(0);
        private static readonly WGroupLayout EmptyLayout = new(
            EmptyOperations,
            EmptyDefinitions,
            new Dictionary<string, WGroupDefinition>(0),
            EmptyGroupedPaths,
            EmptyAnchorToGroups,
            EmptyHiddenPaths
        );

        internal static void ClearCache()
        {
            LayoutCache.Clear();
            PropertyMetadataCache.Clear();
        }

        internal static WGroupLayout Build(
            SerializedObject serializedObject,
            string scriptPropertyPath
        )
        {
            if (serializedObject == null)
            {
                throw new ArgumentNullException(nameof(serializedObject));
            }

            UnityEngine.Object targetObject = serializedObject.targetObject;
            if (targetObject == null)
            {
                return EmptyLayout;
            }

            Type targetType = targetObject.GetType();

            if (LayoutCache.TryGetValue(targetType, out WGroupLayout cachedLayout))
            {
                return cachedLayout;
            }

            TypePropertyMetadata typeMetadata = GetOrCreatePropertyMetadata(targetType);
            if (typeMetadata.PropertyCount == 0)
            {
                LayoutCache[targetType] = EmptyLayout;
                return EmptyLayout;
            }

            WGroupLayout layout = BuildLayoutFromMetadata(typeMetadata, scriptPropertyPath);
            LayoutCache[targetType] = layout;
            return layout;
        }

        private static TypePropertyMetadata GetOrCreatePropertyMetadata(Type targetType)
        {
            if (PropertyMetadataCache.TryGetValue(targetType, out TypePropertyMetadata cached))
            {
                return cached;
            }

            TypePropertyMetadata metadata = BuildTypePropertyMetadata(targetType);
            PropertyMetadataCache[targetType] = metadata;
            return metadata;
        }

        private static TypePropertyMetadata BuildTypePropertyMetadata(Type targetType)
        {
            List<PropertyMetadataEntry> entries = new();
            Type currentType = targetType;

            while (currentType != null && currentType != typeof(UnityEngine.Object))
            {
                FieldInfo[] fields = currentType.GetFields(
                    BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly
                );

                foreach (FieldInfo field in fields)
                {
                    if (!IsSerializableField(field))
                    {
                        continue;
                    }

                    WGroupAttribute[] groupAttrs = field.GetAllAttributesSafe<WGroupAttribute>(
                        inherit: true
                    );
                    WGroupEndAttribute[] endAttrs = field.GetAllAttributesSafe<WGroupEndAttribute>(
                        inherit: true
                    );

                    List<WGroupAttribute> groupList =
                        groupAttrs != null && 0 < groupAttrs.Length
                            ? new List<WGroupAttribute>(groupAttrs)
                            : EmptyGroupAttributes;
                    List<WGroupEndAttribute> endList =
                        endAttrs != null && 0 < endAttrs.Length
                            ? new List<WGroupEndAttribute>(endAttrs)
                            : EmptyEndAttributes;

                    bool isHiddenInInspector = field.IsDefined(typeof(HideInInspector), false);

                    entries.Add(
                        new PropertyMetadataEntry(
                            field.Name,
                            groupList,
                            endList,
                            isHiddenInInspector
                        )
                    );
                }

                currentType = currentType.BaseType;
            }

            return new TypePropertyMetadata(entries);
        }

        private static bool IsSerializableField(FieldInfo field)
        {
            if (field.IsStatic || field.IsInitOnly)
            {
                return false;
            }

            if (field.IsPublic)
            {
                if (field.IsDefined(typeof(NonSerializedAttribute), false))
                {
                    return false;
                }
                return true;
            }

            if (field.IsDefined(typeof(SerializeField), false))
            {
                return true;
            }

            if (field.IsDefined(typeof(SerializeReference), false))
            {
                return true;
            }

            return false;
        }

        private static WGroupLayout BuildLayoutFromMetadata(
            TypePropertyMetadata typeMetadata,
            string scriptPropertyPath
        )
        {
            IReadOnlyList<PropertyMetadataEntry> entries = typeMetadata.Entries;
            if (entries.Count == 0)
            {
                return EmptyLayout;
            }

            UnityHelpersSettings.WGroupAutoIncludeConfiguration configuration =
                UnityHelpersSettings.GetWGroupAutoIncludeConfiguration();
            AutoIncludeConfiguration globalConfiguration = ConvertConfiguration(configuration);

            WallstopGenericPool<Dictionary<string, GroupContext>> contextsByNamePool =
                DictionaryBuffer<string, GroupContext>.GetDictionaryPool(
                    StringComparer.OrdinalIgnoreCase
                );
            using PooledResource<Dictionary<string, GroupContext>> contextsByNameLease =
                contextsByNamePool.Get(out Dictionary<string, GroupContext> contextsByName);
            using PooledResource<List<GroupContext>> contextsInDeclarationOrderLease =
                Buffers<GroupContext>.GetList(
                    entries.Count,
                    out List<GroupContext> contextsInDeclarationOrder
                );
            using PooledResource<List<GroupContext>> activeAutoContextsLease =
                Buffers<GroupContext>.GetList(4, out List<GroupContext> activeAutoContexts);

            List<PropertyDescriptor> descriptors = new(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                PropertyMetadataEntry entry = entries[i];
                if (
                    !string.IsNullOrEmpty(scriptPropertyPath)
                    && string.Equals(
                        entry.PropertyPath,
                        scriptPropertyPath,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                descriptors.Add(
                    new PropertyDescriptor(
                        entry.PropertyPath,
                        entry.GroupAttributes,
                        entry.EndAttributes,
                        entry.IsHiddenInInspector
                    )
                );
            }

            if (descriptors.Count == 0)
            {
                return EmptyLayout;
            }

            for (int index = 0; index < descriptors.Count; index++)
            {
                PropertyDescriptor descriptor = descriptors[index];
                HashSet<GroupContext> explicitContexts = null;
                PooledResource<HashSet<GroupContext>> explicitContextsLease = default;

                // Return the rented set even when group construction throws.
                try
                {
                    if (0 < descriptor.GroupAttributes.Count)
                    {
                        explicitContextsLease = Buffers<GroupContext>.HashSet.Get(
                            out explicitContexts
                        );
                        foreach (WGroupAttribute attribute in descriptor.GroupAttributes)
                        {
                            string normalizedName = NormalizeGroupName(attribute.GroupName);
                            if (
                                !contextsByName.TryGetValue(
                                    normalizedName,
                                    out GroupContext context
                                )
                            )
                            {
                                context = new GroupContext(
                                    normalizedName,
                                    contextsInDeclarationOrder.Count
                                );
                                contextsByName.Add(normalizedName, context);
                                contextsInDeclarationOrder.Add(context);
                            }

                            context.ApplyAttribute(attribute, descriptor.PropertyPath, index);
                            AutoIncludeConfiguration localConfiguration = ResolveAutoInclude(
                                attribute.AutoIncludeCount,
                                globalConfiguration
                            );
                            context.SetAutoInclude(localConfiguration);
                            UpdateActiveContextList(activeAutoContexts, context);

                            explicitContexts.Add(context);
                        }
                    }

                    if (explicitContexts == null || explicitContexts.Count == 0)
                    {
                        // Hidden fields require explicit WGroup inclusion rather than automatic grouping.
                        if (!descriptor.IsHiddenInInspector)
                        {
                            GroupContext autoContext = SelectAutoIncludeTarget(activeAutoContexts);
                            if (autoContext != null)
                            {
                                bool added = autoContext.AddProperty(
                                    descriptor.PropertyPath,
                                    index
                                );
                                if (added)
                                {
                                    autoContext.ConsumeAutoInclude();
                                    UpdateActiveContextList(activeAutoContexts, autoContext);
                                }
                            }
                        }
                    }
                    else
                    {
                        foreach (GroupContext context in explicitContexts)
                        {
                            context.AddProperty(descriptor.PropertyPath, index);
                        }

                        // Nested group anchors have to be included in their parent groups.
                        foreach (WGroupAttribute attribute in descriptor.GroupAttributes)
                        {
                            if (string.IsNullOrEmpty(attribute.ParentGroup))
                            {
                                continue;
                            }

                            string normalizedParentName = NormalizeGroupName(attribute.ParentGroup);
                            if (
                                !contextsByName.TryGetValue(
                                    normalizedParentName,
                                    out GroupContext parentContext
                                )
                            )
                            {
                                continue;
                            }

                            if (explicitContexts.Contains(parentContext))
                            {
                                continue;
                            }

                            bool added = parentContext.AddProperty(descriptor.PropertyPath, index);
                            if (added && parentContext.HasAutoIncludeBudget)
                            {
                                parentContext.ConsumeAutoInclude();
                            }
                        }
                    }

                    if (0 < descriptor.EndAttributes.Count)
                    {
                        ApplyGroupEnds(
                            descriptor.EndAttributes,
                            activeAutoContexts,
                            contextsByName
                        );
                    }
                }
                finally
                {
                    explicitContextsLease.Dispose();
                }
            }

            List<WGroupDefinition> definitions = new(contextsInDeclarationOrder.Count);
            WallstopGenericPool<Dictionary<string, List<WGroupDefinition>>> groupsByAnchorPool =
                DictionaryBuffer<string, List<WGroupDefinition>>.GetDictionaryPool(
                    StringComparer.Ordinal
                );
            using PooledResource<Dictionary<string, List<WGroupDefinition>>> groupsByAnchorLease =
                groupsByAnchorPool.Get(
                    out Dictionary<string, List<WGroupDefinition>> groupsByAnchor
                );
            Dictionary<string, WGroupDefinition> groupsByName = new(
                StringComparer.OrdinalIgnoreCase
            );

            HashSet<string> groupedPaths = new(StringComparer.Ordinal);
            Dictionary<string, List<WGroupDefinition>> anchorToGroupsTemp = new(
                StringComparer.Ordinal
            );

            foreach (GroupContext context in contextsInDeclarationOrder)
            {
                if (context.PropertyCount == 0)
                {
                    continue;
                }

                WGroupDefinition definition = context.ToDefinition();
                definitions.Add(definition);
                groupsByName[definition.Name] = definition;

                List<WGroupDefinition> anchored = groupsByAnchor.GetOrAdd(
                    definition.AnchorPropertyPath
                );
                anchored.Add(definition);

                foreach (string path in definition.PropertyPaths)
                {
                    groupedPaths.Add(path);
                }

                if (
                    !anchorToGroupsTemp.TryGetValue(
                        definition.AnchorPropertyPath,
                        out List<WGroupDefinition> anchorList
                    )
                )
                {
                    anchorList = new List<WGroupDefinition>();
                    anchorToGroupsTemp[definition.AnchorPropertyPath] = anchorList;
                }
                anchorList.Add(definition);
            }

            HashSet<string> circularRefs = null;
            foreach (WGroupDefinition definition in definitions)
            {
                if (string.IsNullOrEmpty(definition.ParentGroupName))
                {
                    continue;
                }

                string normalizedParentName = NormalizeGroupName(definition.ParentGroupName);
                if (
                    !groupsByName.TryGetValue(
                        normalizedParentName,
                        out WGroupDefinition parentDefinition
                    )
                )
                {
                    continue;
                }

                if (HasCircularReference(definition, groupsByName))
                {
                    circularRefs ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (circularRefs.Add(definition.Name))
                    {
                        Debug.LogWarning(
                            $"[WGroup] Circular reference detected for group '{definition.Name}' with parent '{definition.ParentGroupName}'. Treating as top-level group."
                        );
                    }
                    continue;
                }

                parentDefinition.AddChildGroup(definition);
            }

            foreach (WGroupDefinition definition in definitions)
            {
                definition.SortChildGroups();

                if (0 < definition.ChildGroups.Count)
                {
                    HashSet<string> childAnchorPaths = new(StringComparer.Ordinal);
                    foreach (WGroupDefinition child in definition.ChildGroups)
                    {
                        childAnchorPaths.Add(child.AnchorPropertyPath);
                    }

                    List<string> directPaths = new(definition.PropertyPaths.Count);
                    foreach (string path in definition.PropertyPaths)
                    {
                        if (!childAnchorPaths.Contains(path))
                        {
                            directPaths.Add(path);
                        }
                    }
                    definition.SetDirectPropertyPaths(directPaths);
                }
                else
                {
                    definition.SetDirectPropertyPaths(definition.PropertyPaths);
                }
            }

            Dictionary<string, IReadOnlyList<WGroupDefinition>> anchorToGroups = new(
                anchorToGroupsTemp.Count,
                StringComparer.Ordinal
            );
            foreach (KeyValuePair<string, List<WGroupDefinition>> kvp in anchorToGroupsTemp)
            {
                kvp.Value.Sort(
                    (left, right) => left.DeclarationOrder.CompareTo(right.DeclarationOrder)
                );
                anchorToGroups[kvp.Key] = kvp.Value;
            }

            List<WGroupDrawOperation> operations = BuildDrawOperations(
                descriptors,
                groupsByAnchor,
                groupsByName,
                out HashSet<string> hiddenPropertyPaths
            );
            return new WGroupLayout(
                operations,
                definitions,
                groupsByName,
                groupedPaths,
                anchorToGroups,
                hiddenPropertyPaths
            );
        }

        private static AutoIncludeConfiguration ConvertConfiguration(
            UnityHelpersSettings.WGroupAutoIncludeConfiguration configuration
        )
        {
            switch (configuration.Mode)
            {
                case UnityHelpersSettings.WGroupAutoIncludeMode.Infinite:
                    return new AutoIncludeConfiguration(true, 0);
                case UnityHelpersSettings.WGroupAutoIncludeMode.Finite:
                    return new AutoIncludeConfiguration(false, configuration.RowCount);
                default:
                    return new AutoIncludeConfiguration(false, 0);
            }
        }

        private static AutoIncludeConfiguration ResolveAutoInclude(
            int requestedValue,
            AutoIncludeConfiguration globalConfiguration
        )
        {
            if (requestedValue == WGroupAttribute.UseGlobalAutoInclude)
            {
                return globalConfiguration;
            }

            if (requestedValue == WGroupAttribute.InfiniteAutoInclude)
            {
                return new AutoIncludeConfiguration(true, 0);
            }

            if (requestedValue <= 0)
            {
                return new AutoIncludeConfiguration(false, 0);
            }

            return new AutoIncludeConfiguration(false, requestedValue);
        }

        // The most recently encountered group owns following members; declaration-order sorting would choose the wrong group.
        private static void UpdateActiveContextList(
            List<GroupContext> activeAutoContexts,
            GroupContext context
        )
        {
            for (int index = 0; index < activeAutoContexts.Count; index++)
            {
                if (ReferenceEquals(activeAutoContexts[index], context))
                {
                    activeAutoContexts.RemoveAt(index);
                    break;
                }
            }

            if (!context.HasAutoIncludeBudget)
            {
                return;
            }

            activeAutoContexts.Add(context);
        }

        private static GroupContext SelectAutoIncludeTarget(List<GroupContext> activeAutoContexts)
        {
            for (int index = activeAutoContexts.Count - 1; 0 <= index; index--)
            {
                GroupContext candidate = activeAutoContexts[index];
                if (!candidate.HasAutoIncludeBudget)
                {
                    activeAutoContexts.RemoveAt(index);
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private static void ApplyGroupEnds(
            List<WGroupEndAttribute> endAttributes,
            List<GroupContext> activeAutoContexts,
            Dictionary<string, GroupContext> contextsByName
        )
        {
            foreach (WGroupEndAttribute attribute in endAttributes)
            {
                IReadOnlyList<string> groupNames = attribute.GroupNames;
                if (groupNames.Count == 0)
                {
                    foreach (
                        WallstopStudios.UnityHelpers.Editor.Utils.WGroup.WGroupLayoutBuilder.GroupContext activeAutoContextsElement in activeAutoContexts
                    )
                    {
                        activeAutoContextsElement.SetAutoInclude(
                            new AutoIncludeConfiguration(false, 0)
                        );
                    }

                    activeAutoContexts.Clear();
                    continue;
                }

                for (int nameIndex = 0; nameIndex < groupNames.Count; nameIndex++)
                {
                    string groupName = groupNames[nameIndex];
                    if (string.IsNullOrEmpty(groupName))
                    {
                        continue;
                    }

                    string normalizedName = NormalizeGroupName(groupName);
                    if (!contextsByName.TryGetValue(normalizedName, out GroupContext context))
                    {
                        continue;
                    }

                    context.SetAutoInclude(new AutoIncludeConfiguration(false, 0));
                    UpdateActiveContextList(activeAutoContexts, context);
                }
            }
        }

        private static string NormalizeGroupName(string groupName)
        {
            return string.IsNullOrWhiteSpace(groupName) ? string.Empty : groupName.Trim();
        }

        private static List<WGroupDrawOperation> BuildDrawOperations(
            List<PropertyDescriptor> descriptors,
            Dictionary<string, List<WGroupDefinition>> groupsByAnchor,
            Dictionary<string, WGroupDefinition> groupsByName,
            out HashSet<string> hiddenPropertyPaths
        )
        {
            List<WGroupDrawOperation> operations = new(descriptors.Count);
            HashSet<string> hidden = new(StringComparer.Ordinal);
            WallstopGenericPool<HashSet<string>> consumedPool = SetBuffers<string>.GetHashSetPool(
                StringComparer.Ordinal
            );
            using PooledResource<HashSet<string>> consumedLease = consumedPool.Get(
                out HashSet<string> consumed
            );
            {
                foreach (PropertyDescriptor descriptor in descriptors)
                {
                    string propertyPath = descriptor.PropertyPath;
                    bool anchoredHandled = false;
                    if (
                        groupsByAnchor.TryGetValue(
                            propertyPath,
                            out List<WGroupDefinition> anchoredGroups
                        )
                    )
                    {
                        anchoredHandled = true;
                        anchoredGroups.Sort(
                            (left, right) => left.DeclarationOrder.CompareTo(right.DeclarationOrder)
                        );
                        foreach (WGroupDefinition definition in anchoredGroups)
                        {
                            if (definition.HasParent)
                            {
                                string normalizedParentName = NormalizeGroupName(
                                    definition.ParentGroupName
                                );
                                if (
                                    groupsByName.TryGetValue(
                                        normalizedParentName,
                                        out WGroupDefinition parentDef
                                    ) && parentDef.ChildGroups.Contains(definition)
                                )
                                {
                                    for (
                                        int memberIndex = 0;
                                        memberIndex < definition.PropertyPaths.Count;
                                        memberIndex++
                                    )
                                    {
                                        consumed.Add(definition.PropertyPaths[memberIndex]);
                                    }

                                    continue;
                                }
                            }

                            operations.Add(new WGroupDrawOperation(definition));
                            for (
                                int memberIndex = 0;
                                memberIndex < definition.PropertyPaths.Count;
                                memberIndex++
                            )
                            {
                                consumed.Add(definition.PropertyPaths[memberIndex]);
                            }
                        }
                    }

                    if (anchoredHandled)
                    {
                        continue;
                    }

                    if (!consumed.Add(propertyPath))
                    {
                        continue;
                    }

                    bool isHidden = descriptor.IsHiddenInInspector;
                    if (isHidden)
                    {
                        hidden.Add(propertyPath);
                    }

                    operations.Add(new WGroupDrawOperation(propertyPath, isHidden));
                }
            }

            hiddenPropertyPaths = hidden;
            return operations;
        }

        /// <summary>
        /// Checks if a group definition has a circular reference in its parent chain.
        /// </summary>
        private static bool HasCircularReference(
            WGroupDefinition definition,
            Dictionary<string, WGroupDefinition> groupsByName
        )
        {
            HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase) { definition.Name };
            string currentParentName = definition.ParentGroupName;

            while (!string.IsNullOrEmpty(currentParentName))
            {
                string normalizedName = NormalizeGroupName(currentParentName);
                if (!visited.Add(normalizedName))
                {
                    return true;
                }

                if (!groupsByName.TryGetValue(normalizedName, out WGroupDefinition parentDef))
                {
                    break;
                }

                currentParentName = parentDef.ParentGroupName;
            }

            return false;
        }

        private sealed class GroupContext
        {
            private readonly List<PropertyEntry> _entries = new();
            private readonly HashSet<string> _lookup = new(StringComparer.Ordinal);
            private bool _hasExplicitStartCollapsed;
            private WGroupAttribute.WGroupCollapseBehavior _collapseBehavior = WGroupAttribute
                .WGroupCollapseBehavior
                .UseProjectSetting;

            internal GroupContext(string name, int declarationOrder)
            {
                Name = name;
                DeclarationOrder = declarationOrder;
                AnchorIndex = int.MaxValue;
            }

            internal string Name { get; }

            internal int DeclarationOrder { get; }

            internal string DisplayName { get; private set; }

            internal bool Collapsible { get; private set; }

            internal bool StartCollapsed { get; private set; }

            internal bool HideHeader { get; private set; }

            internal int AnchorIndex { get; private set; }

            internal string AnchorPropertyPath { get; private set; }

            internal bool AutoIncludeInfinite { get; private set; }

            internal int RemainingAutoInclude { get; private set; }

            internal string ParentGroupName { get; private set; }

            internal int PropertyCount
            {
                get { return _entries.Count; }
            }

            internal void ApplyAttribute(
                WGroupAttribute attribute,
                string propertyPath,
                int propertyIndex
            )
            {
                // Only explicit display names may overwrite an existing name.
                if (
                    !string.IsNullOrWhiteSpace(attribute.DisplayName)
                    && !string.Equals(
                        attribute.DisplayName,
                        attribute.GroupName,
                        StringComparison.Ordinal
                    )
                )
                {
                    DisplayName = attribute.DisplayName;
                }

                if (!string.IsNullOrWhiteSpace(attribute.ParentGroup))
                {
                    ParentGroupName = attribute.ParentGroup.Trim();
                }

                if (Collapsible != attribute.Collapsible)
                {
                    Collapsible = attribute.Collapsible;
                    if (!Collapsible)
                    {
                        StartCollapsed = false;
                        _hasExplicitStartCollapsed = false;
                        _collapseBehavior = WGroupAttribute
                            .WGroupCollapseBehavior
                            .UseProjectSetting;
                    }
                }

                if (attribute.Collapsible)
                {
                    _collapseBehavior = attribute.CollapseBehavior;

                    switch (_collapseBehavior)
                    {
                        case WGroupAttribute.WGroupCollapseBehavior.ForceCollapsed:
                            StartCollapsed = true;
                            _hasExplicitStartCollapsed = true;
                            break;
                        case WGroupAttribute.WGroupCollapseBehavior.ForceExpanded:
                            StartCollapsed = false;
                            _hasExplicitStartCollapsed = true;
                            break;
                        default:
                            if (!_hasExplicitStartCollapsed)
                            {
                                StartCollapsed = UnityHelpersSettings.ShouldStartWGroupCollapsed();
                            }
                            break;
                    }
                }

                HideHeader = attribute.HideHeader;

                AddProperty(propertyPath, propertyIndex);
            }

            internal void SetAutoInclude(AutoIncludeConfiguration configuration)
            {
                AutoIncludeInfinite = configuration.IsInfinite;
                RemainingAutoInclude = configuration.IsInfinite ? 0 : configuration.Count;
            }

            internal bool AddProperty(string propertyPath, int propertyIndex)
            {
                if (!_lookup.Add(propertyPath))
                {
                    return false;
                }

                PropertyEntry entry = new(propertyPath, propertyIndex);
                _entries.Add(entry);

                if (propertyIndex < AnchorIndex)
                {
                    AnchorIndex = propertyIndex;
                    AnchorPropertyPath = propertyPath;
                }

                return true;
            }

            internal void ConsumeAutoInclude()
            {
                if (AutoIncludeInfinite)
                {
                    return;
                }

                if (RemainingAutoInclude <= 0)
                {
                    return;
                }

                RemainingAutoInclude--;
            }

            internal bool HasAutoIncludeBudget
            {
                get
                {
                    if (AutoIncludeInfinite)
                    {
                        return true;
                    }

                    return 0 < RemainingAutoInclude;
                }
            }

            internal WGroupDefinition ToDefinition()
            {
                _entries.Sort((left, right) => left.PropertyIndex.CompareTo(right.PropertyIndex));
                List<string> orderedPaths = new(_entries.Count);
                foreach (
                    WallstopStudios.UnityHelpers.Editor.Utils.WGroup.WGroupLayoutBuilder.GroupContext.PropertyEntry entriesElement in _entries
                )
                {
                    orderedPaths.Add(entriesElement.PropertyPath);
                }

                string displayName = DisplayName ?? Name;
                string anchorPath = AnchorPropertyPath ?? orderedPaths[0];
                int anchorIndex = AnchorIndex == int.MaxValue ? 0 : AnchorIndex;

                return new WGroupDefinition(
                    Name,
                    displayName,
                    Collapsible,
                    StartCollapsed,
                    HideHeader,
                    orderedPaths,
                    anchorPath,
                    anchorIndex,
                    DeclarationOrder,
                    ParentGroupName
                );
            }

            private readonly struct PropertyEntry
            {
                internal PropertyEntry(string propertyPath, int propertyIndex)
                {
                    PropertyPath = propertyPath;
                    PropertyIndex = propertyIndex;
                }

                internal string PropertyPath { get; }

                internal int PropertyIndex { get; }
            }
        }

        private sealed class PropertyDescriptor
        {
            internal PropertyDescriptor(
                string propertyPath,
                List<WGroupAttribute> groupAttributes,
                List<WGroupEndAttribute> endAttributes,
                bool isHiddenInInspector = false
            )
            {
                PropertyPath = propertyPath;
                GroupAttributes = groupAttributes ?? new List<WGroupAttribute>();
                EndAttributes = endAttributes ?? new List<WGroupEndAttribute>();
                IsHiddenInInspector = isHiddenInInspector;
            }

            internal string PropertyPath { get; }

            internal List<WGroupAttribute> GroupAttributes { get; }

            internal List<WGroupEndAttribute> EndAttributes { get; }

            internal bool IsHiddenInInspector { get; }
        }

        private readonly struct AutoIncludeConfiguration
        {
            internal AutoIncludeConfiguration(bool isInfinite, int count)
            {
                IsInfinite = isInfinite;
                Count = count < 0 ? 0 : count;
            }

            internal bool IsInfinite { get; }

            internal int Count { get; }
        }
    }

    internal enum WGroupDrawOperationType
    {
        Property = 0,
        Group = 1,
    }

    internal readonly struct WGroupDrawOperation
    {
        internal WGroupDrawOperation(string propertyPath, bool isHiddenInInspector = false)
        {
            Type = WGroupDrawOperationType.Property;
            PropertyPath = propertyPath;
            Group = null;
            IsHiddenInInspector = isHiddenInInspector;
        }

        internal WGroupDrawOperation(WGroupDefinition group)
        {
            Type = WGroupDrawOperationType.Group;
            PropertyPath = null;
            Group = group;
            IsHiddenInInspector = false;
        }

        internal WGroupDrawOperationType Type { get; }

        internal string PropertyPath { get; }

        internal WGroupDefinition Group { get; }

        /// <summary>
        /// Whether this property has [HideInInspector] attribute.
        /// Only relevant for Property operations.
        /// </summary>
        internal bool IsHiddenInInspector { get; }
    }

    internal sealed class WGroupDefinition
    {
        private static readonly List<WGroupDefinition> EmptyChildGroups = new(0);
        private static readonly IReadOnlyList<string> EmptyDirectPropertyPaths =
            Array.Empty<string>();

        private List<WGroupDefinition> _childGroups;
        private IReadOnlyList<string> _directPropertyPaths;

        internal WGroupDefinition(
            string name,
            string displayName,
            bool collapsible,
            bool startCollapsed,
            bool hideHeader,
            IReadOnlyList<string> propertyPaths,
            string anchorPropertyPath,
            int anchorIndex,
            int declarationOrder,
            string parentGroupName = null
        )
        {
            Name = name;
            DisplayName = displayName;
            Collapsible = collapsible;
            StartCollapsed = startCollapsed;
            HideHeader = hideHeader;
            PropertyPaths = propertyPaths;
            AnchorPropertyPath = anchorPropertyPath;
            AnchorIndex = anchorIndex;
            DeclarationOrder = declarationOrder;
            ParentGroupName = parentGroupName;
            _childGroups = null;
            _directPropertyPaths = null;
        }

        internal string Name { get; }

        internal string DisplayName { get; }

        internal bool Collapsible { get; }

        internal bool StartCollapsed { get; }

        internal bool HideHeader { get; }

        internal IReadOnlyList<string> PropertyPaths { get; }

        internal string AnchorPropertyPath { get; }

        internal int AnchorIndex { get; }

        internal int DeclarationOrder { get; }

        /// <summary>
        /// The name of the parent group, or null for top-level groups.
        /// </summary>
        internal string ParentGroupName { get; }

        /// <summary>
        /// Child groups to be rendered inside this group. Populated after construction.
        /// </summary>
        internal List<WGroupDefinition> ChildGroups
        {
            get => _childGroups ?? EmptyChildGroups;
        }

        /// <summary>
        /// Property paths excluding child group anchor paths.
        /// </summary>
        internal IReadOnlyList<string> DirectPropertyPaths
        {
            get => _directPropertyPaths ?? EmptyDirectPropertyPaths;
        }

        /// <summary>
        /// Returns true if this group has a parent group.
        /// </summary>
        internal bool HasParent => !string.IsNullOrEmpty(ParentGroupName);

        /// <summary>
        /// Adds a child group to this group's ChildGroups list.
        /// </summary>
        internal void AddChildGroup(WGroupDefinition child)
        {
            _childGroups ??= new List<WGroupDefinition>();
            _childGroups.Add(child);
        }

        /// <summary>
        /// Sorts child groups by their declaration order.
        /// </summary>
        internal void SortChildGroups()
        {
            _childGroups?.Sort(
                (left, right) => left.DeclarationOrder.CompareTo(right.DeclarationOrder)
            );
        }

        /// <summary>
        /// Sets the direct property paths (excluding child anchor paths).
        /// </summary>
        internal void SetDirectPropertyPaths(IReadOnlyList<string> paths)
        {
            _directPropertyPaths = paths ?? EmptyDirectPropertyPaths;
        }
    }

    internal sealed class WGroupLayout
    {
        internal WGroupLayout(
            IReadOnlyList<WGroupDrawOperation> operations,
            IReadOnlyList<WGroupDefinition> groups,
            IReadOnlyDictionary<string, WGroupDefinition> groupsByName,
            IReadOnlyCollection<string> groupedPaths,
            IReadOnlyDictionary<string, IReadOnlyList<WGroupDefinition>> anchorToGroups,
            IReadOnlyCollection<string> hiddenPropertyPaths
        )
        {
            Operations = operations;
            Groups = groups;
            GroupsByName = groupsByName;
            GroupedPaths = groupedPaths;
            AnchorToGroups = anchorToGroups;
            HiddenPropertyPaths = hiddenPropertyPaths;
        }

        internal IReadOnlyList<WGroupDrawOperation> Operations { get; }

        internal IReadOnlyList<WGroupDefinition> Groups { get; }

        internal IReadOnlyDictionary<string, WGroupDefinition> GroupsByName { get; }

        /// <summary>
        /// All property paths that belong to any group. Used for quick lookup during iteration.
        /// </summary>
        internal IReadOnlyCollection<string> GroupedPaths { get; }

        /// <summary>
        /// Maps anchor property paths to the groups that should be drawn at that anchor.
        /// </summary>
        internal IReadOnlyDictionary<
            string,
            IReadOnlyList<WGroupDefinition>
        > AnchorToGroups { get; }

        /// <summary>
        /// All property paths that have [HideInInspector] attribute.
        /// Used for quick lookup during drawing to skip hidden properties.
        /// </summary>
        internal IReadOnlyCollection<string> HiddenPropertyPaths { get; }

        internal bool TryGetGroup(string groupName, out WGroupDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                definition = null;
                return false;
            }

            return GroupsByName.TryGetValue(groupName.Trim(), out definition);
        }
    }

    internal sealed class TypePropertyMetadata
    {
        internal TypePropertyMetadata(List<PropertyMetadataEntry> entries)
        {
            Entries = entries;
        }

        internal IReadOnlyList<PropertyMetadataEntry> Entries { get; }

        internal int PropertyCount => Entries.Count;
    }

    internal readonly struct PropertyMetadataEntry
    {
        internal PropertyMetadataEntry(
            string propertyPath,
            List<WGroupAttribute> groupAttributes,
            List<WGroupEndAttribute> endAttributes,
            bool isHiddenInInspector = false
        )
        {
            PropertyPath = propertyPath;
            GroupAttributes = groupAttributes;
            EndAttributes = endAttributes;
            IsHiddenInInspector = isHiddenInInspector;
        }

        internal string PropertyPath { get; }

        internal List<WGroupAttribute> GroupAttributes { get; }

        internal List<WGroupEndAttribute> EndAttributes { get; }

        internal bool IsHiddenInInspector { get; }
    }
#endif
}
