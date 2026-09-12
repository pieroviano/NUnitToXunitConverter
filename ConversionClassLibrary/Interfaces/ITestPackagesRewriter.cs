namespace ConversionClassLibrary.Interfaces;

/// <summary>
/// Replaces the test-framework <c>PackageReference</c> items of a project file with the set required by xUnit.
/// </summary>
public interface ITestPackagesRewriter
{
    File File { get; set; }

    /// <summary>
    /// The packages written into the project. Ordered as they should appear in the generated item group.
    /// </summary>
    IReadOnlyList<TestPackage> XunitPackages { get; set; }

    /// <summary>
    /// Removes every recognised test-framework package reference and writes <see cref="XunitPackages"/> in the
    /// place of the first one removed.
    /// </summary>
    /// <returns><c>true</c> when the project file was changed on disk.</returns>
    bool RewritePackageReferences(string csprojPath);
}

/// <summary>A package id/version pair to be written as a <c>PackageReference</c>.</summary>
/// <param name="Id">The package id.</param>
/// <param name="Version">The version written to the <c>Version</c> attribute.</param>
public record TestPackage(string Id, string Version);
