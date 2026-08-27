# Failed Tests Exporter

## Overview

The Failed Tests Exporter is an editor utility that hooks into the Unity Test Runner API to automatically capture failed test results. When a test run completes, it records each failure's name, message, and stack trace, and can export them to a timestamped text file in a configurable directory (defaults to the project root).

This is especially useful for CI/CD pipelines where you need a machine-readable artifact of test failures, or for tracking intermittent test failures across multiple runs.

## Setup

The Failed Tests Exporter is **disabled by default**. To enable it:

1. Open **Project Settings** (`Edit > Project Settings`)
2. Navigate to **Wallstop Studios > Unity Helpers**
3. Expand the **Failed Tests Exporter** section
4. Check **Enable Failed Tests Exporter**

The exporter activates immediately when the setting is toggled. No domain reload is required.

### Output Directory

By default, failed test result files are written to the **project root**. You can configure a different output directory:

1. In the **Failed Tests Exporter** settings section, find the **Output Directory** row
2. Click **Browse…** to open a folder picker dialog
3. Select any folder **within your project**; the path is stored as a relative path from the project root
4. Click the **×** button to clear the setting and revert to the project root

The output directory field is read-only to prevent typos; use the **Browse…** button to select a folder visually.

> **Path Validation**
>
> The output directory is validated on every use:
>
> - If the configured directory no longer exists (e.g., it was renamed or deleted), the exporter automatically falls back to the project root
> - Absolute paths, paths containing `..`, and paths outside the project root are rejected
> - Invalid paths are automatically corrected when settings are loaded

## Usage

### Automatic Export

When enabled, the exporter automatically:

1. Clears previous failures when a new test run starts
2. Records each individual test failure (name, message, stack trace)
3. Exports all failures to a timestamped file when the test run completes

The output file is written to the configured output directory (or the project root if none is set) with the format `failed-tests-YYYY-MM-DD-HHmmss.txt`.

### Manual Export

You can manually export or clear captured failures using the menu items:

- **Tools > Wallstop Studios > Unity Helpers > Export Failed Tests**: Writes captured failures to a text file
- **Tools > Wallstop Studios > Unity Helpers > Clear Failed Tests**: Clears the in-memory failure list

Both menu items are only enabled when there are captured failures.

### Output Format

Each failure in the exported file includes:

```text
TEST_FAILURE_1
Name: MyNamespace.MyTestClass.MyTestMethod
Message: Expected 42 but was 0
Stack Trace:
at MyNamespace.MyTestClass.MyTestMethod() in /path/to/file.cs:line 25

---

TEST_FAILURE_2
Name: MyNamespace.MyOtherClass.AnotherTest
Message: Object reference not set
Stack Trace:
(no stack trace)
```

## API Reference

> **Note:** The `FailedTestsExporter` class and its nested `FailedTestInfo` struct are `internal` and primarily intended for use within the Unity Helpers assembly. External consumers interact with this feature through the menu items and the settings UI described above.

### FailedTestsExporter

| Member        | Type                            | Description                                                           |
| ------------- | ------------------------------- | --------------------------------------------------------------------- |
| `Instance`    | `FailedTestsExporter`           | Static reference to the active exporter instance (null when disabled) |
| `Failures`    | `IReadOnlyList<FailedTestInfo>` | Read-only list of captured test failures                              |
| `IsEnabled()` | `bool`                          | Whether the exporter is enabled in Unity Helpers settings             |

### FailedTestInfo

| Field        | Type     | Description                       |
| ------------ | -------- | --------------------------------- |
| `name`       | `string` | Fully qualified test name         |
| `message`    | `string` | Failure message (or empty string) |
| `stackTrace` | `string` | Stack trace (or empty string)     |

## See Also

- [Editor Tools Guide](./editor-tools-guide.md): Complete reference for all Unity Helpers editor tools
- [Inspector Settings](../inspector/inspector-settings.md): Configuring Unity Helpers project settings
