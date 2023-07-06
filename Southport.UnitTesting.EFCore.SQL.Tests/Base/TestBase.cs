using Xunit.Abstractions;

namespace Southport.UnitTesting.EFCore.SQL.Tests.Base;

public class TestBase : SouthportUnitTestBase
{
    protected TestBase(ITestOutputHelper testLogger) : base(testLogger)
    {
    }
}