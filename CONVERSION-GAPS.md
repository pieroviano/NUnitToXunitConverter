# MSTest / NUnit → xUnit conversion coverage

What `MsOrNUnitToXunitConverter` translates, and what it does not. Everything here is observed from running the
real CLI over a scratch project covering a broad slice of both frameworks, not inferred from the source.

The governing rule: **anything with no xUnit equivalent is left untouched on purpose.** Because the framework
`using` is rewritten to `Xunit`, an untranslated construct stops compiling, which puts it in front of a human.
That is always preferable to converting it into something weaker that silently passes.

---

## Lifecycle

| Source | Result |
| --- | --- |
| `[SetUp]`, `[TestInitialize]` | the generated constructor |
| `[TearDown]`, `[TestCleanup]` | a generated `Dispose`, with `System.IDisposable` on the class |
| `[OneTimeSetUp]`, `[ClassInitialize]` | the constructor of a generated `<Class>Fixture`, with `IClassFixture<>` on the class |
| `[OneTimeTearDown]`, `[ClassCleanup]` | `Dispose` on that fixture |
| `[SetUpFixture]`, `[AssemblyInitialize]`, `[AssemblyCleanup]` | `[CollectionDefinition("Assembly")]` + `ICollectionFixture<AssemblyFixture>`, with every test class in the project given `[Collection("Assembly")]` |

Three rules hold the lifecycle handling together, each of them easy to undo by accident:

- **Hooks are matched by signature, never by node reference.** They are recognised on the original class, where
  the attributes still are, but removed from the rewritten member list, whose nodes are different instances —
  `VisitAttributeList` rebuilds every attribute list unconditionally. A reference comparison across that
  boundary never matches, which leaves the hook in place, attribute and all, while its body is *also* copied
  into the generated constructor.
- **Per-class state is reset on entry to each class.** Otherwise one class's setup and teardown are grafted
  onto the next class in the same file, calling methods it does not have.
- **The presence of a hook decides, not the size of its body.** An empty `[OneTimeSetUp]` still means the class
  declares one, and dropping its fixture would change what the class declares.

Hook bodies are taken from the *rewritten* method, so asserts inside a setup are converted too.

`OneTimeSetUpContext` is shared across the whole project by `ConversionService` because assembly-wide setup is
declared in one file and has to be joined by the test classes in all the others.

---

## Attributes

### Test and data

| Source | Result |
| --- | --- |
| `[Test]`, `[TestMethod]` | `[Fact]` |
| `[TestCase]`, `[DataRow]` | `[InlineData]`, and the method is promoted to `[Theory]` |
| `[TestCase(…, ExpectedResult = x)]` | the expectation becomes a trailing `[InlineData]` argument and a trailing parameter; the method becomes `void` and its returned expression becomes `Assert.Equal(expectedResult, …)` |
| `[TestCaseSource]`, `[DynamicData]` | `[MemberData]` + `[Theory]`; a `typeof` argument becomes `MemberType` |
| `[DataTestMethod]` | dropped — `[Theory]` covers it |
| `[TestFixture]`, `[TestClass]` | dropped |
| `[ExpectedException(typeof(T))]` | the whole body is lifted into `Assert.Throws<T>`, or `await Assert.ThrowsAsync<T>` for an async method. `AllowDerivedTypes = true` gives `ThrowsAny<T>` instead, which is the difference between demanding that exact type and accepting a subclass |

### Annotations

| Source | Result |
| --- | --- |
| `[Ignore("reason")]` | `[Fact(Skip = "reason")]`; MSTest's argument-less `[Ignore]` skips with `"Ignored"` |
| `[Explicit]` | `[Fact(Skip = "Explicit")]` |
| `[Timeout(ms)]` | `[Fact(Timeout = ms)]` |
| `[Category]`, `[TestCategory]` | `[Trait("Category", …)]`, on methods and on classes |
| `[Description]`, `[Author]`, `[Owner]`, `[Priority]` | `[Trait]` |
| `[Property(k, v)]`, `[TestProperty(k, v)]` | `[Trait(k, v)]` |

xUnit traits are compile-time constant strings, so a non-string value is quoted: `[Priority(1)]` becomes
`[Trait("Priority", "1")]`.

### Assembly-level

`[DoNotParallelize]`, `[Parallelizable(ParallelScope.None)]` and `[LevelOfParallelism(n)]` become
`[assembly: CollectionBehavior(...)]`. `[Parallelize]` is dropped, since xUnit has no method-level
parallelism. A settings file holding nothing but these attributes is still selected by the detectors, so it is
converted along with everything else rather than left referencing a framework the package swap has removed.

### MSTest `TestContext`

The `TestContext` property is removed and `TestContext.WriteLine` is rewritten to `_output.WriteLine` on the
injected `ITestOutputHelper`.

---

## Assertions

### Scalar asserts

`AreEqual`, `AreNotEqual`, `AreSame`, `AreNotSame`, `IsTrue`, `IsFalse`, `IsNull`, `IsNotNull`, `IsInstanceOf`,
`IsInstanceOfType`, `ThrowsException`, `ThrowsExceptionAsync` map to their xUnit names.

`Greater`, `GreaterOrEqual`, `Less` and `LessOrEqual` have no xUnit assert, so the comparison is spelled out:
`Assert.Greater(a, b)` becomes `Assert.True(a > b)`. `Positive` and `Negative` compare against zero the same
way, `Zero`/`NotZero` become `Assert.Equal(0, x)`/`NotEqual`, `IsEmpty`/`IsNotEmpty` become `Empty`/`NotEmpty`,
and `IsNotInstanceOf`/`IsNotInstanceOfType` become `IsNotType<T>`.

### Failure messages, and the arguments that are not one

A trailing failure message is preserved by setting `_needsOutputHelper` — which injects an
`ITestOutputHelper _output` field and constructor parameter — and wrapping the assert in
`try/catch { _output.WriteLine(message); throw; }`.

A trailing argument is only taken for a message when it *looks* like one (`IsFailureMessage`: a string literal,
an interpolated string, or a concatenation built from one). Two overloads occupy the same position and are not
messages:

- `AreEqual(expected, actual, delta)` is the floating-point comparison. A delta that is a clean power of ten
  becomes xUnit's decimal-places argument — `Assert.AreEqual(1.0, x, 0.01)` → `Assert.Equal(1.0, x, 2)` — and
  any other delta leaves the call untouched, because xUnit counts decimal places rather than accepting a
  tolerance and approximating it would quietly change what the test asserts.
- `CollectionAssert.AreEqual(expected, actual, comparer)` carries comparison semantics no xUnit assert
  expresses, so the call is left untouched.

### NUnit's constraint model

`Assert.That(actual, constraint)` is translated for a fixed vocabulary, with the argument order flipped —
NUnit reads actual-then-expected, every xUnit assert reads expected-then-actual:

`Is.EqualTo` · `Is.Not.EqualTo` · `Is.Null` · `Is.Not.Null` · `Is.True` · `Is.False` · `Is.Empty` ·
`Is.Not.Empty` · `Is.SameAs` · `Is.Not.SameAs` · `Is.GreaterThan` · `Is.GreaterThanOrEqualTo` · `Is.LessThan` ·
`Is.LessThanOrEqualTo` · `Is.InRange` · `Is.TypeOf<T>` · `Is.InstanceOf<T>` · `Does.Contain` ·
`Does.Not.Contain` · `Does.StartWith` · `Does.EndWith` · `Does.Match` · `Contains.Item` · `Throws.TypeOf<T>` ·
`Throws.InstanceOf<T>`

Ordering constraints become explicit comparisons, so `Is.GreaterThan(1)` gives `Assert.True(x > 1)`. A
constraint is an open-ended expression tree; anything outside the vocabulary is left untouched rather than
approximated.

### `CollectionAssert` and `StringAssert`

`CollectionAssert` maps member by member. `Contains`/`DoesNotContain` swap their arguments, because
`CollectionAssert` names the collection first and xUnit names the element first. `AreEquivalent` becomes
`Assert.Equivalent`, which is order-insensitive. `AllItemsAreNotNull`, `AllItemsAreInstancesOfType` and
`IsSubsetOf` expand into `Assert.All(collection, item => …)`. `AllItemsAreUnique` becomes
`Assert.Empty(c.GroupBy(item => item).Where(group => group.Count() > 1))` — the grouping form names the
collection once, where the obvious `Assert.Equal(c.Distinct().Count(), c.Count())` would evaluate a
side-effecting argument twice.

`StringAssert` exists in both frameworks **with the arguments the other way round**: NUnit takes
`(expected, actual)`, MSTest takes `(value, substring)`. `SourceFramework` establishes which framework a file
was written against, from its usings and failing that its attributes, and the MSTest calls are swapped while
the NUnit ones are not. When the framework cannot be established the call is left untouched, because guessing
reverses the meaning of the assert silently.

---

## SpecFlow

SpecFlow is not a unit test framework; it runs on top of one. A SpecFlow project is therefore **retargeted**
rather than converted — the provider changes, the Gherkin and the bindings do not.

| Part | What happens |
| --- | --- |
| `SpecFlow.NUnit`, `SpecFlow.NUnit.Runners`, `SpecFlow.MsTest` | renamed in place to `SpecFlow.xUnit`, keeping the version |
| `SpecFlow`, `SpecFlow.Tools.MsBuild.Generation` | untouched |
| `NUnit`, `MSTest` and the rest | swapped for the xUnit set as usual |
| `unitTestProvider` in `specflow.json` and `App.config` | set to `xunit` |
| `*.feature` files, `[Binding]`, `[Given]`/`[When]`/`[Then]`, `[BeforeScenario]` and the other hooks | untouched |
| `*.feature.cs` | deleted so the build regenerates it, and never rewritten |
| asserts inside step definitions | converted like any other assert |

The provider package is renamed rather than dropped and replaced, because dropping it would take SpecFlow
itself with it. The configuration is edited as well as the package because it names the provider independently:
a project with the right package and the wrong config builds, then fails at run time looking for a provider
that is not there. And `specflow.json` is edited as text rather than reparsed as JSON, so formatting, key order
and every other setting survive.

A SpecFlow package counts as evidence of a test project, so a project whose only framework reference is
`SpecFlow.NUnit` is not skipped by a solution-wide run.

---

## Files, projects and formatting

Both detectors run over every project, so a part-migrated project holding both frameworks converts completely.
`forceMsTestProject` narrows the scan to MSTest only.

Formatting goes through Roslyn's `Formatter` rather than `NormalizeWhitespace()`, so comments, documentation
comments and preprocessor directives survive and untouched code keeps its own layout. Replacement nodes are
given `WithTriviaFrom(node)`: a constructed node carries no trivia of its own, and without this the comment or
`#pragma` attached to the statement being replaced disappears with it. One consequence is that generated code
adopts the surrounding layout — a `try`/`catch` inserted into a method whose body was written on one line stays
on one line.

---

## Not converted

### No xUnit equivalent — left untouched by design

| | |
| --- | --- |
| Attributes | `[Order]`, `[Retry]`, `[Repeat]`, `[MaxTime]`, `[Apartment]`, `[Culture]`, `[Platform]`, `[DeploymentItem]`, `[TestFixtureSource]`, and `[TestFixture(args)]` |
| Asserts | `Assert.Multiple` (xUnit has no soft assertions), `Assert.Pass`, `Assert.Inconclusive`, `Assert.Ignore`, `Assert.Warn`, `DirectoryAssert`, `FileAssert` |

`Assert.Fail` needs no translation: xUnit 2.5+ has it, and the pinned version is 2.9.3.

Ordering would need an `ITestCaseOrderer` rather than an attribute. `[TestFixture(args)]` parameterises the
fixture itself, which xUnit cannot express — dropping it would discard the parameterisations without a word, so
it stays and the compiler raises it.

### Would need more than a syntactic rewrite

| Gap | Why |
| --- | --- |
| `[Values]`, `[Range]`, `[Random]`, `[Combinatorial]`, `[Sequential]` | These generate the cartesian product of per-parameter value sets. Converting means *computing* the combinations and emitting one `[InlineData]` per row, which is only possible when every value is a compile-time literal — and a `[Random]` not at all. |
| The shape of a `[MemberData]` source | The attribute is translated; the source member is not. NUnit sources commonly yield `TestCaseData` and MSTest sources `object[]`, where xUnit wants `IEnumerable<object[]>`. A `TestCaseData` carrying `.Returns(…)`, `.SetName(…)` or `.Ignore(…)` has no xUnit shape at all. |
| Constraints outside the vocabulary | `Has.Count`/`Has.Length`/`Has.Property`, chained modifiers such as `Is.EqualTo(x).Within(d)` or `.IgnoreCase`, `Is.Ordered`, `Is.Unique`, `Is.SupersetOf`, and the `&`/`\|` combinators. Each needs its own rule. |
| Lifecycle inherited from a base class | xUnit's constructor/`IDisposable` model would need the base chain considered, and the base class is usually in another file. |
| Generic fixtures | `[TestFixture]` on a generic class has no xUnit equivalent. |
| SpecFlow to Reqnroll | SpecFlow is end-of-life and Reqnroll is the maintained fork, but moving between them is a different migration: the `TechTalk.SpecFlow` namespaces, the package set and the configuration file name all change. Retargeting the provider is what is offered; the framework itself stays where it is. |
| Two assembly-level declarations | MSTest's `[AssemblyInitialize]` in one file and NUnit's `[SetUpFixture]` in another cannot merge into one fixture. The first wins; the second is reported at `Warn` for merging by hand. |
| Detection accuracy | Detectors are substring sniffs over file text, so a file mentioning `"[TestFixture]"` in a string literal looks like a test file. The project-level gate (`ReferencesTestPackages`) keeps solution-wide runs off ordinary libraries, but not off a genuine test project whose sources discuss the frameworks — this repository's own test project is exactly that. |

---

## Where the work lives

| File | Role |
| --- | --- |
| `Conversion/XunitSyntaxRewriter.cs` | Orchestrates: usings, classes, methods, attributes, statements |
| `Conversion/LifecycleMethods.cs` | Finds lifecycle hooks and resolves their scope |
| `Conversion/FixtureBuilder.cs` | Builds fixture classes and the collection definition |
| `Conversion/AttributeTranslator.cs` | `Skip`, `Timeout`, `Trait`, `MemberData` |
| `Conversion/ExpectedResult.cs` | NUnit's `ExpectedResult` idiom |
| `Conversion/ConstraintTranslator.cs` | `Assert.That` |
| `Conversion/StringAssertTranslator.cs` | `StringAssert`, both argument orders |
| `Conversion/AssemblySettings.cs` | Assembly-level parallelism settings |
| `Conversion/SourceFramework.cs` | Which framework a file was written against |
| `Conversion/AnyFrameworkTestDetector.cs` | Matches either framework |
| `Conversion/SpecFlow.cs` | What the converter knows about SpecFlow |
| `SpecFlowRetargetService.cs` | SpecFlow configuration and generated code-behind |
