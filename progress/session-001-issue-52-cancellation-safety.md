# Session 001: Issue 52 cancellation safety

## Objective

Prevent superseded pull-request runs from starting or acquiring shared licensed
Unity work after a newer commit becomes current.

## Changes

- Disabled cancellation of active Unity workflow holders.
- Granted read-only pull-request metadata access.
- Added exact, immutable current-head guards as the first step and immediately
  before lock acquisition in every licensed job.
- Made the PowerShell workflow contracts data-driven across all licensed jobs.
- Removed the manual matrix-abort option and require literal `fail-fast: false`
  on every licensed matrix, so one failing leg cannot cancel a sibling license
  holder before cleanup.
- Commit `73a82ab` added the first release-export step timeout and a data-driven
  timeout contract. Adversarial review found that the Actions timeout alone did
  not supervise the Docker or Unity process trees.
- Commit `3214794` added nested Unity/container bounds, named-container cleanup,
  and a TERM-resistant regression. Follow-up review found that container PID 1
  did not yet trap host signals and Docker client calls were still unbounded.
- The final revision makes container PID 1 supervise an isolated Unity process
  group, TERM/KILL every captured descendant, return a serial seat exactly once,
  and exit on INT/TERM. Host inspect, graceful stop, and forced removal calls are
  independently watchdog-bounded; inspect uncertainty still reaches `rm -f`.
- Rebalanced both hosted export jobs so the 360-minute job cap includes setup,
  acquisition, container execution, client cleanup, explicit workflow cleanup,
  implicit post-actions, and additional unallocated slack.

## Validation

- Red tests reproduced missing step/process/client bounds and budget equality.
- Data-driven workflow and release-budget contracts pass for both export callers.
- Behavioral fake-container coverage proves TERM-resistant parent and descendant
  cleanup, PID 1 serial return before removal, mutated stop-reserve propagation,
  and unconditional removal after an inspect failure.
- Full pre-push validation and the exact central consumer-policy audit pass.
