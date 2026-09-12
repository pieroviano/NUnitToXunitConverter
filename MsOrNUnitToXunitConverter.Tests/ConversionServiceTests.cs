using System.Reflection;
using ProjectsLibrary;

namespace MsOrNUnitToXunitConverter.Tests
{
    public class ConversionServiceTests
    {

        [Fact]
        public void TestConversionService()
        {
            var location = Assembly.GetExecutingAssembly().Location;
            var csprojPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(location)!,
                "..\\..\\..\\..\\MsTestProjectPoc\\MsTestProjectPoc.csproj"));
            new ConversionService().DoConversion(csprojPath, true);
        }
    }
}
