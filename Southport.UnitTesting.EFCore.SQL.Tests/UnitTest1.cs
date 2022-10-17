using Microsoft.EntityFrameworkCore;
using Southport.UnitTesting.EFCore.SQL.Tests.Base;
using Xunit.Abstractions;

namespace Southport.UnitTesting.EFCore.SQL.Tests
{
    public class UnitTest1 : TestBase
    {
        [Fact]
        public async Task Test1()
        {
            await InitializeTest();
            var entry = await DbContext.TestEntities.FirstOrDefaultAsync();
            Assert.NotNull(entry);
        }

        [Fact]
        public async Task Test2()
        {
            await InitializeTest();
            var entry = await DbContext.TestEntities.FirstOrDefaultAsync();
            Assert.NotNull(entry);
        }

        public UnitTest1(ITestOutputHelper testLogger) : base(testLogger)
        {
        }
    }
}