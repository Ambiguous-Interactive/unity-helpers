// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Reference-only shim for the Test Runner API surface `FailedTestsExporter` and
// `TestRunReporter` bind.
//
// `com.unity.test-framework` is a UPM package with no NuGet equivalent, and it is not part of any
// Unity editor reference assembly. Mirrors the real members' shapes exactly -- declaring a member
// the real API does not have would let a genuine error through.
namespace UnityEditor.TestTools.TestRunner.Api
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    public enum TestStatus
    {
        Inconclusive,
        Skipped,
        Passed,
        Failed,
    }

    [Flags]
    public enum TestMode
    {
        EditMode = 1 << 0,
        PlayMode = 1 << 1,
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
        double Duration { get; }
        string Message { get; }
        string StackTrace { get; }
        bool HasChildren { get; }
        IEnumerable<ITestResultAdaptor> Children { get; }
    }

    public interface ICallbacks
    {
        void RunStarted(ITestAdaptor testsToRun);
        void RunFinished(ITestResultAdaptor result);
        void TestStarted(ITestAdaptor test);
        void TestFinished(ITestResultAdaptor result);
    }

    public class Filter
    {
        public string[] assemblyNames;
        public string[] categoryNames;
        public string[] groupNames;
        public string[] testNames;
        public TestMode testMode;
    }

    public class ExecutionSettings
    {
        public Filter[] filters;

        public ExecutionSettings(params Filter[] filtersToExecute)
        {
            filters = filtersToExecute;
        }
    }

    public class TestRunnerApi : ScriptableObject
    {
        public string Execute(ExecutionSettings executionSettings)
        {
            return string.Empty;
        }

        public void RegisterCallbacks<T>(T testCallbacks, int priority = 0)
            where T : ICallbacks { }

        public void UnregisterCallbacks<T>(T testCallbacks)
            where T : ICallbacks { }
    }
}
