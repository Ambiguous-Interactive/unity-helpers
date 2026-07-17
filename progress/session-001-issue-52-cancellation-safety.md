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
- Final adversarial review found that those PID 1 traps were installed only
  after activation and that the host fixture bypassed the wrapper by invoking
  `docker stop` itself. Activation and return now use the same interruptible
  process-group supervisor, traps are live before any licensed command, and the
  host waits asynchronously so its own INT/TERM handler can enter EXIT cleanup.
- Supervisor launches defer signals only across atomic child-PID registration,
  then replay them immediately; transient Unity output uses a mode-0600
  `mktemp` file that signal and normal paths both remove.
- A final launch-race review found that daemon-side registration can outlive the
  initiating Docker client. Cancellation now stops and reaps that bounded client,
  retries inspection for one bounded client window, then gracefully stops and
  forcibly removes any late named container. Completed runs keep the single-
  inspect fast path.
- The graceful-stop reserve includes separate TERM-to-KILL windows for the
  interrupted command and the subsequent bounded license return.
- Rebalanced both hosted export jobs so the 360-minute job cap includes setup,
  acquisition, container execution, client cleanup, explicit workflow cleanup,
  implicit post-actions, and additional unallocated slack.

## Validation

- Red tests reproduced missing step/process/client bounds and budget equality.
- Data-driven workflow and release-budget contracts pass for both export callers.
- Behavioral fake-container coverage signals the production wrapper before
  registration, during activation, and during main Unity work. The registration
  fixture lets its client exit, makes the first inspect miss, then asynchronously
  registers and starts the named container; cleanup observes it on a bounded
  retry. Coverage also proves TERM-resistant parent and descendant cleanup,
  exactly one PID 1 serial return before removal, no leftover client/daemon/
  container process, mutated two-grace reserve propagation, and unconditional
  removal after inspect failure.
- Hosted package-export CI found that Docker's `-e NAME` form does not receive
  wrapper defaults that were assigned as non-exported shell variables. All four
  in-container timeout/grace controls now pass explicit validated values;
  data-driven fake-Docker coverage rejects any recurrence across the complete
  control set.
- Full pre-push validation and the exact central consumer-policy audit pass.
