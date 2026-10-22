// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.SchemaConformance.Tests
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    internal static class ProtocOracle
    {
        internal static async Task<(int ExitCode, byte[] Output, string Error)> Run(
            string directory,
            string label,
            byte[] input,
            params string[] arguments
        )
        {
            string executable = Path.Combine(AppContext.BaseDirectory, "protoc");
            Assert.IsTrue(File.Exists(executable), "The pinned Linux x64 protoc is required.");
            ProcessStartInfo start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = new Process { StartInfo = start };
            DateTime started = DateTime.UtcNow;
            Assert.IsTrue(process.Start());
            using MemoryStream output = new MemoryStream();
            using MemoryStream errors = new MemoryStream();
            Task stdout = process.StandardOutput.BaseStream.CopyToAsync(output);
            Task stderr = process.StandardError.BaseStream.CopyToAsync(errors);
            Task stdin = WriteInput(process, input);
            using CancellationTokenSource deadline = new CancellationTokenSource(
                TimeSpan.FromSeconds(15)
            );
            bool timedOut = false;
            try
            {
                await process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            await Task.WhenAll(stdout, stderr, stdin);
            byte[] bytes = output.ToArray();
            byte[] errorBytes = errors.ToArray();
            string error = Encoding.UTF8.GetString(errorBytes);
            File.WriteAllBytes(Path.Combine(directory, label + ".stdout"), bytes);
            File.WriteAllBytes(Path.Combine(directory, label + ".stderr"), errorBytes);
            File.WriteAllText(
                Path.Combine(directory, label + ".process.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        Arguments = arguments,
                        StartedUtc = started,
                        CompletedUtc = DateTime.UtcNow,
                        process.ExitCode,
                        TimedOut = timedOut,
                    }
                )
            );
            Assert.IsFalse(timedOut, "protoc exceeded its 15-second deadline: " + label);
            return (process.ExitCode, bytes, error);
        }

        internal static async Task<byte[]> RequireSuccess(
            string directory,
            string label,
            byte[] input,
            params string[] arguments
        )
        {
            (int exitCode, byte[] output, string error) = await Run(
                directory,
                label,
                input,
                arguments
            );
            Assert.AreEqual(0, exitCode, label + ": " + error);
            Assert.IsEmpty(error, label);
            return output;
        }

        private static async Task WriteInput(Process process, byte[] input)
        {
            await process.StandardInput.BaseStream.WriteAsync(input);
            process.StandardInput.Close();
        }
    }
}
