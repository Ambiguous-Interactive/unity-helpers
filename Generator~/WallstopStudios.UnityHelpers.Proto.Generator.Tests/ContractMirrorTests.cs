// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using NUnit.Framework;

    /// <summary>
    /// Asserts that every protobuf-net contract this package ships has an exactly-corresponding
    /// WallstopProto annotation, or an entry in <see cref="NotMirrored"/> saying why it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The port from protobuf-net to WallstopProto is a mirror: same field numbers, same
    /// <c>IgnoreListHandling</c>, same includes, same lifecycle hooks. A mirror that is wrong in one
    /// place is not a compile error and not a test failure anywhere else -- it is a payload that no
    /// longer round-trips through the serializer the package shipped last release, discovered by a
    /// player whose save will not load. Reviewing sixty contracts by eye is not a gate; this is.
    /// </para>
    /// <para>
    /// It reads the sources rather than the compiled symbols because the package's runtime assembly
    /// cannot be loaded outside Unity: the community <c>UnityEngine.Modules</c> reference assemblies
    /// used by the type-check project throw <c>TypeLoadException</c> under CoreCLR, because Unity emits
    /// its internal calls with a method body and CoreCLR refuses to load a type that does. Syntax is
    /// enough here -- every property compared is written literally at the declaration.
    /// </para>
    /// <para>
    /// <b>Adding a contract to <see cref="NotMirrored"/> is the escape hatch and is meant to cost
    /// something.</b> Each entry needs a reason that survives review, because the alternative reading
    /// -- silence -- is what lets a half-finished port look finished.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class ContractMirrorTests
    {
        /// <summary>
        /// Contracts deliberately left without a WallstopProto annotation, and why.
        /// </summary>
        /// <remarks>
        /// Three reasons appear, and each is a decision rather than an omission:
        /// a type protobuf-net reaches through a surrogate never uses its own contract, so annotating
        /// it would create a second, unreachable wire shape; a type the package marshals through a
        /// wrapper is served by the wrapper's contract, and annotating the original would make
        /// <c>WProtoFacade</c> answer first with different bytes; and a type with a hand-written
        /// formatter already has one, hardened beyond what the generator emits.
        /// </remarks>
        private static readonly IReadOnlyDictionary<string, string> NotMirrored = new Dictionary<
            string,
            string
        >(StringComparer.Ordinal)
        {
            ["FastVector2Int"] =
                "protobuf-net reaches it through FastVector2IntSurrogate and WallstopProto through a "
                + "hand-written formatter; both recompute the cached hash on read rather than trusting "
                + "the wire, which a generated formatter would not do.",
            ["FastVector3Int"] =
                "As FastVector2Int, through FastVector3IntSurrogate, including its deliberate "
                + "out-of-order 3=hash/4=z tags.",
            ["Parabola"] =
                "protobuf-net reaches it through ParabolaSurrogate, which bypasses the public "
                + "constructor's positivity validation; WProtoUnitySurrogateRegistrations registers the "
                + "same pair, so the contract on the type itself is unreachable from either serializer.",
            ["ImmutableBitSet"] =
                "protobuf-net reaches it through ImmutableBitSetSurrogate, and "
                + "WProtoUnitySurrogateRegistrations registers the same pair.",
            ["WGuid"] =
                "Hand-written formatter (WGuidWProtoFormatter), byte-verified in session 172.",
            ["RandomState"] =
                "Hand-written formatter (RandomStateWProtoFormatter), byte-verified in session 172.",
            ["Deque"] =
                "Serializer marshals it through DequeProtoWrapper, which carries the WallstopProto "
                + "annotation. Annotating Deque itself would make WProtoFacade answer first, with "
                + "message bytes where the wrapper writes items-plus-capacity.",
            ["CyclicBuffer"] = "As Deque, through CyclicBufferProtoWrapper.",
            ["SparseSet"] = "As Deque, through SparseSetProtoWrapper.",
            ["SerializableDictionaryBase"] =
                "Serializer marshals SerializableDictionary through SerializableDictionaryProtoWrapper, "
                + "which carries the annotation.",
            ["SerializableSortedDictionaryBase"] =
                "As SerializableDictionaryBase, through SerializableSortedDictionaryProtoWrapper.",
            ["SerializableSetBase"] =
                "Serializer marshals SerializableHashSet and SerializableSortedSet through "
                + "SerializableHashSetProtoWrapper and SerializableSortedSetProtoWrapper.",
        };

        private static readonly string[] MemberHookNames =
        {
            "BeforeSerialization",
            "AfterSerialization",
            "BeforeDeserialization",
            "AfterDeserialization",
        };

        [Test]
        public void EveryProtobufContractIsMirroredOrExplicitlyNotMirrored()
        {
            IReadOnlyList<string> failures = Mismatches(RuntimeContracts(), NotMirrored);

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        /// <summary>
        /// Reports every way <paramref name="contracts"/> departs from being a faithful mirror.
        /// </summary>
        /// <remarks>
        /// Separate from the fixture that reads <c>Runtime/</c> so each rule can be driven from a
        /// synthetic source below. Whether a rule fires would otherwise depend on whether some
        /// package contract happens to use the feature, and an unexercised comparison is
        /// indistinguishable from one that always agrees. That is not hypothetical: the include and
        /// hook rules had no live example at all until the generator family was ported, so until then
        /// nothing would have noticed either of them silently passing everything.
        /// </remarks>
        private static IReadOnlyList<string> Mismatches(
            IEnumerable<ContractDeclaration> contracts,
            IReadOnlyDictionary<string, string> notMirrored
        )
        {
            List<string> failures = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (ContractDeclaration contract in contracts)
            {
                seen.Add(contract.Name);
                if (notMirrored.ContainsKey(contract.Name))
                {
                    if (contract.HasWProtoContract)
                    {
                        failures.Add(
                            contract.Where
                                + $"'{contract.Name}' is listed as not mirrored but carries [WProtoContract]."
                        );
                    }

                    continue;
                }

                if (!contract.HasWProtoContract)
                {
                    failures.Add(
                        contract.Where
                            + $"'{contract.Name}' has [ProtoContract] but no [WProtoContract], and is "
                            + "not listed in NotMirrored with a reason."
                    );
                    continue;
                }

                // A contract only WallstopProto knows about has nothing to be a mirror OF, so the
                // comparison below would report every one of its members as one protobuf-net does not
                // declare. The shape requirement still applies and is checked separately.
                if (!contract.HasProtoContract)
                {
                    continue;
                }

                Compare(
                    failures,
                    contract.Where,
                    contract.Name,
                    "contract",
                    contract.ProtoContractArguments,
                    contract.WProtoContractArguments
                );

                foreach (
                    string tag in contract
                        .ProtoIncludes.Except(contract.WProtoIncludes)
                        .OrderBy(x => x)
                )
                {
                    failures.Add(
                        contract.Where
                            + $"'{contract.Name}' has [ProtoInclude({tag})] with no mirror."
                    );
                }

                foreach (
                    string tag in contract
                        .WProtoIncludes.Except(contract.ProtoIncludes)
                        .OrderBy(x => x)
                )
                {
                    failures.Add(
                        contract.Where
                            + $"'{contract.Name}' has [WProtoInclude({tag})] that protobuf-net does not declare."
                    );
                }

                foreach (MemberDeclaration member in contract.Members)
                {
                    Compare(
                        failures,
                        member.Where,
                        contract.Name + "." + member.Name,
                        "member",
                        member.ProtoArguments,
                        member.WProtoArguments
                    );
                }
            }

            foreach (
                string stale in notMirrored.Keys.Where(name => !seen.Contains(name)).OrderBy(x => x)
            )
            {
                failures.Add(
                    $"'{stale}' is listed as not mirrored but no [ProtoContract] by that name exists under "
                        + "Runtime/. Remove the entry."
                );
            }

            return failures;
        }

        /// <summary>
        /// The mirror check is only as good as its reach, so the count of contracts it inspected is
        /// asserted rather than assumed.
        /// </summary>
        /// <remarks>
        /// A path bug, a renamed directory or a parse that silently found nothing all present as a
        /// passing suite otherwise -- the failure mode <c>"Files checked: 0"</c>, which reads as
        /// success everywhere it is not counted.
        /// </remarks>
        [Test]
        public void TheMirrorCheckReachesEveryContractInTheRuntimeTree()
        {
            List<ContractDeclaration> contracts = RuntimeContracts().ToList();

            Assert.That(
                contracts.Count,
                Is.GreaterThanOrEqualTo(60),
                "The runtime tree declares roughly sixty [ProtoContract] types; finding far fewer "
                    + "means the source walk missed a directory."
            );
            Assert.That(
                contracts.Select(contract => contract.Name).Distinct().Count(),
                Is.EqualTo(contracts.Count),
                "Two contracts share a name, so an NotMirrored entry would silently cover both."
            );
        }

        // One case per rule, driven through the same code the Runtime/ walk uses. The expected
        // fragment is the part a reader has to act on: which contract, which property, which side.
        [TestCase(
            "[ProtoContract] class Solo { [ProtoMember(1)] public int Value; }",
            "'Solo' has [ProtoContract] but no [WProtoContract]",
            TestName = "AContractWithNoMirrorIsReported"
        )]
        [TestCase(
            "[ProtoContract] [WProtoContract] partial class Half { [ProtoMember(1)] public int A; "
                + "[ProtoMember(2)] [WProtoMember(2)] public int B; }",
            "'Half.A' differs on 'tag'",
            TestName = "AMemberWithNoMirrorIsReported"
        )]
        [TestCase(
            "[ProtoContract] [WProtoContract] partial class Extra { [WProtoMember(1)] public int A; }",
            "'Extra.A' differs on 'tag'",
            TestName = "AMemberProtobufNetDoesNotDeclareIsReported"
        )]
        [TestCase(
            "[ProtoContract] [WProtoContract] partial class Lists { "
                + "[ProtoMember(1, OverwriteList = true)] [WProtoMember(1)] public int[] A; }",
            "'Lists.A' differs on 'OverwriteList'",
            TestName = "AnOverwriteListThatIsNotMirroredIsReported"
        )]
        [TestCase(
            "[ProtoContract] [WProtoContract] partial class Required { "
                + "[ProtoMember(1, IsRequired = true)] [WProtoMember(1)] public int A; }",
            "'Required.A' differs on 'IsRequired'",
            TestName = "AnIsRequiredThatIsNotMirroredIsReported"
        )]
        [TestCase(
            "[ProtoContract] [WProtoContract] partial class Skipped { "
                + "[ProtoIgnore] public int A; [ProtoMember(1)] [WProtoMember(1)] public int B; }",
            "'Skipped.A' differs on 'Ignore'",
            TestName = "AProtoIgnoreThatIsNotMirroredIsReported"
        )]
        [TestCase(
            "[ProtoContract] [WProtoContract] partial class Hooked { "
                + "[ProtoMember(1)] [WProtoMember(1)] public int A; "
                + "[ProtoAfterDeserialization] private void Rebuild() { } }",
            "'Hooked.Rebuild' differs on 'AfterDeserialization'",
            TestName = "AHookThatIsNotMirroredIsReported"
        )]
        [TestCase(
            "[ProtoContract] [WProtoContract] [ProtoInclude(100, typeof(Sub))] partial class Base { }",
            "[ProtoInclude(100, typeof(Sub))] with no mirror",
            TestName = "AnIncludeThatIsNotMirroredIsReported"
        )]
        [TestCase(
            "[ProtoContract] [WProtoContract] [WProtoInclude(100, typeof(Sub))] partial class Base { }",
            "[WProtoInclude(100, typeof(Sub))] that protobuf-net does not declare",
            TestName = "AnIncludeProtobufNetDoesNotDeclareIsReported"
        )]
        [TestCase(
            "[ProtoContract(IgnoreListHandling = true)] [WProtoContract] partial class Flagged { }",
            "'Flagged' differs on 'IgnoreListHandling'",
            TestName = "AnIgnoreListHandlingThatIsNotMirroredIsReported"
        )]
        [TestCase(
            "[ProtoContract(SkipConstructor = true)] [WProtoContract] partial class Raw { }",
            "'Raw' differs on 'SkipConstructor'",
            TestName = "ASkipConstructorThatIsNotMirroredIsReported"
        )]
        public void EachMirrorRuleReportsItsOwnBreak(string source, string expected)
        {
            IReadOnlyList<string> failures = Mismatches(
                Parse(source),
                new Dictionary<string, string>(StringComparer.Ordinal)
            );

            Assert.That(
                failures.Any(failure => failure.Contains(expected, StringComparison.Ordinal)),
                Is.True,
                "Expected a failure containing \""
                    + expected
                    + "\" but got: "
                    + (failures.Count == 0 ? "<none>" : string.Join(" | ", failures))
            );
        }

        [Test]
        public void AFaithfulMirrorReportsNothing()
        {
            IReadOnlyList<string> failures = Mismatches(
                Parse(
                    "[ProtoContract(IgnoreListHandling = true)] [WProtoContract(IgnoreListHandling = true)] "
                        + "[ProtoInclude(100, typeof(Sub))] [WProtoInclude(100, typeof(Sub))] partial class Whole { "
                        + "[ProtoMember(1, OverwriteList = true)] [WProtoMember(1, OverwriteList = true)] public int[] A; "
                        + "[ProtoIgnore] [WProtoIgnore] public int B; "
                        + "[ProtoAfterDeserialization] [WProtoAfterDeserialization] private void Rebuild() { } }"
                ),
                new Dictionary<string, string>(StringComparer.Ordinal)
            );

            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }

        [Test]
        public void AContractListedAsNotMirroredMayNotCarryTheAnnotation()
        {
            IReadOnlyList<string> failures = Mismatches(
                Parse("[ProtoContract] [WProtoContract] partial class Excluded { }"),
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Excluded"] = "because" }
            );

            Assert.That(
                failures,
                Has.Exactly(1).Contains("is listed as not mirrored but carries [WProtoContract]")
            );
        }

        [Test]
        public void AnEntryForAContractThatNoLongerExistsIsReported()
        {
            IReadOnlyList<string> failures = Mismatches(
                Parse("[ProtoContract] [WProtoContract] partial class Present { }"),
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Departed"] = "because" }
            );

            Assert.That(failures, Has.Exactly(1).Contains("'Departed' is listed as not mirrored"));
        }

        /// <summary>
        /// Every mirrored contract has a stand-in whose bytes are pinned against protobuf-net.
        /// </summary>
        /// <remarks>
        /// Matching attribute sets prove the two serializers were told the same thing; they prove
        /// nothing about what either one writes. Without this link a contract could be annotated,
        /// pass every gate in the repository, and still encode differently from the payloads already
        /// on players' disks -- so annotating one without saying what its bytes are is a failure
        /// here.
        /// </remarks>
        [Test]
        public void EveryMirroredContractHasAShapePinnedAgainstTheOracle()
        {
            // ProtobufUnitySurrogates.cs is exempt because its contracts are ALREADY stand-ins: the
            // surrogates and the collection wrappers exist only to give another type a wire shape,
            // and SurrogateDifferentialTests drives that shape against the oracle directly. Asking
            // for a stand-in for a stand-in would pin the same bytes twice.
            List<string> missing = RuntimeContracts()
                .Where(contract => !NotMirrored.ContainsKey(contract.Name))
                .Where(contract => !PackageContractShapeTests.Mirrors.ContainsKey(contract.Name))
                .Where(contract => !contract.Where.Contains("ProtobufUnitySurrogates.cs"))
                .Select(contract => contract.Where + contract.Name)
                .ToList();

            Assert.That(
                missing,
                Is.Empty,
                "These contracts are annotated but no stand-in in PackageContractShapes pins their "
                    + "bytes: "
                    + string.Join(", ", missing)
            );
        }

        /// <summary>
        /// Every reason either stands on its own or defers to another entry that does.
        /// </summary>
        /// <remarks>
        /// Several entries are legitimately shorthand -- "As Deque, through SparseSetProtoWrapper."
        /// -- and shorthand is only as good as the entry it points at. This catches the case where
        /// the referenced entry is renamed or removed and the deferral becomes a dead end, which
        /// reads exactly like a reason and is not one.
        /// </remarks>
        [Test]
        public void EveryReasonEitherExplainsItselfOrDefersToOneThatDoes()
        {
            foreach (KeyValuePair<string, string> entry in NotMirrored)
            {
                if (!entry.Value.StartsWith("As ", StringComparison.Ordinal))
                {
                    Assert.That(
                        entry.Value,
                        Has.Length.GreaterThan(60),
                        $"'{entry.Key}' needs a reason a reviewer can weigh, not a placeholder."
                    );
                    continue;
                }

                string referenced = entry.Value.Substring("As ".Length).Split(',', ' ')[0].Trim();
                Assert.That(
                    NotMirrored.ContainsKey(referenced),
                    Is.True,
                    $"'{entry.Key}' defers to '{referenced}', which is not itself listed."
                );
                Assert.That(
                    referenced,
                    Is.Not.EqualTo(entry.Key),
                    $"'{entry.Key}' defers to itself."
                );
            }
        }

        private static IEnumerable<ContractDeclaration> Parse(string source)
        {
            SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();
            foreach (
                TypeDeclarationSyntax declaration in root.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
            )
            {
                ContractDeclaration contract = ContractDeclaration.TryCreate(
                    "<synthetic>",
                    declaration
                );
                if (contract != null)
                {
                    yield return contract;
                }
            }
        }

        private static void Compare(
            List<string> failures,
            string where,
            string subject,
            string kind,
            IReadOnlyDictionary<string, string> proto,
            IReadOnlyDictionary<string, string> wproto
        )
        {
            foreach (
                string key in proto.Keys.Union(wproto.Keys).OrderBy(x => x, StringComparer.Ordinal)
            )
            {
                proto.TryGetValue(key, out string protoValue);
                wproto.TryGetValue(key, out string wprotoValue);
                if (string.Equals(protoValue, wprotoValue, StringComparison.Ordinal))
                {
                    continue;
                }

                failures.Add(
                    where
                        + $"{kind} '{subject}' differs on '{key}': protobuf-net says "
                        + $"{Describe(protoValue)}, WallstopProto says {Describe(wprotoValue)}."
                );
            }
        }

        private static string Describe(string value)
        {
            return value == null ? "<absent>" : "'" + value + "'";
        }

        private static IEnumerable<ContractDeclaration> RuntimeContracts()
        {
            foreach (string file in RuntimeSources())
            {
                SyntaxNode root = CSharpSyntaxTree
                    .ParseText(File.ReadAllText(file, Encoding.UTF8))
                    .GetRoot();

                foreach (
                    TypeDeclarationSyntax declaration in root.DescendantNodes()
                        .OfType<TypeDeclarationSyntax>()
                )
                {
                    ContractDeclaration contract = ContractDeclaration.TryCreate(file, declaration);
                    if (contract != null)
                    {
                        yield return contract;
                    }
                }
            }
        }

        private static IEnumerable<string> RuntimeSources()
        {
            string runtime = Path.Combine(RepositoryRoot(), "Runtime");
            return Directory
                .EnumerateFiles(runtime, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal);
        }

        private static readonly Lazy<string> Root = new Lazy<string>(FindRepositoryRoot);

        internal static string RepositoryRoot()
        {
            return Root.Value;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (
                    Directory.Exists(Path.Combine(directory.FullName, "Runtime"))
                    && File.Exists(Path.Combine(directory.FullName, "package.json"))
                )
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "No repository root above " + AppContext.BaseDirectory
            );
        }

        private sealed class ContractDeclaration
        {
            internal string Name { get; private set; }

            internal string Where { get; private set; }

            internal bool HasProtoContract { get; private set; }

            internal bool HasWProtoContract { get; private set; }

            internal IReadOnlyDictionary<string, string> ProtoContractArguments
            {
                get;
                private set;
            }

            internal IReadOnlyDictionary<string, string> WProtoContractArguments
            {
                get;
                private set;
            }

            internal IReadOnlyCollection<string> ProtoIncludes { get; private set; }

            internal IReadOnlyCollection<string> WProtoIncludes { get; private set; }

            internal IReadOnlyList<MemberDeclaration> Members { get; private set; }

            internal static ContractDeclaration TryCreate(string file, TypeDeclarationSyntax type)
            {
                List<AttributeSyntax> attributes = Attributes(type.AttributeLists);
                AttributeSyntax proto = attributes.FirstOrDefault(a =>
                    NameOf(a) == "ProtoContract"
                );
                AttributeSyntax wproto = attributes.FirstOrDefault(a =>
                    NameOf(a) == "WProtoContract"
                );

                // Either attribute is enough to be worth inspecting. A WallstopProto-only contract has
                // nothing to mirror, but it still has bytes, and the shape requirement is the whole
                // point -- keying this on the protobuf-net attribute alone would let a new contract be
                // added with no statement of what it encodes to.
                if (proto == null && wproto == null)
                {
                    return null;
                }

                return new ContractDeclaration
                {
                    Name = type.Identifier.ValueText,
                    Where = Location(file, type),
                    HasProtoContract = proto != null,
                    HasWProtoContract = wproto != null,
                    ProtoContractArguments = NamedArguments(proto),
                    WProtoContractArguments = NamedArguments(wproto),
                    ProtoIncludes = Includes(attributes, "ProtoInclude"),
                    WProtoIncludes = Includes(attributes, "WProtoInclude"),
                    Members = MemberDeclaration.From(file, type),
                };
            }

            private static IReadOnlyCollection<string> Includes(
                IReadOnlyCollection<AttributeSyntax> attributes,
                string name
            )
            {
                return attributes
                    .Where(attribute => NameOf(attribute) == name)
                    .Select(attribute =>
                        string.Join(
                            ", ",
                            (attribute.ArgumentList?.Arguments ?? default)
                                .Where(argument => argument.NameEquals == null)
                                .Select(argument => Normalize(argument.Expression.ToString()))
                        )
                    )
                    .ToList();
            }
        }

        private sealed class MemberDeclaration
        {
            internal string Name { get; private set; }

            internal string Where { get; private set; }

            internal IReadOnlyDictionary<string, string> ProtoArguments { get; private set; }

            internal IReadOnlyDictionary<string, string> WProtoArguments { get; private set; }

            internal static IReadOnlyList<MemberDeclaration> From(
                string file,
                TypeDeclarationSyntax type
            )
            {
                List<MemberDeclaration> members = new List<MemberDeclaration>();
                foreach (MemberDeclarationSyntax member in type.Members)
                {
                    // Nested types carry their own [ProtoContract] and are visited in their own right.
                    if (member is TypeDeclarationSyntax)
                    {
                        continue;
                    }

                    List<AttributeSyntax> attributes = Attributes(member.AttributeLists);
                    Dictionary<string, string> proto = new Dictionary<string, string>(
                        StringComparer.Ordinal
                    );
                    Dictionary<string, string> wproto = new Dictionary<string, string>(
                        StringComparer.Ordinal
                    );

                    foreach (AttributeSyntax attribute in attributes)
                    {
                        string name = NameOf(attribute);
                        Dictionary<string, string> target = name.StartsWith(
                            "WProto",
                            StringComparison.Ordinal
                        )
                            ? wproto
                            : proto;
                        string bare =
                            name.StartsWith("WProto", StringComparison.Ordinal)
                                ? name.Substring("WProto".Length)
                            : name.StartsWith("Proto", StringComparison.Ordinal)
                                ? name.Substring("Proto".Length)
                            : null;
                        if (bare == null)
                        {
                            continue;
                        }

                        if (bare == "Member")
                        {
                            target["tag"] = FirstPositionalArgument(attribute);
                            foreach (
                                KeyValuePair<string, string> named in NamedArguments(attribute)
                            )
                            {
                                target[named.Key] = named.Value;
                            }
                        }
                        else if (bare == "Ignore" || MemberHookNames.Contains(bare))
                        {
                            target[bare] = "present";
                        }
                    }

                    if (proto.Count == 0 && wproto.Count == 0)
                    {
                        continue;
                    }

                    members.Add(
                        new MemberDeclaration
                        {
                            Name = NameOfMember(member),
                            Where = Location(file, member),
                            ProtoArguments = proto,
                            WProtoArguments = wproto,
                        }
                    );
                }

                return members;
            }

            private static string NameOfMember(MemberDeclarationSyntax member)
            {
                switch (member)
                {
                    case FieldDeclarationSyntax field:
                        return string.Join(
                            "/",
                            field.Declaration.Variables.Select(v => v.Identifier.ValueText)
                        );
                    case PropertyDeclarationSyntax property:
                        return property.Identifier.ValueText;
                    case MethodDeclarationSyntax method:
                        return method.Identifier.ValueText;
                    default:
                        return member.Kind().ToString();
                }
            }
        }

        private static List<AttributeSyntax> Attributes(SyntaxList<AttributeListSyntax> lists)
        {
            return lists.SelectMany(list => list.Attributes).ToList();
        }

        private static string NameOf(AttributeSyntax attribute)
        {
            string name = attribute.Name.ToString();
            int lastDot = name.LastIndexOf('.');
            if (lastDot >= 0)
            {
                name = name.Substring(lastDot + 1);
            }

            return name.EndsWith("Attribute", StringComparison.Ordinal)
                ? name.Substring(0, name.Length - "Attribute".Length)
                : name;
        }

        private static string FirstPositionalArgument(AttributeSyntax attribute)
        {
            AttributeArgumentSyntax argument = (
                attribute.ArgumentList?.Arguments ?? default
            ).FirstOrDefault(a => a.NameEquals == null);
            return argument == null ? "<none>" : Normalize(argument.Expression.ToString());
        }

        private static IReadOnlyDictionary<string, string> NamedArguments(AttributeSyntax attribute)
        {
            Dictionary<string, string> arguments = new Dictionary<string, string>(
                StringComparer.Ordinal
            );
            if (attribute?.ArgumentList == null)
            {
                return arguments;
            }

            foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
            {
                if (argument.NameEquals == null)
                {
                    continue;
                }

                string name = argument.NameEquals.Name.Identifier.ValueText;

                // Name is documentation on both sides and never reaches the wire; comparing it would
                // make a schema label a wire-compatibility failure.
                if (name == "Name")
                {
                    continue;
                }

                arguments[name] = Normalize(argument.Expression.ToString());
            }

            return arguments;
        }

        private static string Normalize(string expression)
        {
            return string.Concat(expression.Where(character => !char.IsWhiteSpace(character)));
        }

        private static string Location(string file, SyntaxNode node)
        {
            string root = RepositoryRoot();
            string relative = file.StartsWith(root, StringComparison.Ordinal)
                ? file.Substring(root.Length).TrimStart('/', '\\')
                : file;
            int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            return relative.Replace('\\', '/') + ":" + line + ": ";
        }
    }
}
