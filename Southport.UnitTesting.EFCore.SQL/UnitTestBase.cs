using AutoBogus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Respawn.Graph;
using Xunit.Abstractions;

namespace Southport.UnitTesting.EFCore.SQL;

public abstract class UnitTestBase<TDbContext> where TDbContext : DbContext
{
    protected ITestOutputHelper TestLogger { get; }

    protected virtual string MigrationAssembly => "Southport.EFCore.SQL";

    protected bool IsInitialized;
    protected bool IsInitializing;
    
    protected IConfigurationRoot Configuration;
    protected Respawner Checkpoint;

    protected IServiceScopeFactory ScopeFactory;

    private string _dockerSqlPort;

    protected string ConnectionString;

    protected IServiceScope ServiceScope { get; set; }

    protected TDbContext DbContext { get; set; }

    protected UnitTestBase(ITestOutputHelper testLogger)
    {
        TestLogger = testLogger;
    }


    protected virtual async Task InitializeTest()
    {

        await InitializeServer();
        WriteServerInfoToLog();

        await InitializeScope();

        await ResetState();
    }

    protected virtual async Task InitializeScope()
    {
        if (ServiceScope != null)
        {
            ServiceScope.Dispose();
            ServiceScope = null;
        }

        ServiceScope = ScopeFactory.CreateScope();

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

        var builder = GetConfigurationBuilder(ConnectionString);
        Configuration = builder.Build();

        var services = ConfigureServiceCollection();

        ScopeFactory = services.BuildServiceProvider().GetService<IServiceScopeFactory>();
        
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

    protected virtual IConfigurationBuilder GetConfigurationBuilder(string connectionString)
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

    protected virtual ServiceCollection ConfigureServiceCollection()
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

    protected virtual T GetService<T>()
    {
        return ServiceScope.ServiceProvider.GetService<T>();
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