# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Roslyn-based command-line tool that rewrites MSTest / NUnit test sources into xUnit **in place**, on a
single `.csproj` at a time. It backs the project up first so a run is repeatable.

```
dotnet run --project MsOrNUnitToXunitConverter/MsOrNUnitToXunitConverter.csproj -- <path-to-csproj>
```

`Program.Main` exits 1 with a usage message if the argument is missing or not an existing file.
(The root `README.md` still uses the pre-rename `NUnitToXunitConverter` paths — those commands are stale.)

## Build and test

```bash
dotnet build MsOrNUnitToXunitConverter.slnx
```

Requires the .NET 10 SDK. `Packages/` is a symlink to a shared local NuGet feed (`/d/Starb/Packages`,
registered in `NuGet.config`); `ConversionClassLibrary` and `ProjectsLibrary` have
`GeneratePackageOnBuild=true`, so **every build publishes new `Net4x.*` nupkgs into that shared feed**.
Package versions are `$(VersionPrefix).$(VersionSuffix)` where the suffix is `yy` + day-of-year, so the
version changes daily.

### Running tests requires copying the xUnit VSTest adapter

`Net4x.XunitTests` declares `xunit.runner.visualstudio` with `exclude="Build,Analyzers"`, which strips the
adapter's build assets — so `dotnet test` reports *"No test is available"* and `--test-adapter-path` does
not fix it. Copy the adapter into each test project's output directory once after a build:

```bash
ADAPTER="$HOME/.nuget/packages/xunit.runner.visualstudio/3.1.5/build/net8.0/xunit.runner.visualstudio.testadapter.dll"
cp "$ADAPTER" MsOrNUnitToXunitConverter.Tests/bin/Debug/net10.0/
cp "$ADAPTER" MsTestsToNunit.Tests/bin/Debug/net8.0-windows/

dotnet test MsOrNUnitToXunitConverter.Tests/MsOrNUnitToXunitConverter.Tests.csproj --no-build
dotnet test MsTestsToNunit.Tests/MsTestsToNunit.Tests.csproj --no-build
```

Single test / class: `--filter "FullyQualifiedName~ProjectScannerTests"`.
Visual Studio's Test Explorer discovers the tests without this workaround.

### `ConversionServiceTests` mutates the working tree

`MsOrNUnitToXunitConverter.Tests/ConversionServiceTests.cs` runs the real converter against the committed
`MsTestProjectPoc` project, rewriting `MsTestProjectPoc/**` and refreshing the committed `Old/MsTestProjectPoc`
backup. Exclude it (`--filter "FullyQualifiedName!~ConversionServiceTests"`) or `git checkout -- MsTestProjectPoc Old`
afterwards.

Separately, *building* `MsTestProjectPoc` at all rewrites its `.csproj` (dropping the two inline
`NuGet.Utility` `<Import>` lines) and leaves untracked `Directory.Build.Props`/`.Targets` beside it. That is
`Net4x.NuGetUtility`'s own build targets, not the converter — expect it after any `dotnet build` of the
solution, and `git checkout -- MsTestProjectPoc/MsTestProjectPoc.csproj` to undo.

## Architecture

Three projects, layered by dependency:

- **`ConversionClassLibrary`** (net8.0) — interfaces (`IProjectScanner`, `IUnitTestDetector`, `IUnitTestsFiles`,
  `IMsTestToNUnitContent`), the MSTest→NUnit *text* transformer `MsTestToNUnitContent`, and `OneTimeSetUpContext`.
  No dependency on the other projects.
- **`ProjectsLibrary`** (net8.0) — the orchestration and the Roslyn rewriters. Depends on `ConversionClassLibrary`.
- **`MsOrNUnitToXunitConverter`** (net10.0) — thin `Main` that wires the console logger and calls `ConversionService`.

`ConversionService.DoConversion(csprojPath, forceMsTestProject)` is the whole pipeline, in strict order:

1. `ProjectRestoreService.RestoreBackupIfExists` — restores `../Old/<ProjectName>` over the project **before**
   scanning, so re-running is idempotent rather than cumulative.
2. `UnitTestsFiles(new NUnitTestDetector())` scans; if it finds nothing (or `forceMsTestProject`), it rescans
   with `MsUnitTestDetector`.
3. `ProjectBackupService.CreateBackup` — fresh backup into `../Old/<ProjectName>`.
4. `TestPackagesRewriter.RewritePackageReferences` — swaps the csproj's test-framework `PackageReference`
   items for the fixed xUnit set (only when the scan actually found test files).
5. `NUnitToXunitRewriter.RewriteFile` per file.

Detectors are substring sniffs over file text, not semantic analysis.

`ProjectScanner.GetCsFiles` parses the csproj XML for `Compile Include`/`Remove` (expanding a small set of
MSBuild properties and `*`/`**` globs), and falls back to an SDK-style recursive `*.cs` scan when there are no
`Compile` items; `obj/` is always excluded. `UnitTestsFiles` sorts files containing `[OneTimeSetUp]` first
(by ordering their key as `"_____.cs"`), because `XunitSyntaxRewriter` names the generated `IClassFixture<>`
from the first such class it sees.

Backups of files *outside* the project directory go to `Old/<ProjectName>/_ExternalFiles/`, with the drive
colon encoded as `_`; `ProjectRestoreService.DecodeOriginalPath` reverses that.

`TestPackagesRewriter` (`ProjectsLibrary/TestPackagesRewriter.cs`) is the only code that *writes* a csproj.
It matches `PackageReference` items by id against a known list (`MSTest.*`, `Microsoft.VisualStudio.TestPlatform.*`,
`NUnit*`, `xunit*`, `coverlet.*`, `Microsoft.NET.Test.Sdk`, `Net4x.MsTests`, `Net4x.XunitTests`), drops them,
and writes `XunitPackages` into the position the first match's `ItemGroup` occupied — everything else in the
project is preserved. It also retargets a project-wide `<Using Include="NUnit.Framework" />` (or the MSTest
equivalent) to `Xunit`. The versions live in the settable `XunitPackages` property, not inline in the logic.
Because it round-trips through `XDocument`, the whole csproj is re-indented to two spaces on first run; a
second run over its own output is a no-op and returns `false`.

`XunitSyntaxRewriter` (`ProjectsLibrary/Conversion/XunitSyntaxRewriter.cs`) is the substantive rewriter:
`NUnit.Framework`→`Xunit`, `[Test]`→`[Fact]`, `[TestCase]`→`[InlineData]`, `[TestFixture]`/`[SetUpFixture]`/
`[OneTimeSetUp]` dropped; `[SetUp]` body folded into a generated constructor, `[TearDown]` into a `Dispose`
method plus `System.IDisposable`, `[OneTimeSetUp]` into a generated `<Class>Fixture` class plus
`IClassFixture<>`. `Assert.AreEqual/IsTrue/...` map to the xUnit names; a 3-argument assert sets
`_needsOutputHelper`, which injects an `ITestOutputHelper _output` field and ctor parameter and wraps the
assert in `try/catch { _output.WriteLine(message); throw; }`.

It handles MSTest in the same pass — `[TestMethod]`→`[Fact]`, `[DataRow]`→`[InlineData]`, `[TestClass]` dropped,
`[TestInitialize]`/`[TestCleanup]` folded into the constructor/`Dispose` like their NUnit counterparts, and the
MSTest `using` mapped to `Xunit`. `[ExpectedException(typeof(T))]` has no xUnit equivalent, so
`VisitMethodDeclaration` lifts the entire method body into the delegate of `Assert.Throws<T>(() => { … })`, or
`await Assert.ThrowsAsync<T>(async () => { … })` when the method is async — the body moves across untouched, so
awaits it already contains keep working. An unawaited `Assert.ThrowsAsync` inside an async test also gets its
`await` added, since unawaited it can never fail the test.

### Known gaps in the pipeline (observed, not hypothetical)

- `RewriteFile` ends with `NormalizeWhitespace()`, which reformats the entire file and discards preprocessor
  trivia — `#pragma warning disable` lines are dropped while their matching `restore` survives.
- Assembly-level MSTest settings are not converted. `MsTestProjectPoc/MSTestSettings.cs`
  (`[assembly: Parallelize(...)]`) contains no `[TestClass]`/`[TestMethod]`, so no detector selects it and it is
  left referencing MSTest types after the packages are swapped — the converted project then fails to compile
  until that file is deleted by hand.
- `MsTestToNUnitContent` is no longer part of the conversion pipeline. It still ships and is still covered by
  `MsTestsToNunit.Tests`, but `NUnitToXunitRewriter` does the whole job in Roslyn now; the old regex pre-pass
  would otherwise have intercepted `[TestMethod, ExpectedException(...)]` before the syntax rewriter saw it.

## Wrapper-type conventions (easy to get wrong)

`Net4x.StandardTypesWrappers` injects global usings that **alias BCL static types to interfaces**:

```
File → System.IO.IFile,  Path → System.IO.IPath,  Directory → System.IO.IDirectory,
Console → System.IConsole,  Assembly → System.Reflection.IAssembly, Thread, Process, Stream, ...
```

Consequences when editing any file in this repo:

- `File.ReadAllText(...)` is an *instance* call on an injectable property, conventionally declared as
  `public File File { get; set; } = System.IO.InputOutput.Instance.File;` — that is the seam the tests mock.
- Anything genuinely static must be fully qualified: `System.IO.Path.DirectorySeparatorChar`,
  `System.IO.Path.Combine(...)`. Existing code does this deliberately; don't "simplify" it away.
- New services should follow the same pattern (settable `File`/`Path`/`Directory` properties, behaviour behind
  an interface in `ConversionClassLibrary/Interfaces`) so they stay testable.

Tests use xUnit + NSubstitute, substituting those properties directly
(`sut.File = Substitute.For<IFile>()`) rather than through a DI container.

Logging goes through `LoggerFactoryContainer.Instance.LoggerFactory` (Net4x.BaseTypes); `Program` sets it to
`ConsoleLoggerFactory.Instance`, and libraries assume it is already configured.

## Versions

All package versions come from `Directory.NuGet.props` via MSBuild properties (`Net4xMsTestsVersion`,
`WrapperGeneratorVersion`, `Net4xBaseTypesVersion`, `NuGetUtilityVersion`, …) combined with
`$(VersionBuildSuffix)` (`*` — floating). That file is a large shared catalogue reused across the
CommonLibrary repos; change versions there, not in individual csproj files. The exception is
`MsOrNUnitToXunitConverter.csproj`, which pins `Net4x.StandardTypesWrappers` to `1.2.0.1` directly.
