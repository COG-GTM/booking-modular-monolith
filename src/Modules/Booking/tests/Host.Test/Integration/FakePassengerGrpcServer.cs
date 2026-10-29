using System.Net;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Passenger;

namespace Host.Test.Integration;

public sealed class FakePassengerGrpcServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakePassengerGrpcServer(WebApplication app, FakePassengerService service, string address)
    {
        _app = app;
        Service = service;
        Address = address;
    }

    public string Address { get; }
    public FakePassengerService Service { get; }

    public static async Task<FakePassengerGrpcServer> StartAsync()
    {
        var service = new FakePassengerService();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2)
        );
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(service);

        var app = builder.Build();
        app.MapGrpcService<FakePassengerService>();
        await app.StartAsync();

        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        return new FakePassengerGrpcServer(app, service, address);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public sealed class FakePassengerService : PassengerGrpcService.PassengerGrpcServiceBase
{
    private int _getByIdCalls;

    public int GetByIdCalls => _getByIdCalls;

    public override Task<GetPassengerByIdResult> GetById(Passenger.GetByIdRequest request, ServerCallContext context)
    {
        Interlocked.Increment(ref _getByIdCalls);
        return Task.FromResult(
            new GetPassengerByIdResult
            {
                PassengerDto = new PassengerResponse
                {
                    Id = request.Id,
                    Name = "Host Test Passenger",
                    PassportNumber = "P12345678",
                    Age = 35,
                },
            }
        );
    }
}
