// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [AttributeUsage(
        AttributeTargets.Class
            | AttributeTargets.Field
            | AttributeTargets.Method
            | AttributeTargets.Property
    )]
    public sealed class RuntimeMarkerAttribute : Attribute { }

    [RuntimeMarker]
    public sealed class RuntimeMarkerTarget
    {
        [RuntimeMarker]
        public int markedField;

        [RuntimeMarker]
        public int MarkedProperty { get; set; }

        [RuntimeMarker]
        public void MarkedMethod() { }

        public void Overload(int x) { }

        public void Overload(string s) { }
    }

    public sealed class RuntimeScriptableObjectTarget : ScriptableObject { }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ReflectionHelpersMemberLookupTests : CommonTestBase
    {
        [Test]
        public void GetTypesWithAttributeRuntimeMarkerIncludesType()
        {
            IEnumerable<Type> types =
                ReflectionHelpers.GetTypesWithAttribute<RuntimeMarkerAttribute>(
                    includeAbstract: false
                );
            Assert.IsTrue(
                types.Any(t => t == typeof(RuntimeMarkerTarget)),
                "Expected RuntimeMarkerTarget to be discovered."
            );
        }

        [Test]
        public void GetFieldsWithAttributeWithinReturnsField()
        {
            IEnumerable<FieldInfo> fields =
                ReflectionHelpers.GetFieldsWithAttribute<RuntimeMarkerAttribute>(
                    typeof(RuntimeMarkerTarget)
                );
            FieldInfo fi = fields.FirstOrDefault(f =>
                f.Name == nameof(RuntimeMarkerTarget.markedField)
            );
            Assert.IsTrue(fi != null, "Expected MarkedField to be discovered.");
        }

        [Test]
        public void GetMethodsWithAttributeWithinReturnsMethod()
        {
            IEnumerable<MethodInfo> methods =
                ReflectionHelpers.GetMethodsWithAttribute<RuntimeMarkerAttribute>(
                    typeof(RuntimeMarkerTarget)
                );
            MethodInfo mi = methods.FirstOrDefault(m =>
                m.Name == nameof(RuntimeMarkerTarget.MarkedMethod)
            );
            Assert.IsTrue(mi != null, "Expected MarkedMethod to be discovered.");
        }

        [Test]
        public void GetPropertiesWithAttributeWithinReturnsProperty()
        {
            IEnumerable<PropertyInfo> props =
                ReflectionHelpers.GetPropertiesWithAttribute<RuntimeMarkerAttribute>(
                    typeof(RuntimeMarkerTarget)
                );
            PropertyInfo pi = props.FirstOrDefault(p =>
                p.Name == nameof(RuntimeMarkerTarget.MarkedProperty)
            );
            Assert.IsTrue(pi != null, "Expected MarkedProperty to be discovered.");
        }

        [Test]
        public void TryGetFieldFindsFieldWithCache()
        {
            bool ok = ReflectionHelpers.TryGetField(
                typeof(RuntimeMarkerTarget),
                nameof(RuntimeMarkerTarget.markedField),
                out FieldInfo fi
            );
            Assert.IsTrue(ok, "TryGetField should succeed.");
            Assert.IsTrue(fi != null);
            bool ok2 = ReflectionHelpers.TryGetField(
                typeof(RuntimeMarkerTarget),
                nameof(RuntimeMarkerTarget.markedField),
                out FieldInfo fi2
            );
            Assert.IsTrue(ok2);
            Assert.AreSame(fi, fi2, "Expected cached FieldInfo instance.");
        }

        [Test]
        public void TryGetPropertyFindsPropertyWithCache()
        {
            bool ok = ReflectionHelpers.TryGetProperty(
                typeof(RuntimeMarkerTarget),
                nameof(RuntimeMarkerTarget.MarkedProperty),
                out PropertyInfo pi
            );
            Assert.IsTrue(ok, "TryGetProperty should succeed.");
            Assert.IsTrue(pi != null);
            bool ok2 = ReflectionHelpers.TryGetProperty(
                typeof(RuntimeMarkerTarget),
                nameof(RuntimeMarkerTarget.MarkedProperty),
                out PropertyInfo pi2
            );
            Assert.IsTrue(ok2);
            Assert.AreSame(pi, pi2, "Expected cached PropertyInfo instance.");
        }

        [Test]
        public void TryGetMethodFindsOverloadByParams()
        {
            bool okInt = ReflectionHelpers.TryGetMethod(
                typeof(RuntimeMarkerTarget),
                nameof(RuntimeMarkerTarget.Overload),
                out MethodInfo mInt,
                new[] { typeof(int) }
            );
            bool okStr = ReflectionHelpers.TryGetMethod(
                typeof(RuntimeMarkerTarget),
                nameof(RuntimeMarkerTarget.Overload),
                out MethodInfo mStr,
                new[] { typeof(string) }
            );
            Assert.IsTrue(okInt && okStr, "Expected both overloads to resolve.");
            Assert.AreNotSame(mInt, mStr, "Expected different overloads.");
        }

        [Test]
        public void GetComponentAndScriptableTypesIncludeTargets()
        {
            IEnumerable<Type> components = ReflectionHelpers.GetComponentTypes();
            bool hasTester = components.Any(t => t == typeof(PrewarmTesterComponent));
            Assert.IsTrue(hasTester, "Expected PrewarmTesterComponent among component types.");

            IEnumerable<Type> sos = ReflectionHelpers.GetScriptableObjectTypes();
            bool hasSO = sos.Any(t => t == typeof(RuntimeScriptableObjectTarget));
            Assert.IsTrue(
                hasSO,
                "Expected RuntimeScriptableObjectTarget among scriptable object types."
            );
        }

        [Test]
        public void GetTypesWithAttributeNonGenericFindsRuntimeMarker()
        {
            IEnumerable<Type> types = ReflectionHelpers.GetTypesWithAttribute(
                typeof(RuntimeMarkerAttribute)
            );
            Assert.IsTrue(
                types.Any(t => t == typeof(RuntimeMarkerTarget)),
                "Expected RuntimeMarkerTarget discovered via non-generic attribute query."
            );
        }

        [Test]
        public void TryGetFieldPropertyMethodReturnFalseForMissing()
        {
            Assert.IsFalse(
                ReflectionHelpers.TryGetField(typeof(RuntimeMarkerTarget), "Nope", out _)
            );
            Assert.IsFalse(
                ReflectionHelpers.TryGetProperty(typeof(RuntimeMarkerTarget), "Nope", out _)
            );
            Assert.IsFalse(
                ReflectionHelpers.TryGetMethod(typeof(RuntimeMarkerTarget), "Nope", out _)
            );
        }

        [Test]
        public void TryGetIndexerPropertyFindsGenericDictionaryIndexer()
        {
            // Dictionary<TKey, TValue> implements both IDictionary<TKey, TValue> and IDictionary
            // which both have an indexer named "Item" - this causes AmbiguousMatchException
            // when using GetProperty("Item", BindingFlags) without specifying types.
            Type dictType = typeof(Dictionary<string, int>);

            bool found = ReflectionHelpers.TryGetIndexerProperty(
                dictType,
                typeof(int),
                new[] { typeof(string) },
                out PropertyInfo indexer
            );

            Assert.IsTrue(found, "Should find the generic Dictionary indexer.");
            Assert.IsTrue(indexer != null, "Indexer should not be null.");
            Assert.AreEqual(typeof(int), indexer.PropertyType, "Return type should be int.");
            Assert.AreEqual("Item", indexer.Name, "Indexer should be named 'Item'.");

            ParameterInfo[] parameters = indexer.GetIndexParameters();
            Assert.AreEqual(1, parameters.Length, "Should have exactly one index parameter.");
            Assert.AreEqual(
                typeof(string),
                parameters[0].ParameterType,
                "Index parameter should be string."
            );
        }

        [Test]
        public void TryGetIndexerPropertyReturnsFalseForNonIndexerType()
        {
            // RuntimeMarkerTarget doesn't have an indexer
            bool found = ReflectionHelpers.TryGetIndexerProperty(
                typeof(RuntimeMarkerTarget),
                typeof(int),
                new[] { typeof(int) },
                out PropertyInfo indexer
            );

            Assert.IsFalse(found, "Should not find indexer on non-indexed type.");
            Assert.IsTrue(indexer == null, "Indexer should be null.");
        }

        [Test]
        public void TryGetIndexerPropertyCachesResults()
        {
            Type dictType = typeof(Dictionary<string, float>);

            // First call
            bool found1 = ReflectionHelpers.TryGetIndexerProperty(
                dictType,
                typeof(float),
                new[] { typeof(string) },
                out PropertyInfo indexer1
            );

            // Second call should hit cache
            bool found2 = ReflectionHelpers.TryGetIndexerProperty(
                dictType,
                typeof(float),
                new[] { typeof(string) },
                out PropertyInfo indexer2
            );

            Assert.IsTrue(found1, "First call should find indexer.");
            Assert.IsTrue(found2, "Second call should also find indexer.");
            Assert.AreEqual(indexer1, indexer2, "Both calls should return the same PropertyInfo.");
        }

        [Test]
        public void TryGetIndexerPropertyWithNullParametersReturnsFalse()
        {
            Assert.IsFalse(
                ReflectionHelpers.TryGetIndexerProperty(
                    null,
                    typeof(int),
                    new[] { typeof(string) },
                    out _
                ),
                "Should return false for null type."
            );

            Assert.IsFalse(
                ReflectionHelpers.TryGetIndexerProperty(
                    typeof(Dictionary<string, int>),
                    null,
                    new[] { typeof(string) },
                    out _
                ),
                "Should return false for null return type."
            );

            Assert.IsFalse(
                ReflectionHelpers.TryGetIndexerProperty(
                    typeof(Dictionary<string, int>),
                    typeof(int),
                    null,
                    out _
                ),
                "Should return false for null parameter types."
            );
        }

        [TestCase(
            typeof(string),
            typeof(int),
            typeof(string),
            typeof(string),
            false,
            TestName = "WrongReturnType"
        )]
        [TestCase(
            typeof(string),
            typeof(int),
            typeof(int),
            typeof(int),
            false,
            TestName = "WrongParameterType"
        )]
        [TestCase(
            typeof(string),
            typeof(int),
            typeof(int),
            typeof(string),
            true,
            TestName = "CorrectTypes"
        )]
        [TestCase(
            typeof(int),
            typeof(float),
            typeof(float),
            typeof(int),
            true,
            TestName = "CorrectTypesIntKeyFloatValue"
        )]
        [TestCase(
            typeof(string),
            typeof(int),
            typeof(object),
            typeof(object),
            false,
            TestName = "ObjectTypesNotFoundExplicitInterfaceImplementation"
        )]
        public void TryGetIndexerPropertyValidatesTypesCorrectly(
            Type keyType,
            Type valueType,
            Type requestedReturnType,
            Type requestedParamType,
            bool expectedFound
        )
        {
            Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);

            bool found = ReflectionHelpers.TryGetIndexerProperty(
                dictionaryType,
                requestedReturnType,
                new[] { requestedParamType },
                out PropertyInfo indexer
            );

            // Comprehensive diagnostic output for debugging
            string diagnosticInfo =
                $"\nDictionary type: Dictionary<{keyType.Name}, {valueType.Name}>"
                + $"\nRequested return type: {requestedReturnType.Name}"
                + $"\nRequested parameter type: {requestedParamType.Name}"
                + $"\nActual value type (expected return): {valueType.Name}"
                + $"\nActual key type (expected param): {keyType.Name}"
                + $"\nFound: {found}, Expected: {expectedFound}";

            if (indexer != null)
            {
                ParameterInfo[] indexParams = indexer.GetIndexParameters();
                diagnosticInfo +=
                    $"\nFound indexer return type: {indexer.PropertyType.Name}"
                    + $"\nFound indexer param type: {indexParams[0].ParameterType.Name}"
                    + $"\nIndexer declaring type: {indexer.DeclaringType?.Name ?? "null"}"
                    + $"\nIndexer name: {indexer.Name}";
            }
            else
            {
                // List all available public indexers for debugging
                PropertyInfo[] allProps = dictionaryType.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                );
                string availableIndexers = string.Join(
                    ", ",
                    allProps
                        .Where(p => p.GetIndexParameters().Length > 0)
                        .Select(p =>
                        {
                            ParameterInfo[] parms = p.GetIndexParameters();
                            return $"{p.PropertyType.Name}[{string.Join(",", parms.Select(ip => ip.ParameterType.Name))}]";
                        })
                );
                diagnosticInfo +=
                    $"\nAvailable public indexers: {(string.IsNullOrEmpty(availableIndexers) ? "none" : availableIndexers)}";
            }

            Assert.AreEqual(expectedFound, found, $"Type matching mismatch.{diagnosticInfo}");

            if (expectedFound)
            {
                Assert.IsTrue(
                    indexer != null,
                    $"Indexer should not be null when found is true.{diagnosticInfo}"
                );
                Assert.AreEqual(
                    requestedReturnType,
                    indexer.PropertyType,
                    $"Return type should match requested type.{diagnosticInfo}"
                );
                Assert.AreEqual(
                    requestedParamType,
                    indexer.GetIndexParameters()[0].ParameterType,
                    $"Parameter type should match requested type.{diagnosticInfo}"
                );
            }
            else
            {
                Assert.IsTrue(
                    indexer == null,
                    $"Indexer should be null when not found.{diagnosticInfo}"
                );
            }
        }

        /// <summary>
        /// The returned array's element type must be the requested one, not the source list's.
        /// </summary>
        /// <remarks>
        /// A caller assigns the result straight into a typed field, so a <c>Component[]</c> where a
        /// <c>BoxCollider[]</c> was asked for fails at the assignment rather than here.
        /// </remarks>
        [Test]
        public void CreateTypedArrayReturnsTheRequestedElementType()
        {
            GameObject host = Track(new GameObject("TypedArrayHost"));
            List<Component> source = new()
            {
                host.AddComponent<BoxCollider>(),
                host.AddComponent<BoxCollider>(),
            };

            Array built = ReflectionHelpers.CreateTypedArray(typeof(BoxCollider), source, 2);

            Assert.AreEqual(typeof(BoxCollider[]), built.GetType());
            Assert.AreEqual(2, built.Length);
            Assert.AreSame(source[0], built.GetValue(0));
            Assert.AreSame(source[1], built.GetValue(1));
        }

        [Test]
        [TestCase(0, 0, TestName = "CreateTypedArrayCountZero")]
        [TestCase(1, 1, TestName = "CreateTypedArrayCountPartial")]
        [TestCase(2, 2, TestName = "CreateTypedArrayCountExact")]
        [TestCase(9, 2, TestName = "CreateTypedArrayCountAboveLength")]
        [TestCase(-1, 0, TestName = "CreateTypedArrayCountNegative")]
        public void CreateTypedArrayClampsCount(int requested, int expected)
        {
            GameObject host = Track(new GameObject("TypedArrayClampHost"));
            List<Component> source = new()
            {
                host.AddComponent<BoxCollider>(),
                host.AddComponent<BoxCollider>(),
            };

            Array built = ReflectionHelpers.CreateTypedArray(
                typeof(BoxCollider),
                source,
                requested
            );

            Assert.AreEqual(expected, built.Length);
        }

        /// <summary>
        /// A degenerate argument must produce an empty array rather than throw.
        /// </summary>
        [Test]
        public void CreateTypedArrayHandlesNullArguments()
        {
            Array fromNullList = ReflectionHelpers.CreateTypedArray<Component>(
                typeof(BoxCollider),
                null,
                4
            );
            Assert.AreEqual(typeof(BoxCollider[]), fromNullList.GetType());
            Assert.AreEqual(0, fromNullList.Length);

            Array fromNullType = ReflectionHelpers.CreateTypedArray(null, new List<Component>(), 0);
            Assert.AreEqual(0, fromNullType.Length);
        }

        /// <summary>
        /// An item the requested element type cannot hold becomes null, never an exception.
        /// </summary>
        [Test]
        public void CreateTypedArrayWritesNullForAMismatchedItem()
        {
            GameObject host = Track(new GameObject("TypedArrayMismatchHost"));
            List<Component> source = new() { host.AddComponent<BoxCollider>(), host.transform };

            Array built = ReflectionHelpers.CreateTypedArray(typeof(BoxCollider), source, 2);

            Assert.AreEqual(typeof(BoxCollider[]), built.GetType());
            Assert.AreSame(source[0], built.GetValue(0));
            Assert.IsTrue(built.GetValue(1) == null);
        }

        /// <summary>
        /// An interface element type must close the builder just as a class does.
        /// </summary>
        [Test]
        public void CreateTypedArraySupportsAnInterfaceElementType()
        {
            GameObject host = Track(new GameObject("TypedArrayInterfaceHost"));
            List<Component> source = new() { host.AddComponent<TestInterfaceComponent>() };

            Array built = ReflectionHelpers.CreateTypedArray(typeof(ITestInterface), source, 1);

            Assert.AreEqual(typeof(ITestInterface[]), built.GetType());
            Assert.AreSame(source[0], built.GetValue(0));
        }
    }
}
