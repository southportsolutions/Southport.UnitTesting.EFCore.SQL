// based on https://blog.dangl.me/archive/running-sql-server-integration-tests-in-net-core-projects-via-docker/

using System.Net;
using System.Net.Sockets;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Data.SqlClient;

namespace Southport.UnitTesting.EFCore.SQL;

public static class DockerSqlDatabaseUtilities
{
    public const string DbPassword = "#testingDockerPassword#";
    public const string DbUser = "sa";

    public static string DbImage = "mcr.microsoft.com/mssql/server";
    public static string DbImageTag = "2019-latest";

    public static string DbContainerName = "Docker_SQL_UnitTests";
    public static string DbVolumeName = "Docker_SQL_UnitTests_Volume";

    public static string DbName = "SQLUnitTests";

    public static int ContainerExpirationHours = 24;
    public static int MaxSqlAvailabilityWaitSeconds = 120;

    public static async Task<string> EnsureDockerStartedAndGetContainerIdAndPortAsync(bool forceCleanup = false)
    {
        await CleanupRunningContainers(forceCleanup ? 0 : ContainerExpirationHours);
        await CleanupRunningVolumes(forceCleanup ? 0 : ContainerExpirationHours);
        var dockerClient = GetDockerClient();

        // This call ensures that the latest SQL Server Docker image is pulled
        await dockerClient.Images.CreateImageAsync(new ImagesCreateParameters
        {
            FromImage = $"{DbImage}:{DbImageTag}"
        }, null, new Progress<JSONMessage>());

        // create a volume, if one doesn't already exist
        await ManageVolumes(dockerClient);

        var existingCont = await GetExistingContainer(dockerClient);
        if (existingCont == null) return await CreateContainer(dockerClient);
        if (existingCont.State != "exited") return existingCont.Ports.First().PublicPort.ToString();

        await StartContainer(dockerClient, existingCont.ID);

        existingCont = await GetExistingContainer(dockerClient);
        var databasePort = existingCont.Ports.First().PublicPort.ToString();
        await WaitUntilDatabaseAvailableAsync(databasePort);
        await CreateDatabaseIfDoesNotExist(databasePort);
        return databasePort;
    }

    private static async Task ManageVolumes(DockerClient dockerClient)
    {
        var volumeList = await dockerClient.Volumes.ListAsync();
        var volumeCount = volumeList.Volumes.Count(v => v.Name == DbVolumeName);
        if (volumeCount <= 0)
        {
            await dockerClient.Volumes.CreateAsync(new VolumesCreateParameters
            {
                Name = DbVolumeName,
            });
        }
    }

    private static async Task StartContainer(DockerClient dockerClient, string containerId)
    {
        await dockerClient
            .Containers
            .StartContainerAsync(containerId, new ContainerStartParameters());
    }

    private static bool IsRunningOnWindows()
    {
        return Environment.OSVersion.Platform == PlatformID.Win32NT;
    }

    private static DockerClient GetDockerClient()
    {
        var dockerUri = IsRunningOnWindows()
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";
        return new DockerClientConfiguration(new Uri(dockerUri))
            .CreateClient();
    }

    private static async Task<ContainerListResponse> GetExistingContainer(DockerClient dockerClient)
    {
        var contList = await dockerClient
            .Containers.ListContainersAsync(new ContainersListParameters() { All = true });
        return contList.FirstOrDefault(c => c.Names.Any(n => n.Contains(DbContainerName)));
    }

    private static async Task<string> CreateContainer(DockerClient dockerClient)
    {
        var databasePort = GetFreePort();
        var sqlContainer = await dockerClient
            .Containers
            .CreateContainerAsync(new CreateContainerParameters
            {
                Name = DbContainerName,
                Image = $"{DbImage}:{DbImageTag}",
                Env = new List<string>
                {
                    "ACCEPT_EULA=Y",
                    $"SA_PASSWORD={DbPassword}"
                },
                HostConfig = new HostConfig
                {
                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        {
                            "1433/tcp",
                            new[]
                            {
                                new PortBinding
                                {
                                    HostPort = databasePort
                                }
                            }
                        }
                    },
                    Binds = new List<string>
                    {
                        $"{DbVolumeName}:/Accessioning_data"
                    }
                },
            });

        await dockerClient
            .Containers
            .StartContainerAsync(sqlContainer.ID, new ContainerStartParameters());

        await WaitUntilDatabaseAvailableAsync(databasePort);
        await CreateDatabaseIfDoesNotExist(databasePort);
        return databasePort;
    }

    private static async Task CleanupRunningContainers(int hoursTillExpiration)
    {
        hoursTillExpiration *= -1;
        var dockerClient = GetDockerClient();

        var runningContainers = await dockerClient.Containers
            .ListContainersAsync(new ContainersListParameters());

        foreach (var runningContainer in runningContainers.Where(cont => cont.Names.Any(n => n.Contains(DbContainerName))))
        {
            // Stopping all test containers that are older than 24 hours
            var expiration = hoursTillExpiration > 0
                ? hoursTillExpiration * -1
                : hoursTillExpiration;
            if (runningContainer.Created >= DateTime.UtcNow.AddHours(expiration)) continue;

            try
            {
                await EnsureDockerContainersStoppedAndRemovedAsync(runningContainer.ID);
            }
            catch
            {
                // Ignoring failures to stop running containers
            }
        }
    }

    private static async Task CleanupRunningVolumes(int hoursTillExpiration)
    {
        hoursTillExpiration *= -1;
        var dockerClient = GetDockerClient();

        var runningVolumes = await dockerClient.Volumes.ListAsync();

        foreach (var runningVolume in runningVolumes.Volumes.Where(v => v.Name == DbVolumeName))
        {
            // Stopping all test volumes that are older than 24 hours
            var expiration = hoursTillExpiration > 0
                ? hoursTillExpiration * -1
                : hoursTillExpiration;
            if (DateTime.Parse(runningVolume.CreatedAt) >= DateTime.UtcNow.AddHours(expiration)) continue;

            try
            {
                await EnsureDockerVolumesRemovedAsync(runningVolume.Name);
            }
            catch
            {
                // Ignoring failures to stop running containers
            }
        }
    }

    public static async Task EnsureDockerContainersStoppedAndRemovedAsync(string dockerContainerId)
    {
        var dockerClient = GetDockerClient();
        await dockerClient.Containers
            .StopContainerAsync(dockerContainerId, new ContainerStopParameters());
        await dockerClient.Containers
            .RemoveContainerAsync(dockerContainerId, new ContainerRemoveParameters());
    }

    public static async Task EnsureDockerVolumesRemovedAsync(string volumeName)
    {
        var dockerClient = GetDockerClient();
        await dockerClient.Volumes.RemoveAsync(volumeName);
    }

    private static async Task WaitUntilDatabaseAvailableAsync(string databasePort)
    {
        var start = DateTime.UtcNow;
        while (start.AddSeconds(MaxSqlAvailabilityWaitSeconds) > DateTime.UtcNow)
        {
            try
            {
                await using var sqlConnection = await GetOpenConnection(databasePort);
                return;
            }
            catch
            {
                // If opening the SQL connection fails, SQL Server is not ready yet
                await Task.Delay(1000);
            }
        }

        throw new Exception($"Connection to the SQL docker database could not be established within {MaxSqlAvailabilityWaitSeconds} seconds.");
    }

    private static string GetFreePort()
    {
        // From https://stackoverflow.com/a/150974/4190785
        var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port.ToString();
    }

    private static async Task<SqlConnection> GetOpenConnection(string databasePort)
    {
        var sqlConnectionString = GetSqlConnectionString(databasePort, false);
        var sqlConnection = new SqlConnection(sqlConnectionString);
        await sqlConnection.OpenAsync();

        return sqlConnection;
    }

    private static async Task CreateDatabaseIfDoesNotExist(string databasePort)
    {
        await using var sqlConnection = await GetOpenConnection(databasePort);
        var sqlCommand = new SqlCommand($"SELECT count(*) FROM sys.databases WHERE name = '{DbName}'", sqlConnection);
        var dbExists = (int)(await sqlCommand.ExecuteScalarAsync() ?? 0) > 0;
        if (!dbExists)
        {
            sqlCommand = new SqlCommand($"CREATE DATABASE [{DbName}]", sqlConnection);
            await sqlCommand.ExecuteNonQueryAsync();
        }
    }

    public static string GetSqlConnectionString(string port, bool includeInitialCatalog)
    {
        var sqlConnectionString = new SqlConnectionStringBuilder()
        {
            DataSource = $"localhost,{port}",
            UserID = DbUser,
            Password = DbPassword,
            IntegratedSecurity = false,
            TrustServerCertificate = true
        };

        if (includeInitialCatalog)
        {
            sqlConnectionString.InitialCatalog = DbName;
        }

        return sqlConnectionString.ToString();
    }
}