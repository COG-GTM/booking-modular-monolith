using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Exception;

public class GrpcExceptionInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (RpcException)
        {
            throw;
        }
        catch (System.Exception exception)
        {
            throw new RpcException(new Status(MapStatusCode(exception), exception.Message));
        }
    }

    private static StatusCode MapStatusCode(System.Exception exception) =>
        exception switch
        {
            ConflictException or DbUpdateConcurrencyException => StatusCode.Aborted,
            CustomException { StatusCode: System.Net.HttpStatusCode.Conflict } => StatusCode.Aborted,
            NotFoundException => StatusCode.NotFound,
            ValidationException or BadRequestException or AppException => StatusCode.InvalidArgument,
            _ => StatusCode.Internal
        };
}
