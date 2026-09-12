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

Test parallelisation is switched off for the assembly (`MsOrNUnitToXunitConverter.Tests/AssemblyInfo.cs`). The
converter logs through the process-wide `LoggerFactoryContainer` and several tests install their own factory to
capture that output, so running classes in parallel races: a test sees another class's log lines in its sink,
or has its own dropped because the container momentarily held a factory with a higher minimum level.
Visual Studio's Test Explorer discovers the tests without this workaround.

### `ConversionServiceTests` mutates the working tree

`MsOrNUnitToXunitConverter.Tests/ConversionServiceTests.cs` runs the real converter against the committed
`MsTestProjectPoc` project, rewriting `MsTestProjectPoc/**` and refreshing the committed `Old/MsTestProjectPoc`
backup. Exclude it with `--filter "FullyQualifiedName!~Tests.ConversionServiceTests"` or
`git checkout -- MsTestProjectPoc Old` afterwards. Note the `Tests.` prefix: the bare
`!~ConversionServiceTests` also matches `SolutionConversionServiceTests`, silently skipping seven tests.

`ConversionServiceRollbackTests` also drives the real pipeline, but against a throwaway directory under the
system temp folder, so it leaves the working tree alone and can be run freely.

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
- **`MsOrNUnitToXunitConverter.Mcp`** (net10.0) — an MCP stdio server exposing the conversion as tools. Depends
  on all three of the above (it reuses `SerilogLoggerFactory` from the console project).

`ConversionService.DoConversion(csprojPath, forceMsTestProject)` is the whole pipeline, in strict order:

1. `ProjectRestoreService.RestoreBackupIfExists` — restores `../Old/<ProjectName>` over the project **before**
   scanning, so re-running is idempotent rather than cumulative. It puts back the `.cs` files, the external
   files, **and the csproj** — the csproj matters, because without it step 3 captures an already-converted
   csproj and the original is gone from the second run onwards.
2. `UnitTestsFiles(new AnyFrameworkTestDetector())` scans for both frameworks at once, so a project holding a
   mixture converts completely; `forceMsTestProject` narrows the scan to `MsUnitTestDetector`.
3. `ProjectBackupService.CreateBackup` — fresh backup into `../Old/<ProjectName>`.
4. `TestPackagesRewriter.RewritePackageReferences` — swaps the csproj's test-framework `PackageReference`
   items for the fixed xUnit set (only when the scan actually found test files).
5. `NUnitToXunitRewriter.RewriteFile` per file.

Steps 4 and 5 are the only ones that write to the project, and they run inside a `try/catch` that restores the
backup from step 3 before rethrowing — a failure half way through would otherwise leave xUnit packages against
unconverted sources. A rollback that itself fails is logged at `Error` and swallowed, because letting it escape
would replace the original cause with a symptom; `Program` then reports the real exception at `Fatal`.

`DoConversion` returns a `ConversionResult` (the files rewritten, and whether the packages changed), and
returns early — before step 3 — when the scan finds nothing. Skipping the backup in that case is what keeps a
solution-wide run from leaving an `../Old/<ProjectName>` beside every non-test project it walked past.

`SolutionConversionService` (`ProjectsLibrary/SolutionConversionService.cs`) applies all of the above across a
solution: `SolutionScanner` reads the project list out of either format — the XML `.slnx` and the classic
`.sln`, both parsed textually rather than through MSBuild — and each project is converted in turn. A project
that throws is recorded in its `ProjectConversionOutcome` and the run continues, since `ConversionService` has
already rolled that project back by then. A `.csproj` is accepted anywhere a solution is.

**The project-level gate matters.** Detectors are substring sniffs over file text, not semantic analysis, so a
library that merely mentions `"[TestFixture]"` or `"NUnit.Framework"` in a string literal looks exactly like a
test project to them — *this converter's own `ConversionClassLibrary` and `ProjectsLibrary` do*. A solution-wide
run would therefore have rewritten them. `TestPackagesRewriter.ReferencesTestPackages` is the gate: a project is
only scanned if its csproj references a package from the known test-framework list. Note what this does **not**
fix — a genuine test project whose sources discuss NUnit in string literals, such as
`MsOrNUnitToXunitConverter.Tests` itself, still matches the file-level detectors. Run `list_test_projects`
before `convert_solution` on an unfamiliar solution.

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

`XunitSyntaxRewriter` (`ProjectsLibrary/Conversion/XunitSyntaxRewriter.cs`) orchestrates the rewrite and
delegates the bulky translations to siblings in the same folder: `LifecycleMethods` (finding hooks and
resolving their scope), `FixtureBuilder` (fixture classes and the collection definition), `AttributeTranslator`
(`Skip`, `Timeout`, `Trait`, `MemberData`), `ExpectedResult`, `ConstraintTranslator` (`Assert.That`),
`StringAssertTranslator`, `AssemblySettings`, and `SourceFramework`. Between them:
`NUnit.Framework`→`Xunit`, `[Test]`→`[Fact]`, `[TestCase]`→`[InlineData]`, `[TestFixture]`/`[SetUpFixture]`/
`[OneTimeSetUp]` dropped; `[SetUp]` body folded into a generated constructor, `[TearDown]` into a `Dispose`
method plus `System.IDisposable`, `[OneTimeSetUp]` into a generated `<Class>Fixture` class plus
`IClassFixture<>`. `Assert.AreEqual/IsTrue/...` map to the xUnit names; a 3-argument assert sets
`_needsOutputHelper`, which injects an `ITestOutputHelper _output` field and ctor parameter and wraps the
assert in `try/catch { _output.WriteLine(message); throw; }` — but only when the third argument *looks* like a
message (`IsFailureMessage`), because `AreEqual(expected, actual, delta)` is the floating-point overload and
converts to xUnit's decimal-places argument instead.

Three rules about lifecycle hooks are easy to undo by accident:

- **Hooks are matched by signature, never by node reference.** They have to be recognised on the *original*
  class, where the attributes still are, but removed from the *rewritten* member list, whose nodes are
  different instances — `VisitAttributeList` rebuilds every attribute list unconditionally. Comparing
  references across that boundary silently never matched, so every hook stayed in place, attribute and all,
  while its body was *also* copied into the generated constructor.
- **Per-class state is reset on entry to each class.** Left set, one class's setup and teardown were grafted
  onto the next class in the same file, calling methods it did not have.
- **Presence of the hook decides, not the size of its body.** An empty `[OneTimeSetUp]` still means the class
  declared one, and dropping its fixture would change what the class declares.

`[OneTimeSetUp]`/`[ClassInitialize]` become a generated `<Class>Fixture`; `[SetUpFixture]`/`[AssemblyInitialize]`
are assembly-wide and become a `[CollectionDefinition]` plus `ICollectionFixture<AssemblyFixture>`, with every
test class in the project joining that collection. That last part is why `OneTimeSetUpContext` is shared across
the whole project by `ConversionService`: the file declaring the hooks is not the file that has to join.

Once the data-row attributes have become `[InlineData]`, `VisitMethodDeclaration` promotes the method to
`[Theory]` — xUnit discovers data-driven tests through `[Theory]` and rejects `[Fact]` beside `[InlineData]`.
MSTest always pairs `[DataRow]` with `[TestMethod]` so there is a `[Fact]` to rename, but NUnit's `[TestCase]`
stands on its own, in which case the `[Theory]` is added outright. `VisitCompilationUnit` is where the
generated code's own namespaces are imported: `Xunit.Abstractions` when `_needsOutputHelper` was set (that is
where `ITestOutputHelper` lives, not `Xunit`), and `System.Linq` when `_needsLinq` was — each added through
`WithUsing`, which is a no-op if the file already has it.

`NUnitToXunitRewriter` formats through Roslyn's `Formatter`, not `NormalizeWhitespace()`: the latter rewrote
the whole file, discarding preprocessor directives and making every conversion a whole-file diff. For the same
reason `VisitInvocationExpression` and `VisitExpressionStatement` apply `WithTriviaFrom(node)` to anything they
replace — a constructed node carries no trivia, so without it the comment or `#pragma` attached to the
statement disappears with it.

It handles MSTest in the same pass — `[TestMethod]`→`[Fact]`, `[DataRow]`→`[InlineData]`, `[TestClass]` dropped,
`[TestInitialize]`/`[TestCleanup]` folded into the constructor/`Dispose` like their NUnit counterparts, and the
MSTest `using` mapped to `Xunit`. `[ExpectedException(typeof(T))]` has no xUnit equivalent, so
`VisitMethodDeclaration` lifts the entire method body into the delegate of `Assert.Throws<T>(() => { … })`, or
`await Assert.ThrowsAsync<T>(async () => { … })` when the method is async — the body moves across untouched, so
awaits it already contains keep working. An unawaited `Assert.ThrowsAsync` inside an async test also gets its
`await` added, since unawaited it can never fail the test.

`CollectionAssert` (both frameworks) is translated by `RewriteCollectionAssert`, driven by the
`CollectionAssertArities` table — the member name mapped to how many leading arguments its xUnit rendering
consumes, so everything past them is the failure message and goes through the same `_output.WriteLine`
try/catch as the scalar asserts (format arguments included, since `ITestOutputHelper.WriteLine` has a matching
overload). `AreEqual`/`AreNotEqual`/`IsEmpty`/`IsNotEmpty` are plain renames; `AreEquivalent` becomes
`Assert.Equivalent` (order-insensitive, xUnit ≥ 2.5); `Contains`/`DoesNotContain` additionally swap their two
arguments, because `CollectionAssert` names the collection first and xUnit names the element first.
`AllItemsAreNotNull`, `AllItemsAreInstancesOfType(…, typeof(T))` and `IsSubsetOf` expand into
`Assert.All(collection, item => …)`, and `AllItemsAreUnique` into
`Assert.Empty(c.GroupBy(item => item).Where(group => group.Count() > 1))` — which sets `_needsLinq`, appending
`using System.Linq;` to the compilation unit if it is not already there. That grouping form is deliberate: the
obvious `Assert.Equal(c.Distinct().Count(), c.Count())` names the collection twice, so
`AllItemsAreUnique(GetItems())` would call `GetItems()` twice.
Members with no xUnit counterpart (`AreNotEquivalent`, `IsNotSubsetOf`, `IsOrdered`, and
`AllItemsAreInstancesOfType` given a `Type`-valued expression rather than a `typeof`) are deliberately left
untouched so the converted project fails to compile on them instead of asserting something weaker.

Both frameworks also put an `IComparer` where the failure message goes, and a syntax-only rewriter cannot tell
the two apart by type. `IsFailureMessage` therefore only accepts what is *certainly* text — a string literal,
an interpolated string, or a `+` concatenation or parenthesised expression built from one. Anything else in
that position is assumed to be a comparer and the whole call is left untouched, rather than converted into an
`_output.WriteLine(comparer)` that does not compile. The cost is that
`CollectionAssert.AreEqual(a, b, messageVariable)` is not converted either; the benefit is that nothing is
silently mistranslated.

### Known gaps in the pipeline (observed, not hypothetical)

[CONVERSION-GAPS.md](CONVERSION-GAPS.md) is the coverage reference, observed by running the real CLI rather
than inferred: what the converter translates, what it does not, and why. Read it before extending the rewriter
— in particular, the rule that anything with no xUnit equivalent is left **untouched on purpose**, so the
converted project fails to compile on it rather than being given something weaker that silently passes.

- Data-generation attributes (`[Values]`, `[Range]`, `[Random]`, `[Combinatorial]`) are not converted: they
  generate a cartesian product that xUnit has no equivalent for, so converting means computing the rows.
- `[TestCaseSource]`/`[DynamicData]` have their *attribute* translated to `[MemberData]` but not the source
  member, which commonly yields `TestCaseData` or `object[]` where xUnit wants `IEnumerable<object[]>`.
- A test class inheriting its lifecycle from a base class is not handled, and neither is a generic fixture.
- A project declaring assembly-wide setup twice cannot merge them: the first wins, the second is reported at
  `Warn`.
- `MsTestToNUnitContent` is not part of the conversion pipeline. It ships and is covered by
  `MsTestsToNunit.Tests`, but `NUnitToXunitRewriter` does the whole job in Roslyn; putting the regex pass back
  in front of it would intercept `[TestMethod, ExpectedException(...)]` before the syntax rewriter saw it.

## The MCP server

`MsOrNUnitToXunitConverter.Mcp` exposes the conversion over MCP on stdio. It is registered at **user scope**,
so it is available in every repository rather than only this one, and it follows the same shape as the other
CommonLibrary MCP servers (`rewrite-git-history`, `window-capture`): an absolute path to the **Release**
executable.

```bash
msbuild MsOrNUnitToXunitConverter.Mcp/MsOrNUnitToXunitConverter.Mcp.csproj /p:Configuration=Release

claude mcp add --scope user msornunit-to-xunit \
  "D:\CommonLibrary\MsOrNUnitToXUnitConverter\MsOrNUnitToXunitConverter.Mcp\bin\Release\net10.0\MsOrNUnitToXunitConverter.Mcp.exe"
```

`claude mcp get msornunit-to-xunit` shows it, `claude mcp remove msornunit-to-xunit -s user` undoes it.

Two things that follow from running the built exe. It is **not** `dotnet run`: that writes build output to
stdout, and stdout is the JSON-RPC channel. And because the registration points at `bin/Release`, a rebuilt
Release binary is picked up on the next session, while `msbuild /t:clean` leaves the server unable to start
until Release is built again.

There is deliberately no `.mcp.json` in the repo. A project-scoped entry of the same name conflicts with the
user-scoped one ("defined in multiple scopes with different endpoints") and is redundant, since user scope
already covers this repository. Add one only if the registration should travel with the repo to other people,
and then give it a distinct name.

Four tools, all in `ConversionTools.cs`, each a thin wrapper over `SolutionConversionService`:

| Tool | Hints | Does |
| --- | --- | --- |
| `list_test_projects` | read-only | Reports the test projects of a solution and their framework, changing nothing. |
| `convert_solution` | destructive | Converts every test project in a `.sln`/`.slnx`. |
| `convert_project` | destructive | Converts one `.csproj`, like the CLI. |
| `restore_backup` | destructive | Restores `../Old/<ProjectName>` over a solution's projects, undoing a run. |

**Everything written to stdout corrupts the protocol.** `Program.cs` deals with both logging stacks: Serilog is
built with `standardErrorFromLevel: LogEventLevel.Verbose` so every level goes to stderr, and
`builder.Logging.ClearProviders()` removes the console provider `Host.CreateApplicationBuilder` installs, which
writes to stdout. Anything added later — a `Console.WriteLine`, another logging provider — has to respect that.

Validation failures are thrown as `McpException`: it is the one exception type whose message the SDK passes
through to the client, everything else arriving as a bare `An error occurred invoking '<tool>'`.

The tools return the domain records (`SolutionConversionResult`, `ConversionResult`, `TestProjectInfo`), which
the SDK serialises to JSON, so a client gets structured results rather than prose to parse.

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

## Logging

Logging goes through `LoggerFactoryContainer.Instance.LoggerFactory` (Net4x.BaseTypes) and libraries assume it
is already configured; `ConversionService.DoConversion` reads it once into a local and reports each step of the
pipeline. `Program` installs `SerilogLoggerFactory.Instance`
(`MsOrNUnitToXUnitConverter/Logging/SerilogLoggerFactory.cs`), a `System.Diagnostics.ILoggerFactory` whose
`GetLogger` hands out a `SerilogLogger` writing to Serilog's console sink — the same shape
`CoreLibrary.Logging`'s own `LoggerFactoryInternal` uses. Serilog therefore stays entirely inside the console
project: neither packaged library references it, so their nupkg dependencies are unaffected.

Two traps in that abstraction, both learned the hard way:

- **`MinimumLoggingLevel` filters with a strict `>`, and `DiagnosticLevel` is not a small ordered enum** — its
  members are spread across the int range with `None = int.MinValue` and `Default = 0`. `ConsoleLoggerFactory`
  defaults to `Error`, so while `Program` installed it **the converter printed nothing at all**: every step is
  logged at `Information`. `SerilogLoggerFactory` defaults to `DiagnosticLevel.Default` instead, which lets
  every level through to Serilog. Do not "fix" that to `Information` — it would silence `Info` again.
- **The check is made against the factory installed in the container, not the one the logger came from.**
  `LoggerWithEvents.DoLog` (the synchronous `ILogger.Log` path) consults
  `LoggerFactoryContainer.Instance.LoggerFactory.MinimumLoggingLevel`; `DoLogAsync` does not. So a
  `SerilogLoggerFactory` that has not been installed will appear to swallow everything below `Fatal`, and tests
  that exercise the synchronous path have to install it first — see
  `MsOrNUnitToXunitConverter.Tests/SerilogLoggerFactoryTests.cs`.

`Main` wraps `DoConversion` in a `try/catch` that logs the exception at `Fatal` and returns 1, so a failure is
reported through the same log as every other step instead of crashing the process. Nothing is lost by not
letting it escape: the output template ends in `{Exception}`, so the stack trace is still printed, and the
console sink is configured with `standardErrorFromLevel: LogEventLevel.Error` — the progress lines go to
stdout, `Error`/`Fatal` and their stack traces to stderr, which is where a failure belongs. Exit codes are 0 on
success and 1 for both a usage error and a failed conversion.

`SerilogLogger` adds no filtering of its own: it maps `DiagnosticLevel` onto `LogEventLevel`, prefixes the
title when there is one, passes the exception to Serilog rather than flattening it into the text, and routes
through `DoLog`/`DoLogAsync` so the `BeforeLogging`/`Logged` events keep working. The message is already
formatted by the time it arrives, so the template is `{Message:l}` — the `:l` is what keeps Serilog from
quoting and escaping a Windows path.

## Versions

All package versions come from `Directory.NuGet.props` via MSBuild properties (`Net4xMsTestsVersion`,
`WrapperGeneratorVersion`, `Net4xBaseTypesVersion`, `NuGetUtilityVersion`, …) combined with
`$(VersionBuildSuffix)` (`*` — floating). That file is a large shared catalogue reused across the
CommonLibrary repos; change versions there, not in individual csproj files. The exception is
`MsOrNUnitToXunitConverter.csproj`, which also pins `Net4x.StandardTypesWrappers` (`1.2.0.1`),
`Microsoft.CodeAnalysis.CSharp` (`5.9.0`), `Serilog` (`4.3.1`) and `Serilog.Sinks.Console` (`6.1.1`) directly.

The repo's own packages are versioned in the root `Directory.Build.Props` as
`<Version>$(VersionPrefix).$(VersionBuildNumber)</Version>` — `1.0.0` plus `yy` + day-of-year, so the version
changes daily. The day part must stay out of `VersionSuffix`: MSBuild reads that as a *prerelease label*, which
made `Version` evaluate to `1.0.0-26255` while the packages were still stamped `1.0.0.26255`, so every
`ProjectReference` became a prerelease dependency of a stable package and every build warned `NU5104`.

Note that packing is skipped when the day's version is already in the shared feed, so a build can legitimately
produce no `.nupkg` at all; `msbuild <project> /t:pack /p:PackageOutputPath=<dir>` forces one for inspection.
