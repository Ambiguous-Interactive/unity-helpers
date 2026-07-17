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

## Validation

- Focused and full workflow contract suites passed.
- Prettier, actionlint, yamllint, cspell, markdown, and diff checks passed.
- The broader pre-push validation exceeded the local time window without a
  captured test failure.
- Adversarial review found no functional workflow issue.
