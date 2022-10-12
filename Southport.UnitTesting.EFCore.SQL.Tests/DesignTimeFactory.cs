#if DEBUG
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Southport.UnitTesting.EFCore.SQL.Tests.Database;

namespace Southport.UnitTesting.EFCore.SQL.Tests
{

    public class TestDbContextDesignTimeFactory : DesignTimeDbContextFactoryBase<TestDbContext>
    {
        public TestDbContextDesignTimeFactory()
            : base("default", typeof(TestDbContextDesignTimeFactory).GetTypeInfo().Assembly.GetName().Name)
        {
        }

        protected override TestDbContext CreateNewInstance(DbContextOptions<TestDbContext> options)
        {
            return new TestDbContext(options);
        }
    }

    public abstract class DesignTimeDbContextFactoryBase<TContext> :
        IDesignTimeDbContextFactory<TContext> where TContext : DbContext
    {
        protected string ConnectionStringName { get; }
        protected string MigrationsAssemblyName { get; }

        protected DesignTimeDbContextFactoryBase(string connectionStringName, string migrationsAssemblyName)
        {
            //Debugger.Launch();
            ConnectionStringName = connectionStringName;
            MigrationsAssemblyName = migrationsAssemblyName;
        }

        public TContext CreateDbContext(string[] args)
        {
            return Create(
                Directory.GetCurrentDirectory(),
                ConnectionStringName, MigrationsAssemblyName);
        }

        protected abstract TContext CreateNewInstance(
            DbContextOptions<TContext> options);

        public TContext CreateWithConnectionStringName(string connectionStringName, string migrationsAssemblyName)
        {
            var basePath = AppContext.BaseDirectory;

            return Create(basePath, connectionStringName, migrationsAssemblyName);
        }

        private TContext Create(string basePath, string connectionStringName, string migrationsAssemblyName)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.Migrations.json", false)
                .AddEnvironmentVariables();

            var config = builder.Build();

            var connstr = config.GetConnectionString(connectionStringName);
            if (string.IsNullOrWhiteSpace(connstr))
            {
                throw new InvalidOperationException(
                    $"Could not find a connection string named '{connectionStringName}'. FROM DEVELOPED CODE");
            }

            return CreateWithConnectionString(connstr, migrationsAssemblyName);
        }

        private TContext CreateWithConnectionString(string connectionString, string migrationsAssemblyName)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentException(
                    $"{nameof(connectionString)} is null or empty.",
                    nameof(connectionString));
            }

            var optionsBuilder =
                new DbContextOptionsBuilder<TContext>();

            Console.WriteLine(
                $"{nameof(TContext)}.Create(string): Connection string: {0}",
                connectionString);

            //optionsBuilder.UseSqlServer(connectionString, sqlServerOptions => sqlServerOptions.MigrationsAssembly(migrationsAssemblyName));
            optionsBuilder.UseSqlServer(
                new SqlConnection(connectionString), sqlServerOptions => sqlServerOptions.MigrationsAssembly(migrationsAssemblyName));

            DbContextOptions<TContext> options = optionsBuilder.Options;

            return CreateNewInstance(options);
        }
    }
}
#endif