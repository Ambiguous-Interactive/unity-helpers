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
    /// Every collection <c>Serializer</c> marshals through a wrapper must have a root marshal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two lists are written in different files and nothing but this connects them. A collection
    /// added to <c>Serializer</c>'s interception without a marshal has no visible symptom today --
    /// <c>WProtoFacade</c> declines it and the reflection path answers, exactly as before -- and
    /// becomes unserializable the day <c>WALLSTOP_PROTO</c> becomes the default, in a shipped IL2CPP
    /// player, because that path is the one that cannot run there.
    /// </para>
    /// <para>
    /// Read as syntax rather than as text. A regular expression over these files matches the
    /// <c>typeof</c> inside a comment or a doc reference as readily as the one in the condition, and
    /// a scanner that quietly finds nothing reports success.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class RootMarshalCoverageTests
    {
        private const string SerializerFile = "Runtime/Core/Serialization/Serializer.cs";

        private const string RegistrationsFile =
            "Runtime/Core/Serialization/WProtoCollectionMarshalRegistrations.cs";

        private static readonly string[] InterceptionMethods =
        {
            "IsSerializableCollectionType",
            "IsSpecialCollectionType",
        };

        [Test]
        public void EveryInterceptedCollectionHasARootMarshal()
        {
            IReadOnlyCollection<string> intercepted = InterceptedTypes();
            IReadOnlyCollection<string> marshalled = MarshalledTypes();

            List<string> missing = intercepted
                .Where(name => !marshalled.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.That(
                missing,
                Is.Empty,
                "Serializer marshals these through a wrapper and nothing registers a WallstopProto "
                    + "root marshal for them, so they fall back to protobuf-net -- which does not run "
                    + "under IL2CPP: "
                    + string.Join(", ", missing)
            );
        }

        /// <summary>
        /// A marshal for a type nothing intercepts is a wire shape with no counterpart.
        /// </summary>
        /// <remarks>
        /// The reverse direction matters as much: a marshal whose interception was deleted would
        /// keep writing the wrapper under <c>WALLSTOP_PROTO</c> while the shipped path had moved on
        /// to writing the type itself, and only one of the two would be in any given save file.
        /// </remarks>
        [Test]
        public void EveryRootMarshalHasAnInterception()
        {
            IReadOnlyCollection<string> intercepted = InterceptedTypes();

            List<string> extra = MarshalledTypes()
                .Where(name => !intercepted.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.That(
                extra,
                Is.Empty,
                "These types have a WallstopProto root marshal but Serializer no longer marshals "
                    + "them through a wrapper: "
                    + string.Join(", ", extra)
            );
        }

        private static IReadOnlyCollection<string> InterceptedTypes()
        {
            SyntaxNode root = Parse(SerializerFile);
            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);

            foreach (string name in InterceptionMethods)
            {
                MethodDeclarationSyntax method = root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.Identifier.Text, name, StringComparison.Ordinal)
                    );

                Assert.IsNotNull(
                    method,
                    SerializerFile
                        + " no longer declares '"
                        + name
                        + "'. This gate reads the interception from that method, so a rename leaves "
                        + "it checking nothing."
                );

                List<string> types = TypeNames(method).ToList();
                Assert.IsNotEmpty(
                    types,
                    "'"
                        + name
                        + "' names no type with typeof, so the shape this gate parses has changed."
                );

                foreach (string type in types)
                {
                    found.Add(type);
                }
            }

            return found;
        }

        private static IReadOnlyCollection<string> MarshalledTypes()
        {
            SyntaxNode root = Parse(RegistrationsFile);
            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);

            foreach (AttributeSyntax attribute in root.DescendantNodes().OfType<AttributeSyntax>())
            {
                if (
                    !attribute
                        .Name.ToString()
                        .Contains("WProtoRootMarshal", StringComparison.Ordinal)
                )
                {
                    continue;
                }

                AttributeArgumentSyntax first = attribute.ArgumentList?.Arguments.FirstOrDefault();
                Assert.IsNotNull(
                    first,
                    "A WProtoRootMarshal attribute with no arguments cannot name a type."
                );

                List<string> names = TypeNames(first).ToList();
                Assert.AreEqual(
                    1,
                    names.Count,
                    "Expected exactly one typeof in a marshal's first argument: " + first
                );
                found.Add(names[0]);
            }

            Assert.IsNotEmpty(
                found,
                RegistrationsFile + " declares no root marshals, so this gate compares nothing."
            );
            return found;
        }

        private static IEnumerable<string> TypeNames(SyntaxNode node)
        {
            foreach (
                TypeOfExpressionSyntax expression in node.DescendantNodes()
                    .OfType<TypeOfExpressionSyntax>()
            )
            {
                yield return Simplify(expression.Type);
            }
        }

        private static string Simplify(TypeSyntax type)
        {
            switch (type)
            {
                case GenericNameSyntax generic:
                    return generic.Identifier.Text;
                case QualifiedNameSyntax qualified:
                    return Simplify(qualified.Right);
                case AliasQualifiedNameSyntax aliased:
                    return Simplify(aliased.Name);
                case SimpleNameSyntax simple:
                    return simple.Identifier.Text;
                default:
                    return type.ToString();
            }
        }

        private static SyntaxNode Parse(string relative)
        {
            string path = Path.Combine(ContractMirrorTests.RepositoryRoot(), relative);
            Assert.IsTrue(File.Exists(path), path + " is missing.");
            return CSharpSyntaxTree.ParseText(File.ReadAllText(path, Encoding.UTF8)).GetRoot();
        }
    }
}
