// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Sirenix.OdinInspector.Editor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;

    /// <summary>
    /// Odin Inspector attribute drawer for <see cref="WShowIfAttribute"/>.
    /// Conditionally shows or hides properties based on the value of another field.
    /// </summary>
    /// <remarks>
    /// This drawer ensures WShowIf works correctly when Odin Inspector is installed
    /// and classes derive from SerializedMonoBehaviour or SerializedScriptableObject,
    /// where Unity's standard PropertyDrawer system is bypassed.
    /// </remarks>
    public sealed class WShowIfOdinDrawer : OdinAttributeDrawer<WShowIfAttribute>
    {
        private static readonly Dictionary<(Type, string), MemberInfo> MemberCache = new();

        /// <summary>
        /// Clears all cached state. Called during domain reload via
        /// <see cref="Internal.EditorCacheManager.ClearAllCaches"/>.
        /// </summary>
        internal static void ClearCache()
        {
            MemberCache.Clear();
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            WShowIfAttribute showIf = Attribute;
            if (showIf == null)
            {
                CallNextDrawer(label);
                return;
            }

            object parentValue = Property.Parent?.ValueEntry?.WeakSmartValue;
            if (parentValue == null)
            {
                CallNextDrawer(label);
                return;
            }

            object conditionValue = GetConditionValue(parentValue, showIf.conditionField);
            if (
                !ShowIfConditionEvaluator.TryEvaluateCondition(
                    conditionValue,
                    showIf,
                    out bool shouldShow
                )
            )
            {
                CallNextDrawer(label);
                return;
            }

            if (shouldShow)
            {
                CallNextDrawer(label);
            }
        }

        private static object GetConditionValue(object parent, string conditionField)
        {
            if (parent == null || string.IsNullOrEmpty(conditionField))
            {
                return null;
            }

            Type parentType = parent.GetType();
            (Type, string) cacheKey = (parentType, conditionField);

            if (!MemberCache.TryGetValue(cacheKey, out MemberInfo memberInfo))
            {
                memberInfo = ResolveMember(parentType, conditionField);
                MemberCache[cacheKey] = memberInfo;
            }

            if (memberInfo == null)
            {
                return null;
            }

            return GetMemberValue(memberInfo, parent);
        }

        private static MemberInfo ResolveMember(Type type, string memberName)
        {
            FieldInfo field = type.GetField(
                memberName,
                ShowIfConditionEvaluator.MemberBindingFlags
            );
            if (field != null)
            {
                return field;
            }

            PropertyInfo property = type.GetProperty(
                memberName,
                ShowIfConditionEvaluator.MemberBindingFlags
            );
            if (property != null && property.CanRead)
            {
                return property;
            }

            MethodInfo method = type.GetMethod(
                memberName,
                ShowIfConditionEvaluator.MemberBindingFlags,
                null,
                Type.EmptyTypes,
                null
            );
            if (method != null && method.ReturnType != typeof(void))
            {
                return method;
            }

            return null;
        }

        private static object GetMemberValue(MemberInfo memberInfo, object target)
        {
            if (memberInfo is FieldInfo fieldInfo)
            {
                return fieldInfo.GetValue(target);
            }

            if (memberInfo is PropertyInfo propertyInfo)
            {
                return propertyInfo.GetValue(target);
            }

            if (memberInfo is MethodInfo methodInfo)
            {
                return methodInfo.Invoke(target, null);
            }

            return null;
        }

        /// <summary>
        /// Gets the value of the condition field from the parent object.
        /// </summary>
        /// <remarks>
        /// This method is internal to allow testing without reflection.
        /// </remarks>
        /// <param name="parent">The parent object containing the condition field.</param>
        /// <param name="conditionField">The name of the condition field.</param>
        /// <returns>The value of the condition field, or null if not found.</returns>
        internal static object GetConditionValueForTest(object parent, string conditionField)
        {
            return GetConditionValue(parent, conditionField);
        }
    }
#endif
}
