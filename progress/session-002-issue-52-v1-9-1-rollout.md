# Session 002: issue 52 v1.9.1 rollout

## Objective

Finish draft pull request 305 on current main and migrate every organization
build-lock action from staged issue-52 or v1.8.3 commits to the immutable v1.9.1
release commit.

## Result

- The draft branch includes current main and all guard, runner-preflight,
  acquire, and release references pin
  `a00614ace745152a659c5c2654f7cefb68a5a628` (`v1.9.1`).
- The workflow contract checks every PR-capable acquire for exact token, pull
  request number, and expected-head SHA bindings.
- Negative mutations prove that removing any identity binding is rejected;
  existing literal non-cancellation and matrix fail-fast checks remain active.

## Validation

- Full Unity workflow and runner contract passed.
- Focused acquire-identity mutations, test-lint fix/check, PSScriptAnalyzer at
  error severity, actionlint, and `git diff --check` passed.

## Cancellation review and after-acquire canary

- Cursor found that terminating the initiating Docker client could use Docker's
  default 10-second stop grace instead of the wrapper's computed cleanup window,
  and that a TERM during serial return could suppress the signal-handler retry.
- Red tests reproduce both defects. The wrapper now passes its computed stop
  timeout to `docker run` and records serial-return completion only after the
  supervised command settles; the focused signal contract passes with both
  fixes, and both edited shell files pass `bash -n` and `git diff --check`.
- Before this follow-up head is pushed, old head
  `3dd97f447ac669ee4a23f6fc1ee9deb89d8a81e2` still owns exact holder
  `Ambiguous-Interactive/unity-helpers:29782129274:unity-tests:6000.3.16f1-editmode`,
  acquired at `2026-07-20T22:18:30.207Z` on `ELI-MACHINE`. The push therefore
  exercises the after-acquire boundary: the old licensed job must remain active
  and release normally even though a newer PR head exists.
- Result: the old holder remained unchanged after the newer head was pushed,
  completed successfully at `2026-07-20T22:23:34Z`, and then disappeared from
  central lock state without quarantine. Supersession did not cancel or bypass
  its licensed cleanup.
