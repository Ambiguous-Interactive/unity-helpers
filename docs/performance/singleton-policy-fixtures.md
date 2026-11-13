# Singleton policy fixture isolation

Six pure metadata and folder-policy cases now run in `SingletonAssetPolicyTests`,
without asset creation, save, refresh or cleanup. The seven integration cases keep
the original `SingletonAssetPollutionPreventionTests` setup and cleanup. Both
fixtures share the same cached type registry. All thirteen case bodies remain
covered; no production test hooks were added.

Three fresh-process pairs in Windows x64 Unity 6000.6.7f1 Editor Mono retain the
same thirteen normalized case identities. The second pair reverses order. Every
process passes all cases, returns raw exit zero and reaches both stream ends
without stopping output drainage. Fixture ownership checks distinguish the
baseline's zero policy-fixture cases from the candidate's six.

| Pair | Original summed fixture time | Split fixture time | After/before |
| ---- | ---------------------------: | -----------------: | -----------: |
| 1    |                   8.265951 s |         7.973383 s |       0.9646 |
| 2    |                   8.322782 s |         7.902470 s |       0.9495 |
| 3    |                  10.121433 s |         7.837843 s |       0.7744 |

The median decreases from 8.322782 s to 7.902470 s, about 5%. These observations
measure the selected fixtures, including their setup and cleanup, and exclude
editor startup. They establish no whole-suite speed claim. Shared-machine
variation is visible in the third baseline process.

The [replay archive](https://raw.githubusercontent.com/Ambiguous-Interactive/unity-helpers/587d3de6fde16c76a0ba256872cfe8c800bf1287/docs/performance/singleton-policy-fixtures.zip) contains exact source
variants, all six complete NUnit trees, process receipts, credential-checked logs,
the native controller and a SHA-256 inventory. The controller rejects changed
sources, missing cases and unhealthy process completion. Its README gives replay
instructions. A preparatory count guard incorrectly expected fourteen cases and
rejected that capture; it contributes no accepted observation.
