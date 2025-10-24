# Crystal Report Archiver (WPF, .NET Framework4.8)

This app creates a versioned archive for changes to Crystal Reports (`.rpt`) files. It copies the original and changed files into a time-stamped version folder, computes a binary diff summary, and optionally performs deeper Crystal-level analysis when the SAP Crystal runtime is available.

## Purpose

The Crystal Report Archiver is designed to help users track and manage changes in Crystal Reports by creating detailed archives of report versions. This is particularly useful for businesses and individuals who need to maintain a history of report changes, ensure report integrity, and analyze differences between report versions.

## Features

- **Versioned Archiving:** Automatically organizes report versions in a time-stamped folder structure.
- **Binary Diff Summary:** Provides a high-level overview of changes between report versions, including SHA-256 hashes, size differences, and percentage changes.
- **Crystal-Level Analysis:** When the SAP Crystal runtime is available, the app analyzes report elements such as selection formulas, formula fields, parameters, and tables. This provides deeper insight into how reports differ at a structural level.
- **Comprehensive Reporting:** Each archive includes a `readme.html` file summarizing the binary and Crystal-level analysis, and a `metadata.json` file for programmatic access to report attributes and analysis results.

## Requirements

- **Software:**
  - .NET Framework 4.8
  - Optional: SAP Crystal Reports runtime (CRRuntime_64bit_13_0_xx) for advanced Crystal-level analysis.
- **Hardware:** No specific hardware requirements, but sufficient disk space is needed for archiving reports.

## Usage

To use the Crystal Report Archiver, follow these steps:

1. Run the application on a computer with the required software installed.
2. In the app, select the original and changed `.rpt` files you want to compare.
3. Specify the archive root path where the versioned folders will be created (e.g., `C:\ReportsArchive`).
4. Optionally, enter a message to document the reason for the archive or any other notes.
5. Click the "Create Archive" button to generate the archive.
6. After the archive is created, click "Open Archive Folder" to quickly access the newly created archive location.

## Outputs

For each version of a report, the archiver produces a folder containing:

- `original/` and `changed/` directories with the copied report files.
- `readme.html` file with a summary of the binary and Crystal-level analysis.
- `metadata.json` file containing detailed information about the report versions, including report key, timestamps, sizes, hashes, and analysis results.
- Optionally, a `crystal_diff.json` file may be produced if a helper process for detailed Crystal analysis is integrated in the future.

## Notes

- The app is capable of creating archives and binary diffs even if the Crystal runtime is not installed. In such cases, the Crystal-level analysis is skipped, but the basic archiving functionality remains intact.
- A stub for `CrystalHelperRunner` is included in the codebase. This is intended for a potential external .NET Framework helper process that could be developed later to extend the app's functionality, particularly for Crystal-level analysis.

## Build Instructions

To build the Crystal Report Archiver from the source:

1. Download the source code from the repository.
2. Open the `ironmanx-04-2/ironmanx-04-2.csproj` file in Visual Studio.
3. Ensure that the project is set to target .NET Framework 4.8.
4. Build the project using Visual Studio's build tools.

Once built, you can run the application from the output directory or publish it for deployment.