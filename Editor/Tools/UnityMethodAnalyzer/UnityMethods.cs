// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Unity MonoBehaviour lifecycle methods and their expected signatures.
    /// </summary>
    public static class UnityMethods
    {
        public static readonly HashSet<string> LifecycleMethods = new(StringComparer.Ordinal)
        {
            "Awake",
            "OnEnable",
            "Start",
            "OnDisable",
            "OnDestroy",
            "Update",
            "LateUpdate",
            "FixedUpdate",
            "OnCollisionEnter",
            "OnCollisionStay",
            "OnCollisionExit",
            "OnCollisionEnter2D",
            "OnCollisionStay2D",
            "OnCollisionExit2D",
            "OnTriggerEnter",
            "OnTriggerStay",
            "OnTriggerExit",
            "OnTriggerEnter2D",
            "OnTriggerStay2D",
            "OnTriggerExit2D",
            "OnControllerColliderHit",
            "OnJointBreak",
            "OnJointBreak2D",
            "OnParticleCollision",
            "OnParticleTrigger",
            "OnParticleSystemStopped",
            "OnParticleUpdateJobScheduled",
            "OnPreCull",
            "OnPreRender",
            "OnPostRender",
            "OnRenderObject",
            "OnWillRenderObject",
            "OnBecameVisible",
            "OnBecameInvisible",
            "OnRenderImage",
            "OnGUI",
            "OnDrawGizmos",
            "OnDrawGizmosSelected",
            "OnApplicationFocus",
            "OnApplicationPause",
            "OnApplicationQuit",
            "OnMouseDown",
            "OnMouseUp",
            "OnMouseUpAsButton",
            "OnMouseEnter",
            "OnMouseExit",
            "OnMouseDrag",
            "OnMouseOver",
            "OnAnimatorIK",
            "OnAnimatorMove",
            "OnAudioFilterRead",
            "OnServerInitialized",
            "OnConnectedToServer",
            "OnDisconnectedFromServer",
            "OnFailedToConnect",
            "OnFailedToConnectToMasterServer",
            "OnMasterServerEvent",
            "OnNetworkInstantiate",
            "OnPlayerConnected",
            "OnPlayerDisconnected",
            "OnSerializeNetworkView",
            "OnLevelWasLoaded",
            "OnValidate",
            "Reset",
            "OnTransformChildrenChanged",
            "OnTransformParentChanged",
        };

        public static readonly HashSet<string> MonoBehaviourBaseClasses = new(
            StringComparer.Ordinal
        )
        {
            "MonoBehaviour",
            "ScriptableObject",
            "StateMachineBehaviour",
            "NetworkBehaviour",
            "Editor",
            "EditorWindow",
            "PropertyDrawer",
            "DecoratorDrawer",
            "AssetPostprocessor",
            "ScriptedImporter",
        };

        /// <summary>
        /// Lifecycle methods that accept parameters.
        /// </summary>
        public static readonly HashSet<string> MethodsWithParameters = new(StringComparer.Ordinal)
        {
            "OnCollisionEnter",
            "OnCollisionStay",
            "OnCollisionExit",
            "OnCollisionEnter2D",
            "OnCollisionStay2D",
            "OnCollisionExit2D",
            "OnTriggerEnter",
            "OnTriggerStay",
            "OnTriggerExit",
            "OnTriggerEnter2D",
            "OnTriggerStay2D",
            "OnTriggerExit2D",
            "OnControllerColliderHit",
            "OnJointBreak",
            "OnJointBreak2D",
            "OnParticleCollision",
            "OnAnimatorIK",
            "OnAnimatorMove",
            "OnAudioFilterRead",
            "OnRenderImage",
            "OnApplicationFocus",
            "OnApplicationPause",
            "OnSerializeNetworkView",
            "OnLevelWasLoaded",
        };

        /// <summary>
        /// Returns true if the given return type is valid for the specified Unity lifecycle method.
        /// </summary>
        public static bool IsValidUnityLifecycleReturnType(string methodName, string returnType)
        {
            if (string.IsNullOrEmpty(returnType))
            {
                return false;
            }

            string normalized = returnType.Trim();

            if (string.Equals(normalized, "void", StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(methodName, "Start", StringComparison.Ordinal))
            {
                return string.Equals(normalized, "IEnumerator", StringComparison.Ordinal)
                    || string.Equals(
                        normalized,
                        "System.Collections.IEnumerator",
                        StringComparison.Ordinal
                    );
            }

            return false;
        }
    }
#endif
}
