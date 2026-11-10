// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.IntMap.Tests
{
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using NUnit.Framework;

    [TestFixture]
    public sealed class IntMapRecoveryTests
    {
        [TestCase(
            IntMapRecoveryProcess.LongMapKind,
            TestName = "FailedGrowth.LongMap.PreservesMap"
        )]
        [TestCase(
            IntMapRecoveryProcess.ZeroKeyKind,
            TestName = "FailedGrowth.ZeroKey.PreservesMap"
        )]
        [TestCase(
            IntMapRecoveryProcess.NonzeroKeyKind,
            TestName = "FailedGrowth.NonzeroKey.PreservesMap"
        )]
        [TestCase(
            IntMapRecoveryProcess.IndicesKeyKind,
            TestName = "FailedGrowth.IndexStorage.PreservesMap"
        )]
        public void FailedGrowthPreservesMap(string keyKind)
        {
            string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrWhiteSpace(host))
            {
                host = "dotnet";
            }
            ProcessStartInfo start = new ProcessStartInfo(host)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(typeof(IntMapRecoveryTests).Assembly.Location);
            start.ArgumentList.Add(keyKind);
            start.Environment["DOTNET_GCHeapHardLimit"] = string.Equals(
                keyKind,
                IntMapRecoveryProcess.IndicesKeyKind,
                StringComparison.Ordinal
            )
                ? "3000000"
                : "2000000";
            start.Environment.Remove("DOTNET_GCHeapHardLimitPercent");
            start.Environment.Remove("COMPlus_GCHeapHardLimit");
            using Process child = Process.Start(start);
            Assert.That(child, Is.Not.Null);
            Task<string> output = child.StandardOutput.ReadToEndAsync();
            Task<string> errors = child.StandardError.ReadToEndAsync();
            bool exited = child.WaitForExit(30000);
            if (!exited)
            {
                child.Kill(entireProcessTree: true);
                Assert.That(child.WaitForExit(5000), Is.True, "The terminated child did not exit.");
            }
            Task[] readers = { output, errors };
            Assert.That(
                Task.WaitAll(readers, 5000),
                Is.True,
                "The child output streams did not close."
            );
            string diagnostic = output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult();
            TestContext.WriteLine(diagnostic);
            Assert.That(exited, Is.True, diagnostic);
            Assert.That(child.ExitCode, Is.Zero, diagnostic);
            Assert.That(diagnostic, Does.Contain(IntMapRecoveryProcess.PassMarker));
        }
    }
}
