// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only shim for the Test Runner API surface `FailedTestsExporter` binds.
//
// `com.unity.test-framework` is a UPM package with no NuGet equivalent, and it is not part of any
// Unity editor reference assembly. Mirrors the real members' shapes exactly -- declaring a member
// the real API does not have would let a genuine error through.
namespace UnityEditor.TestTools.TestRunner.Api
{
    using UnityEngine;

    public enum TestStatus
    {
        Inconclusive,
        Skipped,
        Passed,
        Failed,
    }

    public interface ITestAdaptor
    {
        string FullName { get; }
        string Name { get; }
        bool HasChildren { get; }
    }

    public interface ITestResultAdaptor
    {
        ITestAdaptor Test { get; }
        string FullName { get; }
        string Name { get; }
        TestStatus TestStatus { get; }
        string Message { get; }
        string StackTrace { get; }
        bool HasChildren { get; }
    }

    public interface ICallbacks
    {
        void RunStarted(ITestAdaptor testsToRun);
        void RunFinished(ITestResultAdaptor result);
        void TestStarted(ITestAdaptor test);
        void TestFinished(ITestResultAdaptor result);
    }

    public class TestRunnerApi : ScriptableObject
    {
        public void RegisterCallbacks<T>(T testCallbacks, int priority = 0)
            where T : ICallbacks { }

        public void UnregisterCallbacks<T>(T testCallbacks)
            where T : ICallbacks { }
    }
}
