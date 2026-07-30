# Session 003: Organization Unity Enrollment

## Scope

Remediate
[issue #322](https://github.com/Ambiguous-Interactive/unity-helpers/issues/322)
under the central organization tracker
[build-lock #113](https://github.com/Ambiguous-Interactive/ambiguous-organization-build-lock/issues/113).
The default branch began with 74 source-free enrollment findings.

## Canonical paid lifecycle

`.github/workflows/unity-tests.yml` now owns one licensed Windows lifecycle for
Unity `6000.5.2f1`. It:

1. classifies the complete pull request and binds trust to the immutable PR
   author;
2. fails closed when the required Windows runner is unavailable;
3. revalidates the current PR head before setup and lock acquisition;
4. acquires one organization lock with resource-lifecycle and cooldown guards;
5. runs EditMode, PlayMode, and standalone validation sequentially;
6. delegates the authoritative final return to the central serial-redacting
   action;
7. preserves only sanitized account-health evidence, then classifies cleanup;
8. releases with typed cleanup outputs and executes the final cleanup gate;
9. supplies one exact hosted fallback and one typed always-reporting aggregate.

The runner harness keeps local callers compatible through
`LicenseReturnOwner=Local`. Enrolled CI passes `Central`, preventing a local
finally block from returning the seat before the trusted terminal evidence
chain.

## Retired contexts

- The scheduled benchmark matrix is credential-free and visibly retired. It can
  return only with its own reviewed lifecycle, fallback, aggregate, and runtime
  validation.
- Release publication is blocked fail closed at `.unitypackage` export. That
  exporter activates inside a container, which the Windows host return action
  cannot truthfully clean. Central build-lock #153 owns the required
  container-aware capability.

No credentialed job remains in either retired workflow.
Follow-up
[issue #323](https://github.com/Ambiguous-Interactive/unity-helpers/issues/323)
tracks restoration of the supported-version matrix, benchmarks, and
container-based publication after central build-lock #153 lands.

## Verification

- Modified-worktree central enrollment analysis against candidate policy
  `beac39704b90c8db8def0c9d2eaafe0449971768`: zero findings.
- YAML parsed successfully; Actionlint and Prettier pass all changed workflows.
- Changed-file spelling checks pass.
- The replacement workflow contract mutation-checks cancellation, immutable
  author trust, immutable action revisions, central return ownership, terminal
  cleanup ordering, fallback identity, and typed aggregation, rejecting 11
  unsafe mutations.
- Central classifier and cleanup-gate parity pass 11 classifier and 9 gate
  cases against exact return revision
  `0ce3dce6cbe29af210432087e3b6d81509258063`.
- The release synchronization contract passes all 132 cases.
- Full repository `validate:prepush` passes with portable PowerShell available.
- Two independent final reviews report no actionable findings.
- Hosted exact-head CI and the committed exact-snapshot audit remain pending
  because repository instructions reserve staging and commits for the user.
