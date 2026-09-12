# Keeping a .NET Test Suite Current: Migrating MSTest and NUnit to xUnit as a Repeatable Loop

## Introduction

Moving a test suite off MSTest or NUnit is usually treated as a one-way project: a branch, a fortnight, and a
merge that everybody dreads. The translation itself is rarely what stalls it. What stalls it is that the
translation is irreversible, so it has to be finished before anyone can judge it — and a half-converted branch
is worth nothing while it rots against `main` and the team keeps writing tests in the old framework.

This article describes a different approach, and the open-source tool I built to support it:
**MsOrNUnitToXunitConverter**, a Roslyn-based converter that rewrites MSTest and NUnit test sources into xUnit
in place. The tool is on GitHub under the Apache 2.0 licence:

**https://github.com/pieroviano/NUnitToXunitConverter**

(The repository name predates MSTest support; the solution inside it is `MsOrNUnitToXunitConverter.slnx`.)

The design goal was not maximum coverage. It was to make conversion **repeatable and reversible**, so that
"keep the test suite current" becomes ordinary maintenance rather than a project somebody has to schedule.

## Prerequisites

- .NET 10 SDK
- Visual Studio 2026, JetBrains Rider, or the `dotnet` CLI
- A solution containing MSTest or NUnit test projects

```bash
git clone https://github.com/pieroviano/NUnitToXunitConverter.git
cd NUnitToXunitConverter
dotnet build MsOrNUnitToXunitConverter.slnx
```

## Why a Loop Beats a Migration

The converter is built so that **every run starts by restoring the project from its own backup.** A second run
is therefore not cumulative — it is the *same* run, against the original sources, with whatever you have
improved in between.

That single property is what changes the economics. You can run the converter, look at the diff, throw it
away, adjust something, and run it again, as many times as you like, on a project that is still shipping.
Nothing about a run is a commitment.

The workflow that falls out of it has four moves.

## Move 1: Survey Before You Touch Anything

The `list_test_projects` operation reads a solution file — either the classic `.sln` or the newer XML-based
`.slnx` — and reports which projects contain MSTest or NUnit tests, which framework each one uses, and how
many files are involved. It changes nothing on disk.

```text
list_test_projects  Acme.slnx

  Acme.Billing.Tests       NUnit     47 files
  Acme.Identity.Tests      MSTest    23 files
  Acme.Web.Tests           NUnit      9 files
  Acme.Billing                 —      0 files   (not a test project)
```

Projects that do not reference a test-framework package are never opened at all. That gate matters more than
it sounds. File-level detection is a text search, so an ordinary library that merely mentions `"[TestFixture]"`
inside a string literal would otherwise look exactly like a test suite and get rewritten.

## Move 2: Convert a Solution, or One Project at a Time

`convert_solution` walks every test project in the solution and rewrites it in place. A project that fails is
rolled back, recorded in the result, and the run carries on — one unconvertible project does not abandon the
other twelve.

For a narrower blast radius, `convert_project` takes a single `.csproj`. The command line does the same thing
outside any editor:

```bash
dotnet run --project MsOrNUnitToXunitConverter/MsOrNUnitToXunitConverter.csproj -- Acme.Billing.Tests/Acme.Billing.Tests.csproj
```

Progress goes to standard output and failures to standard error, so it drops into a CI pipeline without
ceremony. The exit code is `0` for success and `1` for a usage error or a failed conversion.

Alongside the source rewrite, the project's test-framework `PackageReference` items are swapped for the xUnit
set — `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` and `coverlet.collector` — so the project
restores and builds against the new framework without hand-editing the csproj.

## Move 3: Build, and Read What It Refused to Do

This is the move that makes the loop work, and it rests on a deliberate design decision:

> **Anything with no xUnit equivalent is left exactly as it was.**

Because the framework `using` has been rewritten to `Xunit`, every untranslated construct becomes a compile
error with a file name and a line number. The compiler hands you the worklist. Not a report to read, not a
summary to trust — a build log naming each thing a human still has to decide.

The alternative is worse than it first appears. Quietly rewriting `Assert.Multiple` into a sequence of hard
assertions would compile beautifully and silently change what your tests mean: the first failure would now
stop the test instead of collecting all of them. A conversion that fails loudly is strictly better than one
that succeeds misleadingly.

## Move 4: Undo, or Run It Again

Every project is copied to `../Old/<ProjectName>` before anything is written, and `restore_backup` puts it all
back — source files and csproj together. You can convert an entire solution at four in the afternoon purely to
see the size of the diff, and restore it before you go home.

## What Comes Back Rewritten

The translation covers the parts of both frameworks that make up the bulk of an ordinary suite: the lifecycle
hooks, the attributes, and the assertions — including NUnit's constraint model, which is the form most modern
NUnit code is actually written in.

**Test attributes and data rows.** Note that the argument order flips: NUnit reads actual-then-expected, while
every xUnit assert reads expected-then-actual.

```csharp
// Before (NUnit)
[Test]
[TestCase(1, 2)]
public void Adds(int a, int b)
{
    Assert.That(a + b, Is.EqualTo(3));
}

// After (xUnit)
[Theory]
[InlineData(1, 2)]
public void Adds(int a, int b)
{
    Assert.Equal(3, a + b);
}
```

**Lifecycle hooks.** xUnit has no setup and teardown attributes: a constructor runs before each test and
`Dispose` runs after it. The hook bodies are *moved*, not copied.

```csharp
// Before (MSTest)
[TestInitialize]
public void Before() => Open();

[TestCleanup]
public void After() => Close();

// After (xUnit) — the class also gains : System.IDisposable
public Tests() => Open();

public void Dispose() => Close();
```

`[OneTimeSetUp]` and `[ClassInitialize]` become a generated `<Class>Fixture` with `IClassFixture<>` on the test
class. NUnit's `[SetUpFixture]` and MSTest's `[AssemblyInitialize]` are assembly-wide, so they become a
`[CollectionDefinition]` with an `ICollectionFixture<>`, and every test class in the project is given the
matching `[Collection]` attribute.

**Annotations.** Several MSTest and NUnit attributes have no attribute equivalent in xUnit, but do have a
named argument or a trait:

```csharp
// Before                              // After
[Ignore("flaky")]                      [Fact(Skip = "flaky")]
[Category("slow")]                     [Trait("Category", "slow")]
[Timeout(500)]                         [Fact(Timeout = 500)]
Assert.AreEqual(1.0, x, 0.01);         Assert.Equal(1.0, x, 2);
```

That last line is worth pausing on. `AreEqual(expected, actual, delta)` is the floating-point overload, and
xUnit's third argument is a number of **decimal places**, not a tolerance. A delta that is a clean power of ten
converts exactly; any other delta is left untouched, because approximating it would change what the test
asserts.

**MSTest's `TestContext`** is replaced by xUnit's `ITestOutputHelper`: the property is removed, the field and
constructor parameter are injected, and `TestContext.WriteLine(...)` becomes `_output.WriteLine(...)`.

## What It Deliberately Leaves Alone

```csharp
// Before (NUnit)                      // After (xUnit) — still does not compile
[Test, Retry(3)]                       [Fact, Retry(3)]
public void Flaky()                    public void Flaky()
{                                      {
    Assert.Multiple(() =>                  Assert.Multiple(() =>
    {                                      {
        Assert.AreEqual(1, a);                 Assert.Equal(1, a);
        Assert.AreEqual(2, b);                 Assert.Equal(2, b);
    });                                    });
}                                      }
```

The asserts *inside* were translated, because they have exact counterparts. `Retry` and `Assert.Multiple` were
not, because xUnit has neither automatic retries nor soft assertions. The file will not compile until somebody
decides what those tests should do instead — and that is the tool working correctly.

The same applies to `[Order]`, `[Repeat]`, `[MaxTime]`, `[DeploymentItem]`, `Assert.Inconclusive`,
`Assert.Warn`, `FileAssert`, a parameterised `[TestFixture(1)]`, and an `IComparer` overload. The full coverage
reference lives in
[CONVERSION-GAPS.md](https://github.com/pieroviano/NUnitToXunitConverter/blob/master/CONVERSION-GAPS.md).

## Driving It From an AI Assistant

Besides the command line, the solution ships an **MCP server** — Model Context Protocol, the standard that
lets an AI coding assistant call external tools. Registered once at user scope, it is available in every
repository on the machine rather than only the one it lives in:

```bash
claude mcp add --scope user msornunit-to-xunit \
  /path/to/MsOrNUnitToXunitConverter.Mcp.exe
```

| Tool | What it does |
| --- | --- |
| `list_test_projects` | **Read-only.** Survey a solution and change nothing. |
| `convert_solution` | Convert every test project in a `.sln` or `.slnx`. |
| `convert_project` | Convert a single `.csproj`. |
| `restore_backup` | Put a solution, or one project, back. |

Each tool is annotated with the MCP behaviour hints, so a client knows that `list_test_projects` is safe to
call speculatively and the other three are destructive. It communicates over stdio, which means all logging is
routed to standard error — standard output carries the JSON-RPC protocol.

## Two Things to Know Before the First Run

**Mixed projects convert completely.** A project holding both MSTest and NUnit files — the normal state of a
codebase halfway through a previous migration attempt — is scanned for both frameworks, not for whichever one
it happens to find first.

**Your formatting survives.** The rewrite is formatted through Roslyn's `Formatter` rather than
`NormalizeWhitespace()`, so comments, XML documentation comments and `#pragma` directives all come through and
untouched code keeps its own layout. The diff is the conversion, not the conversion plus every line in the
file — which matters enormously when that diff is what a reviewer has to read.

## A Practical Cadence

For a codebase that nobody has time to migrate in one go: convert one project per sprint, starting with the
smallest. Each one is a self-contained pull request, the suite stays green throughout, and the projects you
have not reached yet keep working exactly as they did. Because re-running restores first, a project you
converted last month can be reconverted this month against an improved converter without any of the runs
compounding.

## Conclusion

Test-framework migration gets treated as a big-bang project because the tools make it one. Make the conversion
repeatable — restore first, back up before writing, roll back on failure — and the same work becomes something
you can do incrementally, on a branch that merges the same week.

The measure of a converter like this is not how much of the framework it covers. It is that the part it cannot
convert arrives as a build error with a line number, and that everything it did can be undone before lunch.

## References

- Source code: **https://github.com/pieroviano/NUnitToXunitConverter** (Apache 2.0)
- Coverage reference: [CONVERSION-GAPS.md](https://github.com/pieroviano/NUnitToXunitConverter/blob/master/CONVERSION-GAPS.md)
- [xUnit.net documentation](https://xunit.net/)
- [Roslyn (.NET Compiler Platform) SDK](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/)
- [Model Context Protocol](https://modelcontextprotocol.io/)
