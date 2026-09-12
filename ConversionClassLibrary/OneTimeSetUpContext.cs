namespace ConversionClassLibrary;

/// <summary>
/// The little that has to be known across files while a project is converted. Assembly-wide setup is declared
/// in one file but has to be joined by the test classes in every other file, so whether the project has any
/// cannot be decided from the file in hand.
/// </summary>
public class OneTimeSetUpContext
{
    /// <summary>
    /// Kept for the class-level fixture naming that predates <see cref="ProjectHasAssemblyFixture"/>.
    /// </summary>
    public string? FixtureClassName;

    /// <summary>
    /// Whether any file in the project declares assembly-wide setup - MSTest's
    /// <c>[AssemblyInitialize]</c>/<c>[AssemblyCleanup]</c> or NUnit's <c>[SetUpFixture]</c>. When it does,
    /// every test class in the project is put into the generated collection so the fixture actually applies
    /// to it. <see cref="ProjectsLibrary.ConversionService"/> settles this before the first file is rewritten.
    /// </summary>
    public bool ProjectHasAssemblyFixture;

    /// <summary>
    /// Set once the collection definition has been emitted, so the second file holding assembly-wide hooks
    /// does not emit a duplicate of it.
    /// </summary>
    public bool AssemblyCollectionEmitted;
}
