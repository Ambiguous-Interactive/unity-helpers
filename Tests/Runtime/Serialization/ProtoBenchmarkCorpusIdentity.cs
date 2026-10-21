// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;

    internal static class ProtoBenchmarkCorpusIdentity
    {
        private static readonly Dictionary<string, (int Length, string Sha256)> Cases =
            new Dictionary<string, (int Length, string Sha256)>(StringComparer.Ordinal)
            {
                ["scalar-heavy/0"] = (3, "3mOdw7kyqqS2gGqzogi0tJtnpAPO+Db9R4Xl5mHsmqw="),
                ["text-heavy/0"] = (5, "gAxlCjp9y9Pbq1sVkM0eH3AxFdyT4rdGN2PtUqoVgSw="),
                ["repeated-numeric/0"] = (3, "3mOdw7kyqqS2gGqzogi0tJtnpAPO+Db9R4Xl5mHsmqw="),
                ["repeated-numeric/0-unpacked"] = (
                    3,
                    "3mOdw7kyqqS2gGqzogi0tJtnpAPO+Db9R4Xl5mHsmqw="
                ),
                ["maps/0"] = (3, "3mOdw7kyqqS2gGqzogi0tJtnpAPO+Db9R4Xl5mHsmqw="),
                ["nested-polymorphic/0"] = (3, "3mOdw7kyqqS2gGqzogi0tJtnpAPO+Db9R4Xl5mHsmqw="),
                ["generic-enum/0"] = (3, "3mOdw7kyqqS2gGqzogi0tJtnpAPO+Db9R4Xl5mHsmqw="),
                ["large-binary/0"] = (5, "gAxlCjp9y9Pbq1sVkM0eH3AxFdyT4rdGN2PtUqoVgSw="),
                ["scalar-heavy/1"] = (5, "0Z9Uac0g0gpi2VZKgIFMgrDOSuABvhB5ihikstIg168="),
                ["text-heavy/1"] = (53, "kxf6mcTj41fRMhR9HzPI3pS3w8Mo4Ctf1UqF9A8Drto="),
                ["repeated-numeric/1"] = (6, "vwkMdJD7q1orchRgRy3rmHxLMughe9DPYLvJa1L6uDw="),
                ["repeated-numeric/1-unpacked"] = (
                    5,
                    "pW/nfwZy+rP6vZ1q1JeQ6pcxvdhpyZoqPxucVIw0Zds="
                ),
                ["maps/1"] = (12, "E80tiGXk3yI/Cp5N4QpnZrmAeREYYhohEt6BVB5WgA4="),
                ["nested-polymorphic/1"] = (15, "J4om/Nv5CBvyQWa7cnSBjtSbUaG95A95sGU/nmgDtHE="),
                ["generic-enum/1"] = (6, "vwkMdJD7q1orchRgRy3rmHxLMughe9DPYLvJa1L6uDw="),
                ["large-binary/1"] = (1030, "JT1PmkupXxvW//cHVPZ6O8on5zxQuE66hT0eqClbiKY="),
                ["scalar-heavy/32"] = (23, "BE5YRFwCmpZ0dxqVnixT/atGjFCKI6KXXz+QMGcs8/Q="),
                ["text-heavy/32"] = (1542, "o70Iaofj00JOIISJdjYAWH5J8HFfpFLbKzslremKW3c="),
                ["repeated-numeric/32"] = (164, "Ic8Esl4Ev85nqoElDZvkv0Zfbi+gKMItj+V+bDLEMIk="),
                ["repeated-numeric/32-unpacked"] = (
                    193,
                    "Eq684kcGivfzGhWdj5nqqMFCYTIKTvOFFK1RL1OS0fU="
                ),
                ["maps/32"] = (399, "HWvg8I1/+lOBu5NKqTam6ycopE4pLE9u8LyP3usljdk="),
                ["nested-polymorphic/32"] = (471, "T9kDPieycb+8xjmYXWW8p9kYETaWAcOlJ48qp/pS3jg="),
                ["generic-enum/32"] = (37, "AipqZ/JVsQOYLmtKk+xYD29eejXpVbpil38tuk5JAjo="),
                ["large-binary/32"] = (32775, "OvwIAeVQ/I/WqKvk18JuaRuSUITOGFE/jvKQ5rEpUwc="),
                ["scalar-heavy/1024"] = (68, "/vm147VHtmb8HxHbsrvG4vYDOneADHB7efZCFWDTtVM="),
                ["text-heavy/1024"] = (49159, "+NzGalGiHBlkxMEFdYRxrYiHoJ2UtN1EGyr23CvEk/c="),
                ["repeated-numeric/1024"] = (5215, "TR+pyGjEnMYzjqWAgVNniWFbGXFTScIoGX2glxZdRTw="),
                ["repeated-numeric/1024-unpacked"] = (
                    6236,
                    "/Y6mIS6IdYXxoL5f4FhVDdgELAN4L2XGf6cDmrb1tGk="
                ),
                ["maps/1024"] = (14303, "xlgKI3gOmY/jz/qqgZCahaabRvUM0E7XyNOT96RtWD4="),
                ["nested-polymorphic/1024"] = (
                    17195,
                    "Ev/I6rCEQxF1HppVEEPwlQZxW1S3kfrYT8ROVsAF/Go="
                ),
                ["generic-enum/1024"] = (1030, "WMg9n9ZUFPk3zyEGLW9exRLS4bKfaXwYo9K80eLzgC8="),
                ["large-binary/1024"] = (1048583, "BdeT0I+79itTTfahxMMx6WS/cSf5b9nLvexOqmwdH64="),
            };

        internal static IEnumerable<string> Identifiers(bool includeUnpacked)
        {
            foreach (KeyValuePair<string, (int Length, string Sha256)> entry in Cases)
            {
                if (includeUnpacked || !entry.Key.EndsWith("-unpacked", StringComparison.Ordinal))
                {
                    yield return entry.Key;
                }
            }
        }

        internal static bool TryAccept(
            HashSet<string> observed,
            string identifier,
            int length,
            string sha256
        )
        {
            return observed != null
                && identifier != null
                && Cases.TryGetValue(identifier, out (int Length, string Sha256) expected)
                && length == expected.Length
                && string.Equals(sha256, expected.Sha256, StringComparison.Ordinal)
                && observed.Add(identifier);
        }

        internal static bool IsComplete(HashSet<string> observed, bool includeUnpacked)
        {
            if (observed == null)
            {
                return false;
            }
            int expectedCount = 0;
            foreach (string identifier in Identifiers(includeUnpacked))
            {
                ++expectedCount;
                if (!observed.Contains(identifier))
                {
                    return false;
                }
            }
            return observed.Count == expectedCount;
        }
    }
}
