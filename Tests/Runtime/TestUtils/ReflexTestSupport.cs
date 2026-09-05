// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

/*
    This reflection-only helper lives in an assembly without REFLEX_PRESENT; guarding it would break integration
    references.
*/
namespace WallstopStudios.UnityHelpers.Tests.TestUtils
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using UnityEngine;

    /// <summary>
    ///     Provides test support utilities for the Reflex dependency injection library.
    ///     This class uses reflection to access Reflex internals because Reflex does not
    ///     expose public APIs for test setup/reset of its singleton ReflexSettings instance.
    /// </summary>
    public static class ReflexTestSupport
    {
        /*
            Reflex exposes no public settings-reset API, requiring access to its private singleton and read-only
            properties.
        */
        private const string ReflexSettingsTypeName = "Reflex.Configuration.ReflexSettings";
        private const string InstanceFieldName = "_instance";
        private const string LogLevelBackingFieldName = "<LogLevel>k__BackingField";
        private const string ProjectScopesBackingFieldName = "<ProjectScopes>k__BackingField";
        private const string LogLevelTypeName = "Reflex.Logging.LogLevel";
        private const string ProjectScopeTypeName = "Reflex.Core.ProjectScope";

        /*
            Install settings before any scene unload; the CI project has no asset, and the first ReflexLogger
            initializer would otherwise poison the domain.
        */
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void BootstrapForPlayer()
        {
            EnsureReflexSettings();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void BootstrapForEditor()
        {
            EnsureReflexSettings();
        }
#endif

        /// <summary>
        ///     Ensures a usable ReflexSettings.Instance exists for testing, creating a
        ///     reflection-built stand-in if Reflex is present and no live instance is set.
        ///     Idempotent and self-healing: a no-op when Reflex is absent or an instance is
        ///     already alive, and it rebuilds the stand-in if a prior one was destroyed.
        /// </summary>
        public static void EnsureReflexSettings()
        {
            FieldInfo instanceField = ResolveInstanceField(out Type settingsType);
            if (instanceField == null)
            {
                return;
            }

            // Destroyed settings retain a managed reference; Unity-aware null detection must rebuild the stand-in.
            if (instanceField.GetValue(null) is UnityEngine.Object existing && existing != null)
            {
                return;
            }

            ScriptableObject settings = ScriptableObject.CreateInstance(settingsType);
            // HideAndDontSave preserves settings through the scene unload that triggers Reflex logging.
            settings.hideFlags = HideFlags.HideAndDontSave;
            SetInstanceField(settingsType, settings, LogLevelBackingFieldName, GetLogLevelInfo());
            SetInstanceField(
                settingsType,
                settings,
                ProjectScopesBackingFieldName,
                CreateEmptyProjectScopesList()
            );
            instanceField.SetValue(null, settings);
        }

        private static bool _instanceFieldResolved;
        private static Type _cachedSettingsType;
        private static FieldInfo _cachedInstanceField;

        private static FieldInfo ResolveInstanceField(out Type settingsType)
        {
            if (!_instanceFieldResolved)
            {
                _instanceFieldResolved = true;
                _cachedSettingsType = FindType(ReflexSettingsTypeName);
                _cachedInstanceField = _cachedSettingsType?.GetField(
                    InstanceFieldName,
                    BindingFlags.NonPublic | BindingFlags.Static
                );
            }

            settingsType = _cachedSettingsType;
            return _cachedInstanceField;
        }

        private static void SetInstanceField(
            Type declaringType,
            object instance,
            string fieldName,
            object value
        )
        {
            FieldInfo field = declaringType.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            field?.SetValue(instance, value);
        }

        private static object GetLogLevelInfo()
        {
            Type logLevelType = FindType(LogLevelTypeName);
            if (logLevelType == null)
            {
                return null;
            }

            return Enum.Parse(logLevelType, "Info", ignoreCase: true);
        }

        private static object CreateEmptyProjectScopesList()
        {
            Type projectScopeType = FindType(ProjectScopeTypeName);
            if (projectScopeType == null)
            {
                return null;
            }

            Type listType = typeof(List<>).MakeGenericType(projectScopeType);
            return Activator.CreateInstance(listType);
        }

        private static Type FindType(string fullName)
        {
            return AppDomain
                .CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName))
                .FirstOrDefault(type => type != null);
        }
    }
}
