﻿using Southport.UnitTesting.EFCore.SQL.Tests.Database;
using Southport.UnitTesting.EFCore.SQL.Tests.Fakes;
using Xunit.Abstractions;

namespace Southport.UnitTesting.EFCore.SQL.Tests.Base;

public class DbTestBase : SouthportUnitTestBase<TestDbContext>
{
    protected override string MigrationAssembly => null;

    protected DbTestBase(ITestOutputHelper testLogger) : base(testLogger)
    {
        DockerSqlDatabaseUtilities.ContainerExpirationHours = 1;
    }

    protected override async Task ResetState()
    {
        await base.ResetState();

        var testEntity = new FakeTestEntity().Generate();
        DbContext.Add(testEntity);
        await DbContext.SaveChangesAsync();
    }
}