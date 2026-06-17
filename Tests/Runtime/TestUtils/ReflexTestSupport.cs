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

        // Reflex's UnityInjector registers a SceneManager.sceneUnloaded handler at
        // AfterAssembliesLoaded; the FIRST scene unload runs Reflex.Logging.ReflexLogger's
        // static ctor, which reads ReflexSettings.Instance and Assert.IsNotNull's it. The
        // ephemeral CI test project installs Reflex (to exercise the integration) but ships
        // no ReflexSettings asset, so absent a pre-installed instance that static ctor throws
        // a TypeInitializationException that POISONS ReflexLogger for the whole domain --
        // failing every test that unloads a scene and corrupting the run's results.xml. We
        // install a reflection-built stand-in as early as Unity allows, BEFORE any scene can
        // unload, in both the player (RuntimeInitializeOnLoadMethod) and the editor
        // (InitializeOnLoadMethod). This type is reflection-only and no-ops when Reflex is
        // absent, so the unconditional hooks are inert outside the integration legs.
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
                // Reflex is not installed in this project -- nothing to stand in for.
                return;
            }

            // Unity-aware null check: a destroyed ScriptableObject is a non-null managed
            // reference but compares == null via UnityEngine.Object's overload, so a plain
            // reference check would wrongly skip rebuilding a stand-in Unity already tore down.
            if (instanceField.GetValue(null) is UnityEngine.Object existing && existing != null)
            {
                return;
            }

            ScriptableObject settings = ScriptableObject.CreateInstance(settingsType);
            // HideAndDontSave keeps the stand-in alive across scene unloads (the exact event
            // that triggers ReflexLogger); without it Unity may destroy the instance and the
            // next scene unload re-throws the very assertion this exists to prevent.
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

        // Caches the resolved Reflex type + private static _instance field (or a sentinel
        // miss) so the repeated bootstrap/setup calls do not re-walk every loaded assembly.
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
