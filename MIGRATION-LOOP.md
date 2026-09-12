# The xUnit Migration Loop

Moving a test suite off MSTest or NUnit is usually treated as a one-way project: a branch, a fortnight, and a
merge everybody dreads. It goes better as *a loop you can run on a Tuesday* — survey, convert, fix what it left
you, run it again.

Repository: <https://github.com/pieroviano/NUnitToXunitConverter>

```bash
git clone https://github.com/pieroviano/NUnitToXunitConverter.git
dotnet build MsOrNUnitToXunitConverter.slnx
```

---

## Why a loop beats a migration

The reason framework migrations stall is rarely the translation itself. It is that the translation is
irreversible, so it has to be finished before anyone can judge it. A half-converted branch is worth nothing,
and it rots against main while the team keeps writing tests in the old framework.

This converter is built the other way round. Every run starts by restoring the project from its own backup, so
a second run is not cumulative — it is the *same* run, against the original sources, with whatever you have
improved in the converter since. That one property is what turns migration into maintenance. You can run it,
look at the diff, throw it away, adjust, and run it again, as many times as you like, on a project that is
still shipping.

---

## The four moves

### 1. Survey before you touch anything

`list_test_projects` reads a `.sln` or `.slnx` and reports which projects hold MSTest or NUnit tests, which
framework each one uses, and how many files are involved. It changes nothing — it is the only tool marked
read-only — and it is the honest answer to "how big is this really?"

```
→ list_test_projects  Acme.slnx

  Acme.Billing.Tests       NUnit     47 files
  Acme.Identity.Tests      MSTest    23 files
  Acme.Web.Tests           NUnit      9 files
  Acme.Billing                 —      0 files   ← not a test project
```

Projects without a test-framework package reference are never opened. That gate matters more than it sounds:
file-level detection is a text search, and an ordinary library that merely mentions `"[TestFixture]"` in a
string literal would otherwise look exactly like a test suite.

### 2. Convert a solution, or one project at a time

`convert_solution` walks every test project and rewrites it in place. A project that fails is rolled back,
recorded, and the run carries on — one unconvertible project does not abandon the other twelve. For a narrower
blast radius, `convert_project` takes a single `.csproj`, and the command line does the same thing outside any
editor:

```bash
dotnet run --project MsOrNUnitToXunitConverter/MsOrNUnitToXunitConverter.csproj -- Acme.Billing.Tests/Acme.Billing.Tests.csproj
```

Progress goes to stdout and failures to stderr, so it drops into CI without ceremony: exit code `0` for
success, `1` for a usage error or a failed conversion.

### 3. Build, and read what it refused to do

This is the step that makes the loop work, and it depends on a deliberate design decision: **anything with no
xUnit equivalent is left exactly as it was.** Since the framework `using` has been rewritten to `Xunit`, every
untranslated construct becomes a compile error with a file and a line number.

So the compiler hands you the worklist. Not a report to read, not a summary to trust — a build log naming each
thing a human still has to decide. The alternative, quietly converting `Assert.Multiple` into a sequence of
hard assertions, would compile beautifully and change what your tests mean.

### 4. Undo, or run it again

Each project is copied to `../Old/<ProjectName>` before anything is written, and `restore_backup` puts it all
back — sources and csproj together. Nothing about the run is a commitment, which is the point: you can convert
the whole solution at four in the afternoon purely to see the size of the diff, and restore it before you go
home.

---

## What comes back rewritten

The translation covers the parts of both frameworks that make up the bulk of an ordinary suite — the lifecycle,
the attributes, and the assertions, including NUnit's constraint model, which is the form most modern NUnit
code is actually written in.

**Translated** — note the argument order flips:

```csharp
// NUnit                                    // xUnit
[Test]                                      [Theory]
[TestCase(1, 2)]                            [InlineData(1, 2)]
public void Adds(int a, int b)              public void Adds(int a, int b)
{                                           {
    Assert.That(a + b, Is.EqualTo(3));          Assert.Equal(3, a + b);
}                                           }
```

**Translated** — moved, not copied:

```csharp
// MSTest                                   // xUnit
[TestInitialize]                            public Tests() => Open();
public void Before() => Open();
                                            public void Dispose() => Close();
[TestCleanup]                               // : System.IDisposable
public void After() => Close();
```

**Translated** — the delta becomes decimal places:

```csharp
// Either framework                         // xUnit
[Ignore("flaky")]                           [Fact(Skip = "flaky")]
[Category("slow")]                          [Trait("Category", "slow")]
Assert.AreEqual(1.0, x, 0.01);              Assert.Equal(1.0, x, 2);
```

**Left for you** — xUnit has no retry and no soft assertions:

```csharp
// NUnit                                    // xUnit
[Test, Retry(3)]                            [Fact, Retry(3)]
public void Flaky()                         public void Flaky()
{                                           {
    Assert.Multiple(() =>                       Assert.Multiple(() =>
    {                                           {
        Assert.AreEqual(1, a);                      Assert.Equal(1, a);
        Assert.AreEqual(2, b);                      Assert.Equal(2, b);
    });                                         });
}                                           }
```

The last pair is the interesting one. The asserts inside were translated, because they have exact
counterparts. `Retry` and `Assert.Multiple` were not, because they do not, and the file will not compile until
somebody decides what those tests should do instead. That is the tool working correctly.

> **Where it stops on purpose.** `[Order]`, `[Repeat]`, `[MaxTime]`, `[DeploymentItem]`, `Assert.Inconclusive`,
> `Assert.Warn`, `FileAssert`, and a parameterised `[TestFixture(1)]` all stay put. So does an `IComparer`
> overload, or a floating-point delta that cannot be expressed exactly as decimal places — approximating either
> one would change what the test asserts without telling you.

[CONVERSION-GAPS.md](CONVERSION-GAPS.md) is the full coverage reference.

---

## SpecFlow projects are retargeted, not converted

SpecFlow is not a unit test framework. It runs on top of one, which is why a SpecFlow suite that fails to
convert cleanly usually fails in a confusing way: the Gherkin is fine, the bindings are fine, and the thing
that is wrong is a package reference and a line of configuration.

So a SpecFlow project is **retargeted**. The provider package is renamed in place, keeping its version, and
SpecFlow itself is left alone:

```
SpecFlow.NUnit  3.9.74   →   SpecFlow.xUnit  3.9.74
SpecFlow        3.9.74   →   SpecFlow        3.9.74      (untouched)
NUnit           3.13.3   →   xunit, xunit.runner.visualstudio, ...
```

The provider is also named in `specflow.json` or `App.config`, independently of the package, so both are set to
`xunit`. Swapping only the package gives you a project that builds and then fails at run time looking for a
provider that is not there — the worst place to find out.

The `.feature` files, the `[Binding]` classes, the `[Given]`/`[When]`/`[Then]` attributes and the hooks are all
framework-agnostic and are not touched. The only thing inside a step definition that belongs to the unit test
framework is the assertion:

```csharp
[Binding]
public class LoginSteps
{
    [BeforeScenario]                        // untouched
    public void Before() { }

    [Then(@"the total is (.*)")]            // untouched
    public void ThenTotal(int expected)
    {
        Assert.AreEqual(expected, _total);  // → Assert.Equal(expected, _total);
    }
}
```

Generated code-behind — the `Foo.feature.cs` the legacy generator writes beside `Foo.feature` — is never
rewritten, and is deleted so the build regenerates it against the new provider. A stale code-behind still
shaped for the old provider is what actually breaks the build after a retarget.

One thing to weigh before you start: SpecFlow is end-of-life, and Reqnroll is the maintained fork. Retargeting
to `SpecFlow.xUnit` is a real move, but it is a move within a framework that has stopped.

---

## Wiring it into the week

Registered once at user scope, the MCP server is available in every repository on the machine rather than only
the one it lives in — which is the difference between a tool you remember exists and one you actually reach
for.

```bash
claude mcp add --scope user msornunit-to-xunit \
  /path/to/MsOrNUnitToXunitConverter.Mcp.exe
```

| Tool | Does |
| --- | --- |
| `list_test_projects` | **read-only** — survey a solution, change nothing |
| `convert_solution` | every test project in a `.sln` or `.slnx` |
| `convert_project` | a single `.csproj` |
| `restore_backup` | put a solution, or one project, back |

A practical cadence for a codebase nobody has time to migrate in one go: convert one project per sprint,
starting with the smallest. Each is a self-contained pull request, the suite stays green throughout, and the
projects you have not reached yet keep working exactly as they did.

---

## Two things to know before the first run

**Mixed projects convert completely.** A project holding both MSTest and NUnit files — the normal state of a
codebase halfway through a previous migration attempt — is scanned for both, not for whichever it finds first.

**Your formatting survives.** Conversion goes through Roslyn's formatter rather than a whole-file reformat, so
comments, documentation comments and `#pragma` directives all come through and untouched code keeps its own
layout. The diff is the conversion, not the conversion plus every line in the file — which matters enormously
when the diff is the thing a reviewer has to read.

---

*The measure of this tool is not how much it converts. It is that the part it cannot convert arrives as a build
error with a line number, and that everything it did can be undone before lunch.*
