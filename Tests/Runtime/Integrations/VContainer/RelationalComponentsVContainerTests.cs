// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if VCONTAINER_PRESENT
namespace WallstopStudios.UnityHelpers.Tests.Integrations.VContainer.Runtime
{
    using System;
    using System.Collections.Generic;
    using global::VContainer;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Integrations.VContainer;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentsVContainerTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            ReflexTestSupport.EnsureReflexSettings();
        }

        [Test]
        public void ResolverExtensionsUseBoundAssigner()
        {
            RecordingAssigner assigner = new();
            ContainerBuilder builder = new();
            builder.RegisterInstance(assigner).As<IRelationalComponentAssigner>();
            IObjectResolver resolver = builder.Build();

            VContainerRelationalTester tester = CreateHierarchy();

            resolver.AssignRelationalComponents(tester);

            Assert.That(
                assigner.CallCount,
                Is.EqualTo(1),
                "Assigner should be called exactly once when bound"
            );
            Assert.That(
                assigner.LastComponent,
                Is.SameAs(tester),
                "Assigner should receive the same component instance"
            );
            Assert.IsTrue(
                tester.parentBody != null,
                "ParentComponent assignment should set parentBody"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "ChildComponent assignment should set childCollider"
            );
        }

        [Test]
        public void ResolverExtensionsFallBackWhenAssignerMissing()
        {
            ContainerBuilder builder = new();
            IObjectResolver resolver = builder.Build();

            VContainerRelationalTester tester = CreateHierarchy();

            resolver.AssignRelationalComponents(tester);

            Assert.IsTrue(
                tester.parentBody != null,
                "Fallback should assign parentBody without a bound assigner"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "Fallback should assign childCollider without a bound assigner"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator EntryPointAssignsActiveSceneComponents()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(VContainerRelationalTester));
            Scene scene = CreateTempScene("VContainerTestScene_Active");
            ContainerBuilder builder = new();
            builder.RegisterInstance(cache).AsSelf();
            RecordingAssigner assigner = new();
            builder.RegisterInstance(assigner).As<IRelationalComponentAssigner>();
            IObjectResolver resolver = builder.Build();

            VContainerRelationalTester tester = CreateHierarchy();
            GameObject rootObj = tester.transform.root.gameObject;
            SceneManager.MoveGameObjectToScene(rootObj, scene);
            yield return null;

            RelationalComponentEntryPoint entryPoint = new(
                resolver.Resolve<IRelationalComponentAssigner>(),
                cache,
                RelationalSceneAssignmentOptions.Default
            );
            entryPoint.Initialize();
            yield return null;

            Assert.IsTrue(
                tester.parentBody != null,
                "Entry point should assign parentBody in the active scene"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "Entry point should assign childCollider in the active scene"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneLoadListenerAssignsAdditiveSceneSinglePass()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(VContainerRelationalTester));
            RelationalComponentAssigner assigner = new(cache);
            RelationalSceneAssignmentOptions options = new(
                includeInactive: true,
                useSinglePassScan: true
            );
            RelationalSceneLoadListener listener = new(assigner, cache, options);
            listener.Initialize();
            TrackDisposable(listener);

            Scene additive = CreateTempScene(
                "VContainer_Additive_Runtime_Single",
                setActive: false
            );

            VContainerRelationalTester tester = CreateHierarchy();
            GameObject root = tester.transform.root.gameObject;
            SceneManager.MoveGameObjectToScene(root, additive);

            yield return null;

            listener.OnSceneLoaded(additive, LoadSceneMode.Additive);
            yield return null;

            Assert.IsTrue(
                tester.parentBody != null,
                "Scene load listener should assign parentBody in single-pass mode"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "Scene load listener should assign childCollider in single-pass mode"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneLoadListenerAssignsAdditiveSceneMultiPass()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(VContainerRelationalTester));
            RelationalComponentAssigner assigner = new(cache);
            RelationalSceneAssignmentOptions options = new(
                includeInactive: true,
                useSinglePassScan: false
            );
            RelationalSceneLoadListener listener = new(assigner, cache, options);
            listener.Initialize();
            TrackDisposable(listener);

            Scene additive = CreateTempScene("VContainer_Additive_Runtime_Multi", setActive: false);

            VContainerRelationalTester tester = CreateHierarchy();
            GameObject root = tester.transform.root.gameObject;
            SceneManager.MoveGameObjectToScene(root, additive);

            yield return null;

            listener.OnSceneLoaded(additive, LoadSceneMode.Additive);
            yield return null;

            Assert.IsTrue(
                tester.parentBody != null,
                "Scene load listener should assign parentBody in multi-pass mode"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "Scene load listener should assign childCollider in multi-pass mode"
            );
        }

        [Test]
        public void BuildUpWithRelationsAssignsFields()
        {
            ContainerBuilder builder = new();
            IObjectResolver resolver = builder.Build();

            VContainerRelationalTester tester = CreateHierarchy();

            VContainerRelationalTester result = resolver.BuildUpWithRelations(tester);

            Assert.That(
                result,
                Is.SameAs(tester),
                "BuildUpWithRelations should return the same component instance"
            );
            Assert.IsTrue(
                tester.parentBody != null,
                "BuildUpWithRelations should assign parentBody"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "BuildUpWithRelations should assign childCollider"
            );
        }

        [Test]
        public void RegisterRelationalComponentsRegistersAssignerSingleton()
        {
            ContainerBuilder builder = new();
            builder.RegisterRelationalComponents();
            IObjectResolver resolver = builder.Build();

            IRelationalComponentAssigner a = resolver.Resolve<IRelationalComponentAssigner>();
            IRelationalComponentAssigner b = resolver.Resolve<IRelationalComponentAssigner>();
            Assert.That(
                a,
                Is.SameAs(b),
                "RegisterRelationalComponents should register assigner as singleton"
            );
        }

        [Test]
        public void ResolverExtensionsAssignRelationalHierarchyAssignsFields()
        {
            ContainerBuilder builder = new();
            IObjectResolver resolver = builder.Build();
            VContainerRelationalTester tester = CreateHierarchy();
            resolver.AssignRelationalHierarchy(tester.gameObject, includeInactiveChildren: false);
            Assert.IsTrue(
                tester.parentBody != null,
                "AssignRelationalHierarchy should assign parentBody"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "AssignRelationalHierarchy should assign childCollider"
            );
        }

        [Test]
        public void ResolverExtensionsAssignRelationalHierarchyUsesBoundAssigner()
        {
            RecordingAssigner assigner = new();
            ContainerBuilder builder = new();
            builder.RegisterInstance(assigner).As<IRelationalComponentAssigner>();
            IObjectResolver resolver = builder.Build();
            VContainerRelationalTester tester = CreateHierarchy();
            resolver.AssignRelationalHierarchy(tester.gameObject, includeInactiveChildren: true);
            Assert.That(
                assigner.HierarchyCallCount,
                Is.EqualTo(1),
                "AssignRelationalHierarchy should use bound assigner"
            );
        }

        [Test]
        public void ResolverAssignRelationalHierarchyRespectsIncludeInactiveChildren()
        {
            ContainerBuilder builder = new();
            IObjectResolver resolver = builder.Build();

            // Root tester stays active (always included); create an inactive sub-tester
            VContainerRelationalTester rootTester = CreateHierarchy();
            GameObject subTesterGO = Track(new GameObject("VContainerSubTester"));
            subTesterGO.transform.SetParent(rootTester.transform);
            VContainerRelationalTester subTester =
                subTesterGO.AddComponent<VContainerRelationalTester>();
            GameObject subChild = Track(new GameObject("VContainerSubChild"));
            subChild.AddComponent<CapsuleCollider>();
            subChild.transform.SetParent(subTesterGO.transform);
            subTesterGO.SetActive(false);

            // exclude inactive children: root assigned, sub-tester skipped
            resolver.AssignRelationalHierarchy(
                rootTester.gameObject,
                includeInactiveChildren: false
            );
            Assert.IsTrue(
                rootTester.parentBody != null,
                "Root tester should be assigned even when includeInactiveChildren is false"
            );
            Assert.IsTrue(
                rootTester.childCollider != null,
                "Root tester should be assigned even when includeInactiveChildren is false"
            );
            Assert.IsTrue(
                subTester.parentBody == null,
                "Inactive sub tester should be skipped when includeInactiveChildren is false"
            );
            Assert.IsTrue(
                subTester.childCollider == null,
                "Inactive sub tester should be skipped when includeInactiveChildren is false"
            );

            // include inactive: sub tester now assigned
            resolver.AssignRelationalHierarchy(
                rootTester.gameObject,
                includeInactiveChildren: true
            );
            Assert.IsTrue(
                subTester.parentBody != null,
                "Inactive sub tester should be assigned when includeInactiveChildren is true"
            );
            Assert.IsTrue(
                subTester.childCollider != null,
                "Inactive sub tester should be assigned when includeInactiveChildren is true"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator EntryPointIgnoresNonActiveScenes()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(VContainerRelationalTester));
            Scene active = CreateTempScene("VContainerActiveScene_Sep");
            ContainerBuilder builder = new();
            builder.RegisterInstance(cache).AsSelf();
            RecordingAssigner assigner = new();
            builder.RegisterInstance(assigner).As<IRelationalComponentAssigner>();
            IObjectResolver resolver = builder.Build();

            VContainerRelationalTester testerA = CreateHierarchy();
            SceneManager.MoveGameObjectToScene(testerA.transform.root.gameObject, active);

            Scene secondary = CreateTempScene("VContainerSecondaryScene_Sep", setActive: false);
            VContainerRelationalTester testerB = CreateHierarchy();
            SceneManager.MoveGameObjectToScene(testerB.transform.root.gameObject, secondary);
            yield return null;

            RelationalComponentEntryPoint entryPoint = new(
                resolver.Resolve<IRelationalComponentAssigner>(),
                cache,
                new RelationalSceneAssignmentOptions(includeInactive: true)
            );
            entryPoint.Initialize();
            yield return null;

            Assert.That(
                assigner.CallCount,
                Is.EqualTo(1),
                "Entry point should only process components from the active scene"
            );
            Assert.That(
                assigner.LastComponent,
                Is.SameAs(testerA),
                "Active scene tester should be assigned"
            );
        }

        [Test]
        public void ChildAttributeIncludeInactiveControlsSearch()
        {
            ContainerBuilder builder = new();
            IObjectResolver resolver = builder.Build();

            GameObject parent = Track(new GameObject("IncludeInactiveParent"));
            parent.AddComponent<Rigidbody>();
            GameObject middle = Track(new GameObject("IncludeInactiveMiddle"));
            AttributeIncludeInactiveTester tester =
                middle.AddComponent<AttributeIncludeInactiveTester>();
            middle.transform.SetParent(parent.transform);
            GameObject child = Track(new GameObject("IncludeInactiveChild"));
            child.AddComponent<CapsuleCollider>();
            child.SetActive(false);
            child.transform.SetParent(middle.transform);

            // Expect an error about missing child component due to IncludeInactive=false on attribute.
            // Emitted via the package logger, which is compiled out in a non-development player.
            ExpectWallstopLog(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    ".*Unable to find child component of type UnityEngine\\.CapsuleCollider for field 'childCollider'.*"
                )
            );
            resolver.AssignRelationalComponents(tester);
            Assert.IsTrue(tester.parentBody != null, "Parent assignment should succeed");
            Assert.IsTrue(
                tester.childCollider == null,
                "Child assignment should ignore inactive child when IncludeInactive is false"
            );

            child.SetActive(true);
            resolver.AssignRelationalComponents(tester);
            Assert.IsTrue(
                tester.childCollider != null,
                "Child assignment should include active child when IncludeInactive is false"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator EntryPointRespectsIncludeInactiveOption()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(VContainerRelationalTester));
            Scene scene = CreateTempScene("VContainerTestScene_Inactive");
            ContainerBuilder builder = new();
            builder.RegisterInstance(cache).AsSelf();
            RecordingAssigner assigner = new();
            builder.RegisterInstance(assigner).As<IRelationalComponentAssigner>();
            IObjectResolver resolver = builder.Build();

            VContainerRelationalTester tester = CreateHierarchy();
            tester.gameObject.SetActive(false);
            GameObject rootObj = tester.transform.root.gameObject;
            SceneManager.MoveGameObjectToScene(rootObj, scene);
            yield return null;

            RelationalComponentEntryPoint disabledEntryPoint = new(
                resolver.Resolve<IRelationalComponentAssigner>(),
                cache,
                new RelationalSceneAssignmentOptions(includeInactive: false)
            );
            disabledEntryPoint.Initialize();
            Assert.IsTrue(
                tester.parentBody == null,
                "Disabled option should skip inactive components"
            );
            Assert.IsTrue(
                tester.childCollider == null,
                "Disabled option should skip inactive components"
            );

            RelationalComponentEntryPoint enabledEntryPoint = new(
                resolver.Resolve<IRelationalComponentAssigner>(),
                cache,
                new RelationalSceneAssignmentOptions(includeInactive: true)
            );
            enabledEntryPoint.Initialize();
            yield return null;
            Assert.IsTrue(
                tester.parentBody != null,
                "Enabled option should include inactive components"
            );
            Assert.IsTrue(
                tester.childCollider != null,
                "Enabled option should include inactive components"
            );
        }

        [UnityTest]
        public System.Collections.IEnumerator EntryPointUsesMultiPassWhenConfigured()
        {
            AttributeMetadataCache cache = CreateCacheFor(typeof(VContainerRelationalTester));
            Scene scene = CreateTempScene("VContainerMultiPassScene");
            ContainerBuilder builder = new();
            builder.RegisterInstance(cache).AsSelf();
            RecordingAssigner assigner = new();
            builder.RegisterInstance(assigner).As<IRelationalComponentAssigner>();
            IObjectResolver resolver = builder.Build();

            VContainerRelationalTester tester = CreateHierarchy();
            SceneManager.MoveGameObjectToScene(tester.transform.root.gameObject, scene);
            yield return null;

            RelationalComponentEntryPoint entryPoint = new(
                resolver.Resolve<IRelationalComponentAssigner>(),
                cache,
                new RelationalSceneAssignmentOptions(
                    includeInactive: true,
                    useSinglePassScan: false
                )
            );
            entryPoint.Initialize();
            yield return null;

            Assert.That(
                assigner.CallCount,
                Is.EqualTo(1),
                "Multi-pass configuration should still assign each relational component once"
            );
            Assert.That(
                assigner.LastComponent,
                Is.SameAs(tester),
                "Multi-pass configuration should target the tracked tester"
            );
        }

        private VContainerRelationalTester CreateHierarchy()
        {
            GameObject parent = Track(new GameObject("VContainerParent"));
            parent.AddComponent<Rigidbody>();

            GameObject middle = Track(new GameObject("VContainerMiddle"));
            VContainerRelationalTester tester = middle.AddComponent<VContainerRelationalTester>();
            middle.transform.SetParent(parent.transform);

            GameObject child = Track(new GameObject("VContainerChild"));
            child.AddComponent<CapsuleCollider>();
            child.transform.SetParent(middle.transform);

            return tester;
        }

        private AttributeMetadataCache CreateCacheFor(Type componentType)
        {
            AttributeMetadataCache cache = Track(
                ScriptableObject.CreateInstance<AttributeMetadataCache>()
            );

            AttributeMetadataCache.RelationalFieldMetadata[] fields =
            {
                new(
                    nameof(VContainerRelationalTester.parentBody),
                    AttributeMetadataCache.RelationalAttributeKind.Parent,
                    AttributeMetadataCache.FieldKind.Single,
                    typeof(Rigidbody).AssemblyQualifiedName,
                    isInterface: false
                ),
                new(
                    nameof(VContainerRelationalTester.childCollider),
                    AttributeMetadataCache.RelationalAttributeKind.Child,
                    AttributeMetadataCache.FieldKind.Single,
                    typeof(CapsuleCollider).AssemblyQualifiedName,
                    isInterface: false
                ),
            };

            AttributeMetadataCache.RelationalTypeMetadata[] relationalTypes =
            {
                new(componentType.AssemblyQualifiedName, fields),
            };

            cache._relationalTypeMetadata = relationalTypes;
            cache.ForceRebuildForTests();
            return cache;
        }

        // No reflection needed: tests provide a cache instance to the assigner via DI

        private sealed class RecordingAssigner : IRelationalComponentAssigner
        {
            public int CallCount { get; private set; }

            public Component LastComponent { get; private set; }

            public int HierarchyCallCount { get; private set; }

            public bool HasRelationalAssignments(Type componentType)
            {
                return true;
            }

            public void Assign(Component component)
            {
                LastComponent = component;
                CallCount++;
                component?.AssignRelationalComponents();
            }

            public void Assign(IEnumerable<Component> components)
            {
                if (components == null)
                {
                    return;
                }

                foreach (Component component in components)
                {
                    Assign(component);
                }
            }

            public void AssignHierarchy(GameObject root, bool includeInactiveChildren = true)
            {
                if (root == null)
                {
                    return;
                }

                HierarchyCallCount++;
                Component[] components = root.GetComponentsInChildren<Component>(
                    includeInactiveChildren
                );
                foreach (Component component in components)
                {
                    Assign(component);
                }
            }
        }

        private sealed class VContainerRelationalTester : MonoBehaviour
        {
            [ParentComponent(OnlyAncestors = true)]
            public Rigidbody parentBody;

            [ChildComponent(OnlyDescendants = true)]
            public CapsuleCollider childCollider;
        }

        private sealed class AttributeIncludeInactiveTester : MonoBehaviour
        {
            [ParentComponent(OnlyAncestors = true)]
            public Rigidbody parentBody;

            [ChildComponent(OnlyDescendants = true, IncludeInactive = false)]
            public CapsuleCollider childCollider;
        }
    }
}
#endif
