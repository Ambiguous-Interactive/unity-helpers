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
    /// Pins the shape a deleted asset leaves behind: a serialized row that is still there and empty.
    /// </summary>
    /// <remarks>
    /// The negative cases carry the weight. A rule that fired on a guarded walk, or on a local list
    /// nobody authored, would be turned off -- and so would one that refused the compaction repair,
    /// which is the right fix where a list is walked more than once.
    /// </remarks>
    [TestFixture]
    public sealed class SerializedRowDereferenceAnalyzerTests
    {
        private const string DiagnosticId = "WUH012";

        private const string UnityStubs =
            @"namespace UnityEngine
              {
                  public class Object
                  {
                      public string name;
                      public static implicit operator bool(Object exists) { return !object.ReferenceEquals(exists, null); }
                      public static bool operator ==(Object left, Object right) { return object.ReferenceEquals(left, right); }
                      public static bool operator !=(Object left, Object right) { return !object.ReferenceEquals(left, right); }
                      public override bool Equals(object other) { return base.Equals(other); }
                      public override int GetHashCode() { return base.GetHashCode(); }
                  }

                  public class Component : Object { }

                  public class MonoBehaviour : Component { }

                  public class ScriptableObject : Object { public virtual void Load() { } }

                  public sealed class SerializeField : System.Attribute { }

                  public sealed class SerializeReference : System.Attribute { }
              }";

        [Test]
        public void AnUnguardedWalkOfASerializedListIsReported()
        {
            Diagnostic reported = Single(
                @"public sealed class Glyphs : UnityEngine.MonoBehaviour
                  {
                      [UnityEngine.SerializeField]
                      private System.Collections.Generic.List<UnityEngine.ScriptableObject> _keycaps;

                      private void OnEnable()
                      {
                          foreach (UnityEngine.ScriptableObject keycap in _keycaps)
                          {
                              keycap.Load();
                          }
                      }
                  }"
            );

            Assert.AreEqual(DiagnosticId, reported.Id);
            Assert.AreEqual(DiagnosticSeverity.Warning, reported.Severity);
            StringAssert.Contains("_keycaps", reported.GetMessage());
            StringAssert.Contains("keycap", reported.GetMessage());
        }

        [Test]
        public void AnUnguardedWalkOfASerializedArrayIsReported()
        {
            Assert.AreEqual(
                1,
                Analyze(
                    @"public sealed class Follower : UnityEngine.MonoBehaviour
                          {
                              public UnityEngine.Component[] waypoints;

                              private void Update()
                              {
                                  foreach (UnityEngine.Component waypoint in waypoints)
                                  {
                                      string label = waypoint.name;
                                  }
                              }
                          }"
                ).Length
            );
        }

        [TestCase("if (keycap == null) { continue; }")]
        [TestCase("if (null != keycap) { keycap.Load(); }")]
        [TestCase("if (!keycap) { continue; }")]
        [TestCase("if (keycap) { keycap.Load(); }")]
        [TestCase("if (keycap is null) { continue; }")]
        public void AWalkThatTestsTheRowIsNotReported(string guard)
        {
            Assert.IsEmpty(
                Analyze(
                    @"public sealed class Glyphs : UnityEngine.MonoBehaviour
                      {
                          [UnityEngine.SerializeField]
                          private System.Collections.Generic.List<UnityEngine.ScriptableObject> _keycaps;

                          private void OnEnable()
                          {
                              foreach (UnityEngine.ScriptableObject keycap in _keycaps)
                              {
                                  "
                        + guard
                        + @"
                                  keycap.Load();
                              }
                          }
                      }"
                )
            );
        }

        [Test]
        public void TestingACopyOfTheRowCountsAsTestingTheRow()
        {
            Assert.IsEmpty(
                Analyze(
                    @"public sealed class Glyphs : UnityEngine.MonoBehaviour
                      {
                          [UnityEngine.SerializeField]
                          private System.Collections.Generic.List<UnityEngine.ScriptableObject> _keycaps;

                          private void OnEnable()
                          {
                              foreach (UnityEngine.ScriptableObject keycap in _keycaps)
                              {
                                  UnityEngine.ScriptableObject candidate = keycap;
                                  if (candidate == null)
                                  {
                                      continue;
                                  }

                                  keycap.Load();
                                  candidate.Load();
                              }
                          }
                      }"
                )
            );
        }

        [Test]
        public void DereferencingACopyOfAnUntestedRowIsStillReported()
        {
            Assert.AreEqual(
                1,
                Analyze(
                    @"public sealed class Glyphs : UnityEngine.MonoBehaviour
                          {
                              [UnityEngine.SerializeField]
                              private System.Collections.Generic.List<UnityEngine.ScriptableObject> _keycaps;

                              private void OnEnable()
                              {
                                  foreach (UnityEngine.ScriptableObject keycap in _keycaps)
                                  {
                                      UnityEngine.ScriptableObject candidate = keycap;
                                      candidate.Load();
                                  }
                              }
                          }"
                ).Length
            );
        }

        [Test]
        public void CompactingTheListOnceCountsAsTheGuard()
        {
            Assert.IsEmpty(
                Analyze(
                    @"public sealed class Glyphs : UnityEngine.MonoBehaviour
                      {
                          [UnityEngine.SerializeField]
                          private System.Collections.Generic.List<UnityEngine.ScriptableObject> _keycaps;

                          private void Awake()
                          {
                              _keycaps.RemoveAll(keycap => keycap == null);
                          }

                          private void OnEnable()
                          {
                              foreach (UnityEngine.ScriptableObject keycap in _keycaps)
                              {
                                  keycap.Load();
                              }
                          }

                          private void Update()
                          {
                              foreach (UnityEngine.ScriptableObject keycap in _keycaps)
                              {
                                  keycap.Load();
                              }
                          }
                      }"
                )
            );
        }

        [Test]
        public void ReassigningTheFieldWithoutTheNullRowsCountsAsTheGuard()
        {
            Assert.IsEmpty(
                Analyze(
                    @"public sealed class Follower : UnityEngine.MonoBehaviour
                      {
                          public UnityEngine.Component[] waypoints;

                          private void Awake()
                          {
                              waypoints = System.Array.FindAll(waypoints, waypoint => waypoint != null);
                          }

                          private void Update()
                          {
                              foreach (UnityEngine.Component waypoint in waypoints)
                              {
                                  string label = waypoint.name;
                              }
                          }
                      }"
                )
            );
        }

        [Test]
        public void GuardingTheListRatherThanTheRowsIsStillReported()
        {
            Assert.AreEqual(
                1,
                Analyze(
                    @"public sealed class Generator : UnityEngine.MonoBehaviour
                          {
                              [UnityEngine.SerializeField]
                              private System.Collections.Generic.List<UnityEngine.ScriptableObject> _postProcessors;

                              private void Bake()
                              {
                                  if (_postProcessors != null)
                                  {
                                      foreach (UnityEngine.ScriptableObject processor in _postProcessors)
                                      {
                                          processor.Load();
                                      }
                                  }
                              }
                          }"
                ).Length
            );
        }

        [Test]
        public void ACollectionNobodyAuthoredIsNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    @"public sealed class Runtime : UnityEngine.MonoBehaviour
                      {
                          [System.NonSerialized]
                          public System.Collections.Generic.List<UnityEngine.ScriptableObject> cache;

                          private System.Collections.Generic.List<UnityEngine.ScriptableObject> _private;

                          private void Update()
                          {
                              foreach (UnityEngine.ScriptableObject entry in cache)
                              {
                                  entry.Load();
                              }

                              foreach (UnityEngine.ScriptableObject entry in _private)
                              {
                                  entry.Load();
                              }

                              System.Collections.Generic.List<UnityEngine.ScriptableObject> local = cache;
                              foreach (UnityEngine.ScriptableObject entry in local)
                              {
                                  entry.Load();
                              }
                          }
                      }"
                )
            );
        }

        [Test]
        public void ASerializedCollectionOfValuesIsNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    @"public sealed class Numbers : UnityEngine.MonoBehaviour
                      {
                          [UnityEngine.SerializeField]
                          private System.Collections.Generic.List<string> _labels;

                          public int[] weights;

                          private void Update()
                          {
                              foreach (string label in _labels)
                              {
                                  int length = label.Length;
                              }

                              foreach (int weight in weights)
                              {
                                  int doubled = weight + weight;
                              }
                          }
                      }"
                )
            );
        }

        [Test]
        public void AWalkThatNeverDereferencesTheRowIsNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    @"public sealed class Counter : UnityEngine.MonoBehaviour
                      {
                          [UnityEngine.SerializeField]
                          private System.Collections.Generic.List<UnityEngine.ScriptableObject> _rows;

                          public int Count()
                          {
                              int total = 0;
                              foreach (UnityEngine.ScriptableObject row in _rows)
                              {
                                  total = total + 1;
                              }

                              return total;
                          }
                      }"
                )
            );
        }

        [Test]
        public void EachUnguardedWalkIsReportedOnceRatherThanPerDereference()
        {
            Assert.AreEqual(
                2,
                Analyze(
                    @"public sealed class Glyphs : UnityEngine.MonoBehaviour
                          {
                              [UnityEngine.SerializeField]
                              private System.Collections.Generic.List<UnityEngine.ScriptableObject> _keycaps;

                              private void OnEnable()
                              {
                                  foreach (UnityEngine.ScriptableObject first in _keycaps)
                                  {
                                      first.Load();
                                      first.Load();
                                      string label = first.name;
                                  }

                                  foreach (UnityEngine.ScriptableObject second in _keycaps)
                                  {
                                      second.Load();
                                  }
                              }
                          }"
                ).Length
            );
        }

        [Test]
        public void ACompilationWithNoUnityAtAllReportsNothing()
        {
            Assert.IsEmpty(
                Analyze(
                    @"public sealed class Plain
                      {
                          public System.Collections.Generic.List<string> rows;

                          public void Walk()
                          {
                              foreach (string row in rows)
                              {
                                  int length = row.Length;
                              }
                          }
                      }",
                    string.Empty
                )
            );
        }

        private static Diagnostic Single(string body)
        {
            ImmutableArray<Diagnostic> reported = Analyze(body);
            Assert.AreEqual(1, reported.Length, "Expected exactly one diagnostic");
            return reported[0];
        }

        private static ImmutableArray<Diagnostic> Analyze(string body)
        {
            return Analyze(body, UnityStubs);
        }

        private static ImmutableArray<Diagnostic> Analyze(string body, string stubs)
        {
            string source = "namespace Consumer { " + body + " }\n" + stubs;

            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly",
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.CSharp9)
                    ),
                },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            ImmutableArray<Diagnostic> compileErrors = compilation
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
            Assert.IsEmpty(
                compileErrors.Select(diagnostic => diagnostic.ToString()).ToArray(),
                "The fixture must compile"
            );

            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(
                        new SerializedRowDereferenceAnalyzer()
                    )
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }
    }
}
