# MsTestProjectPoc

Input fixture inside **MsOrNUnitToXunitConverter**, a Roslyn-based command-line tool that rewrites
MSTest/NUnit test sources into xUnit in place, one `.csproj` at a time, taking a restorable backup first.

**Not a NuGet package**, and not a library: `GeneratePackageOnBuild` is `False`, and `IsTestProject` is
`false` so the build does not collect it as a test project even though it contains MSTest code. (The csproj
still sets `PackageReadmeFile` and packs this file with a `None Include="README.md" Pack="true"` item; those
are copy-pasted from the two packable projects and never run.)

Target framework `net10.0`, `LangVersion=latest`, `ImplicitUsings` and `Nullable` enabled. It references
`Net4x.MsTests` plus `Net4x.NuGetUtility` (`PrivateAssets="All"`, with imports of its props and targets), and
declares a project-wide `<Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />`, so the sources
carry no test-framework `using` of their own. Contents:

- `MathTests.cs` — namespace `TestProjectPoc` (not `MsTestProjectPoc`); one `[TestClass]` / `[TestMethod]`
  with a single `Assert.AreEqual(2, 1 + 1)` between `#pragma warning disable MSTEST0032` and its `restore`.
- `MSTestSettings.cs` — `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`. It contains none of
  `MsUnitTestDetector`'s markers, so it is never among the detected files and is never rewritten.

## Its contents are expected to be overwritten

`MsOrNUnitToXunitConverter.Tests/ConversionServiceTests.cs` resolves this `.csproj` and calls the real
`ProjectsLibrary.ConversionService.DoConversion(csprojPath, forceMsTestProject: true)` against it. That is a
live run of the production pipeline, so running that test:

1. restores every `*.cs` from `../Old/MsTestProjectPoc` over this directory (`ProjectRestoreService`);
   nothing else is restored — the backed-up `.csproj`, this README and `bin`/`obj` are left as they are;
2. scans with `NUnitTestDetector`, then — because `forceMsTestProject` is `true` — rescans with
   `MsUnitTestDetector`, which is also what sets the `hasMsTests` flag that enables the MSTest pre-pass in
   `NUnitToXunitRewriter`;
3. refreshes the committed `../Old/MsTestProjectPoc` backup from the current directory
   (`ProjectBackupService`, a recursive copy of the whole project directory, `bin` and `obj` included);
4. rewrites the detected `.cs` files in place with `NUnitToXunitRewriter` and re-emits them through Roslyn
   `NormalizeWhitespace`, so formatting changes too.

**After a run this project does not compile.** The rewriter only renames assertions — `Assert.AreEqual`
becomes `Assert.Equal` — while `[TestClass]` and `[TestMethod]` survive untouched, because the pipeline calls
`MsTestToNUnitContent.TransformMethods` (the `[TestMethod, ExpectedException]` regex) and never `Transform`,
which is where the MSTest attribute replacements live. The result is an MSTest-attributed file calling
`Assert.Equal`, which MSTest's `Assert` does not define, in a project with no xUnit reference. Since this
project is listed in `MsOrNUnitToXunitConverter.slnx`, a solution build fails until the tree is restored:

```
git checkout -- MsTestProjectPoc Old
```

That restores tracked files only; the `bin`/`obj` copies the backup step wrote under `Old/MsTestProjectPoc`
stay behind (they are gitignored).

Edit anything here only with that in mind. A `.cs` change is lost on the next run unless the matching backup
under `Old/MsTestProjectPoc` is updated and committed with it; a change to anything else — the `.csproj`,
this README — survives the run and is instead copied *into* `Old/MsTestProjectPoc`, dirtying it.

## House convention in the converter sources

The converter libraries take `Net4x.StandardTypesWrappers`, whose global usings alias BCL static types to
interfaces (`File` is `System.IO.IFile`, `Path` is `System.IO.IPath`, `Directory` is `System.IO.IDirectory`,
`Console` is `System.IConsole`). `File.ReadAllText(x)` there is an instance call on an injectable property,
declared as `public File File { get; set; } = System.IO.InputOutput.Instance.File;`, and genuinely static
members must be fully qualified (`System.IO.Path.DirectorySeparatorChar`). This fixture does not reference
those wrappers, but the code that rewrites it does.

## License

Apache-2.0. See `LICENSE.md` at the repository root:
https://github.com/pieroviano/NUnitToXunitConverter
