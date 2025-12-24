using NSubstitute;
using Xunit;
using NUnitToXunitConverter.Conversion;

namespace NUnitToXunitConverter.Tests;

public class NUnitToXunitRewriterTests
{
    [Fact]
    public void RewriteFile_ConvertsNUnit_using_and_attributes_and_asserts_to_xunit_equivalents()
    {
        // Arrange
        var sut = new NUnitToXunitRewriter();
        var file = Substitute.For<IFile>();
        sut.File = file;

        var path = "C:\\proj\\Tests\\MyTests.cs";

        var inputCode = @"
using System;
using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class MyTests
    {
        [OneTimeSetUp]
        public void Init() { }

        [Test]
        public void Should_Do_Something()
        {
            Assert.AreEqual(1, 1, ""failed"");
        }
    }
}
";
        file.ReadAllText(path).Returns(inputCode);

        // Act
        sut.RewriteFile(path);

        // Assert
        file.Received(1).ReadAllText(path);

        // Verify the output contains key transformed tokens:
        // - NUnit -> Xunit using
        // - [Test] -> Fact
        // - Assert.AreEqual -> Assert.Equal
        // - OneTimeSetUp fixture generated
        file.Received(1).WriteAllText(path, Arg.Is<string>(s =>
            s.Contains("using Xunit") &&
            s.Contains("Fact") &&
            s.Contains("Assert.Equal") &&
            s.Contains("MyTestsFixture")));
    }

    [Fact]
    public void RewriteFile_WritesOutputEvenForEmptyFile()
    {
        // Arrange
        var sut = new NUnitToXunitRewriter();
        var file = Substitute.For<IFile>();
        sut.File = file;

        var path = "empty.cs";
        file.ReadAllText(path).Returns(string.Empty);

        // Act
        sut.RewriteFile(path);

        // Assert
        file.Received(1).ReadAllText(path);
        file.Received(1).WriteAllText(path, Arg.Any<string>());
    }
}