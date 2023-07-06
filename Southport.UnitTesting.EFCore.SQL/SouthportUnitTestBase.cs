using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Southport.UnitTesting.EFCore.SQL;

public abstract class SouthportUnitTestBase
{
    protected ITestOutputHelper TestLogger { get; }
    
    protected IConfigurationRoot Configuration;

    protected IServiceScopeFactory ScopeFactory;

    protected IServiceScope ServiceScope { get; set; }

    protected SouthportUnitTestBase(ITestOutputHelper testLogger)
    {
        TestLogger = testLogger;
    }


    protected virtual async Task InitializeTest()
    {
        InitializeDependencyInjection();
        await InitializeScope();
    }

    protected virtual void InitializeDependencyInjection(string connectionString = null)
    {
        if (Configuration != null) return;

        var builder = GetConfigurationBuilder(connectionString);
        Configuration = builder.Build();

        var services = ConfigureServiceCollection();

        ScopeFactory = services.BuildServiceProvider().GetService<IServiceScopeFactory>();
    }

    protected virtual async Task InitializeScope()
    {
        if (ServiceScope != null)
        {
            ServiceScope.Dispose();
            ServiceScope = null;
        }

        ServiceScope = ScopeFactory.CreateScope();
    }

    #region Server Initialization

    protected virtual IConfigurationBuilder GetConfigurationBuilder(string connectionString)
    {
        var configBuilder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory());

        if (!string.IsNullOrEmpty(connectionString))
            configBuilder = configBuilder.AddInMemoryCollection(new Dictionary<string, string> { { "UseInMemoryDatabase", "false" }, { "ConnectionStrings:DbConnectionString", connectionString } });

        configBuilder.AddEnvironmentVariables();

        return configBuilder;
    }

    protected virtual ServiceCollection ConfigureServiceCollection()
    {
        var services = new ServiceCollection();

        services.AddLogging();

        return services;
    }

    #endregion

    protected virtual T GetService<T>()
    {
        return ServiceScope.ServiceProvider.GetService<T>();
    }
}