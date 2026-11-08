// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    [TestFixture]
    public sealed class AuthoredAssetHierarchyTests
    {
        private const string ScriptGuid = "abcdef0123456789abcdef0123456789";

        private static IReadOnlyList<AuthoredAssetDocument> Documents(params string[] lines)
        {
            return AuthoredAssetYaml.ReadDocuments(lines);
        }

        private static IReadOnlyList<AuthoredAssetDocument> Hierarchy(
            string parent = "{fileID: 20}",
            int transformType = 4
        )
        {
            return Documents(
                "--- !u!1 &1",
                "GameObject:",
                "  m_Name: Child",
                "  m_Component:",
                "  - component: {fileID: 11}",
                "  - component: {fileID: 10}",
                "--- !u!114 &11",
                "MonoBehaviour:",
                "  m_GameObject: {fileID: 1}",
                $"  m_Script: {{fileID: 11500000, guid: {ScriptGuid}, type: 3}}",
                $"--- !u!{transformType} &10",
                "Transform:",
                "  m_GameObject: {fileID: 1}",
                $"  m_Father: {parent}",
                "--- !u!1 &2",
                "GameObject:",
                "  m_Name: Parent",
                "  m_Component:",
                "  - component: {fileID: 20}",
                "--- !u!4 &20",
                "Transform:",
                "  m_GameObject: {fileID: 2}",
                "  m_Father: {fileID: 0}"
            );
        }

        [TestCase(4, TestName = "Hierarchy.Transform.WalksNearestFirst")]
        [TestCase(224, TestName = "Hierarchy.RectTransform.WalksNearestFirst")]
        public void WalksAuthoredHierarchyWithoutLoadingObjects(int transformType)
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Hierarchy(
                transformType: transformType
            );
            AuthoredAssetHierarchy hierarchy = new(documents);
            Assert.That(hierarchy.TryGetDocument(11, out AuthoredAssetDocument component), Is.True);
            Assert.That(hierarchy.TryGetOwner(component, out AuthoredAssetDocument owner), Is.True);
            Assert.That(owner, Is.SameAs(documents[0]));
            Assert.That(hierarchy.OwnerName(component), Is.EqualTo("Child"));
            Assert.That(hierarchy.ComponentIdsOf(owner), Is.EqualTo(new long[] { 11, 10 }));
            Assert.That(
                hierarchy.ComponentsOf(owner),
                Is.EqualTo(new[] { documents[1], documents[2] })
            );
            Assert.That(
                hierarchy.SiblingComponents(component),
                Is.EqualTo(new[] { documents[1], documents[2] })
            );
            Assert.That(
                hierarchy.SelfAndAncestorObjectIds(component),
                Is.EqualTo(new long[] { 1, 2 })
            );
        }

        [TestCase("{fileID: 0}", TestName = "Parent.Null.Stops")]
        [TestCase("{fileID: 999}", TestName = "Parent.Missing.Stops")]
        [TestCase("{fileID: 11}", TestName = "Parent.NotTransform.Stops")]
        [TestCase("{fileID: 20, guid: abc}", TestName = "Parent.External.Stops")]
        [TestCase("broken", TestName = "Parent.Malformed.Stops")]
        [TestCase("{fileID: 10}", TestName = "Parent.SelfCycle.Stops")]
        public void InvalidParentStopsAtOwner(string parent)
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Hierarchy(parent);
            AuthoredAssetHierarchy hierarchy = new(documents);
            Assert.That(
                hierarchy.SelfAndAncestorObjectIds(documents[1]),
                Is.EqualTo(new long[] { 1 })
            );
        }

        [Test]
        public void MultiObjectCycleTerminatesWithoutRepeatingAnObject()
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Hierarchy();
            List<AuthoredAssetDocument> cyclic = new(documents);
            IReadOnlyList<AuthoredAssetDocument> replacement = Documents(
                "--- !u!4 &20",
                "Transform:",
                "  m_GameObject: {fileID: 2}",
                "  m_Father: {fileID: 10}"
            );
            cyclic[4] = replacement[0];
            AuthoredAssetHierarchy hierarchy = new(cyclic);
            Assert.That(
                hierarchy.SelfAndAncestorObjectIds(cyclic[1]),
                Is.EqualTo(new long[] { 1, 2 })
            );
        }

        [Test]
        public void ComponentListIgnoresOtherFieldsAndPreservesRepeatedAndMissingAnchors()
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Documents(
                "--- !u!1 &1",
                "GameObject:",
                "  nested:",
                "    m_Component:",
                "    - component: {fileID: 777}",
                "  m_Component:",
                "  - component: {fileID: 11}",
                "  - component: {fileID: 999}",
                "  - component: {fileID: 11}",
                "  - component: {fileID: 0}",
                "  - component: {fileID: 11, guid: external}",
                "  other:",
                "  - component: {fileID: 888}",
                "--- !u!114 &11",
                "MonoBehaviour:",
                "  m_GameObject: {fileID: 1}"
            );
            AuthoredAssetHierarchy hierarchy = new(documents);
            Assert.That(
                hierarchy.ComponentIdsOf(documents[0]),
                Is.EqualTo(new long[] { 11, 999, 11 })
            );
            Assert.That(
                hierarchy.ComponentsOf(documents[0]),
                Is.EqualTo(new[] { documents[1], documents[1] })
            );
        }

        [TestCase(null, TestName = "Index.NullDocuments.IsEmpty")]
        [TestCase(false, TestName = "Index.EmptyDocuments.IsEmpty")]
        public void EmptyIndexFailsSoft(object empty)
        {
            IReadOnlyList<AuthoredAssetDocument> documents =
                empty == null ? null : Array.Empty<AuthoredAssetDocument>();
            AuthoredAssetHierarchy hierarchy = new(documents);
            Assert.That(hierarchy.TryGetDocument(1, out AuthoredAssetDocument found), Is.False);
            Assert.That(found, Is.Null);
            Assert.That(hierarchy.TryGetOwner(null, out found), Is.False);
            Assert.That(found, Is.Null);
            Assert.That(hierarchy.OwnerName(null), Is.EqualTo("<unnamed object>"));
            Assert.That(hierarchy.OwnerName(null, "fallback"), Is.EqualTo("fallback"));
            Assert.That(hierarchy.ComponentIdsOf(null), Is.Empty);
            Assert.That(hierarchy.ComponentsOf(null), Is.Empty);
            Assert.That(hierarchy.SiblingComponents(null), Is.Empty);
            Assert.That(hierarchy.SelfAndAncestorObjectIds(null), Is.Empty);
        }

        [Test]
        public void DuplicateAnchorsRemainAmbiguousAfterThreeOccurrences()
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Hierarchy();
            List<AuthoredAssetDocument> duplicates = new(documents)
            {
                documents[0],
                documents[0],
                null,
            };
            AuthoredAssetHierarchy hierarchy = new(duplicates);
            Assert.That(hierarchy.TryGetDocument(1, out AuthoredAssetDocument found), Is.False);
            Assert.That(found, Is.Null);
            Assert.That(hierarchy.TryGetOwner(documents[1], out found), Is.False);
            Assert.That(found, Is.Null);
            Assert.That(hierarchy.ComponentIdsOf(documents[0]), Is.Empty);
        }

        [TestCase("{fileID: 0}", TestName = "Owner.Null.FailsSoft")]
        [TestCase("{fileID: 20}", TestName = "Owner.WrongType.FailsSoft")]
        [TestCase("{fileID: 1, guid: abc}", TestName = "Owner.External.FailsSoft")]
        [TestCase("{fileID: 999}", TestName = "Owner.Missing.FailsSoft")]
        public void InvalidOwnerDoesNotResolve(string reference)
        {
            List<AuthoredAssetDocument> documents = new(Hierarchy());
            IReadOnlyList<AuthoredAssetDocument> replacement = Documents(
                "--- !u!114 &11",
                "MonoBehaviour:",
                $"  m_GameObject: {reference}"
            );
            documents[1] = replacement[0];
            AuthoredAssetHierarchy hierarchy = new(documents);
            Assert.That(
                hierarchy.TryGetOwner(documents[1], out AuthoredAssetDocument found),
                Is.False
            );
            Assert.That(found, Is.Null);
            Assert.That(hierarchy.SiblingComponents(documents[1]), Is.Empty);
            Assert.That(hierarchy.SelfAndAncestorObjectIds(documents[1]), Is.Empty);
        }

        [Test]
        public void ForeignDocumentWithSameAnchorCannotBorrowIndexMembership()
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Hierarchy();
            AuthoredAssetHierarchy hierarchy = new(documents);
            IReadOnlyList<AuthoredAssetDocument> foreign = Hierarchy();
            Assert.That(hierarchy.TryGetOwner(foreign[1], out _), Is.False);
            Assert.That(hierarchy.ComponentsOf(foreign[0]), Is.Empty);
            Assert.That(hierarchy.ComponentIdsOf(documents[1]), Is.Empty);
        }

        [TestCase(
            114,
            "{fileID: 11500000, guid: abcdef0123456789abcdef0123456789}",
            ScriptGuid,
            true,
            TestName = "Script.Exact.Matches"
        )]
        [TestCase(
            114,
            "{fileID: 11500000, guid: ABCDEF0123456789ABCDEF0123456789}",
            ScriptGuid,
            true,
            TestName = "Script.Uppercase.Matches"
        )]
        [TestCase(
            114,
            "{fileID: 0, guid: abcdef0123456789abcdef0123456789}",
            ScriptGuid,
            false,
            TestName = "Script.NullReference.DoesNotMatch"
        )]
        [TestCase(
            114,
            "{fileID: 11500000, guid: abcdef0123456789abcdef0123456789}",
            "abcdef",
            false,
            TestName = "Script.Prefix.DoesNotMatch"
        )]
        [TestCase(
            1,
            "{fileID: 11500000, guid: abcdef0123456789abcdef0123456789}",
            ScriptGuid,
            false,
            TestName = "Script.WrongDocumentType.DoesNotMatch"
        )]
        [TestCase(114, "{fileID: 11500000}", "", false, TestName = "Script.EmptyGuid.DoesNotMatch")]
        [TestCase(
            114,
            "{fileID: 11500000}",
            null,
            false,
            TestName = "Script.NullGuid.DoesNotMatch"
        )]
        public void ScriptIdentityUsesExactDirectReference(
            int type,
            string reference,
            string guid,
            bool expected
        )
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Documents(
                $"--- !u!{type} &1",
                "MonoBehaviour:",
                $"  m_Script: {reference}"
            );
            Assert.That(AuthoredAssetYaml.IsScriptedBy(documents[0], guid), Is.EqualTo(expected));
            Assert.That(AuthoredAssetYaml.IsScriptedBy(null, guid), Is.False);
        }

        [Test]
        public void NestedScriptReferenceDoesNotDeclareDocumentIdentity()
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Documents(
                "--- !u!114 &1",
                "MonoBehaviour:",
                "  nested:",
                $"    m_Script: {{fileID: 11500000, guid: {ScriptGuid}}}"
            );
            Assert.That(AuthoredAssetYaml.IsScriptedBy(documents[0], ScriptGuid), Is.False);
            Assert.That(documents[0].ScriptGuid, Is.Null);
        }

        [TestCase("{fileID: 1, fileID: 2}", TestName = "Reference.DuplicateFileId.Refused")]
        [TestCase(
            "{fileID: nope, fileID: 1}",
            TestName = "Reference.InvalidThenValidFileId.Refused"
        )]
        [TestCase(
            "{fileID: 1, guid: external, guid: }",
            TestName = "Reference.ExternalThenEmptyGuid.Refused"
        )]
        [TestCase(
            "{fileID: 1, guid: , guid: external}",
            TestName = "Reference.EmptyThenExternalGuid.Refused"
        )]
        public void DuplicateReferenceIdentityCannotResolveToAnOwner(string reference)
        {
            Assert.That(
                AuthoredAssetYaml.TryParseObjectReference(
                    reference,
                    out long fileId,
                    out string guid
                ),
                Is.False
            );
            Assert.That(fileId, Is.EqualTo(0));
            Assert.That(guid, Is.Null);
        }

        [TestCase(0, TestName = "Index.ZeroAnchor.CannotResolve")]
        [TestCase(-99, TestName = "Index.NegativeAnchor.Resolves")]
        [TestCase(long.MinValue, TestName = "Index.MinimumInt64Anchor.Resolves")]
        public void IndexPreservesSignedAnchorsExceptZero(long anchor)
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Documents(
                $"--- !u!1 &{anchor}",
                "GameObject:"
            );
            AuthoredAssetHierarchy hierarchy = new(documents);
            Assert.That(
                hierarchy.TryGetDocument(anchor, out AuthoredAssetDocument document),
                Is.EqualTo(anchor != 0)
            );
            Assert.That(document, anchor == 0 ? Is.Null : Is.SameAs(documents[0]));
        }

        [Test]
        public void DuplicateOwnerFieldsAreRefusedButNestedOwnerNamesAreIgnored()
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Documents(
                "--- !u!1 &1",
                "GameObject:",
                "--- !u!114 &2",
                "MonoBehaviour:",
                "  nested:",
                "    m_GameObject: {fileID: 999}",
                "  m_GameObject: {fileID: 1}",
                "--- !u!114 &3",
                "MonoBehaviour:",
                "  m_GameObject: {fileID: 1}",
                "  m_GameObject: {fileID: 1}"
            );
            AuthoredAssetHierarchy hierarchy = new(documents);
            Assert.That(
                hierarchy.TryGetOwner(documents[1], out AuthoredAssetDocument owner),
                Is.True
            );
            Assert.That(owner, Is.SameAs(documents[0]));
            Assert.That(hierarchy.TryGetOwner(documents[2], out owner), Is.False);
            Assert.That(owner, Is.Null);
        }

        [Test]
        public void DuplicateComponentListsAreAmbiguous()
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Documents(
                "--- !u!1 &1",
                "GameObject:",
                "  m_Component:",
                "  - component: {fileID: 2}",
                "  m_Component:",
                "  - component: {fileID: 3}"
            );
            AuthoredAssetHierarchy hierarchy = new(documents);
            Assert.That(hierarchy.ComponentIdsOf(documents[0]), Is.Empty);
        }

        [Test]
        public void MultipleTransformsAndTransformsOwnedElsewhereStopTheParentWalk()
        {
            List<AuthoredAssetDocument> documents = new(Hierarchy());
            IReadOnlyList<AuthoredAssetDocument> replacement = Documents(
                "--- !u!1 &1",
                "GameObject:",
                "  m_Component:",
                "  - component: {fileID: 10}",
                "  - component: {fileID: 20}"
            );
            documents[0] = replacement[0];
            AuthoredAssetHierarchy multiple = new(documents);
            Assert.That(
                multiple.SelfAndAncestorObjectIds(documents[1]),
                Is.EqualTo(new long[] { 1 })
            );
            replacement = Documents(
                "--- !u!1 &1",
                "GameObject:",
                "  m_Component:",
                "  - component: {fileID: 20}"
            );
            documents[0] = replacement[0];
            AuthoredAssetHierarchy foreign = new(documents);
            Assert.That(
                foreign.SelfAndAncestorObjectIds(documents[1]),
                Is.EqualTo(new long[] { 1 })
            );
        }

        [Test]
        public void DuplicateDirectScriptFieldsCannotIdentifyABehaviour()
        {
            IReadOnlyList<AuthoredAssetDocument> documents = Documents(
                "--- !u!114 &1",
                "MonoBehaviour:",
                "  m_Script: {fileID: 11500000, guid: abc, type: 3}",
                "  m_Script: {fileID: 11500000, guid: def, type: 3}"
            );
            Assert.That(documents[0].ScriptGuid, Is.Null);
            Assert.That(AuthoredAssetYaml.IsScriptedBy(documents[0], "abc"), Is.False);
            Assert.That(AuthoredAssetYaml.IsScriptedBy(documents[0], "def"), Is.False);
        }

        [TestCase(false, true, TestName = "ComponentEntries.List.ClearBetweenYields")]
        [TestCase(true, true, TestName = "ComponentEntries.ReadOnlyView.ClearBetweenYields")]
        [TestCase(false, false, TestName = "ComponentEntries.List.AppendBetweenYields")]
        [TestCase(true, false, TestName = "ComponentEntries.ReadOnlyView.AppendBetweenYields")]
        public void ComponentIterationObservesBorrowedEntryChanges(bool readOnlyView, bool clear)
        {
            IReadOnlyList<AuthoredAssetDocument> parsed = Documents(
                "--- !u!1 &1",
                "GameObject:",
                "  m_Component:",
                "  - component: {fileID: 2}",
                "  - component: {fileID: 3}"
            );
            AuthoredAssetDocument original = parsed[0];
            List<AuthoredAssetEntry> entries = new(original.Entries);
            IReadOnlyList<AuthoredAssetEntry> borrowed = readOnlyView
                ? entries.AsReadOnly()
                : entries;
            AuthoredAssetDocument document = new(
                original.FileId,
                original.UnityTypeId,
                original.RootKey,
                original.IsStripped,
                original.StartLineNumber,
                original.EndLineNumber,
                borrowed
            );
            AuthoredAssetHierarchy hierarchy = new(new[] { document });
            using IEnumerator<long> iterator = hierarchy.ComponentIdsOf(document).GetEnumerator();
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(2));
            if (clear)
            {
                entries.Clear();
                Assert.That(iterator.MoveNext(), Is.False);
            }
            else
            {
                entries.Add(entries[1]);
                Assert.That(iterator.MoveNext(), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(3));
                Assert.That(iterator.MoveNext(), Is.True);
                Assert.That(iterator.Current, Is.EqualTo(2));
                Assert.That(iterator.MoveNext(), Is.False);
            }
        }
    }
}
