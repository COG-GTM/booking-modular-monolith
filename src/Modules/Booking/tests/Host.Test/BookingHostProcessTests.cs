using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using Xunit;
using Xunit.Abstractions;

namespace Host.Test;

[Collection(BookingHostCollection.Name)]
public sealed class BookingHostProcessTests(BookingHostFixture fixture, ITestOutputHelper output)
{
    [LinuxFact]
    public async Task HostSurvivesUnavailableEventStoreAndStopsGracefully()
    {
        await RunChildAsync(false);
        await RunChildAsync(true);
    }

    private async Task RunChildAsync(bool eventStoreAvailable)
    {
        var port = FreePort();
        var address = $"http://127.0.0.1:{port}";
        var assembly = typeof(Booking.Host.Program).Assembly.Location;
        var info = new ProcessStartInfo("dotnet", $"\"{assembly}\"")
        {
            WorkingDirectory = Path.GetDirectoryName(assembly)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var env = info.Environment;
        env["ASPNETCORE_ENVIRONMENT"] = "Development";
        env["ASPNETCORE_URLS"] = address;
        env["Modules__Booking__BackgroundProcessingEnabled"] = "true";
        env["Messaging__Transport"] = "RabbitMq";
        env["RabbitMqOptions__HostName"] = fixture.RabbitMqTestContainer.Hostname;
        env["RabbitMqOptions__Port"] = fixture.RabbitMqTestContainer.GetMappedPublicPort(5672).ToString();
        env["RabbitMqOptions__UserName"] = BuildingBlocks
            .TestBase
            .TestContainers
            .RabbitMqContainerConfiguration
            .UserName;
        env["RabbitMqOptions__Password"] = BuildingBlocks
            .TestBase
            .TestContainers
            .RabbitMqContainerConfiguration
            .Password;
        env["MongoOptions__ConnectionString"] = fixture.MongoDbTestContainer.GetConnectionString();
        env["PersistMessageOptions__ConnectionString__Booking"] =
            fixture.Configuration.ResolvePersistMessageConnectionString("Booking");
        env["EventStoreOptions__ConnectionString"] = eventStoreAvailable
            ? fixture.EventStoreDbTestContainer.GetConnectionString()
            : $"esdb://127.0.0.1:{FreePort()}?tls=false";
        env["Grpc__FlightAddress"] = fixture.GrpcServer.Address;
        env["Grpc__PassengerAddress"] = fixture.GrpcServer.Address;
        using var process = Process.Start(info)!;
        var childOutput = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                childOutput.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                childOutput.AppendLine(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            await WaitUntilAsync(
                async () =>
                {
                    try
                    {
                        return (await client.GetAsync($"{address}/health/live")).StatusCode == HttpStatusCode.OK;
                    }
                    catch
                    {
                        return false;
                    }
                },
                TimeSpan.FromSeconds(30)
            );

            if (eventStoreAvailable)
                await WaitUntilAsync(
                    () =>
                        Task.FromResult(
                            childOutput
                                .ToString()
                                .Contains("Subscription to all 'default' started", StringComparison.Ordinal)
                        ),
                    TimeSpan.FromSeconds(30)
                );
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                using var ready = await client.GetAsync($"{address}/health/ready");
                Assert.True(ready.StatusCode == HttpStatusCode.ServiceUnavailable, childOutput.ToString());
            }

            var shutdown = Stopwatch.StartNew();
            using var kill = Process.Start("kill", $"-TERM {process.Id}")!;
            await kill.WaitForExitAsync();
            var exitTask = process.WaitForExitAsync();
            Assert.True(
                await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(15))) == exitTask,
                childOutput.ToString()
            );
            shutdown.Stop();
            output.WriteLine(
                $"Host SIGTERM exit: {shutdown.Elapsed.TotalMilliseconds:F0} ms; ESDB available: {eventStoreAvailable}."
            );
            Assert.True(shutdown.Elapsed < TimeSpan.FromSeconds(10), childOutput.ToString());
            if (eventStoreAvailable)
                Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                Process.Start("kill", $"-TERM {process.Id}")?.WaitForExit();
                var exitTask = process.WaitForExitAsync();
                await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(15)));
            }
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            if (await condition())
                return;
            await Task.Delay(200);
        }
        throw new TimeoutException("Child Booking.Host did not reach the expected state.");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Requires Linux.";
    }
}
