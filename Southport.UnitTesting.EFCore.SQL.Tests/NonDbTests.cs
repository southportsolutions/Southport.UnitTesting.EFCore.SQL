using Microsoft.EntityFrameworkCore;
using Southport.UnitTesting.EFCore.SQL.Tests.Base;
using Xunit.Abstractions;

namespace Southport.UnitTesting.EFCore.SQL.Tests
{
    public class NonDbTests : TestBase
    {
        [Fact]
        public async Task Test1()
        {
            await InitializeTest();
        }

        public NonDbTests(ITestOutputHelper testLogger) : base(testLogger)
        {
        }
    }
}