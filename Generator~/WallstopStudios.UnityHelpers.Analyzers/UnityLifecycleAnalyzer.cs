// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    /// <summary>
    /// Checks Unity lifecycle callback signatures against their resolved types.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnityLifecycleAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>
        /// Gets the lifecycle signature diagnostic.
        /// </summary>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.InvalidUnityLifecycleSignature);

        /// <summary>
        /// Registers lifecycle callback analysis.
        /// </summary>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(startContext =>
            {
                INamedTypeSymbol monoBehaviour = startContext.Compilation.GetTypeByMetadataName(
                    "UnityEngine.MonoBehaviour"
                );
                INamedTypeSymbol scriptableObject = startContext.Compilation.GetTypeByMetadataName(
                    "UnityEngine.ScriptableObject"
                );
                INamedTypeSymbol enumerator = startContext.Compilation.GetTypeByMetadataName(
                    "System.Collections.IEnumerator"
                );
                INamedTypeSymbol suppressionAttribute =
                    startContext.Compilation.GetTypeByMetadataName(
                        "WallstopStudios.UnityHelpers.Tests.Core.SuppressAnalyzerAttribute"
                    );
                if (monoBehaviour == null && scriptableObject == null)
                {
                    return;
                }

                startContext.RegisterSymbolAction(
                    symbolContext =>
                    {
                        IMethodSymbol method = (IMethodSymbol)symbolContext.Symbol;
                        if (
                            method.MethodKind != MethodKind.Ordinary
                            || method.IsImplicitlyDeclared
                            || method.IsOverride
                            || IsSuppressed(method, suppressionAttribute)
                            || IsSuppressed(method.ContainingType, suppressionAttribute)
                        )
                        {
                            return;
                        }

                        bool isMonoBehaviour = DerivesFrom(method.ContainingType, monoBehaviour);
                        if (
                            !isMonoBehaviour
                            && !DerivesFrom(method.ContainingType, scriptableObject)
                        )
                        {
                            return;
                        }

                        bool isEditorWindow = DerivesFrom(
                            method.ContainingType,
                            startContext.Compilation.GetTypeByMetadataName(
                                "UnityEditor.EditorWindow"
                            )
                        );
                        bool isEditor = DerivesFrom(
                            method.ContainingType,
                            startContext.Compilation.GetTypeByMetadataName("UnityEditor.Editor")
                        );
                        if (!IsCallback(method.Name, isMonoBehaviour, isEditorWindow, isEditor))
                        {
                            return;
                        }

                        CallbackSignature signature = GetSignature(method.Name);
                        if (!isMonoBehaviour)
                        {
                            signature = new CallbackSignature(false, false, signature.Parameters);
                        }
                        if (IsValid(method, signature, startContext.Compilation, enumerator))
                        {
                            return;
                        }

                        symbolContext.ReportDiagnostic(
                            Diagnostic.Create(
                                UnityHelpersDiagnostics.InvalidUnityLifecycleSignature,
                                method.Locations[0],
                                method.ToDisplayString(
                                    SymbolDisplayFormat.CSharpErrorMessageFormat
                                ),
                                signature.Description
                            )
                        );
                    },
                    SymbolKind.Method
                );
            });
        }

        internal static bool DerivesFrom(INamedTypeSymbol candidate, INamedTypeSymbol expected)
        {
            if (expected == null)
            {
                return false;
            }

            for (
                INamedTypeSymbol ancestor = candidate.BaseType;
                ancestor != null;
                ancestor = ancestor.BaseType
            )
            {
                if (SymbolEqualityComparer.Default.Equals(ancestor, expected))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsSuppressed(ISymbol symbol, INamedTypeSymbol suppressionAttribute)
        {
            if (suppressionAttribute == null)
            {
                return false;
            }

            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (
                    SymbolEqualityComparer.Default.Equals(
                        attribute.AttributeClass,
                        suppressionAttribute
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsCallback(
            string name,
            bool isMonoBehaviour,
            bool isEditorWindow = false,
            bool isEditor = false
        )
        {
            switch (name)
            {
                case "OnFocus":
                case "OnLostFocus":
                case "OnInspectorUpdate":
                case "OnHierarchyChange":
                case "OnProjectChange":
                case "OnSelectionChange":
                case "CreateGUI":
                    return isEditorWindow;
                case "OnSceneGUI":
                    return isEditor;
            }
            if (
                isEditorWindow
                && (
                    name == "OnGUI"
                    || name == "Update"
                    || name == "OnBecameVisible"
                    || name == "OnBecameInvisible"
                )
            )
            {
                return true;
            }
            if (!isMonoBehaviour)
            {
                return name == "Awake"
                    || name == "OnEnable"
                    || name == "OnDisable"
                    || name == "OnDestroy"
                    || name == "OnValidate"
                    || name == "Reset";
            }
            return GetSignature(name).Parameters != null;
        }

        internal static bool HasValidSignature(IMethodSymbol method, Compilation compilation)
        {
            CallbackSignature signature = GetSignature(method.Name);
            if (signature.Parameters == null)
            {
                return false;
            }
            bool isMonoBehaviour = DerivesFrom(
                method.ContainingType,
                compilation.GetTypeByMetadataName("UnityEngine.MonoBehaviour")
            );
            if (!isMonoBehaviour)
            {
                signature = new CallbackSignature(false, false, signature.Parameters);
            }
            return IsValid(
                method,
                signature,
                compilation,
                compilation.GetTypeByMetadataName("System.Collections.IEnumerator")
            );
        }

        private static bool IsValid(
            IMethodSymbol method,
            CallbackSignature signature,
            Compilation compilation,
            INamedTypeSymbol enumerator
        )
        {
            if (
                method.IsStatic
                || method.Arity != 0
                || method.ReturnsByRef
                || method.ReturnsByRefReadonly
                || !(
                    method.ReturnsVoid
                    || (
                        signature.Coroutine
                        && SymbolEqualityComparer.Default.Equals(method.ReturnType, enumerator)
                    )
                )
            )
            {
                return false;
            }
            if (signature.OptionalParameter && method.Parameters.Length == 0)
            {
                return true;
            }
            if (method.Parameters.Length != signature.Parameters.Length)
            {
                return false;
            }
            for (int index = 0; index < method.Parameters.Length; index++)
            {
                IParameterSymbol parameter = method.Parameters[index];
                string expectedName = signature.Parameters[index];
                ITypeSymbol expected =
                    expectedName == "System.Single[]"
                        ? compilation.CreateArrayTypeSymbol(
                            compilation.GetSpecialType(SpecialType.System_Single)
                        )
                        : compilation.GetTypeByMetadataName(expectedName);
                if (
                    parameter.RefKind != RefKind.None
                    || !SymbolEqualityComparer.Default.Equals(parameter.Type, expected)
                )
                {
                    return false;
                }
            }
            return true;
        }

        private static CallbackSignature GetSignature(string name)
        {
            switch (name)
            {
                case "OnFocus":
                case "OnLostFocus":
                case "OnInspectorUpdate":
                case "OnHierarchyChange":
                case "OnProjectChange":
                case "OnSelectionChange":
                case "CreateGUI":
                case "OnSceneGUI":
                case "Awake":
                case "OnEnable":
                case "OnDisable":
                case "OnDestroy":
                case "OnValidate":
                case "Reset":
                case "Update":
                case "FixedUpdate":
                case "LateUpdate":
                case "OnApplicationQuit":
                case "OnParticleTrigger":
                case "OnParticleSystemStopped":
                case "OnParticleUpdateJobScheduled":
                case "OnPreCull":
                case "OnRenderObject":
                case "OnWillRenderObject":
                case "OnGUI":
                case "OnDrawGizmos":
                case "OnDrawGizmosSelected":
                case "OnAnimatorMove":
                case "OnTransformChildrenChanged":
                case "OnTransformParentChanged":
                    return new CallbackSignature(false, false);
                case "Start":
                case "OnPreRender":
                case "OnPostRender":
                case "OnBecameVisible":
                case "OnBecameInvisible":
                case "OnMouseDown":
                case "OnMouseUp":
                case "OnMouseUpAsButton":
                case "OnMouseEnter":
                case "OnMouseExit":
                case "OnMouseDrag":
                case "OnMouseOver":
                case "OnServerInitialized":
                case "OnConnectedToServer":
                    return new CallbackSignature(true, false);
                case "OnCollisionEnter":
                case "OnCollisionStay":
                case "OnCollisionExit":
                    return new CallbackSignature(true, true, "UnityEngine.Collision");
                case "OnCollisionEnter2D":
                case "OnCollisionStay2D":
                case "OnCollisionExit2D":
                    return new CallbackSignature(true, true, "UnityEngine.Collision2D");
                case "OnTriggerEnter":
                case "OnTriggerStay":
                case "OnTriggerExit":
                    return new CallbackSignature(true, true, "UnityEngine.Collider");
                case "OnTriggerEnter2D":
                case "OnTriggerStay2D":
                case "OnTriggerExit2D":
                    return new CallbackSignature(true, true, "UnityEngine.Collider2D");
                case "OnControllerColliderHit":
                    return new CallbackSignature(false, false, "UnityEngine.ControllerColliderHit");
                case "OnJointBreak":
                    return new CallbackSignature(false, false, "System.Single");
                case "OnJointBreak2D":
                    return new CallbackSignature(false, false, "UnityEngine.Joint2D");
                case "OnParticleCollision":
                    return new CallbackSignature(true, false, "UnityEngine.GameObject");
                case "OnApplicationFocus":
                case "OnApplicationPause":
                    return new CallbackSignature(true, false, "System.Boolean");
                case "OnAnimatorIK":
                case "OnLevelWasLoaded":
                    return new CallbackSignature(false, false, "System.Int32");
                case "OnDisconnectedFromServer":
                    return new CallbackSignature(true, false, "UnityEngine.NetworkDisconnection");
                case "OnFailedToConnect":
                case "OnFailedToConnectToMasterServer":
                    return new CallbackSignature(true, false, "UnityEngine.NetworkConnectionError");
                case "OnMasterServerEvent":
                    return new CallbackSignature(true, false, "UnityEngine.MasterServerEvent");
                case "OnNetworkInstantiate":
                    return new CallbackSignature(false, false, "UnityEngine.NetworkMessageInfo");
                case "OnPlayerConnected":
                case "OnPlayerDisconnected":
                    return new CallbackSignature(true, false, "UnityEngine.NetworkPlayer");
                case "OnAudioFilterRead":
                    return new CallbackSignature(false, false, "System.Single[]", "System.Int32");
                case "OnRenderImage":
                    return new CallbackSignature(
                        false,
                        false,
                        "UnityEngine.RenderTexture",
                        "UnityEngine.RenderTexture"
                    );
                case "OnSerializeNetworkView":
                    return new CallbackSignature(
                        false,
                        false,
                        "UnityEngine.BitStream",
                        "UnityEngine.NetworkMessageInfo"
                    );
                default:
                    return default;
            }
        }

        private readonly struct CallbackSignature
        {
            internal readonly bool Coroutine;
            internal readonly bool OptionalParameter;
            internal readonly string[] Parameters;

            internal CallbackSignature(
                bool coroutine,
                bool optionalParameter,
                params string[] parameters
            )
            {
                Coroutine = coroutine;
                OptionalParameter = optionalParameter;
                Parameters = parameters;
            }

            internal string Description =>
                (Coroutine ? "void or System.Collections.IEnumerator" : "void")
                + " ("
                + string.Join(", ", Parameters)
                + ")"
                + (OptionalParameter ? "; the event argument may also be omitted" : string.Empty);
        }
    }
}
