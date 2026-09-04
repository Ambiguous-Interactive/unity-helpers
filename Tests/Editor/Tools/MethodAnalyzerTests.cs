// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer;

    /// <summary>
    /// Tests for the MethodAnalyzer to ensure it correctly identifies issues
    /// and avoids false positives.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class MethodAnalyzerTests
    {
        private MethodAnalyzer _analyzer;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _analyzer = new MethodAnalyzer();
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "MethodAnalyzerTests_" + Path.GetRandomFileName()
            );
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        private void WriteTestFile(string filename, string content)
        {
            File.WriteAllText(Path.Combine(_tempDir, filename), content);
        }

        private void AnalyzeTestFiles()
        {
            _analyzer.Analyze(_tempDir, new[] { _tempDir });
        }

        [Test]
        public void GenericTypeResolutionDoesNotFlagMatchingOverrides()
        {
            WriteTestFile(
                "GenericBase.cs",
                @"
namespace TestNs
{
    public abstract class GenericBase<TValue>
    {
        protected abstract TValue GetValue();
        protected abstract void SetValue(TValue value);
    }

    public class ConcreteInt : GenericBase<int>
    {
        protected override int GetValue() => 0;
        protected override void SetValue(int value) { }
    }

    public class ConcreteString : GenericBase<string>
    {
        protected override string GetValue() => string.Empty;
        protected override void SetValue(string value) { }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> returnTypeMismatches = issues
                .Where(i => i.IssueType == "ReturnTypeMismatch")
                .ToList();

            Assert.That(
                returnTypeMismatches.Count,
                Is.EqualTo(0),
                $"Should not flag return type mismatch for correctly resolved generics. Found: {string.Join(", ", returnTypeMismatches.Select(i => $"{i.ClassName}.{i.MethodName}"))}"
            );
        }

        [Test]
        public void GenericTypeResolutionWithMultipleTypeParametersWorks()
        {
            WriteTestFile(
                "MultiGenericBase.cs",
                @"
namespace TestNs
{
    public abstract class DictionaryBase<TKey, TValue>
    {
        protected abstract TValue GetValue(TKey key);
        protected abstract TKey GetKey(int index);
    }

    public class StringIntDict : DictionaryBase<string, int>
    {
        protected override int GetValue(string key) => 0;
        protected override string GetKey(int index) => string.Empty;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> returnTypeMismatches = issues
                .Where(i => i.IssueType == "ReturnTypeMismatch")
                .ToList();

            Assert.That(
                returnTypeMismatches.Count,
                Is.EqualTo(0),
                $"Should resolve multiple generic type parameters. Found: {string.Join(", ", returnTypeMismatches.Select(i => $"{i.ClassName}.{i.MethodName}"))}"
            );
        }

        [Test]
        public void NewExpressionsAreNotFlaggedAsMethods()
        {
            WriteTestFile(
                "NewExpressions.cs",
                @"
namespace TestNs
{
    using System.Collections.Generic;

    public class TestClass
    {
        private List<int> _list = new List<int>();

        public void TestMethod()
        {
            var a = new Vector3(1, 2, 3);
            var b = new Bounds(Vector3.zero, Vector3.one);
            var arr = new[] { new Vector3(0, 0, 0), new Vector3(1, 1, 1) };
        }
    }

    public struct Vector3 { public Vector3(float x, float y, float z) { } public static Vector3 zero; public static Vector3 one; }
    public struct Bounds { public Bounds(Vector3 center, Vector3 size) { } }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            // Should not have any issues flagging Vector3 or Bounds as methods
            List<AnalyzerIssue> falsePositives = issues
                .Where(i => i.MethodName == "Vector3" || i.MethodName == "Bounds")
                .ToList();

            Assert.That(
                falsePositives.Count,
                Is.EqualTo(0),
                $"Should not flag new expressions as methods. Found: {string.Join(", ", falsePositives.Select(i => $"{i.ClassName}.{i.MethodName}: {i.IssueType}"))}"
            );
        }

        [Test]
        public void CommentsDoNotCreateFalsePositives()
        {
            WriteTestFile(
                "Comments.cs",
                @"
namespace TestNs
{
    /// <summary>
    /// This method DoSomething(int value) is documented here.
    /// </summary>
    public class TestClass
    {
        // Single line comment: void FakeMethod(int x)
        /* Multi-line comment:
           void AnotherFakeMethod(string s)
           protected override int GetValue() */
        public void RealMethod() { }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            // Should not flag methods found in comments
            List<AnalyzerIssue> commentFalsePositives = issues
                .Where(i =>
                    i.MethodName == "FakeMethod"
                    || i.MethodName == "AnotherFakeMethod"
                    || i.MethodName == "DoSomething"
                )
                .ToList();

            Assert.That(
                commentFalsePositives.Count,
                Is.EqualTo(0),
                $"Should not flag methods in comments. Found: {string.Join(", ", commentFalsePositives.Select(i => i.MethodName))}"
            );
        }

        [Test]
        public void InterfaceImplementationIsNotFlaggedAsMethodHiding()
        {
            WriteTestFile(
                "InterfaceImpl.cs",
                @"
namespace TestNs
{
    public interface ILogger
    {
        void Log(string message);
        void OnInitialized();
    }

    public class ConsoleLogger : ILogger
    {
        public void Log(string message) { }
        public void OnInitialized() { }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            // Should not flag interface implementations
            List<AnalyzerIssue> hidingIssues = issues
                .Where(i =>
                    i.IssueType == "HidingNonVirtualMethod" && i.ClassName == "ConsoleLogger"
                )
                .ToList();

            Assert.That(
                hidingIssues.Count,
                Is.EqualTo(0),
                $"Should not flag interface implementations as hiding. Found: {string.Join(", ", hidingIssues.Select(i => i.MethodName))}"
            );
        }

        [Test]
        public void NestedInterfaceImplementationIsDetected()
        {
            WriteTestFile(
                "NestedInterface.cs",
                @"
namespace TestNs
{
    public class OuterClass<T>
    {
        public interface INestedInterface
        {
            void DoWork();
        }
    }

    public class Implementor : OuterClass<int>.INestedInterface
    {
        public void DoWork() { }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            // Should detect that Implementor implements a nested interface, not inherits from OuterClass
            List<AnalyzerIssue> hidingIssues = issues
                .Where(i => i.IssueType == "HidingNonVirtualMethod" && i.ClassName == "Implementor")
                .ToList();

            Assert.That(
                hidingIssues.Count,
                Is.EqualTo(0),
                $"Should recognize nested interface implementation. Found: {string.Join(", ", hidingIssues.Select(i => i.MethodName))}"
            );
        }

        [Test]
        public void ActualReturnTypeMismatchIsFlagged()
        {
            WriteTestFile(
                "ActualMismatch.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual int GetValue() => 0;
    }

    public class DerivedClass : BaseClass
    {
        // This is a real mismatch - can't change return type on override
        public override string GetValue() => string.Empty;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> returnTypeMismatches = issues
                .Where(i =>
                    i.IssueType == "ReturnTypeMismatch"
                    && i.ClassName == "DerivedClass"
                    && i.MethodName == "GetValue"
                )
                .ToList();

            Assert.That(
                returnTypeMismatches.Count,
                Is.EqualTo(1),
                "Should flag actual return type mismatch (int vs string)"
            );
        }

        [Test]
        public void PrivateMethodShadowingInBaseAndDerivedIsFlagged()
        {
            WriteTestFile(
                "PrivateShadowing.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        private void HelperMethod() { }
    }

    public class DerivedClass : BaseClass
    {
        private void HelperMethod() { }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> shadowingIssues = issues
                .Where(i =>
                    i.IssueType == "PrivateMethodShadowing"
                    && i.ClassName == "DerivedClass"
                    && i.MethodName == "HelperMethod"
                )
                .ToList();

            Assert.That(
                shadowingIssues.Count,
                Is.EqualTo(1),
                "Should flag private method shadowing between base and derived"
            );
        }

        [Test]
        public void StringLiteralsDoNotCreateFalsePositives()
        {
            WriteTestFile(
                "StringLiterals.cs",
                @"
namespace TestNs
{
    public class TestClass
    {
        private string _message = ""void FakeMethod(int x) is not a method"";
        private string _verbatim = @""protected override int GetValue() is in a string"";

        public void RealMethod()
        {
            var s = ""new Vector3(1, 2, 3)"";
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            // Should not flag content from string literals
            List<AnalyzerIssue> stringFalsePositives = issues
                .Where(i => i.MethodName == "FakeMethod" || i.MethodName == "GetValue")
                .ToList();

            Assert.That(
                stringFalsePositives.Count,
                Is.EqualTo(0),
                $"Should not flag methods in string literals. Found: {string.Join(", ", stringFalsePositives.Select(i => i.MethodName))}"
            );
        }

        [Test]
        public void ControlFlowKeywordsAreNotFlaggedAsMethods()
        {
            WriteTestFile(
                "ControlFlow.cs",
                @"
namespace TestNs
{
    public class TestClass
    {
        public void TestMethod()
        {
            if (true) { }
            while (false) { }
            for (int i = 0; i < 10; i++) { }
            foreach (var x in new int[0]) { }
            switch (0) { default: break; }
            using (var d = new System.IO.MemoryStream()) { }
            lock (this) { }
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            // Should not flag control flow keywords as methods
            string[] keywords = { "if", "while", "for", "foreach", "switch", "using", "lock" };
            List<AnalyzerIssue> keywordFalsePositives = issues
                .Where(i => keywords.Contains(i.MethodName))
                .ToList();

            Assert.That(
                keywordFalsePositives.Count,
                Is.EqualTo(0),
                $"Should not flag control flow keywords. Found: {string.Join(", ", keywordFalsePositives.Select(i => i.MethodName))}"
            );
        }

        [Test]
        public void GenericTypeParameterPassThroughIsResolved()
        {
            // Test case where derived class passes its own type parameter to base
            WriteTestFile(
                "GenericPassThrough.cs",
                @"
namespace TestNs
{
    public abstract class BaseCache<TValueCache>
    {
        protected abstract TValueCache GetValue(int index);
    }

    // TValue is passed directly as TValueCache
    public class DirectCache<TValue> : BaseCache<TValue>
    {
        protected override TValue GetValue(int index) => default;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> returnTypeMismatches = issues
                .Where(i => i.IssueType == "ReturnTypeMismatch" && i.ClassName == "DirectCache")
                .ToList();

            Assert.That(
                returnTypeMismatches.Count,
                Is.EqualTo(0),
                $"Should recognize type parameter pass-through. Found: {string.Join(", ", returnTypeMismatches.Select(i => $"{i.MethodName}: base={i.BaseMethodSignature}, derived={i.DerivedMethodSignature}"))}"
            );
        }

        [Test]
        public void SuppressAnalyzerAttributeOnClassSkipsAllIssues()
        {
            WriteTestFile(
                "SuppressedClass.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual int GetValue() => 0;
    }

    [SuppressAnalyzer(""Testing analyzer suppression"")]
    public class SuppressedClass : BaseClass
    {
        // This would normally be flagged as return type mismatch
        public override string GetValue() => string.Empty;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> suppressedClassIssues = issues
                .Where(i => i.ClassName == "SuppressedClass")
                .ToList();

            Assert.That(
                suppressedClassIssues.Count,
                Is.EqualTo(0),
                $"Should skip issues for classes with [SuppressAnalyzer]. Found: {string.Join(", ", suppressedClassIssues.Select(i => $"{i.MethodName}: {i.IssueType}"))}"
            );
        }

        [Test]
        public void SuppressAnalyzerAttributeOnMethodSkipsSpecificMethod()
        {
            WriteTestFile(
                "SuppressedMethod.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual int GetValue() => 0;
        public virtual int GetOther() => 0;
    }

    public class DerivedClass : BaseClass
    {
        [SuppressAnalyzer]
        public override string GetValue() => string.Empty;

        // This should still be flagged
        public override string GetOther() => string.Empty;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> allDerivedIssues = issues
                .Where(i => i.ClassName == "DerivedClass")
                .ToList();

            // GetValue should be suppressed
            List<AnalyzerIssue> getValueIssues = allDerivedIssues
                .Where(i => i.MethodName == "GetValue")
                .ToList();

            Assert.That(
                getValueIssues.Count,
                Is.EqualTo(0),
                $"Should skip issues for methods with [SuppressAnalyzer]. Found: {string.Join(", ", getValueIssues.Select(i => i.IssueType))}. All DerivedClass issues: {string.Join("; ", allDerivedIssues.Select(i => $"{i.MethodName}:{i.IssueType}"))}"
            );

            // GetOther should still be flagged
            List<AnalyzerIssue> getOtherIssues = allDerivedIssues
                .Where(i => i.MethodName == "GetOther")
                .ToList();

            Assert.That(
                getOtherIssues.Count,
                Is.EqualTo(1),
                $"Should still flag methods without [SuppressAnalyzer]. All DerivedClass issues: {string.Join("; ", allDerivedIssues.Select(i => $"{i.MethodName}:{i.IssueType}"))}"
            );
        }

        [Test]
        public void SuppressAnalyzerAttributeWithAttributeSuffixWorks()
        {
            WriteTestFile(
                "SuppressedWithSuffix.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual int GetValue() => 0;
    }

    [SuppressAnalyzerAttribute(""With Attribute suffix"")]
    public class SuppressedClass : BaseClass
    {
        public override string GetValue() => string.Empty;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> suppressedClassIssues = issues
                .Where(i => i.ClassName == "SuppressedClass")
                .ToList();

            Assert.That(
                suppressedClassIssues.Count,
                Is.EqualTo(0),
                $"Should recognize [SuppressAnalyzerAttribute] variant. Found: {string.Join(", ", suppressedClassIssues.Select(i => $"{i.MethodName}: {i.IssueType}"))}"
            );
        }

        [Test]
        public void NewExpressionsInCollectionInitializersAreNotFlaggedAsMethods()
        {
            WriteTestFile(
                "CollectionInitializers.cs",
                @"
namespace TestNs
{
    using System.Collections.Generic;

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public class TestClass
    {
        public void TestMethod()
        {
            // Multiple new expressions after commas - this was causing false positives
            List<Vector3> points = new() { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f) };
            var arr = new Vector3[] { new Vector3(1, 2, 3), new Vector3(4, 5, 6) };
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> falsePositives = issues
                .Where(i => i.MethodName == "Vector3")
                .ToList();

            Assert.That(
                falsePositives.Count,
                Is.EqualTo(0),
                $"Should not flag new expressions in collection initializers. Found: {string.Join(", ", falsePositives.Select(i => $"{i.ClassName}.{i.MethodName}: {i.IssueType}"))}"
            );
        }

        [Test]
        public void NewExpressionsWithNamedParametersAreNotFlaggedAsMethods()
        {
            WriteTestFile(
                "NamedParameters.cs",
                @"
namespace TestNs
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
    }

    public class TestClass
    {
        public void TestMethod()
        {
            // Named parameters with new expressions
            Bounds bounds = new(center: new Vector3(0.5f, 0f, 0f), size: new Vector3(1f, 0.1f, 0.1f));
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> falsePositives = issues
                .Where(i => i.MethodName == "Vector3" || i.MethodName == "Bounds")
                .ToList();

            Assert.That(
                falsePositives.Count,
                Is.EqualTo(0),
                $"Should not flag new expressions with named parameters. Found: {string.Join(", ", falsePositives.Select(i => $"{i.ClassName}.{i.MethodName}: {i.IssueType}"))}"
            );
        }

        [Test]
        public void NewExpressionsInMethodArgumentsAreNotFlaggedAsMethods()
        {
            WriteTestFile(
                "MethodArguments.cs",
                @"
namespace TestNs
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public class TestClass
    {
        public void ProcessVectors(Vector3 a, Vector3 b) { }

        public void TestMethod()
        {
            // New expressions as method arguments
            ProcessVectors(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> falsePositives = issues
                .Where(i => i.MethodName == "Vector3")
                .ToList();

            Assert.That(
                falsePositives.Count,
                Is.EqualTo(0),
                $"Should not flag new expressions in method arguments. Found: {string.Join(", ", falsePositives.Select(i => $"{i.ClassName}.{i.MethodName}: {i.IssueType}"))}"
            );
        }

        [Test]
        public void GenericBaseClassWithThreeTypeParametersResolvesCorrectly()
        {
            // This test case mirrors SerializableSortedDictionary's inheritance pattern
            WriteTestFile(
                "ThreeTypeParams.cs",
                @"
namespace TestNs
{
    public abstract class DictionaryBase<TKey, TValue, TValueCache>
    {
        protected abstract TValue GetValue(TValueCache[] cache, int index);
        protected abstract void SetValue(TValueCache[] cache, int index, TValue value);
    }

    // TValue is passed as TValueCache (third type parameter)
    public class SimpleDictionary<TKey, TValue> : DictionaryBase<TKey, TValue, TValue>
    {
        protected override TValue GetValue(TValue[] cache, int index) => cache[index];
        protected override void SetValue(TValue[] cache, int index, TValue value) { cache[index] = value; }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i => i.IssueType == "SignatureMismatch" && i.ClassName == "SimpleDictionary")
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should resolve type parameter substitution (TValueCache -> TValue). Found: {string.Join(", ", signatureMismatches.Select(i => $"{i.MethodName}: base={i.BaseMethodSignature}, derived={i.DerivedMethodSignature}"))}"
            );
        }

        [Test]
        public void GenericBaseClassWithWhereConstraintResolvesCorrectly()
        {
            WriteTestFile(
                "WhereConstraint.cs",
                @"
namespace TestNs
{
    using System;

    public abstract class ComparableBase<TKey, TValue, TValueCache>
        where TKey : IComparable<TKey>
    {
        protected abstract TValue GetValue(TValueCache[] cache, int index);
    }

    public class ComparableDict<TKey, TValue> : ComparableBase<TKey, TValue, TValue>
        where TKey : IComparable<TKey>
    {
        protected override TValue GetValue(TValue[] cache, int index) => cache[index];
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i => i.IssueType == "SignatureMismatch" && i.ClassName == "ComparableDict")
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should handle where constraints when parsing base class. Found: {string.Join(", ", signatureMismatches.Select(i => $"{i.MethodName}: base={i.BaseMethodSignature}, derived={i.DerivedMethodSignature}"))}"
            );
        }

        [Test]
        public void GenericBaseClassWithNestedGenericTypesResolvesCorrectly()
        {
            WriteTestFile(
                "NestedGenerics.cs",
                @"
namespace TestNs
{
    using System.Collections.Generic;

    public abstract class CollectionBase<TItem, TCollection>
    {
        protected abstract TCollection GetCollection();
    }

    public class ListWrapper<T> : CollectionBase<T, List<T>>
    {
        protected override List<T> GetCollection() => new List<T>();
    }

    public class DictWrapper<TKey, TValue> : CollectionBase<TValue, Dictionary<TKey, TValue>>
    {
        protected override Dictionary<TKey, TValue> GetCollection() => new Dictionary<TKey, TValue>();
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> returnTypeMismatches = issues
                .Where(i =>
                    i.IssueType == "ReturnTypeMismatch"
                    && (i.ClassName == "ListWrapper" || i.ClassName == "DictWrapper")
                )
                .ToList();

            Assert.That(
                returnTypeMismatches.Count,
                Is.EqualTo(0),
                $"Should resolve nested generic types in return type. Found: {string.Join(", ", returnTypeMismatches.Select(i => $"{i.ClassName}.{i.MethodName}: base={i.BaseMethodSignature}, derived={i.DerivedMethodSignature}"))}"
            );
        }

        [Test]
        public void MultipleInterfacesWithGenericBaseClassAreHandledCorrectly()
        {
            WriteTestFile(
                "MultipleInterfaces.cs",
                @"
namespace TestNs
{
    using System;

    public interface IDisposable { void Dispose(); }
    public interface IComparable<T> { int CompareTo(T other); }

    public abstract class BaseWithInterfaces<TKey, TValue, TCache>
        : IDisposable, IComparable<BaseWithInterfaces<TKey, TValue, TCache>>
    {
        protected abstract TValue GetValue(TCache[] cache);
        public abstract void Dispose();
        public abstract int CompareTo(BaseWithInterfaces<TKey, TValue, TCache> other);
    }

    public class ConcreteWithInterfaces<TKey, TValue>
        : BaseWithInterfaces<TKey, TValue, TValue>
    {
        protected override TValue GetValue(TValue[] cache) => cache[0];
        public override void Dispose() { }
        public override int CompareTo(BaseWithInterfaces<TKey, TValue, TValue> other) => 0;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch" && i.ClassName == "ConcreteWithInterfaces"
                )
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should parse base class correctly when interfaces are also implemented. Found: {string.Join(", ", signatureMismatches.Select(i => $"{i.MethodName}: base={i.BaseMethodSignature}, derived={i.DerivedMethodSignature}"))}"
            );
        }

        [Test]
        public void PrivateMethodShadowingIsNotReportedForConstructorCalls()
        {
            WriteTestFile(
                "NoFalsePrivateShadowing.cs",
                @"
namespace TestNs
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public class BaseClass
    {
        private void Helper() { }
    }

    public class DerivedClass : BaseClass
    {
        public void TestMethod()
        {
            // These constructor calls should NOT be flagged as private method shadowing
            var a = new Vector3(1, 2, 3);
            var b = new Vector3(4, 5, 6);
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> shadowingIssues = issues
                .Where(i => i.IssueType == "PrivateMethodShadowing" && i.MethodName == "Vector3")
                .ToList();

            Assert.That(
                shadowingIssues.Count,
                Is.EqualTo(0),
                $"Should not report private method shadowing for constructor calls. Found: {string.Join(", ", shadowingIssues.Select(i => $"{i.ClassName}.{i.MethodName}"))}"
            );
        }

        [Test]
        public void ArrayInitializerNewExpressionsAfterCommaAreNotFlaggedAsMethods()
        {
            WriteTestFile(
                "ArrayInitializer.cs",
                @"
namespace TestNs
{
    public struct Bounds
    {
        public Bounds(float x, float y) { }
    }

    public class TestClass
    {
        // This specific pattern was causing false positives: new(new Bounds(...), new Bounds(...))
        private Bounds[] _bounds = new Bounds[] { new Bounds(1, 2), new Bounds(3, 4) };

        public void TestMethod()
        {
            var arr = new[] { new Bounds(5, 6), new Bounds(7, 8) };
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> boundsIssues = issues.Where(i => i.MethodName == "Bounds").ToList();

            Assert.That(
                boundsIssues.Count,
                Is.EqualTo(0),
                $"Should not flag new expressions in array initializers. Found: {string.Join(", ", boundsIssues.Select(i => $"{i.ClassName}.{i.MethodName}: {i.IssueType}"))}"
            );
        }

        [Test]
        public void OverrideMethodInGrandparentClassIsNotFlaggedAsSignatureMismatch()
        {
            // This test reproduces the WanderingProjectile bug
            WriteTestFile(
                "GrandparentOverride.cs",
                @"
namespace TestNs
{
    public class Core { }
    public class Projectile
    {
        protected virtual void ReturnToPool(Core targetToDamage) { }
        protected virtual void ReturnToPool() { }
    }
    public class MissileProjectile : Projectile
    {
        protected override void ReturnToPool() { base.ReturnToPool(); }
    }
    public class WanderingProjectile : MissileProjectile
    {
        protected override void ReturnToPool(Core targetToDamage) { base.ReturnToPool(targetToDamage); }
    }
}
"
            );
            AnalyzeTestFiles();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch" && i.ClassName == "WanderingProjectile"
                )
                .ToList();
            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should find matching virtual method in grandparent class. Found: {string.Join(", ", signatureMismatches.Select(i => i.DerivedMethodSignature))}"
            );
        }

        [Test]
        public void ActualSignatureMismatchInGrandchildIsStillFlagged()
        {
            WriteTestFile(
                "ActualMismatchGrandchild.cs",
                @"
namespace TestNs
{
    public class Base { protected virtual void Process(int value) { } }
    public class Middle : Base { protected override void Process(int value) { } }
    public class Derived : Middle { protected override void Process(string value) { } }
}
"
            );
            AnalyzeTestFiles();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i => i.IssueType == "SignatureMismatch" && i.ClassName == "Derived")
                .ToList();
            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(1),
                "Should flag actual signature mismatch when no matching method exists in chain"
            );
        }

        /// <summary>
        /// Reproduces the AIMovePositionState bug: Enter(object data) is defined in the
        /// great-grandparent class, while the analyzer looked only at the immediate base.
        /// </summary>
        [Test]
        public void OverrideMethodInGreatGrandparentClassIsNotFlaggedAsSignatureMismatch()
        {
            WriteTestFile(
                "GreatGrandparentOverride.cs",
                @"
namespace TestNs
{
    public class EntityState
    {
        public virtual void Enter(object data) { Enter(); }
        public virtual void Enter() { }
    }
    public class E_FollowTarget_Astar_Base : EntityState
    {
        public override void Enter() { base.Enter(); }
    }
    public class AIMovePositionState : E_FollowTarget_Astar_Base
    {
        public override void Enter(object data) { base.Enter(data); }
        public override void Enter() { base.Enter(); }
    }
}
"
            );
            AnalyzeTestFiles();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch" && i.ClassName == "AIMovePositionState"
                )
                .ToList();
            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should find matching virtual method in great-grandparent class. Found: {string.Join(", ", signatureMismatches.Select(i => $"{i.MethodName}: {i.DerivedMethodSignature}"))}"
            );
        }

        [Test]
        public void OverrideInDeepInheritanceChainWithIntermediateOverridesIsNotFlagged()
        {
            WriteTestFile(
                "DeepInheritance.cs",
                @"
namespace TestNs
{
    public class Level0
    {
        public virtual void MethodA() { }
        public virtual void MethodB(int x) { }
    }
    public class Level1 : Level0
    {
        public override void MethodA() { }
        // Does not override MethodB
    }
    public class Level2 : Level1
    {
        // Does not override either
    }
    public class Level3 : Level2
    {
        // Overrides MethodB from Level0
        public override void MethodB(int x) { base.MethodB(x); }
    }
}
"
            );
            AnalyzeTestFiles();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch"
                    && i.ClassName == "Level3"
                    && i.MethodName == "MethodB"
                )
                .ToList();
            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should traverse entire chain to find MethodB in Level0. Found: {string.Join(", ", signatureMismatches.Select(i => i.DerivedMethodSignature))}"
            );
        }

        [Test]
        public void MultipleOverloadsInDifferentAncestorLevelsAreHandledCorrectly()
        {
            WriteTestFile(
                "MultipleOverloadsAncestors.cs",
                @"
namespace TestNs
{
    public class RCDirgeState
    {
        public virtual void ExecuteCommand(MoveParameter param) { }
    }
    public class RCDirgeInteractState : RCDirgeState
    {
        public virtual void ExecuteCommand(InteractParameter param) { }
    }
    public class RCDirgeMoveState : RCDirgeInteractState
    {
        // Overrides the grandparent's method, not the parent's
        public override void ExecuteCommand(MoveParameter param) { base.ExecuteCommand(param); }
    }
    public class MoveParameter { }
    public class InteractParameter { }
}
"
            );
            AnalyzeTestFiles();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i => i.IssueType == "SignatureMismatch" && i.ClassName == "RCDirgeMoveState")
                .ToList();
            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should find matching ExecuteCommand(MoveParameter) in grandparent class. Found: {string.Join(", ", signatureMismatches.Select(i => i.DerivedMethodSignature))}"
            );
        }

        [Test]
        public void OverrideWithDifferentOverloadInImmediateBaseIsNotFlagged()
        {
            WriteTestFile(
                "DifferentOverloadInBase.cs",
                @"
namespace TestNs
{
    public class Projectile
    {
        protected virtual void ReturnToPool(Core targetToDamage) { }
        protected virtual void ReturnToPool() { }
    }
    public class MissileProjectile : Projectile
    {
        // Only overrides parameterless version
        protected override void ReturnToPool() { base.ReturnToPool(); }
    }
    public class WanderingProjectile : MissileProjectile
    {
        // Overrides Core version from Projectile (grandparent)
        protected override void ReturnToPool(Core targetToDamage)
        {
            base.ReturnToPool(targetToDamage);
        }
    }
    public class Core { }
}
"
            );
            AnalyzeTestFiles();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch" && i.ClassName == "WanderingProjectile"
                )
                .ToList();
            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should find ReturnToPool(Core) in Projectile grandparent. Found: {string.Join(", ", signatureMismatches.Select(i => i.DerivedMethodSignature))}"
            );
        }

        [Test]
        public void NewKeywordWithDifferentReturnTypeIsNotFlaggedAsReturnTypeMismatch()
        {
            // Using 'new' to hide a method with different return type is valid
            WriteTestFile(
                "NewKeywordReturnType.cs",
                @"
namespace TestNs
{
    public class EnvironmentalEffectBase
    {
        protected virtual void ApplyEffect(Core core) { }
    }
    public class FirePillarBurst : EnvironmentalEffectBase
    {
        protected new bool ApplyEffect(Core core) { return true; }
    }
    public class Core { }
}
"
            );
            AnalyzeTestFiles();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> returnTypeMismatches = issues
                .Where(i => i.IssueType == "ReturnTypeMismatch" && i.ClassName == "FirePillarBurst")
                .ToList();
            Assert.That(
                returnTypeMismatches.Count,
                Is.EqualTo(0),
                $"Should not flag return type mismatch when using 'new' keyword. Found: {string.Join(", ", returnTypeMismatches.Select(i => $"{i.MethodName}: {i.DerivedMethodSignature}"))}"
            );
        }

        [Test]
        public void NewKeywordWithSameReturnTypeIsNotFlagged()
        {
            WriteTestFile(
                "NewKeywordSameReturnType.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public void DoWork() { }
    }
    public class DerivedClass : BaseClass
    {
        public new void DoWork() { }
    }
}
"
            );
            AnalyzeTestFiles();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> newKeywordIssues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "DoWork")
                .ToList();
            // Should only report HidingNonVirtualMethod, not ReturnTypeMismatch
            List<AnalyzerIssue> returnTypeIssues = newKeywordIssues
                .Where(i => i.IssueType == "ReturnTypeMismatch")
                .ToList();
            Assert.That(
                returnTypeIssues.Count,
                Is.EqualTo(0),
                "Should not flag return type mismatch for 'new' methods with same return type"
            );
        }

        [Test]
        public void OverrideWithDifferentReturnTypeIsStillFlagged()
        {
            // Ensure we still flag actual return type mismatches for override methods
            WriteTestFile(
                "OverrideReturnTypeMismatch.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual int GetValue() => 0;
    }
    public class DerivedClass : BaseClass
    {
        public override string GetValue() => string.Empty;
    }
}
"
            );
            AnalyzeTestFiles();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> returnTypeMismatches = issues
                .Where(i => i.IssueType == "ReturnTypeMismatch" && i.ClassName == "DerivedClass")
                .ToList();
            Assert.That(
                returnTypeMismatches.Count,
                Is.EqualTo(1),
                "Should still flag return type mismatch for 'override' methods"
            );
        }

        /// <summary>
        /// <c>new</c> states an intention to hide a base method, so a signature that matches
        /// nothing in the base hides nothing and is worth reporting.
        /// </summary>
        [Test]
        public void NewKeywordWithDifferentSignatureIsFlaggedAsSignatureMismatch()
        {
            WriteTestFile(
                "NewKeywordDifferentSignature.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual void Process() { }
    }
    public class DerivedClass : BaseClass
    {
        public new int Process(string input) => 0;
    }
}
"
            );
            AnalyzeTestFiles();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> processIssues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "Process")
                .ToList();
            Assert.That(
                processIssues.Count,
                Is.EqualTo(1),
                $"Should flag 'new' method with different signature as SignatureMismatch. Found {processIssues.Count} issues: {string.Join(", ", processIssues.Select(i => i.IssueType))}"
            );
            Assert.That(
                processIssues[0].IssueType,
                Is.EqualTo("SignatureMismatch"),
                $"Issue type should be SignatureMismatch, but was {processIssues[0].IssueType}"
            );
        }

        [Test]
        public void MultipleNewMethodsWithDifferentReturnTypesAreNotFlagged()
        {
            // Real-world pattern from LightningBurst and FirePillarBurst
            WriteTestFile(
                "MultipleNewMethods.cs",
                @"
namespace TestNs
{
    public class EnvironmentalEffectBase
    {
        protected virtual void ApplyEffect(Core core) { }
        protected virtual void PlayEffect() { }
    }
    public class FirePillarBurst : EnvironmentalEffectBase
    {
        protected new bool ApplyEffect(Core core) { return true; }
    }
    public class LightningBurst : EnvironmentalEffectBase
    {
        protected new bool ApplyEffect(Core core) { return false; }
    }
    public class Core { }
}
"
            );
            AnalyzeTestFiles();
            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> returnTypeMismatches = issues
                .Where(i =>
                    i.IssueType == "ReturnTypeMismatch"
                    && (i.ClassName == "FirePillarBurst" || i.ClassName == "LightningBurst")
                )
                .ToList();
            Assert.That(
                returnTypeMismatches.Count,
                Is.EqualTo(0),
                $"Should not flag return type mismatch for any 'new' method. Found: {string.Join(", ", returnTypeMismatches.Select(i => $"{i.ClassName}.{i.MethodName}"))}"
            );
        }

        /// <summary>
        /// Reproduces the Frostbite.StartEffect bug, where
        /// "void StartEffect(GameObject source, int amount)" was flagged as mismatching
        /// "void StartEffect(GameObject source, int amount = 1)".
        /// </summary>
        [Test]
        public void DefaultParameterValueDoesNotCauseSignatureMismatch()
        {
            WriteTestFile(
                "DefaultParams.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual void StartEffect(string source, int amount) { }
    }

    public class DerivedClass : BaseClass
    {
        public override void StartEffect(string source, int amount = 1)
        {
            base.StartEffect(source, amount);
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch"
                    && i.ClassName == "DerivedClass"
                    && i.MethodName == "StartEffect"
                )
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should not flag signature mismatch when only difference is default parameter value. Found: {string.Join(", ", signatureMismatches.Select(i => $"base={i.BaseMethodSignature}, derived={i.DerivedMethodSignature}"))}"
            );
        }

        [Test]
        public void MultipleDefaultParametersDoNotCauseSignatureMismatch()
        {
            WriteTestFile(
                "MultipleDefaultParams.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual void Configure(string name, int value, bool enabled) { }
    }

    public class DerivedClass : BaseClass
    {
        public override void Configure(string name, int value = 10, bool enabled = true)
        {
            base.Configure(name, value, enabled);
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch"
                    && i.ClassName == "DerivedClass"
                    && i.MethodName == "Configure"
                )
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                "Should not flag signature mismatch with multiple default parameters"
            );
        }

        [Test]
        public void DefaultParameterWithStringValueDoesNotCauseSignatureMismatch()
        {
            WriteTestFile(
                "StringDefaultParam.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual void Log(string message) { }
    }

    public class DerivedClass : BaseClass
    {
        public override void Log(string message = ""default message"")
        {
            base.Log(message);
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch"
                    && i.ClassName == "DerivedClass"
                    && i.MethodName == "Log"
                )
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                "Should not flag signature mismatch when default parameter is a string value"
            );
        }

        [Test]
        public void DefaultParameterWithNullableValueDoesNotCauseSignatureMismatch()
        {
            WriteTestFile(
                "NullableDefaultParam.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual void Process(object data, int? count) { }
    }

    public class DerivedClass : BaseClass
    {
        public override void Process(object data, int? count = null)
        {
            base.Process(data, count);
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch"
                    && i.ClassName == "DerivedClass"
                    && i.MethodName == "Process"
                )
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                "Should not flag signature mismatch when default parameter is null"
            );
        }

        /// <summary>
        /// Reproduces the SequenceBehaviourItem.Delay bug, where
        /// "yield return Delay(TimeDelayAfterComplete);" was detected as a method declaration.
        /// </summary>
        [Test]
        public void YieldReturnStatementIsNotDetectedAsMethod()
        {
            WriteTestFile(
                "YieldReturn.cs",
                @"
namespace TestNs
{
    using System.Collections;

    public class BaseClass
    {
        protected static IEnumerator Delay(float delay)
        {
            yield return null;
        }
    }

    public class DerivedClass : BaseClass
    {
        private float TimeDelayAfterComplete = 1.0f;

        public IEnumerator DoSomething()
        {
            yield return Delay(TimeDelayAfterComplete);
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            // Should not detect "Delay" as a method being shadowed when it's just a method call
            List<AnalyzerIssue> delayIssues = issues
                .Where(i =>
                    i.MethodName == "Delay"
                    && (
                        i.IssueType == "PrivateMethodShadowing"
                        || i.IssueType == "SignatureMismatch"
                    )
                )
                .ToList();

            Assert.That(
                delayIssues.Count,
                Is.EqualTo(0),
                $"Should not flag 'yield return Delay(...)' as a method declaration. Found: {string.Join(", ", delayIssues.Select(i => $"{i.ClassName}.{i.MethodName}: {i.IssueType}"))}"
            );
        }

        [Test]
        public void YieldReturnWithTabIsNotDetectedAsMethod()
        {
            WriteTestFile(
                "YieldReturnTab.cs",
                @"
namespace TestNs
{
    using System.Collections;

    public class TestClass
    {
        public IEnumerator Process()
        {
            yield	return	WaitForSomething();
        }

        private IEnumerator WaitForSomething()
        {
            yield return null;
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> falsePositives = issues
                .Where(i => i.MethodName == "WaitForSomething" || i.MethodName == "Process")
                .ToList();

            // Should only find real methods, not yield return statements
            Assert.That(
                falsePositives.Count,
                Is.EqualTo(0),
                $"Should not flag yield return statements as method declarations. Found: {string.Join(", ", falsePositives.Select(i => $"{i.ClassName}.{i.MethodName}: {i.IssueType}"))}"
            );
        }

        [Test]
        public void MultipleYieldReturnStatementsAreNotDetectedAsMethods()
        {
            // Reproduces real-world pattern from SequenceBehaviourItem inheritance
            WriteTestFile(
                "MultipleYieldReturns.cs",
                @"
namespace TestNs
{
    using System.Collections;

    public class SequenceBehaviourItem
    {
        public float TimeDelayAfterComplete = 0f;

        protected virtual IEnumerator InternalInitiate()
        {
            yield return Delay(TimeDelayAfterComplete);
        }

        protected static IEnumerator Delay(float delay)
        {
            if (delay > 0f)
                yield return null;
        }
    }

    public class SequenceBehaviourItemControlToggle : SequenceBehaviourItem
    {
        protected override IEnumerator InternalInitiate()
        {
            // Do something
            yield return Delay(TimeDelayAfterComplete);
        }
    }

    public class SequenceBehaviourItemFadeScreen : SequenceBehaviourItem
    {
        protected override IEnumerator InternalInitiate()
        {
            // Do something else
            yield return Delay(TimeDelayAfterComplete);
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            // Should not flag any "Delay" as private method shadowing
            List<AnalyzerIssue> shadowingIssues = issues
                .Where(i => i.MethodName == "Delay" && i.IssueType == "PrivateMethodShadowing")
                .ToList();

            Assert.That(
                shadowingIssues.Count,
                Is.EqualTo(0),
                $"Should not flag 'yield return Delay(...)' calls in multiple derived classes as method shadowing. Found: {string.Join(", ", shadowingIssues.Select(i => $"{i.ClassName}.{i.MethodName}"))}"
            );
        }

        [Test]
        public void YieldBreakIsNotDetectedAsMethod()
        {
            WriteTestFile(
                "YieldBreak.cs",
                @"
namespace TestNs
{
    using System.Collections;

    public class TestClass
    {
        public IEnumerator Process(bool shouldExit)
        {
            if (shouldExit)
            {
                yield break;
            }
            yield return null;
        }
    }
}
"
            );

            AnalyzeTestFiles();

            // This should not throw or cause any issues
            Assert.That(_analyzer.Issues.Count, Is.EqualTo(0));
        }

        [Test]
        public void RealWorldFrostbitePatternDoesNotCauseFalsePositive()
        {
            // Exact reproduction of the Frostbite issue
            WriteTestFile(
                "EffectBase.cs",
                @"
namespace TestNs
{
    public abstract class EffectBase
    {
        public virtual void StartEffect(object source, int amount) { }
        public virtual void ApplyEffect() { }
        public virtual void RemoveEffect() { }
    }
}
"
            );

            WriteTestFile(
                "Frostbite.cs",
                @"
namespace TestNs
{
    public class Frostbite : EffectBase
    {
        public override void StartEffect(object source, int amount = 1)
        {
            base.StartEffect(source, amount);
        }

        public override void ApplyEffect() { }
        public override void RemoveEffect() { }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> frostbiteIssues = issues
                .Where(i => i.ClassName == "Frostbite")
                .ToList();

            Assert.That(
                frostbiteIssues.Count,
                Is.EqualTo(0),
                $"Real-world Frostbite pattern should not cause false positives. Found: {string.Join(", ", frostbiteIssues.Select(i => $"{i.MethodName}: {i.IssueType}"))}"
            );
        }

        [Test]
        public void RealWorldSequenceBehaviourPatternDoesNotCauseFalsePositive()
        {
            // Exact reproduction of the SequenceBehaviourItem issue
            WriteTestFile(
                "SequenceBase.cs",
                @"
namespace TestNs
{
    using System.Collections;

    public class SequenceBehaviourItem
    {
        public float TimeDelayAfterComplete = 0f;

        protected virtual IEnumerator InternalInitiate()
        {
            yield return Delay(TimeDelayAfterComplete);
        }

        protected static IEnumerator Delay(float delay)
        {
            yield return null;
        }
    }
}
"
            );

            WriteTestFile(
                "SequenceControlToggle.cs",
                @"
namespace TestNs
{
    using System.Collections;

    public class SequenceBehaviourItemControlToggle : SequenceBehaviourItem
    {
        protected override IEnumerator InternalInitiate()
        {
            yield return Delay(TimeDelayAfterComplete);
        }
    }
}
"
            );

            WriteTestFile(
                "SequenceFadeScreen.cs",
                @"
namespace TestNs
{
    using System.Collections;

    public class SequenceBehaviourItemFadeScreen : SequenceBehaviourItem
    {
        protected override IEnumerator InternalInitiate()
        {
            yield return Delay(TimeDelayAfterComplete);
        }
    }
}
"
            );

            WriteTestFile(
                "SequenceSetLockState.cs",
                @"
namespace TestNs
{
    using System.Collections;

    public class SequenceBehaviourItemSetLockState : SequenceBehaviourItem
    {
        protected override IEnumerator InternalInitiate()
        {
            yield return Delay(TimeDelayAfterComplete);
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> delayIssues = issues.Where(i => i.MethodName == "Delay").ToList();

            Assert.That(
                delayIssues.Count,
                Is.EqualTo(0),
                $"Real-world SequenceBehaviourItem pattern should not cause false positives. Found: {string.Join(", ", delayIssues.Select(i => $"{i.ClassName}.{i.MethodName}: {i.IssueType}"))}"
            );
        }

        /// <summary>
        /// Reproduces the AIMovePositionState bug: EntityState defines Enter() and
        /// Enter(object data), E_FollowTarget_Astar_Base overrides only Enter(), and
        /// AIMovePositionState overrides EntityState's Enter(object data). The analyzer flagged
        /// this because it found Enter() in the immediate base but not the overload.
        /// </summary>
        [Test]
        public void OverrideMethodWhenImmediateBaseHasDifferentOverloadIsNotFlagged()
        {
            WriteTestFile(
                "ImmediateBaseDifferentOverload.cs",
                @"
namespace TestNs
{
    public class EntityState
    {
        public virtual void Enter(object data) { Enter(); }
        public virtual void Enter() { }
        public virtual void Exit() { }
    }

    public class E_FollowTarget_Astar_Base : EntityState
    {
        // Only overrides the parameterless version
        public override void Enter() { base.Enter(); }
    }

    public class AIMovePositionState : E_FollowTarget_Astar_Base
    {
        // Overrides the Enter(object data) from EntityState (grandparent)
        public override void Enter(object data) { base.Enter(data); }

        // Also overrides the parameterless Enter from immediate base
        public override void Enter() { base.Enter(); }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch"
                    && i.ClassName == "AIMovePositionState"
                    && i.MethodName == "Enter"
                )
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should find Enter(object data) in grandparent EntityState. Found: {string.Join(", ", signatureMismatches.Select(i => $"base={i.BaseMethodSignature}, derived={i.DerivedMethodSignature}"))}"
            );
        }

        /// <summary>
        /// Reproduces the WanderingProjectile pattern: Projectile has ReturnToPool() and
        /// ReturnToPool(Core), MissileProjectile overrides only ReturnToPool(), and
        /// WanderingProjectile overrides Projectile's ReturnToPool(Core).
        /// </summary>
        [Test]
        public void OverrideMethodWithParameterWhenImmediateBaseHasParameterlessIsNotFlagged()
        {
            WriteTestFile(
                "ParameterOverloadInGrandparent.cs",
                @"
namespace TestNs
{
    public class Core { }

    public class Projectile
    {
        protected virtual void ReturnToPool() { }
        protected virtual void ReturnToPool(Core targetToDamage) { }
    }

    public class MissileProjectile : Projectile
    {
        protected override void ReturnToPool() { base.ReturnToPool(); }
    }

    public class WanderingProjectile : MissileProjectile
    {
        // Override the Core overload from grandparent Projectile
        protected override void ReturnToPool(Core targetToDamage)
        {
            base.ReturnToPool(targetToDamage);
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch" && i.ClassName == "WanderingProjectile"
                )
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should find ReturnToPool(Core) in grandparent Projectile. Found: {string.Join(", ", signatureMismatches.Select(i => $"{i.MethodName}: {i.DerivedMethodSignature}"))}"
            );
        }

        /// <summary>
        /// Reproduces the RCDirgeMoveState pattern: RCDirgeState defines
        /// ExecuteCommand(MoveParameter), RCDirgeInteractState adds
        /// ExecuteCommand(InteractParameter), and RCDirgeMoveState overrides the grandparent's
        /// ExecuteCommand(MoveParameter).
        /// </summary>
        [Test]
        public void OverrideMethodWithDifferentParameterTypeFromDifferentAncestorIsNotFlagged()
        {
            WriteTestFile(
                "DifferentParameterTypeInAncestor.cs",
                @"
namespace TestNs
{
    public class MoveParameter { }
    public class InteractParameter { }

    public class RCDirgeState
    {
        public virtual void ExecuteCommand(MoveParameter param) { }
    }

    public class RCDirgeInteractState : RCDirgeState
    {
        // Adds a different overload with different parameter type
        public virtual void ExecuteCommand(InteractParameter param) { }
    }

    public class RCDirgeMoveState : RCDirgeInteractState
    {
        // Overrides the grandparent's method, not the parent's
        public override void ExecuteCommand(MoveParameter param)
        {
            base.ExecuteCommand(param);
        }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i => i.IssueType == "SignatureMismatch" && i.ClassName == "RCDirgeMoveState")
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should find ExecuteCommand(MoveParameter) in grandparent RCDirgeState. Found: {string.Join(", ", signatureMismatches.Select(i => $"{i.MethodName}: base={i.BaseMethodSignature}, derived={i.DerivedMethodSignature}"))}"
            );
        }

        [Test]
        public void FourLevelDeepInheritanceWithDifferentOverloadsAtEachLevelIsNotFlagged()
        {
            // Tests a complex scenario with many levels and different overloads at each
            WriteTestFile(
                "FourLevelDeepOverloads.cs",
                @"
namespace TestNs
{
    public class Level0
    {
        public virtual void Process() { }
        public virtual void Process(int a) { }
        public virtual void Process(int a, int b) { }
        public virtual void Process(string s) { }
    }

    public class Level1 : Level0
    {
        // Only overrides Process()
        public override void Process() { base.Process(); }
    }

    public class Level2 : Level1
    {
        // Only overrides Process(int)
        public override void Process(int a) { base.Process(a); }
    }

    public class Level3 : Level2
    {
        // Overrides Process(int, int) from Level0 (3 levels up)
        public override void Process(int a, int b) { base.Process(a, b); }

        // Overrides Process(string) from Level0 (3 levels up)
        public override void Process(string s) { base.Process(s); }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i => i.IssueType == "SignatureMismatch" && i.ClassName == "Level3")
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should find all overloads in ancestor chain. Found: {string.Join(", ", signatureMismatches.Select(i => $"{i.MethodName}: {i.DerivedMethodSignature}"))}"
            );
        }

        [Test]
        public void ActualSignatureMismatchIsStillFlaggedWhenImmediateBaseHasDifferentOverload()
        {
            // Ensure we still catch real signature mismatches even when searching ancestors
            WriteTestFile(
                "RealMismatchWithDifferentOverloads.cs",
                @"
namespace TestNs
{
    public class Level0
    {
        public virtual void Process() { }
        public virtual void Process(int a) { }
    }

    public class Level1 : Level0
    {
        public override void Process() { base.Process(); }
    }

    public class Level2 : Level1
    {
        // This is a real mismatch - trying to override Process(string)
        // which doesn't exist anywhere in the chain
        public override void Process(string s) { }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch"
                    && i.ClassName == "Level2"
                    && i.MethodName == "Process"
                )
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(1),
                "Should flag actual signature mismatch when method doesn't exist in any ancestor"
            );
        }

        [Test]
        public void OverrideFromAncestorWithReturnTypeMismatchIsFlagged()
        {
            // When we find a method in an ancestor but return type doesn't match
            WriteTestFile(
                "AncestorReturnTypeMismatch.cs",
                @"
namespace TestNs
{
    public class Level0
    {
        public virtual int GetValue() => 0;
        public virtual int GetValue(int input) => input;
    }

    public class Level1 : Level0
    {
        public override int GetValue() => 1;
    }

    public class Level2 : Level1
    {
        // Tries to override GetValue(int) from Level0 but with wrong return type
        public override string GetValue(int input) => input.ToString();
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> returnTypeMismatches = issues
                .Where(i =>
                    i.IssueType == "ReturnTypeMismatch"
                    && i.ClassName == "Level2"
                    && i.MethodName == "GetValue"
                )
                .ToList();

            Assert.That(
                returnTypeMismatches.Count,
                Is.EqualTo(1),
                "Should flag return type mismatch when overriding ancestor method"
            );
        }

        [Test]
        public void MultipleOverloadsInSameAncestorWithOneMatchingIsNotFlagged()
        {
            // Tests when an ancestor has multiple overloads and we override one of them
            WriteTestFile(
                "MultipleOverloadsOneMatching.cs",
                @"
namespace TestNs
{
    public class Level0
    {
        public virtual void Handle(int a) { }
        public virtual void Handle(int a, int b) { }
        public virtual void Handle(int a, int b, int c) { }
    }

    public class Level1 : Level0
    {
        public override void Handle(int a) { base.Handle(a); }
    }

    public class Level2 : Level1
    {
        // Override the two-param version from Level0
        public override void Handle(int a, int b) { base.Handle(a, b); }
    }

    public class Level3 : Level2
    {
        // Override the three-param version from Level0 (3 levels up)
        public override void Handle(int a, int b, int c) { base.Handle(a, b, c); }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i =>
                    i.IssueType == "SignatureMismatch"
                    && (i.ClassName == "Level2" || i.ClassName == "Level3")
                )
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should find matching overloads even with multiple options. Found: {string.Join(", ", signatureMismatches.Select(i => $"{i.ClassName}.{i.MethodName}"))}"
            );
        }

        [Test]
        public void OverrideWithGenericParameterFromAncestorIsNotFlagged()
        {
            // Tests the combination of generic types and ancestor lookups
            WriteTestFile(
                "GenericAncestorOverride.cs",
                @"
namespace TestNs
{
    public class BaseHandler<T>
    {
        public virtual void Handle(T item) { }
        public virtual void Handle() { }
    }

    public class MiddleHandler<T> : BaseHandler<T>
    {
        // Only overrides parameterless
        public override void Handle() { base.Handle(); }
    }

    public class IntHandler : MiddleHandler<int>
    {
        // Override Handle(T) which becomes Handle(int) from BaseHandler
        public override void Handle(int item) { base.Handle(item); }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> signatureMismatches = issues
                .Where(i => i.IssueType == "SignatureMismatch" && i.ClassName == "IntHandler")
                .ToList();

            Assert.That(
                signatureMismatches.Count,
                Is.EqualTo(0),
                $"Should resolve generic type parameters when searching ancestors. Found: {string.Join(", ", signatureMismatches.Select(i => $"{i.MethodName}: {i.DerivedMethodSignature}"))}"
            );
        }

        [Test]
        public void SuppressAnalyzerOnlyAffectsImmediatelyFollowingMethod()
        {
            // Verify that [SuppressAnalyzer] on one method doesn't suppress subsequent methods
            WriteTestFile(
                "SuppressOnlyImmediate.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual int Method1() => 0;
        public virtual int Method2() => 0;
        public virtual int Method3() => 0;
    }

    public class DerivedClass : BaseClass
    {
        [SuppressAnalyzer(""Intentional for testing"")]
        public override string Method1() => string.Empty;

        // Method2 should still be flagged - no [SuppressAnalyzer]
        public override string Method2() => string.Empty;

        [SuppressAnalyzer]
        public override string Method3() => string.Empty;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            // Method1 should be suppressed
            List<AnalyzerIssue> method1Issues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "Method1")
                .ToList();
            Assert.That(
                method1Issues.Count,
                Is.EqualTo(0),
                $"Method1 should be suppressed. Found: {string.Join(", ", method1Issues.Select(i => i.IssueType))}"
            );

            // Method2 should be flagged
            List<AnalyzerIssue> method2Issues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "Method2")
                .ToList();
            Assert.That(
                method2Issues.Count,
                Is.EqualTo(1),
                $"Method2 should be flagged (no [SuppressAnalyzer]). All issues: {FormatIssues(issues)}"
            );
            Assert.That(
                method2Issues[0].IssueType,
                Is.EqualTo("ReturnTypeMismatch"),
                $"Method2 should have ReturnTypeMismatch issue. Found: {method2Issues[0].IssueType}"
            );

            // Method3 should be suppressed
            List<AnalyzerIssue> method3Issues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "Method3")
                .ToList();
            Assert.That(
                method3Issues.Count,
                Is.EqualTo(0),
                $"Method3 should be suppressed. Found: {string.Join(", ", method3Issues.Select(i => i.IssueType))}"
            );
        }

        [Test]
        public void SuppressAnalyzerOnClassDoesNotAffectOtherClasses()
        {
            // Verify that [SuppressAnalyzer] on one class doesn't suppress issues in other classes
            WriteTestFile(
                "SuppressOnlyThisClass.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual int GetValue() => 0;
    }

    [SuppressAnalyzer(""Suppressed class"")]
    public class SuppressedClass : BaseClass
    {
        public override string GetValue() => string.Empty;
    }

    public class NotSuppressedClass : BaseClass
    {
        public override string GetValue() => string.Empty;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;

            // SuppressedClass should have no issues
            List<AnalyzerIssue> suppressedIssues = issues
                .Where(i => i.ClassName == "SuppressedClass")
                .ToList();
            Assert.That(
                suppressedIssues.Count,
                Is.EqualTo(0),
                $"SuppressedClass should have no issues. Found: {string.Join(", ", suppressedIssues.Select(i => $"{i.MethodName}: {i.IssueType}"))}"
            );

            // NotSuppressedClass should be flagged
            List<AnalyzerIssue> notSuppressedIssues = issues
                .Where(i => i.ClassName == "NotSuppressedClass")
                .ToList();
            Assert.That(
                notSuppressedIssues.Count,
                Is.EqualTo(1),
                $"NotSuppressedClass should be flagged. All issues: {FormatIssues(issues)}"
            );
        }

        [Test]
        [TestCase("UsingNewOnVirtual", "new void DoWork()")]
        [TestCase("SignatureMismatch", "new int DoWork(string arg)")]
        public void NewKeywordIsFlaggedWithAppropriateIssueType(
            string expectedIssueType,
            string methodDeclaration
        )
        {
            // The 'new' keyword is bad practice and should always be flagged
            WriteTestFile(
                "NewKeyword.cs",
                $@"
namespace TestNs
{{
    public class BaseClass
    {{
        public virtual void DoWork() {{ }}
    }}
    public class DerivedClass : BaseClass
    {{
        public {methodDeclaration} {{ }}
    }}
}}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> derivedIssues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "DoWork")
                .ToList();

            Assert.That(
                derivedIssues.Count,
                Is.GreaterThan(0),
                $"'new' keyword should be flagged. Method: '{methodDeclaration}'. All issues: {FormatIssues(issues)}"
            );

            Assert.That(
                derivedIssues.Any(i => i.IssueType == expectedIssueType),
                Is.True,
                $"Expected issue type '{expectedIssueType}' for method '{methodDeclaration}'. Found: {string.Join(", ", derivedIssues.Select(i => i.IssueType))}"
            );
        }

        [Test]
        public void NewKeywordOnNonVirtualMethodIsFlagged()
        {
            // Using 'new' to hide a non-virtual method should be flagged as UsingNewOnNonVirtual
            WriteTestFile(
                "NewOnNonVirtual.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public void Process() { }
    }
    public class DerivedClass : BaseClass
    {
        public new void Process() { }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> processIssues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "Process")
                .ToList();

            // Should have a UsingNewOnNonVirtual issue
            Assert.That(
                processIssues.Count,
                Is.EqualTo(1),
                $"'new' keyword hiding non-virtual method should be flagged exactly once. All issues: {FormatIssues(issues)}"
            );
            Assert.That(
                processIssues[0].IssueType,
                Is.EqualTo("UsingNewOnNonVirtual"),
                $"Issue type should be UsingNewOnNonVirtual. Actual: {processIssues[0].IssueType}"
            );
        }

        [Test]
        public void SuppressAnalyzerInCommentDoesNotSuppressMethod()
        {
            // Verify that [SuppressAnalyzer] in a comment doesn't suppress the following method
            WriteTestFile(
                "SuppressInComment.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual int GetValue() => 0;
    }

    public class DerivedClass : BaseClass
    {
        // This comment mentions [SuppressAnalyzer] but should not suppress
        public override string GetValue() => string.Empty;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> getValueIssues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "GetValue")
                .ToList();

            Assert.That(
                getValueIssues.Count,
                Is.EqualTo(1),
                $"Method should be flagged despite [SuppressAnalyzer] appearing in a comment. All issues: {FormatIssues(issues)}"
            );
            Assert.That(
                getValueIssues[0].IssueType,
                Is.EqualTo("ReturnTypeMismatch"),
                $"Issue type should be ReturnTypeMismatch. Actual: {getValueIssues[0].IssueType}"
            );
        }

        [Test]
        public void SuppressAnalyzerInMultiLineCommentDoesNotSuppressMethod()
        {
            // Verify that [SuppressAnalyzer] in a multi-line comment doesn't suppress the method
            WriteTestFile(
                "SuppressInMultiLineComment.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual int GetValue() => 0;
    }

    public class DerivedClass : BaseClass
    {
        /*
         * Some documentation that mentions [SuppressAnalyzer] attribute
         * but should not actually suppress analysis
         */
        public override string GetValue() => string.Empty;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> getValueIssues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "GetValue")
                .ToList();

            Assert.That(
                getValueIssues.Count,
                Is.EqualTo(1),
                $"Method should be flagged despite [SuppressAnalyzer] appearing in multi-line comment. All issues: {FormatIssues(issues)}"
            );
        }

        [Test]
        public void SuppressAnalyzerInStringLiteralDoesNotSuppressMethod()
        {
            // Verify that [SuppressAnalyzer] in a string literal doesn't suppress the method
            WriteTestFile(
                "SuppressInString.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual int GetValue() => 0;
    }

    public class DerivedClass : BaseClass
    {
        private const string Instructions = ""Add [SuppressAnalyzer] to suppress"";
        public override string GetValue() => string.Empty;
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> getValueIssues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "GetValue")
                .ToList();

            Assert.That(
                getValueIssues.Count,
                Is.EqualTo(1),
                $"Method should be flagged despite [SuppressAnalyzer] appearing in a string. All issues: {FormatIssues(issues)}"
            );
        }

        [Test]
        public void NewKeywordOnVirtualMethodIsFlagged()
        {
            // Using 'new' on a virtual method should be flagged as UsingNewOnVirtual
            WriteTestFile(
                "NewOnVirtual.cs",
                @"
namespace TestNs
{
    public class BaseClass
    {
        public virtual void Process() { }
    }
    public class DerivedClass : BaseClass
    {
        public new void Process() { }
    }
}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> processIssues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "Process")
                .ToList();

            Assert.That(
                processIssues.Count,
                Is.EqualTo(1),
                $"'new' keyword on virtual method should be flagged. All issues: {FormatIssues(issues)}"
            );
            Assert.That(
                processIssues[0].IssueType,
                Is.EqualTo("UsingNewOnVirtual"),
                $"Issue type should be UsingNewOnVirtual. Actual: {processIssues[0].IssueType}"
            );
        }

        [TestCase("public", "Low", Description = "Non-Unity base should have Low severity")]
        [TestCase("private", null, Description = "Private methods should not be flagged")]
        public void NewKeywordOnNonVirtualMethodSeverityDependsOnVisibility(
            string visibility,
            string expectedSeverity
        )
        {
            // Test severity levels and visibility handling for 'new' on non-virtual methods
            WriteTestFile(
                "NewOnNonVirtualVisibility.cs",
                $@"
namespace TestNs
{{
    public class BaseClass
    {{
        public void Process() {{ }}
    }}
    public class DerivedClass : BaseClass
    {{
        {visibility} new void Process() {{ }}
    }}
}}
"
            );

            AnalyzeTestFiles();

            IReadOnlyList<AnalyzerIssue> issues = _analyzer.Issues;
            List<AnalyzerIssue> processIssues = issues
                .Where(i => i.ClassName == "DerivedClass" && i.MethodName == "Process")
                .ToList();

            if (expectedSeverity == null)
            {
                Assert.That(
                    processIssues.Count,
                    Is.EqualTo(0),
                    $"{visibility} methods should not be flagged for UsingNewOnNonVirtual. All issues: {FormatIssues(issues)}"
                );
            }
            else
            {
                Assert.That(
                    processIssues.Count,
                    Is.EqualTo(1),
                    $"{visibility} methods should be flagged. All issues: {FormatIssues(issues)}"
                );
                Assert.That(
                    processIssues[0].Severity.ToString(),
                    Is.EqualTo(expectedSeverity),
                    $"Severity for {visibility} should be {expectedSeverity}. Actual: {processIssues[0].Severity}"
                );
            }
        }

        /// <summary>
        /// Helper method to format all issues for diagnostic output.
        /// </summary>
        private static string FormatIssues(IEnumerable<AnalyzerIssue> issues)
        {
            List<AnalyzerIssue> issueList = issues.ToList();
            if (issueList.Count == 0)
            {
                return "(no issues)";
            }

            return string.Join(
                "; ",
                issueList.Select(i => $"{i.ClassName}.{i.MethodName}:{i.IssueType}")
            );
        }
    }
}
#endif
