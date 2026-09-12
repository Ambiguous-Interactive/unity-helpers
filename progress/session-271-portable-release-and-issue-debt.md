# Session 271: Portable Release and Issue Debt

## Scope and baseline

- Inventory every open issue through paginated GitHub reads, ordered by gameplay impact before
  selecting bounded work.
- Address [#775](https://github.com/Ambiguous-Interactive/unity-helpers/issues/775) without adding a
  CI job, Unity launch, test exclusion, or coverage gap.
- Remove the vestigial tracked Claude configuration from
  [#774](https://github.com/Ambiguous-Interactive/unity-helpers/issues/774).
- Retire completed issue debt in
  [#736](https://github.com/Ambiguous-Interactive/unity-helpers/issues/736) and
  [#769](https://github.com/Ambiguous-Interactive/unity-helpers/issues/769).

The authenticated GitHub login was `wallstop`. All selected issues were authored by that login. The
inventory returned 30 open issues in one terminal page (`hasNextPage: false`) and no open or draft
pull requests. Main and both configured remote main branches matched `08de79125`; its repository CI
and 3.6.0 Release Publish run were green.

Release Publish previously waited for the Windows Unity fleet, acquired the organization lock,
launched a licensed editor, exported one archive, returned the license, and ran a second aggregate
job. The native Unity Tests smoke already performed the same release-payload compile and export on
every change, so repeating that lifecycle during publication added no test coverage.

## Changes

- `create-unitypackage.js` now streams the validated npm payload into the Unity package tar layout.
  It preserves tracked GUIDs, renames `Samples~` to `Samples`, gives Unity-generated folders stable
  path-derived GUIDs, rejects missing metadata and duplicate GUIDs, writes deterministic gzip bytes,
  and publishes a SHA256 sidecar atomically.
- Release Publish creates the archive on `ubuntu-latest`. It no longer needs a Unity editor, license
  secrets, the organization lock, a self-hosted runner, cleanup diagnostics, or the export aggregate.
  The Unity Tests native smoke remains unchanged, retaining release-optimized sample compilation and
  Unity import/export coverage.
- Release Prepare no longer copies the complete changelog section into its GitHub-size-limited pull
  request body. It checks the final body size before pushing the remote release branch.
- The tracked `.claude` project settings and their now-unreachable cspell hook were removed. Agent
  guidance continues to require the same manual spelling gate, and local Claude state remains
  ignored.
- The applicable #772 enrollment fix is carried forward: the licensed Unity test matrix has a
  statically auditable version axis plus a contract that keeps it aligned with the canonical JSON
  source. A selected-version axis and complete mismatch exclusions preserve one-version manual
  dispatches without weakening that audit. Its release-cancellation flip is intentionally obsolete
  here because release publication no longer acquires the organization lock and must not be
  interrupted midway.
- The active plan dropped the completed #736 work and records the remaining #775 workflow-UX
  decision.

## Archive science and CI cost

The published Unity-produced 3.6.0 package and the portable archive each contained 1,159 pathnames,
1,159 metadata records, and 1,015 file payloads, with identical logical path sets. Ordinary asset
bytes matched except for one material whose line endings Unity normalized and one text scene Unity
reserialized into its binary form. Metadata differed only for the package root and renamed Samples
folder that the temporary Unity project previously generated afresh. Two portable full-package
builds were byte-identical.

The release workflow removes one self-hosted job, its preflight, one licensed editor launch, and one
aggregate job. It adds no PR/push job: archive validity and determinism run inside the existing
parallel contract aggregate. The native smoke retains every prior Unity compile and export check, so
test and version coverage do not decrease. The 3.6.0 release took 9m38s; its native export took
1m57s after a 12-second preflight. The portable full archive takes 8–11 seconds locally, so the
expected critical-path reduction is at least roughly one minute before runner-dispatch savings.

## Verification

- Portable archive controls: deterministic bytes, checksum, independently recomputed tar header
  checksums, USTAR markers and terminators, path mapping, folder/file shapes, hidden-file exclusion,
  missing metadata, duplicate GUIDs, and overlong tar names passed.
- A full package build produced 1,159 assets and a valid checksum.
- Unity workflow/matrix contracts passed with four remaining licensed lifecycles.
- Release/sync contracts passed 157 checks.
- The published 3.6.0 release comparison matched all 1,159 logical paths.
- #736, #769, and #766 were closed as completed with disclosed evidence comments.
