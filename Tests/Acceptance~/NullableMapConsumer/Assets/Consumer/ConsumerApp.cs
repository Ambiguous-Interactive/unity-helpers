// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace NestedOnlyConsumer
{
    using System;
    using System.IO;
    using UnityEngine;

    public sealed class ConsumerApp : MonoBehaviour
    {
        private static string Argument(string[] args, string name)
        {
            for (int index = 0; index + 1 < args.Length; ++index)
            {
                if (string.Equals(args[index], name, StringComparison.Ordinal))
                {
                    return args[index + 1];
                }
            }
            throw new ArgumentException("Missing argument " + name);
        }

        private static bool IsIl2Cpp()
        {
#if ENABLE_IL2CPP
            return true;
#else
            return false;
#endif
        }

        private void Start()
        {
            string output = null;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                output = Argument(args, "--result");
                string mode = Argument(args, "--mode");
                string shape = Argument(args, "--shape");
                string golden = Argument(args, "--golden");
                string expectedVersion = Argument(args, "--unity");
                string expectedCommit = Argument(args, "--commit");
                string expectedSource = Argument(args, "--source");
                string runToken = Argument(args, "--run-token");
                ConsumerCases.Require(
                    Application.unityVersion == expectedVersion
                        && expectedVersion == ConsumerBinding.UnityVersion,
                    "Exact Unity version binding"
                );
                ConsumerCases.Require(
                    expectedCommit == ConsumerBinding.Commit
                        && expectedSource == ConsumerBinding.SourceManifest
                        && runToken == ConsumerBinding.RunToken,
                    "Candidate source/run binding"
                );
                string provenance =
                    "|commit=" + expectedCommit + "|source=" + expectedSource + "|run=" + runToken;

                ConsumerCases.Require(
                    IsIl2Cpp() && !Debug.isDebugBuild,
                    "Requires nondevelopment IL2CPP player"
                );
                if (mode.StartsWith("presence-", StringComparison.Ordinal))
                {
                    byte[] presenceExpected = Convert.FromBase64String(
                        File.ReadAllText(Path.Combine(golden, "presence.base64")).Trim()
                    );
                    PresenceCases.Run(mode, presenceExpected);
                    File.WriteAllText(
                        output,
                        "Passed|"
                            + mode
                            + "|"
                            + shape
                            + "|"
                            + Application.unityVersion
                            + "|IL2CPP="
                            + IsIl2Cpp()
                            + provenance
                    );
                    Application.Quit(0);
                    return;
                }
                byte[] expected = Convert.FromBase64String(
                    File.ReadAllText(Path.Combine(golden, shape + ".base64")).Trim()
                );
                ConsumerCases.Run(mode, shape, expected);
                File.WriteAllText(
                    output,
                    "Passed|"
                        + mode
                        + "|"
                        + shape
                        + "|"
                        + Application.unityVersion
                        + "|IL2CPP="
                        + IsIl2Cpp()
                        + provenance
                );
                Application.Quit(0);
            }
            catch (Exception failure)
            {
                if (!string.IsNullOrWhiteSpace(output))
                {
                    File.WriteAllText(output, "Failed|" + failure.ToString());
                }
                Debug.LogException(failure);
                Application.Quit(1);
            }
        }
    }
}
