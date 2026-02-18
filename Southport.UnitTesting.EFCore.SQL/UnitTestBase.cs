using AutoBogus;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Xunit.Abstractions;

namespace Southport.UnitTesting.EFCore.SQL;

public abstract class SouthportUnitTestBase<TDbContext> : SouthportUnitTestBase where TDbContext : DbContext
{
    protected virtual string MigrationAssembly => "Southport.EFCore.SQL";

    // Static: server-level state cached across test instances (per TDbContext type)
    private static bool _serverInitialized;
    private static readonly SemaphoreSlim _serverInitLock = new(1, 1);
    private static string _cachedConnectionString;
    private static Respawner _cachedCheckpoint;

    protected Respawner Checkpoint;

    protected string ConnectionString;

    protected TDbContext DbContext { get; set; }

    protected SouthportUnitTestBase(ITestOutputHelper testLogger) : base(testLogger)
    {
    }

    protected override async Task InitializeTest(CancellationToken cancellationToken = default)
    {
        await InitializeServer();
        WriteServerInfoToLog();

        await InitializeScope();

        await ResetState(cancellationToken);
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
        if (_serverInitialized)
        {
            ConnectionString = _cachedConnectionString;
            Checkpoint = _cachedCheckpoint;
            InitializeDependencyInjection(ConnectionString);
            return;
        }

        await _serverInitLock.WaitAsync();
        try
        {
            // Double-check after acquiring the lock
            if (_serverInitialized)
            {
                ConnectionString = _cachedConnectionString;
                Checkpoint = _cachedCheckpoint;
                InitializeDependencyInjection(ConnectionString);
                return;
            }

            var migrated = false;
            var migrationAttempts = 0;
            while (!migrated)
            {
                var dockerSqlPort = await DockerSqlDatabaseUtilities.EnsureDockerStartedAndGetContainerIdAndPortAsync(migrationAttempts > 0);
                ConnectionString = DockerSqlDatabaseUtilities.GetSqlConnectionString(dockerSqlPort, true);

                InitializeDependencyInjection(ConnectionString);

                try
                {
                    await MigrateDatabase();
                    migrated = true;
                }
                catch (Exception ex)
                {
                    TestLogger.WriteLine($"Error migrating database: {ex.Message}");
                    migrationAttempts++;
                    if (migrationAttempts >= 2)
                    {
                        throw new Exception($"Failed to migrate database after {migrationAttempts} attempts. Error: {ex.Message}");
                    }
                }
            }

            await using (var connection = new SqlConnection(ConnectionString))
            {
                await connection.OpenAsync();
                Checkpoint = await Respawner.CreateAsync(connection, new RespawnerOptions()
                {
                    TablesToIgnore = ["__EFMigrationsHistory"]
                });
            }

            _cachedConnectionString = ConnectionString;
            _cachedCheckpoint = Checkpoint;
            _serverInitialized = true;
        }
        finally
        {
            _serverInitLock.Release();
        }
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

    protected virtual async Task ResetState(CancellationToken cancellationToken = default)
    {
        DbContext.ChangeTracker.Clear();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await Checkpoint.ResetAsync(connection);
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