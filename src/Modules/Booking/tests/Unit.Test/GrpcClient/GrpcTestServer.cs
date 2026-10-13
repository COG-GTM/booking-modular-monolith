using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using GetByIdRequest = Contracts.Grpc.Flight.GetByIdRequest;

namespace Unit.Test.GrpcClient;

// Real Kestrel gRPC server on a loopback port, so calls cross an actual HTTP/2 network hop.
public sealed class GrpcTestServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private GrpcTestServer(WebApplication app, FakeFlightState state)
    {
        _app = app;
        State = state;
        Address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
    }

    public string Address { get; }

    public FakeFlightState State { get; }

    public static async Task<GrpcTestServer> StartAsync(bool useTls = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        var certificate = useTls ? CreateSelfSignedCertificate() : null;
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(
                IPAddress.Loopback,
                0,
                listen =>
                {
                    listen.Protocols = HttpProtocols.Http2;
                    if (certificate != null)
                    {
                        listen.UseHttps(certificate);
                    }
                }
            )
        );

        var state = new FakeFlightState();
        builder.Services.AddSingleton(state);
        builder.Services.AddGrpc();
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy());
        builder.Services.AddGrpcHealthChecks(options =>
        {
            options.Services.Map(FlightGrpcService.Descriptor.FullName, _ => true);
            options.Services.Map(PassengerGrpcService.Descriptor.FullName, _ => true);
        });

        var app = builder.Build();
        app.MapGrpcService<FakeFlightService>();
        app.MapGrpcService<FakePassengerService>();
        app.MapGrpcHealthChecksService();
        await app.StartAsync();

        return new GrpcTestServer(app, state);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false)
        );

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1)
        );
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}

public class FakeFlightState
{
    private int _getByIdCalls;

    public int GetByIdCalls => _getByIdCalls;

    public int FailuresBeforeSuccess { get; set; }

    public StatusCode FailureStatus { get; set; } = StatusCode.Unavailable;

    public TimeSpan Delay { get; set; }

    public DateTime? LastDeadline { get; set; }

    public int NextCall() => Interlocked.Increment(ref _getByIdCalls);
}

public class FakeFlightService(FakeFlightState state) : FlightGrpcService.FlightGrpcServiceBase
{
    public override async Task<GetFlightByIdResult> GetById(GetByIdRequest request, ServerCallContext context)
    {
        var call = state.NextCall();
        state.LastDeadline = context.Deadline;

        if (call <= state.FailuresBeforeSuccess)
        {
            throw new RpcException(new Status(state.FailureStatus, $"failure {call}"));
        }

        if (state.Delay > TimeSpan.Zero)
        {
            await Task.Delay(state.Delay, context.CancellationToken);
        }

        return new GetFlightByIdResult
        {
            FlightDto = new FlightResponse { Id = request.Id, FlightNumber = "TEST-1" },
        };
    }
}

public class FakePassengerService : PassengerGrpcService.PassengerGrpcServiceBase
{
    public override Task<GetPassengerByIdResult> GetById(
        Contracts.Grpc.Passenger.GetByIdRequest request,
        ServerCallContext context
    ) =>
        Task.FromResult(
            new GetPassengerByIdResult
            {
                PassengerDto = new PassengerResponse { Id = request.Id, Name = "Test Passenger" },
            }
        );
}
