using BuildingBlocks.MassTransit;
using FluentAssertions;
using MassTransit;
using Xunit;

namespace BuildingBlocks.Tests.Messaging;

public class ModuleEndpointNameFormatterTests
{
    [Fact]
    public void saga_name_is_prefixed_with_its_module()
    {
        ModuleEndpointNameFormatter.Instance.Saga<FormatterSaga>().Should().StartWith("buildingblocks-tests.");
    }

    [Fact]
    public void execute_activity_name_is_prefixed_with_its_module()
    {
        ModuleEndpointNameFormatter
            .Instance.ExecuteActivity<FormatterExecuteActivity, FormatterArguments>()
            .Should()
            .StartWith("buildingblocks-tests.");
    }

    [Fact]
    public void compensate_activity_name_is_prefixed_with_its_module()
    {
        ModuleEndpointNameFormatter
            .Instance.CompensateActivity<FormatterCompensateActivity, FormatterLog>()
            .Should()
            .StartWith("buildingblocks-tests.");
    }

    [Fact]
    public void module_name_is_lowercase_with_dots_replaced_by_hyphens()
    {
        ModuleEndpointNameFormatter
            .ModuleName(typeof(ModuleEndpointNameFormatterTests))
            .Should()
            .Be("buildingblocks-tests");
    }

    private sealed class FormatterSaga : ISaga
    {
        public Guid CorrelationId { get; set; }
    }

    private sealed class FormatterExecuteActivity : IExecuteActivity<FormatterArguments>
    {
        public Task<ExecutionResult> Execute(ExecuteContext<FormatterArguments> context) =>
            Task.FromResult<ExecutionResult>(null!);
    }

    private sealed class FormatterCompensateActivity : ICompensateActivity<FormatterLog>
    {
        public Task<CompensationResult> Compensate(CompensateContext<FormatterLog> context) =>
            Task.FromResult<CompensationResult>(null!);
    }

    private sealed class FormatterArguments { }

    private sealed class FormatterLog { }
}
