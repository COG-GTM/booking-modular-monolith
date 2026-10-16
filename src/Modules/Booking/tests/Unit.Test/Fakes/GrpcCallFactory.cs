using Grpc.Core;

namespace Unit.Test.Fakes;

internal static class GrpcCallFactory
{
    public static AsyncUnaryCall<TResponse> Create<TResponse>(TResponse response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { }
        );
}
