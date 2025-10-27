# Crystal Report Versioner (WPF, .NET Framework4.8)

This app versions and archives changes to Crystal Reports (`.rpt`) files, with easy push and revert flows.

## Features

- **Push Changes:** Archive diff of Original vs Changed report into `archive/<reportName>/vyyyyMMdd-HHmmss/`, generate `changes.html` and `metadata.json`, zip the version, and replace the Original file with the Changed file.
- **Auto-clean:** If Original and Changed are in the same folder, the Changed file is removed after replacement to avoid duplicates.
- **Revert to Last Change:** Restore the previous archived Original back to the live location and record the revert in changelog.
- **Master changelog:** `ChangeLog-<reportName>.html` in the report’s archive root, appending a section per push/revert with links to the created zip.
- **Optional deep Crystal analysis:** When SAP Crystal runtime is available, shows selection formula, formula fields, parameters, and table changes.

## Requirements

- **Software:**
  - .NET Framework 4.8
  - Optional: SAP Crystal Reports runtime (CRRuntime_64bit_13_0_xx) for deep analysis.
- **Hardware:** No specific hardware requirements, but sufficient disk space is needed for archiving reports.

## How to use

To use the Crystal Report Versioner, follow these steps:

1. Select the Original `.rpt` and Changed `.rpt` files.
2. Archive root auto-fills to `<original directory>\archive` or set it manually.
3. Enter a message (optional).
4. Click Push Changes.
5. To undo, select the Original `.rpt` path and click Revert to Last Change.

## Outputs per push

For each push of a report, the versioner produces:

- A zip file: `<archive root>/<reportName>/<reportName>-<timestamp>.zip`
- `changes.html`, `metadata.json` inside each version (pre-zip)
- Master `ChangeLog-<reportName>.html` updated in `<archive root>/<reportName>/`

## Notes

- If Crystal runtime is unavailable, push still works; Crystal analysis is skipped.
- Folder picker provided for selecting archive root; defaults to `<original>\archive`.
- UI uses a dark “hyper-clean” theme with flat, modern buttons.