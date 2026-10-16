// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace NestedOnlyConsumer
{
    using System;
    using System.Runtime.InteropServices;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    public sealed class BrowserConsumerApp : MonoBehaviour
    {
        private static string Query(string name)
        {
            string query = new Uri(Application.absoluteURL).Query.TrimStart('?');
            string result = null;
            foreach (string part in query.Split('&'))
            {
                int separator = part.IndexOf('=');
                if (separator < 0 || Uri.UnescapeDataString(part.Substring(0, separator)) != name)
                {
                    continue;
                }
                ConsumerCases.Require(result == null, "Duplicate query argument: " + name);
                result = Uri.UnescapeDataString(part.Substring(separator + 1));
            }
            ConsumerCases.Require(
                !string.IsNullOrWhiteSpace(result),
                "Missing query argument: " + name
            );
            return result;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void BrowserConsumerReport(string result);
#else
        private static void BrowserConsumerReport(string result)
        {
            Debug.Log(result);
        }
#endif

        private void Start()
        {
            try
            {
#if !UNITY_WEBGL || !ENABLE_IL2CPP || !SINGLE_THREADED
                throw new InvalidOperationException("Requires SINGLE_THREADED WebGL IL2CPP");
#else
                ConsumerCases.Require(!Debug.isDebugBuild, "Requires nondevelopment player");
                ConsumerCases.Require(
                    WProtoFormatterProvider.TryGet<Doc>(out _)
                        && WProtoFormatterProvider.TryGet<OwnedMessage>(out _)
                        && WProtoFormatterProvider.TryGet<PresenceDoc>(out _)
                        && WProtoFormatterProvider.TryGet<PresenceValue>(out _),
                    "Generated consumer formatters are required; legacy fallback is not acceptance"
                );
                ConsumerCases.Require(
                    Application.unityVersion == ConsumerBinding.UnityVersion,
                    "Unity binding changed"
                );
                ConsumerCases.Require(
                    Query("commit") == ConsumerBinding.Commit,
                    "Commit binding changed"
                );
                ConsumerCases.Require(
                    Query("source") == ConsumerBinding.SourceManifest,
                    "Source binding changed"
                );
                ConsumerCases.Require(
                    Query("token") == ConsumerBinding.RunToken,
                    "Run binding changed"
                );
                string mode = Query("mode");
                string shape = Query("shape");
                TextAsset golden = Resources.Load<TextAsset>("BrowserGoldens/" + shape);
                ConsumerCases.Require(golden != null, "Missing retained golden");
                byte[] expected = Convert.FromBase64String(golden.text.Trim());
                if (Query("control") == "corrupt-golden")
                {
                    expected[0] ^= 1;
                }
                if (mode == "safety")
                {
                    BrowserSafetyCases.Run();
                }
                else if (mode.StartsWith("presence-", StringComparison.Ordinal))
                {
                    PresenceCases.Run(mode, expected);
                }
                else
                {
                    ConsumerCases.Run(mode, shape, expected);
                }
                BrowserConsumerReport(
                    "Passed|"
                        + mode
                        + "|"
                        + shape
                        + "|"
                        + Application.unityVersion
                        + "|IL2CPP=True|SINGLE_THREADED=True|commit="
                        + ConsumerBinding.Commit
                        + "|source="
                        + ConsumerBinding.SourceManifest
                        + "|run="
                        + ConsumerBinding.RunToken
                );
#endif
            }
            catch (Exception failure)
            {
                BrowserConsumerReport("Failed|" + failure);
            }
        }
    }
}
