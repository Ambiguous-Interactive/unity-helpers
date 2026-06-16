// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// NOTE: intentionally NOT guarded by #if REFLEX_PRESENT. This helper is reflection-only
// (it resolves every Reflex type by string name and no-ops when Reflex is absent) and lives
// in the WallstopStudios.UnityHelpers.Tests.Runtime assembly, which never defines
// REFLEX_PRESENT. The VContainer/Zenject integration test assemblies reference it, so guarding
// it on a define its home assembly cannot set would exclude the type and break their compile.
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
        // Reflection required: Reflex provides no public API to create or reset ReflexSettings for testing.
        // The ReflexSettings class uses a private static _instance field for its singleton pattern,
        // and the LogLevel/ProjectScopes properties have no public setters.
        private const string ReflexSettingsTypeName = "Reflex.Configuration.ReflexSettings";
        private const string InstanceFieldName = "_instance";
        private const string LogLevelBackingFieldName = "<LogLevel>k__BackingField";
        private const string ProjectScopesBackingFieldName = "<ProjectScopes>k__BackingField";
        private const string LogLevelTypeName = "Reflex.Logging.LogLevel";
        private const string ProjectScopeTypeName = "Reflex.Core.ProjectScope";

        private static bool _initialized;

        /// <summary>
        ///     Ensures that ReflexSettings is initialized for testing.
        ///     Creates a mock ReflexSettings instance via reflection if one doesn't exist.
        /// </summary>
        public static void EnsureReflexSettings()
        {
            if (_initialized)
            {
                return;
            }

            Type settingsType = FindType(ReflexSettingsTypeName);
            if (settingsType == null)
            {
                return;
            }

            FieldInfo instanceField = settingsType.GetField(
                InstanceFieldName,
                BindingFlags.NonPublic | BindingFlags.Static
            );
            if (instanceField == null)
            {
                return;
            }

            object currentInstance = instanceField.GetValue(null);
            if (currentInstance == null)
            {
                ScriptableObject settings = ScriptableObject.CreateInstance(settingsType);
                SetInstanceField(
                    settingsType,
                    settings,
                    LogLevelBackingFieldName,
                    GetLogLevelInfo()
                );
                SetInstanceField(
                    settingsType,
                    settings,
                    ProjectScopesBackingFieldName,
                    CreateEmptyProjectScopesList()
                );
                instanceField.SetValue(null, settings);
            }

            _initialized = true;
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
