// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Text;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    public static class RuntimeStateTestUtilities
    {
        public static UnityMainThreadDispatcher.AutoCreationScope CreateDispatcherScope(
            bool destroyImmediate = true
        )
        {
            UnityMainThreadDispatcher.AutoCreationScope scope =
                UnityMainThreadDispatcher.AutoCreationScope.Disabled(
                    destroyExistingInstanceOnEnter: true,
                    destroyInstancesOnDispose: true,
                    destroyImmediate: destroyImmediate
                );

            UnityMainThreadDispatcher.SetAutoCreationEnabled(true);
            return scope;
        }

        public static void SimulateApplicationQuitting()
        {
            RuntimeSingletonRegistry.OnApplicationQuitting();
#if UNITY_EDITOR
            RuntimeSingletonRegistry._isEditorQuitting = true;
#endif
        }

        public static void PrepareForSceneLoad()
        {
            RuntimeSingletonRegistry.OnSubsystemRegistration();
            RuntimeSingletonRegistry.ResetAllRegisteredCaches();
        }

        public static void ReturnToEditMode()
        {
            RuntimeSingletonRegistry._isApplicationQuitting = false;
#if UNITY_EDITOR
            RuntimeSingletonRegistry._isEditorQuitting = false;
#endif
        }

        public static string DescribeLiveSingletons()
        {
            StringBuilder builder = null;
            foreach (
                RuntimeSingletonRegistry.RuntimeSingletonRegistration registration in RuntimeSingletonRegistry.GetRegistrationsSnapshot()
            )
            {
                UnityEngine.Object cachedInstance = null;
                try
                {
                    cachedInstance = registration.getCachedInstance();
                }
                catch (Exception ex)
                {
                    builder ??= new StringBuilder();
                    builder.Append(registration.type.FullName);
                    builder.Append(" cache inspection failed: ");
                    builder.Append(ex);
                    builder.AppendLine();
                }

                UnityEngine.Object[] liveInstances = Array.Empty<UnityEngine.Object>();
                try
                {
                    liveInstances =
                        registration.findLiveInstances() ?? Array.Empty<UnityEngine.Object>();
                }
                catch (Exception ex)
                {
                    builder ??= new StringBuilder();
                    builder.Append(registration.type.FullName);
                    builder.Append(" live-instance inspection failed: ");
                    builder.Append(ex);
                    builder.AppendLine();
                }

                foreach (UnityEngine.Object liveInstance in liveInstances)
                {
                    if (liveInstance == null)
                    {
                        continue;
                    }

                    builder ??= new StringBuilder();
                    builder.Append(registration.type.FullName);
                    builder.Append(" '");
                    builder.Append(liveInstance.name);
                    builder.Append("'#");
                    builder.Append(liveInstance.GetUnityObjectId());
                    if (liveInstance is Component component && component.gameObject != null)
                    {
                        builder.Append(" scene='");
                        builder.Append(component.gameObject.scene.name);
                        builder.Append("'");
                    }
                    builder.Append(" cached=");
                    builder.Append(cachedInstance == liveInstance);
                    builder.AppendLine();
                }
            }

            return builder?.ToString().Trim();
        }

        public static int GetPendingActionCount()
        {
            int pendingActionCount = 0;
            UnityMainThreadDispatcher[] dispatchers =
                Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>();
            foreach (UnityMainThreadDispatcher dispatcher in dispatchers)
            {
                if (dispatcher == null)
                {
                    continue;
                }

                pendingActionCount += dispatcher.PendingActionCount;
            }

            return pendingActionCount;
        }

        public static int DrainPendingActions(int maxActions = 1024)
        {
            int remainingActionBudget = Math.Max(1, maxActions);
            UnityMainThreadDispatcher[] dispatchers =
                Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>();
            foreach (UnityMainThreadDispatcher dispatcher in dispatchers)
            {
                if (dispatcher == null || dispatcher.PendingActionCount <= 0)
                {
                    continue;
                }

                while (
                    0 < remainingActionBudget
                    && dispatcher.TryDequeueQueuedAction(
                        out UnityMainThreadDispatcher.QueuedAction queued
                    )
                )
                {
                    --remainingActionBudget;
                    dispatcher.ExecuteQueuedAction(queued);
                }
            }

            return GetPendingActionCount();
        }

        public static string DescribeLiveDispatchers()
        {
            UnityMainThreadDispatcher[] dispatchers =
                Resources.FindObjectsOfTypeAll<UnityMainThreadDispatcher>();
            if (dispatchers.Length == 0)
            {
                return "No dispatchers found.";
            }

            int dispatchersLength = dispatchers.Length;
            string[] descriptions = new string[dispatchersLength];
            for (int i = 0; i < dispatchersLength; ++i)
            {
                UnityMainThreadDispatcher dispatcher = dispatchers[i];
                if (dispatcher == null)
                {
                    descriptions[i] = "null";
                    continue;
                }

                GameObject dispatcherObject = dispatcher.gameObject;
                string sceneName = dispatcherObject == null ? "null" : dispatcherObject.scene.name;
                descriptions[i] =
                    $"{dispatcher.name}#{dispatcher.GetUnityObjectId()} scene='{sceneName}' "
                    + $"active={dispatcherObject != null && dispatcherObject.activeInHierarchy} "
                    + $"pending={dispatcher.PendingActionCount}";
            }

            return string.Join(", ", descriptions);
        }
    }
}
