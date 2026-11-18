using BuildingBlocks.Exception;
using FluentAssertions;
using Grpc.Core;
using NSubstitute;
using Xunit;

namespace Unit.Test.Infrastructure;

public class GrpcExceptionInterceptorTests
{
    public static TheoryData<Exception, StatusCode> Errors =>
        new()
        {
            { new CustomException("Duplicate record", System.Net.HttpStatusCode.Conflict), StatusCode.Aborted },
            { new NotFoundException("Record not found"), StatusCode.NotFound },
            { new ValidationException("Validation failed"), StatusCode.InvalidArgument },
            { new BadRequestException("Invalid request"), StatusCode.InvalidArgument },
            { new AppException("Invalid operation"), StatusCode.InvalidArgument },
            { new InvalidOperationException("Unexpected failure"), StatusCode.Internal },
        };

    [Theory]
    [MemberData(nameof(Errors), DisableDiscoveryEnumeration = true)]
    public async Task unary_handler_should_translate_application_errors_to_grpc_status(
        Exception error,
        StatusCode expectedStatus
    )
    {
        var interceptor = new GrpcExceptionInterceptor();
        var context = Substitute.For<ServerCallContext>();

        var act = () =>
            interceptor.UnaryServerHandler<string, string>(
                "request",
                context,
                (_, _) => Task.FromException<string>(error)
            );

        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;
        exception.StatusCode.Should().Be(expectedStatus);
        exception.Status.Detail.Should().Be(error.Message);
    }
}
