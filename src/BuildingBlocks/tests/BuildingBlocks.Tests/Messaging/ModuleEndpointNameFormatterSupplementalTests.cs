#pragma warning disable SA1300

using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using Xunit;

namespace BuildingBlocks.Tests.Messaging;

public class ModuleEndpointNameFormatterSupplementalTests
{
    [Fact]
    public void saga_endpoint_name_matches_the_base_formatter_with_module_prefix()
    {
        var expected = KebabCaseEndpointNameFormatter.Instance.Saga<TestSaga>();
        var actual = ModuleEndpointNameFormatter.Instance.Saga<TestSaga>();

        actual.Should().Be($"buildingblocks-tests.{expected}");
    }

    [Fact]
    public void execute_activity_endpoint_name_matches_the_base_formatter_with_module_prefix()
    {
        var expected = KebabCaseEndpointNameFormatter.Instance.ExecuteActivity<TestExecuteActivity, TestArguments>();
        var actual = ModuleEndpointNameFormatter.Instance.ExecuteActivity<TestExecuteActivity, TestArguments>();

        actual.Should().Be($"buildingblocks-tests.{expected}");
    }

    [Fact]
    public void compensate_activity_endpoint_name_matches_the_base_formatter_with_module_prefix()
    {
        var expected = KebabCaseEndpointNameFormatter.Instance.CompensateActivity<TestCompensateActivity, TestLog>();
        var actual = ModuleEndpointNameFormatter.Instance.CompensateActivity<TestCompensateActivity, TestLog>();

        actual.Should().Be($"buildingblocks-tests.{expected}");
    }

    [Fact]
    public void module_names_differ_for_types_from_different_assemblies()
    {
        var testModule = ModuleEndpointNameFormatter.ModuleName(typeof(TestConsumer));
        var persistMessageModule = ModuleEndpointNameFormatter.ModuleName(typeof(PersistMessage));

        testModule.Should().Be("buildingblocks-tests");
        persistMessageModule.Should().Be("buildingblocks-persistmessageprocessor");
        testModule.Should().NotBe(persistMessageModule);
    }

    private sealed class TestSaga : ISaga
    {
        public Guid CorrelationId { get; set; }
    }

    private sealed class TestExecuteActivity : IExecuteActivity<TestArguments>
    {
        public Task<ExecutionResult> Execute(ExecuteContext<TestArguments> context)
        {
            var result = context.Completed();
            return Task.FromResult(result);
        }
    }

    private sealed class TestCompensateActivity : ICompensateActivity<TestLog>
    {
        public Task<CompensationResult> Compensate(CompensateContext<TestLog> context)
        {
            var result = context.Compensated();
            return Task.FromResult(result);
        }
    }

    private sealed class TestArguments { }

    private sealed class TestLog { }

    private sealed class TestConsumer { }
}
