// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Loader;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    // UNH-SUPPRESS UNH011: This .NET-only harness compiles the Unity-free planner sources directly.
    using WallstopStudios.UnityHelpers.Editor.Tools;

    [TestFixture]
    public sealed class CrossAssemblySubtypeTests
    {
        private const string Imports =
            "using System; using System.IO; using System.Collections.Generic; using NUnit.Framework; using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto; ";
        private const string Upstream =
            @"
namespace Upstream {
    [WProtoContract, ProtoBuf.ProtoContract, WProtoInclude(100, typeof(Melee)), ProtoBuf.ProtoInclude(100, typeof(Melee))]
    public partial class Weapon {        public int Damage { get => damage; set => damage = value; }

        [WProtoMember(1), ProtoBuf.ProtoMember(1)] private int damage;
    }
    [WProtoContract, ProtoBuf.ProtoContract] public sealed partial class Melee : Weapon {
        [WProtoMember(1), ProtoBuf.ProtoMember(1)] public int Sharpness;
    }
    [WProtoContract, ProtoBuf.ProtoContract] public sealed partial class Inventory {
        [WProtoMember(1), ProtoBuf.ProtoMember(1)] public Weapon Selected;
        [WProtoMember(2), ProtoBuf.ProtoMember(2)] public List<Weapon> Weapons;
    }
}";
        private const string Consumer =
            @"
namespace Consumer {
    [WProtoContract, ProtoBuf.ProtoContract, WProtoSubtype(typeof(Upstream.Weapon), 200)]
    public sealed partial class Plasma : Upstream.Weapon {
        [WProtoMember(1), ProtoBuf.ProtoMember(1)] public int Charge;
    }
    public static class Probe {
        public static void Run() {
            Upstream.Inventory value = new Upstream.Inventory {
                Selected = new Plasma { Damage = 17, Charge = 29 },
                Weapons = new List<Upstream.Weapon> { new Plasma { Damage = 31, Charge = 43 }, new Upstream.Melee { Damage = 47, Sharpness = 53 } }
            };
            Assert.IsTrue(WProtoFacade.TrySerialize(value, out byte[] actual));
            Assert.IsTrue(WProtoFacade.TryDeserialize<Upstream.Inventory>(actual, out Upstream.Inventory read));
            Check(read);
            ProtoBuf.Meta.RuntimeTypeModel model = ProtoBuf.Meta.RuntimeTypeModel.Create();
            model.Add(typeof(Upstream.Weapon), true).AddSubType(200, typeof(Plasma));
            using (MemoryStream stream = new MemoryStream()) {
                model.Serialize(stream, value);
                byte[] expected = stream.ToArray();
                CollectionAssert.AreEqual(expected, actual);
                Assert.IsTrue(WProtoFacade.TryDeserialize<Upstream.Inventory>(expected, out Upstream.Inventory fromOracle));
                Check(fromOracle);
            }
            using (MemoryStream stream = new MemoryStream(actual)) {
                Check((Upstream.Inventory)model.Deserialize(stream, null, typeof(Upstream.Inventory)));
            }
            Assert.IsTrue(WProtoFacade.TrySerialize((Plasma)value.Selected, out byte[] subtypeBytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize<Plasma>(subtypeBytes, out Plasma subtype));
            Assert.AreEqual(17, subtype.Damage);
            Assert.AreEqual(29, subtype.Charge);
        }
        private static void Check(Upstream.Inventory read) {
            Assert.IsTrue(read != null);
            Assert.IsInstanceOf<Plasma>(read.Selected);
            Assert.AreEqual(17, read.Selected.Damage);
            Assert.AreEqual(29, ((Plasma)read.Selected).Charge);
            Assert.AreEqual(2, read.Weapons.Count);
            Assert.IsInstanceOf<Plasma>(read.Weapons[0]);
            Assert.AreEqual(31, read.Weapons[0].Damage);
            Assert.AreEqual(43, ((Plasma)read.Weapons[0]).Charge);
            Assert.IsInstanceOf<Upstream.Melee>(read.Weapons[1]);
            Assert.AreEqual(47, read.Weapons[1].Damage);
            Assert.AreEqual(53, ((Upstream.Melee)read.Weapons[1]).Sharpness);
        }
    }
}";

        private static Compilation Generate(
            string name,
            string source,
            out ImmutableArray<Diagnostic> diagnostics,
            params MetadataReference[] additional
        )
        {
            List<MetadataReference> references = new List<MetadataReference>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            references.AddRange(additional);
            CSharpCompilation compilation = CSharpCompilation.Create(
                name,
                new[] { CSharpSyntaxTree.ParseText(Imports + source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            CSharpGeneratorDriver
                .Create(new WProtoGenerator())
                .RunGeneratorsAndUpdateCompilation(
                    compilation,
                    out Compilation generated,
                    out diagnostics
                );
            return generated;
        }

        private static byte[] Emit(Compilation compilation)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
                Assert.IsTrue(result.Success, string.Join("\n", result.Diagnostics));
                return stream.ToArray();
            }
        }

        [Test]
        public void CompiledUpstreamMemberAndCollectionRoundTripConsumerSubtypeAgainstOracle()
        {
            byte[] upstream = Emit(
                Generate(
                    "ExtensionUpstream" + Guid.NewGuid().ToString("N"),
                    Upstream,
                    out ImmutableArray<Diagnostic> upstreamDiagnostics
                )
            );
            Assert.IsEmpty(upstreamDiagnostics);
            Compilation consumer = Generate(
                "ExtensionConsumer" + Guid.NewGuid().ToString("N"),
                Consumer,
                out ImmutableArray<Diagnostic> diagnostics,
                MetadataReference.CreateFromImage(upstream)
            );
            Assert.IsEmpty(diagnostics);
            string generated = string.Join(
                "\n",
                consumer.SyntaxTrees.Skip(1).Select(tree => tree.ToString())
            );
            StringAssert.Contains("value is global::Consumer.Plasma", generated);
            StringAssert.Contains("value is global::Upstream.Melee", generated);
            StringAssert.DoesNotContain("MakeGenericType", generated);
            StringAssert.DoesNotContain("System.Reflection", generated);
            using (MemoryStream upstreamStream = new MemoryStream(upstream))
            {
                AssemblyLoadContext.Default.LoadFromStream(upstreamStream);
            }
            using (MemoryStream consumerStream = new MemoryStream(Emit(consumer)))
            {
                Assembly loaded = AssemblyLoadContext.Default.LoadFromStream(consumerStream);
                Action run = (Action)
                    loaded
                        .GetType("Consumer.Probe", true)
                        .GetMethod("Run")
                        .CreateDelegate(typeof(Action));
                run();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InstallingReplacementPreservesPrivateMemberSeedsWhenReadingAndPromoting(
            bool promote
        )
        {
            string upstreamSource =
                @"
namespace Upstream {
    [WProtoContract, ProtoBuf.ProtoContract] public partial class Weapon {        public int Damage { get => damage; set => damage = value; }
        [WProtoMember(2), ProtoBuf.ProtoMember(2)] public int Energy;

        [WProtoMember(1), ProtoBuf.ProtoMember(1)] private int damage;
    }
    [WProtoContract, ProtoBuf.ProtoContract] public sealed partial class Inventory {
        [WProtoMember(1), ProtoBuf.ProtoMember(1)] public Weapon Selected = new Weapon { Damage = 17 };
    }
}";
            byte[] upstream = Emit(
                Generate(
                    "SeededUpstream" + Guid.NewGuid().ToString("N"),
                    upstreamSource,
                    out ImmutableArray<Diagnostic> upstreamDiagnostics
                )
            );
            Assert.IsEmpty(upstreamDiagnostics);
            string payload = promote
                ? "new byte[] { 0x0A, 0x05, 0xC2, 0x0C, 0x02, 0x08, 0x1D }"
                : "new byte[] { 0x0A, 0x02, 0x10, 0x1F }";
            string consumerSource =
                @"
namespace Consumer {
    [WProtoContract, ProtoBuf.ProtoContract, WProtoSubtype(typeof(Upstream.Weapon), 200)]
    public sealed partial class Plasma : Upstream.Weapon {
        [WProtoMember(1), ProtoBuf.ProtoMember(1)] public int Charge;
    }
    public static class Probe {
        public static void Run() {
            byte[] payload = "
                + payload
                + @";
            Assert.IsTrue(WProtoFormatterProvider.TryGetReplacement<Upstream.Weapon>(out IWProtoReplacementFormatter<Upstream.Weapon> replacement));
            Assert.IsTrue(WProtoFacade.TryDeserialize(payload, out Upstream.Inventory read));
            Assert.AreEqual(17, read.Selected.Damage, ""Installing a replacement must retain the private seed member omitted by the payload."");
            ProtoBuf.Meta.RuntimeTypeModel model = ProtoBuf.Meta.RuntimeTypeModel.Create();
            model.Add(typeof(Upstream.Weapon), true).AddSubType(200, typeof(Plasma));
            using (MemoryStream stream = new MemoryStream(payload)) {
                Upstream.Inventory expected = (Upstream.Inventory)model.Deserialize(stream, null, typeof(Upstream.Inventory));
                Assert.AreEqual(expected.Selected.GetType(), read.Selected.GetType());
                Assert.AreEqual(expected.Selected.Damage, read.Selected.Damage);
                Assert.AreEqual(expected.Selected.Energy, read.Selected.Energy);
                if (expected.Selected is Plasma expectedPlasma) {
                    Assert.AreEqual(expectedPlasma.Charge, ((Plasma)read.Selected).Charge);
                }
            }
        }
    }
}";
            Compilation consumer = Generate(
                "SeededConsumer" + Guid.NewGuid().ToString("N"),
                consumerSource,
                out ImmutableArray<Diagnostic> diagnostics,
                MetadataReference.CreateFromImage(upstream)
            );
            Assert.IsEmpty(diagnostics);
            using (MemoryStream upstreamStream = new MemoryStream(upstream))
            {
                AssemblyLoadContext.Default.LoadFromStream(upstreamStream);
            }
            using (MemoryStream consumerStream = new MemoryStream(Emit(consumer)))
            {
                Assembly loaded = AssemblyLoadContext.Default.LoadFromStream(consumerStream);
                Action run = (Action)
                    loaded
                        .GetType("Consumer.Probe", true)
                        .GetMethod("Run")
                        .CreateDelegate(typeof(Action));
                run();
            }
        }

        [Test]
        public void ReplacementReadsDeferBaseAssignmentsAndKeepPrivateHookOrdering()
        {
            string upstreamSource = Upstream.Replace(
                "public int Damage",
                "[WProtoBeforeDeserialization] private void BeforeRead() { damage = -100; } [WProtoAfterDeserialization] private void AfterRead() { if (damage < 0) throw new InvalidOperationException(); } public int Damage"
            );
            byte[] upstream = Emit(
                Generate(
                    "HookExtensionUpstream" + Guid.NewGuid().ToString("N"),
                    upstreamSource,
                    out ImmutableArray<Diagnostic> upstreamDiagnostics
                )
            );
            Assert.IsEmpty(upstreamDiagnostics);
            string source = Consumer.Replace(
                "public static void Run() {",
                @"public static void Run() {
                byte[] reordered = new byte[] { 0x08, 0x11, 0xC2, 0x0C, 0x02, 0x08, 0x1D };
                Assert.IsTrue(WProtoFacade.TryDeserialize<Upstream.Weapon>(reordered, out Upstream.Weapon reorderedRead));
                Assert.IsInstanceOf<Plasma>(reorderedRead);
                Assert.AreEqual(17, reorderedRead.Damage);
                Assert.AreEqual(29, ((Plasma)reorderedRead).Charge);
                WProtoReader malformedReader = new WProtoReader(new byte[] { 0xC2, 0x0C, 0x02, 0x08 });
                Assert.IsFalse(Upstream.Weapon.WProtoFormatter.Instance.TryRead(ref malformedReader, out Upstream.Weapon malformed));
                Assert.IsTrue(malformed == null);
            "
            );
            Compilation consumer = Generate(
                "HookExtensionConsumer" + Guid.NewGuid().ToString("N"),
                source,
                out ImmutableArray<Diagnostic> diagnostics,
                MetadataReference.CreateFromImage(upstream)
            );
            Assert.IsEmpty(diagnostics);
            using (MemoryStream upstreamStream = new MemoryStream(upstream))
            {
                AssemblyLoadContext.Default.LoadFromStream(upstreamStream);
            }
            using (MemoryStream consumerStream = new MemoryStream(Emit(consumer)))
            {
                Assembly loaded = AssemblyLoadContext.Default.LoadFromStream(consumerStream);
                Action run = (Action)
                    loaded
                        .GetType("Consumer.Probe", true)
                        .GetMethod("Run")
                        .CreateDelegate(typeof(Action));
                run();
            }
        }

        [TestCase(1, "damage")]
        [TestCase(100, "Melee")]
        public void ConsumerCollisionNamesTheUpstreamOwner(int tag, string owner)
        {
            byte[] upstream = Emit(
                Generate(
                    "CollisionUpstream",
                    Upstream,
                    out ImmutableArray<Diagnostic> upstreamDiagnostics
                )
            );
            Assert.IsEmpty(upstreamDiagnostics);
            Generate(
                "CollisionConsumer",
                Consumer.Replace("200)", tag + ")"),
                out ImmutableArray<Diagnostic> diagnostics,
                MetadataReference.CreateFromImage(upstream)
            );
            Diagnostic error = diagnostics.Single(item => item.Id == "WPROTO040");
            Assert.AreEqual(DiagnosticSeverity.Error, error.Severity);
            StringAssert.Contains(owner, error.GetMessage());
        }

        [Test]
        public void RepeatedOwnerIsIdempotentAndDifferentOwnerPermanentlyRefusesBothChains()
        {
            ReplacementA<ConflictContract> first = new ReplacementA<ConflictContract>();
            WProtoFormatterProvider.RegisterReplacement(first);
            WProtoFormatterProvider.RegisterReplacement(new ReplacementA<ConflictContract>());
            Assert.IsTrue(
                WProtoFormatterProvider.TryGetReplacement(
                    out IWProtoReplacementFormatter<ConflictContract> registered
                )
            );
            Assert.IsInstanceOf<ReplacementA<ConflictContract>>(registered);
            InvalidOperationException conflict = Assert.Throws<InvalidOperationException>(() =>
                WProtoFormatterProvider.RegisterReplacement(new ReplacementB<ConflictContract>())
            );
            StringAssert.Contains(typeof(ConflictContract).FullName, conflict.Message);
            StringAssert.Contains(nameof(ReplacementA<ConflictContract>), conflict.Message);
            StringAssert.Contains(nameof(ReplacementB<ConflictContract>), conflict.Message);
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFormatterProvider.TryGetReplacement(
                    out IWProtoReplacementFormatter<ConflictContract> _
                )
            );
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFormatterProvider.RegisterReplacement(first)
            );
        }

        [Test]
        public void ArbitrationRejectsTwoProjectOwnersAndOnlyShippedPlayerOwners()
        {
            Dictionary<string, List<string>> owners = new Dictionary<string, List<string>>
            {
                ["Upstream.Weapon, Upstream"] = new List<string> { "Second", "First", "First" },
            };
            List<string> conflicts = WProtoSubtypeTagManifestFile.ReplacementConflicts(
                owners,
                null
            );
            Assert.AreEqual(1, conflicts.Count);
            StringAssert.Contains("First, Second", conflicts[0]);
            Assert.IsEmpty(
                WProtoSubtypeTagManifestFile.ReplacementConflicts(
                    owners,
                    new HashSet<string> { "First" }
                )
            );
            Assert.AreEqual(
                1,
                WProtoSubtypeTagManifestFile
                    .ReplacementConflicts(owners, new HashSet<string> { "First", "Second" })
                    .Count
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ImplicitAndTaglessCrossAssemblySubtypesUseTheConsumersCommittedManifest(
            bool explicitAttribute
        )
        {
            byte[] upstream = Emit(
                Generate(
                    "TaglessUpstream",
                    Upstream,
                    out ImmutableArray<Diagnostic> upstreamDiagnostics
                )
            );
            Assert.IsEmpty(upstreamDiagnostics);
            string declaration = explicitAttribute
                ? "[WProtoSubtype(typeof(Upstream.Weapon))]"
                : "";
            string source =
                "[assembly: WProtoSubtypeTag(\"Consumer.Plasma\", typeof(Upstream.Weapon), 200)] namespace Consumer { "
                + declaration
                + " public sealed partial class Plasma : Upstream.Weapon { [WProtoMember(1)] public int Charge; } }";
            Compilation consumer = Generate(
                "TaglessConsumer",
                source,
                out ImmutableArray<Diagnostic> diagnostics,
                MetadataReference.CreateFromImage(upstream)
            );
            Assert.AreEqual(1, diagnostics.Length);
            Assert.AreEqual("WPROTO047", diagnostics[0].Id);
            Assert.IsNotEmpty(Emit(consumer));
            StringAssert.Contains(
                "case 200",
                string.Join("\n", consumer.SyntaxTrees.Select(tree => tree.ToString()))
            );
        }

        [Test]
        public void AReferencedReplacementOwnerIsAConsumerCompilationError()
        {
            byte[] upstream = Emit(
                Generate(
                    "SiblingUpstream",
                    Upstream,
                    out ImmutableArray<Diagnostic> upstreamDiagnostics
                )
            );
            Assert.IsEmpty(upstreamDiagnostics);
            MetadataReference reference = MetadataReference.CreateFromImage(upstream);
            byte[] first = Emit(
                Generate(
                    "FirstExtender",
                    Consumer,
                    out ImmutableArray<Diagnostic> firstDiagnostics,
                    reference
                )
            );
            Assert.IsEmpty(firstDiagnostics);
            Generate(
                "SecondExtender",
                "[WProtoContract, WProtoSubtype(typeof(Upstream.Weapon), 201)] public sealed partial class Other : Upstream.Weapon {}",
                out ImmutableArray<Diagnostic> diagnostics,
                reference,
                MetadataReference.CreateFromImage(first)
            );
            Diagnostic error = diagnostics.Single(item => item.Id == "WPROTO040");
            StringAssert.Contains("FirstExtender", error.GetMessage());
        }

        [TestCase(101, "WProtoReserved(101)", "reserved")]
        [TestCase(102, "", "Deleted")]
        public void ExtensionCannotSpendReservedOrRetiredUpstreamNumbers(
            int tag,
            string reserved,
            string owner
        )
        {
            string source =
                "[assembly: WProtoRetiredSubtypeTag(\"Deleted\", typeof(Upstream.Weapon), 102)] "
                + Upstream.Replace(
                    "[WProtoContract, ProtoBuf.ProtoContract, WProtoInclude",
                    "["
                        + (reserved.Length == 0 ? "" : reserved + ", ")
                        + "WProtoContract, ProtoBuf.ProtoContract, WProtoInclude"
                );
            byte[] upstream = Emit(
                Generate(
                    "ReservedUpstream",
                    source,
                    out ImmutableArray<Diagnostic> upstreamDiagnostics
                )
            );
            Assert.IsEmpty(upstreamDiagnostics);
            Generate(
                "ReservedConsumer",
                Consumer.Replace("200)", tag + ")"),
                out ImmutableArray<Diagnostic> diagnostics,
                MetadataReference.CreateFromImage(upstream)
            );
            StringAssert.Contains(
                owner,
                diagnostics.Single(item => item.Id == "WPROTO040").GetMessage()
            );
        }

        [Test]
        public void ACompleteChainCanExtendAnExistingUpstreamSubtype()
        {
            string upstreamSource = Upstream.Replace(
                "sealed partial class Melee",
                "partial class Melee"
            );
            byte[] upstream = Emit(
                Generate(
                    "DeepExtensionUpstream" + Guid.NewGuid().ToString("N"),
                    upstreamSource,
                    out ImmutableArray<Diagnostic> upstreamDiagnostics
                )
            );
            Assert.IsEmpty(upstreamDiagnostics);
            string consumerSource = Consumer
                .Replace(
                    "WProtoSubtype(typeof(Upstream.Weapon), 200)",
                    "WProtoSubtype(typeof(Upstream.Melee), 200)"
                )
                .Replace("class Plasma : Upstream.Weapon", "class Plasma : Upstream.Melee")
                .Replace(
                    "model.Add(typeof(Upstream.Weapon), true).AddSubType",
                    "model.Add(typeof(Upstream.Melee), true).AddSubType"
                );
            Compilation consumer = Generate(
                "DeepExtensionConsumer" + Guid.NewGuid().ToString("N"),
                consumerSource,
                out ImmutableArray<Diagnostic> diagnostics,
                MetadataReference.CreateFromImage(upstream)
            );
            Assert.IsEmpty(diagnostics);
            using (MemoryStream upstreamStream = new MemoryStream(upstream))
            {
                AssemblyLoadContext.Default.LoadFromStream(upstreamStream);
            }
            using (MemoryStream consumerStream = new MemoryStream(Emit(consumer)))
            {
                Assembly loaded = AssemblyLoadContext.Default.LoadFromStream(consumerStream);
                Action run = (Action)
                    loaded
                        .GetType("Consumer.Probe", true)
                        .GetMethod("Run")
                        .CreateDelegate(typeof(Action));
                run();
            }
        }

        [Test]
        public void AnInaccessibleUpstreamSubtypeRefusesAnIncompleteReplacement()
        {
            string upstreamSource = Upstream.Replace(
                "public sealed partial class Melee",
                "internal sealed partial class Melee"
            );
            byte[] upstream = Emit(
                Generate(
                    "HiddenUpstream",
                    upstreamSource,
                    out ImmutableArray<Diagnostic> upstreamDiagnostics
                )
            );
            Assert.IsEmpty(upstreamDiagnostics);
            Generate(
                "HiddenConsumer",
                "[WProtoContract, WProtoSubtype(typeof(Upstream.Weapon), 200)] public sealed partial class Plasma : Upstream.Weapon {}",
                out ImmutableArray<Diagnostic> diagnostics,
                MetadataReference.CreateFromImage(upstream)
            );
            StringAssert.Contains(
                "inaccessible",
                diagnostics.Single(item => item.Id == "WPROTO040").GetMessage()
            );
        }

        [Test]
        public void NestedBasesPublishPrivateSpentNumbers()
        {
            string upstreamSource =
                "public partial class Container { [WProtoContract] public partial class Weapon { [WProtoMember(7)] private int secret; } }";
            byte[] upstream = Emit(
                Generate(
                    "NestedUpstream",
                    upstreamSource,
                    out ImmutableArray<Diagnostic> upstreamDiagnostics
                )
            );
            Assert.IsEmpty(upstreamDiagnostics);
            Generate(
                "NestedConsumer",
                "[WProtoContract, WProtoSubtype(typeof(Container.Weapon), 7)] public sealed partial class Plasma : Container.Weapon {}",
                out ImmutableArray<Diagnostic> diagnostics,
                MetadataReference.CreateFromImage(upstream)
            );
            StringAssert.Contains(
                "secret",
                diagnostics.Single(item => item.Id == "WPROTO040").GetMessage()
            );
        }

        private sealed class ConflictContract { }

        private class ReplacementA<T> : IWProtoReplacementFormatter<T>
        {
            public bool CanWrite(Type runtimeType) => runtimeType == typeof(T);

            public int Measure(in T value) => 0;

            public bool Write(ref WProtoWriter writer, in T value) => true;

            public bool TryRead(ref WProtoReader reader, out T value)
            {
                value = default;
                return true;
            }
        }

        private sealed class ReplacementB<T> : ReplacementA<T> { }
    }
}
