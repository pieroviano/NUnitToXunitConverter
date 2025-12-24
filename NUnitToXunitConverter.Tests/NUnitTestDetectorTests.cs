using NSubstitute;
using Xunit;
using NUnitToXunitConverter.Conversion;
using ConversionClassLibrary.Interfaces;

namespace NUnitToXunitConverter.Tests;

public class NUnitTestDetectorTests
{
    [Fact]
    public void IsNUnitTest_ReturnsTrue_WhenContainsNUnitFramework()
    {
        // Arrange
        var sut = new NUnitTestDetector();
        var file = Substitute.For<IFile>();
        sut.File = file;

        var path = "some/path/A.cs";
        file.ReadAllText(path).Returns("using NUnit.Framework;");

        // Act
        var result = sut.IsNUnitTest(path);

        // Assert
        Assert.True(result);
        file.Received(1).ReadAllText(path);
    }

    [Fact]
    public void IsNUnitTest_ReturnsTrue_WhenContainsTestAttribute()
    {
        // Arrange
        var sut = new NUnitTestDetector();
        var file = Substitute.For<IFile>();
        sut.File = file;

        var path = "some/path/B.cs";
        file.ReadAllText(path).Returns("class C { [Test] public void T() {} }");

        // Act
        var result = sut.IsNUnitTest(path);

        // Assert
        Assert.True(result);
        file.Received(1).ReadAllText(path);
    }

    [Fact]
    public void IsNUnitTest_ReturnsFalse_WhenNoNUnitMarkers()
    {
        // Arrange
        var sut = new NUnitTestDetector();
        var file = Substitute.For<IFile>();
        sut.File = file;

        var path = "some/path/C.cs";
        file.ReadAllText(path).Returns("using System; class D { void M() {} }");

        // Act
        var result = sut.IsNUnitTest(path);

        // Assert
        Assert.False(result);
        file.Received(1).ReadAllText(path);
    }
}