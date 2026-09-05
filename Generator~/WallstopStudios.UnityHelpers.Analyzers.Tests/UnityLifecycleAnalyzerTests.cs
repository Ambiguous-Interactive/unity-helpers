// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NUnit.Framework;

    /// <summary>
    /// Exercises lifecycle signatures with semantic rather than textual type matching.
    /// </summary>
    [TestFixture]
    public sealed class UnityLifecycleAnalyzerTests
    {
        private const string UnityTypes =
            @"
namespace UnityEngine
{
    public class Object { }
    public class MonoBehaviour : Object { }
    public class ScriptableObject : Object { }
    public class Collision { }
    public class Collision2D { }
    public class Collider : Object { }
    public class Collider2D : Object { }
    public class ControllerColliderHit { }
    public class Joint2D : Object { }
    public class GameObject : Object { }
    public class RenderTexture : Object { }
    public class BitStream { }
    public struct NetworkMessageInfo { }
    public struct NetworkPlayer { }
    public enum NetworkDisconnection { }
    public enum NetworkConnectionError { }
    public enum MasterServerEvent { }
}
namespace UnityEditor { public class EditorWindow : UnityEngine.ScriptableObject {} public class Editor : UnityEngine.ScriptableObject {} }
namespace WallstopStudios.UnityHelpers.Tests.Core
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method)]
    public sealed class SuppressAnalyzerAttribute : System.Attribute { }
}";

        [TestCase("static void Awake() {}")]
        [TestCase("int Awake() => 1;")]
        [TestCase("void Update(int count) {}")]
        [TestCase("void FixedUpdate(int count) {}")]
        [TestCase("void LateUpdate(int count) {}")]
        [TestCase("void OnApplicationQuit(int count) {}")]
        [TestCase("void OnEnable<T>() {}")]
        [TestCase("System.Collections.IEnumerator OnDisable() { yield break; }")]
        [TestCase("System.Collections.Generic.IEnumerator<int> Start() { yield break; }")]
        [TestCase("System.Threading.Tasks.Task Start() => null;")]
        public void InvalidMonoBehaviourSignatureIsReported(string method)
        {
            Diagnostic[] diagnostics = Analyze(
                "class Subject : UnityEngine.MonoBehaviour { " + method + " }"
            );
            Assert.That(diagnostics, Has.Length.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo("WUH015"));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        }

        [TestCase("void Awake() {}")]
        [TestCase("void Start() {}")]
        [TestCase("System.Collections.IEnumerator Start() { yield break; }")]
        [TestCase("void OnTriggerEnter(UnityEngine.Collider collider) {}")]
        public void ValidOrOutOfScopeSignatureIsNotReported(string method)
        {
            Assert.That(
                Analyze("class Subject : UnityEngine.MonoBehaviour { " + method + " }"),
                Is.Empty
            );
        }

        [TestCase("Awake", 1)]
        [TestCase("OnEnable", 1)]
        [TestCase("OnDisable", 1)]
        [TestCase("OnDestroy", 1)]
        [TestCase("OnValidate", 1)]
        [TestCase("Reset", 1)]
        [TestCase("Update", 0)]
        [TestCase("Start", 0)]
        public void ScriptableObjectsOnlyUseTheirOwnCallbacks(string name, int expected)
        {
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.ScriptableObject { int " + name + "() => 1; }"
                ),
                Has.Length.EqualTo(expected)
            );
        }

        [Test]
        public void AliasedBaseAndPartialDeclarationsResolveSemantically()
        {
            Assert.That(
                Analyze(
                    @"using Base = UnityEngine.MonoBehaviour;
partial class Subject : Base { }
partial class Subject { int Awake() => 1; }"
                ),
                Has.Length.EqualTo(1)
            );
        }

        [Test]
        public void AliasedCoroutineReturnTypeIsAccepted()
        {
            Assert.That(
                Analyze(
                    @"using Coroutine = System.Collections.IEnumerator;
class Subject : UnityEngine.MonoBehaviour { Coroutine Start() { yield break; } }"
                ),
                Is.Empty
            );
        }

        [Test]
        public void GenericAncestorIsResolved()
        {
            Assert.That(
                Analyze(
                    @"class Base<T> : UnityEngine.MonoBehaviour { }
class Subject : Base<int> { int Awake() => 1; }"
                ),
                Has.Length.EqualTo(1)
            );
        }

        [TestCase("class MonoBehaviour {} class Subject : MonoBehaviour { int Awake() => 1; }")]
        [TestCase("class Subject { static int Awake() => 1; }")]
        [TestCase(
            "class Subject : UnityEngine.MonoBehaviour { void Run() { int Awake() => 1; Awake(); } }"
        )]
        [TestCase(
            "class Subject : UnityEngine.MonoBehaviour { class Nested { int Awake() => 1; } }"
        )]
        [TestCase(
            "class Subject : UnityEngine.MonoBehaviour { string text = @\"static int Awake() => 1;\"; }"
        )]
        [TestCase(
            "class Subject : UnityEngine.MonoBehaviour {\n#if NEVER_ENABLED\nint Awake() => 1;\n#endif\n}"
        )]
        [TestCase(
            "interface ICallback { int Awake(); } class Subject : UnityEngine.MonoBehaviour, ICallback { int ICallback.Awake() => 1; }"
        )]
        public void SimilarTextOutsideUnityCallbacksIsNotReported(string source)
        {
            Assert.That(Analyze(source), Is.Empty);
        }

        [TestCase(
            "[WallstopStudios.UnityHelpers.Tests.Core.SuppressAnalyzer] class Subject : UnityEngine.MonoBehaviour { int Awake() => 1; }"
        )]
        [TestCase(
            "class Subject : UnityEngine.MonoBehaviour { [WallstopStudios.UnityHelpers.Tests.Core.SuppressAnalyzer] int Awake() => 1; }"
        )]
        [TestCase(
            "#pragma warning disable WUH015\nclass Subject : UnityEngine.MonoBehaviour { int Awake() => 1; }"
        )]
        public void ExistingAndCompilerSuppressionsAreRespected(string source)
        {
            Assert.That(Analyze(source), Is.Empty);
        }

        [Test]
        public void UnrelatedSuppressionAttributeDoesNotHideCallbackErrors()
        {
            Assert.That(
                Analyze(
                    @"class SuppressAnalyzerAttribute : System.Attribute { }
[SuppressAnalyzer] class Subject : UnityEngine.MonoBehaviour { int Awake() => 1; }"
                ),
                Has.Length.EqualTo(1)
            );
        }

        [TestCase("OnCollisionEnter", "UnityEngine.Collision")]
        [TestCase("OnCollisionStay", "UnityEngine.Collision")]
        [TestCase("OnCollisionExit", "UnityEngine.Collision")]
        [TestCase("OnCollisionEnter2D", "UnityEngine.Collision2D")]
        [TestCase("OnCollisionStay2D", "UnityEngine.Collision2D")]
        [TestCase("OnCollisionExit2D", "UnityEngine.Collision2D")]
        [TestCase("OnTriggerEnter", "UnityEngine.Collider")]
        [TestCase("OnTriggerStay", "UnityEngine.Collider")]
        [TestCase("OnTriggerExit", "UnityEngine.Collider")]
        [TestCase("OnTriggerEnter2D", "UnityEngine.Collider2D")]
        [TestCase("OnTriggerStay2D", "UnityEngine.Collider2D")]
        [TestCase("OnTriggerExit2D", "UnityEngine.Collider2D")]
        [TestCase("OnControllerColliderHit", "UnityEngine.ControllerColliderHit")]
        [TestCase("OnJointBreak", "float")]
        [TestCase("OnJointBreak2D", "UnityEngine.Joint2D")]
        [TestCase("OnParticleCollision", "UnityEngine.GameObject")]
        [TestCase("OnApplicationFocus", "bool")]
        [TestCase("OnApplicationPause", "bool")]
        [TestCase("OnAnimatorIK", "int")]
        [TestCase("OnLevelWasLoaded", "int")]
        [TestCase("OnDisconnectedFromServer", "UnityEngine.NetworkDisconnection")]
        [TestCase("OnFailedToConnect", "UnityEngine.NetworkConnectionError")]
        [TestCase("OnFailedToConnectToMasterServer", "UnityEngine.NetworkConnectionError")]
        [TestCase("OnMasterServerEvent", "UnityEngine.MasterServerEvent")]
        [TestCase("OnNetworkInstantiate", "UnityEngine.NetworkMessageInfo")]
        [TestCase("OnPlayerConnected", "UnityEngine.NetworkPlayer")]
        [TestCase("OnPlayerDisconnected", "UnityEngine.NetworkPlayer")]
        public void ParameterContractsUseResolvedTypes(string name, string parameterType)
        {
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.MonoBehaviour { void "
                        + name
                        + "("
                        + parameterType
                        + " value) {} }"
                ),
                Is.Empty
            );
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.MonoBehaviour { void "
                        + name
                        + "(string value) {} }"
                ),
                Has.Length.EqualTo(1)
            );
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.MonoBehaviour { void "
                        + name
                        + "(ref "
                        + parameterType
                        + " value) {} }"
                ),
                Has.Length.EqualTo(1)
            );
        }

        [TestCase("OnParticleTrigger")]
        [TestCase("OnParticleSystemStopped")]
        [TestCase("OnParticleUpdateJobScheduled")]
        [TestCase("OnPreCull")]
        [TestCase("OnPreRender")]
        [TestCase("OnPostRender")]
        [TestCase("OnRenderObject")]
        [TestCase("OnWillRenderObject")]
        [TestCase("OnBecameVisible")]
        [TestCase("OnBecameInvisible")]
        [TestCase("OnGUI")]
        [TestCase("OnDrawGizmos")]
        [TestCase("OnDrawGizmosSelected")]
        [TestCase("OnMouseDown")]
        [TestCase("OnMouseUp")]
        [TestCase("OnMouseUpAsButton")]
        [TestCase("OnMouseEnter")]
        [TestCase("OnMouseExit")]
        [TestCase("OnMouseDrag")]
        [TestCase("OnMouseOver")]
        [TestCase("OnAnimatorMove")]
        [TestCase("OnServerInitialized")]
        [TestCase("OnConnectedToServer")]
        [TestCase("OnTransformChildrenChanged")]
        [TestCase("OnTransformParentChanged")]
        public void ParameterlessMessageContractsAreChecked(string name)
        {
            Assert.That(
                Analyze("class Subject : UnityEngine.MonoBehaviour { void " + name + "() {} }"),
                Is.Empty
            );
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.MonoBehaviour { static void " + name + "() {} }"
                ),
                Has.Length.EqualTo(1)
            );
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.MonoBehaviour { void " + name + "(int value) {} }"
                ),
                Has.Length.EqualTo(1)
            );
        }

        [TestCase(
            "OnRenderImage",
            "UnityEngine.RenderTexture source, UnityEngine.RenderTexture destination"
        )]
        [TestCase("OnAudioFilterRead", "float[] data, int channels")]
        [TestCase(
            "OnSerializeNetworkView",
            "UnityEngine.BitStream stream, UnityEngine.NetworkMessageInfo info"
        )]
        public void MultipleParametersAreChecked(string name, string parameters)
        {
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.MonoBehaviour { void "
                        + name
                        + "("
                        + parameters
                        + ") {} }"
                ),
                Is.Empty
            );
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.MonoBehaviour { void " + name + "(int value) {} }"
                ),
                Has.Length.EqualTo(1)
            );
        }

        [TestCase("OnCollisionEnter")]
        [TestCase("OnCollisionStay")]
        [TestCase("OnCollisionExit")]
        [TestCase("OnBecameVisible")]
        [TestCase("OnBecameInvisible")]
        [TestCase("OnMouseDown")]
        [TestCase("OnMouseOver")]
        [TestCase("OnPreRender")]
        [TestCase("OnPostRender")]
        public void AcceptedOptionalArgumentsAndCoroutineFormsAreNotReported(string name)
        {
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.MonoBehaviour { System.Collections.IEnumerator "
                        + name
                        + "() { yield break; } }"
                ),
                Is.Empty
            );
        }

        /// <summary>Preserves conservative diagnostics without asserting engine coroutine dispatch.</summary>
        [TestCase("OnCollisionEnter2D", "UnityEngine.Collision2D")]
        [TestCase("OnCollisionStay2D", "UnityEngine.Collision2D")]
        [TestCase("OnCollisionExit2D", "UnityEngine.Collision2D")]
        [TestCase("OnTriggerEnter2D", "UnityEngine.Collider2D")]
        [TestCase("OnTriggerStay2D", "UnityEngine.Collider2D")]
        [TestCase("OnTriggerExit2D", "UnityEngine.Collider2D")]
        public void Uncertain2DCoroutineFormsRemainUnreported(string name, string parameterType)
        {
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.MonoBehaviour { System.Collections.IEnumerator "
                        + name
                        + "("
                        + parameterType
                        + " other) { yield break; } }"
                ),
                Is.Empty
            );
            Assert.That(
                Analyze(
                    "class Subject : UnityEngine.MonoBehaviour { System.Collections.IEnumerator "
                        + name
                        + "() { yield break; } }"
                ),
                Is.Empty
            );
        }

        [Test]
        public void PhysicsAliasesResolveAndLookalikesAreRejected()
        {
            Assert.That(
                Analyze(
                    "using Hit = UnityEngine.Collider; class Subject : UnityEngine.MonoBehaviour"
                        + " { void OnTriggerEnter(Hit other) {} }"
                ),
                Is.Empty
            );
            Assert.That(
                Analyze(
                    "class Collider {} class Subject : UnityEngine.MonoBehaviour"
                        + " { void OnTriggerEnter(Collider other) {} }"
                ),
                Has.Length.EqualTo(1)
            );
        }

        [TestCase("OnGUI")]
        [TestCase("Update")]
        [TestCase("OnFocus")]
        [TestCase("OnLostFocus")]
        [TestCase("OnInspectorUpdate")]
        [TestCase("OnHierarchyChange")]
        [TestCase("OnProjectChange")]
        [TestCase("OnSelectionChange")]
        [TestCase("CreateGUI")]
        public void EditorWindowMessagesAreCheckedOnTheirActualOwner(string name)
        {
            Assert.That(
                Analyze("class Subject : UnityEditor.EditorWindow { void " + name + "() {} }"),
                Is.Empty
            );
            Assert.That(
                Analyze("class Subject : UnityEditor.EditorWindow { int " + name + "() => 1; }"),
                Has.Length.EqualTo(1)
            );
        }

        [Test]
        public void EditorOnlyMessagesDoNotBecomeMonoBehaviourCallbacks()
        {
            Assert.That(
                Analyze("class Subject : UnityEngine.MonoBehaviour { int CreateGUI() => 1; }"),
                Is.Empty
            );
            Assert.That(
                Analyze("class Subject : UnityEditor.Editor { int OnSceneGUI() => 1; }"),
                Has.Length.EqualTo(1)
            );
        }

        private static Diagnostic[] Analyze(string source)
        {
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            CSharpCompilation compilation = CSharpCompilation.Create(
                "LifecycleFixture",
                new[]
                {
                    CSharpSyntaxTree.ParseText(UnityTypes),
                    CSharpSyntaxTree.ParseText(source),
                },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            Assert.That(
                compilation
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                Is.Empty
            );
            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new UnityLifecycleAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult()
                .ToArray();
        }
    }
}
