# NUnitToXunitConverter

NUnitToXunitConverter is a small command-line tool that automates conversion of NUnit test code to xUnit using Roslyn-based source rewrites. It scans projects and source files, detects NUnit tests, and applies syntax transformations to produce xUnit-compatible test code. The project targets .NET 10.

Features
- Project and file scanning to locate NUnit tests.
- Roslyn-based rewrites to convert common NUnit attributes and assertions to xUnit equivalents.
- Project backup and restore support before making changes.
- Designed for use in CI or interactively in your dev environment.

Requirements
- .NET 10 SDK
- Visual Studio 2026 or the __dotnet__ CLI
- The project uses `Microsoft.CodeAnalysis.CSharp` (Roslyn) for code analysis and rewrites.

Build and run
- Open the solution in Visual Studio 2026 and build, or use the CLI:
  - __dotnet build__
  - __dotnet run --project NUnitToXunitConverter\NUnitToXunitConverter.csproj -- [path-to-solution-or-folder]__

Usage (example)
- Run the executable or via __dotnet run__ and provide a solution file, project folder, or source folder. The tool will:
  - Scan projects for NUnit tests.
  - Create backups before modifying files (see `ProjectBackupService` / `ProjectRestoreService`).
  - Apply Roslyn-based rewrites implemented in `NUnitToXunitRewriter`, `XunitSyntaxRewriter`, and related classes.

Notes & limitations
- Not all NUnit constructs have a 1:1 mapping to xUnit; review changes and run tests after conversion.
- Complex custom assertion helpers or test frameworks extensions may require manual intervention.
- Always verify and run tests in CI after conversion.

Contributing
- Fork the repo, create a branch, implement changes, and open a pull request.
- Prefer small, focused commits and add unit tests for new rewrite rules where feasible.

License
- See the license file in the repository root (`LICENSE.md`).