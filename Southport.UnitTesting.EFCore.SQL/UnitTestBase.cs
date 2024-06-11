using AutoBogus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Respawn.Graph;
using Xunit.Abstractions;

namespace Southport.UnitTesting.EFCore.SQL;

[Obsolete($"Use {nameof(SouthportUnitTestBase)}.")]
public abstract class UnitTestBase<TDbContext> : SouthportUnitTestBase<TDbContext> where TDbContext : DbContext
{
    protected UnitTestBase(ITestOutputHelper testLogger) : base(testLogger)
    {
    }
}

public abstract class SouthportUnitTestBase<TDbContext> : SouthportUnitTestBase where TDbContext : DbContext
{
    protected virtual string MigrationAssembly => "Southport.EFCore.SQL";

    protected static bool IsInitialized;
    protected static bool IsInitializing;
    
    protected static Respawner Checkpoint;
    

    private static string _dockerSqlPort;

    protected static string ConnectionString;
    

    protected TDbContext DbContext { get; set; }

    protected SouthportUnitTestBase(ITestOutputHelper testLogger) : base(testLogger)
    {
    }


    protected override async Task InitializeTest()
    {
        await InitializeServer();
        WriteServerInfoToLog();

        InitializeDependencyInjection();
        await InitializeScope();

        await ResetState();
    }

    protected override async Task InitializeScope()
    {
        await base.InitializeScope();
        InitializeDbContext();
    }

    protected virtual void WriteServerInfoToLog()
    {
        var stringParts = ConnectionString.Split(";");
        foreach (var part in stringParts)
        {
            if (part.Contains("Data Source"))
            {
                TestLogger.WriteLine($"Server: {part.Split("=")[1]}");
            }else if (part.Contains("Initial Catalog"))
            {
                TestLogger.WriteLine($"Database: {part.Split("=")[1]}");
            }
        }
    }

    #region Server Initialization

    protected async Task InitializeServer()
    {
        if (IsInitialized) return;

        if (IsInitializing)
        {
            do
            {
                Thread.Sleep(1000);
            } while (IsInitializing && !IsInitialized);
            return;
        }

        IsInitializing = true;

        _dockerSqlPort = await DockerSqlDatabaseUtilities.EnsureDockerStartedAndGetContainerIdAndPortAsync();
        ConnectionString = DockerSqlDatabaseUtilities.GetSqlConnectionString(_dockerSqlPort, true);

        InitializeDependencyInjection(ConnectionString);
        
        await MigrateDatabase();

        Checkpoint = await Respawner.CreateAsync(ConnectionString, new RespawnerOptions()
        {
            TablesToIgnore = new Table[]
            {
                "__EFMigrationsHistory"
            }
        });

        IsInitialized = true;
        IsInitializing = false;
    }

    protected override IConfigurationBuilder GetConfigurationBuilder(string connectionString)
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddInMemoryCollection(new Dictionary<string, string> { { "UseInMemoryDatabase", "false" }, { "ConnectionStrings:DbConnectionString", connectionString } })
            .AddEnvironmentVariables();
    }

    protected virtual async Task MigrateDatabase()
    {
        using var scope = ScopeFactory.CreateScope();
        
        var context = scope.ServiceProvider.GetService<TDbContext>();
        await context.Database.MigrateAsync();
    }

    protected override ServiceCollection ConfigureServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TDbContext>(options =>
            options.UseSqlServer(ConnectionString,
                sqlOptions =>
                {
                    if (string.IsNullOrWhiteSpace(MigrationAssembly)) return;
                    sqlOptions.MigrationsAssembly(MigrationAssembly);
                }));

        services.AddLogging();

        return services;
    }

    #endregion

    protected virtual async Task ResetState()
    {
        await Checkpoint.ResetAsync(ConnectionString);
    }

    protected virtual void InitializeDbContext()
    {
        DbContext = GetService<TDbContext>();
    }

    #region Add Fake Items

    protected virtual async Task<T> AddFakeEntity<T>(AutoFaker<T> generate) where T : class
    {
        var entity = generate.Generate();
        DbContext.Add(entity);
        await DbContext.SaveChangesAsync();
        return entity;
    }

    #endregion
}