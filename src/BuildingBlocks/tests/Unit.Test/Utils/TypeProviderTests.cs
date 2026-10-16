using BuildingBlocks.Utils;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Unit.Test.UtilsTests;

public class TypeProviderTests
{
    public sealed class UtilsMarkerType;

    [Fact]
    public void get_first_matching_type_finds_by_full_name_and_short_name()
    {
        TypeProvider
            .GetFirstMatchingTypeFromCurrentDomainAssembly(typeof(UtilsMarkerType).FullName!)
            .Should()
            .Be(typeof(UtilsMarkerType));
        TypeProvider
            .GetFirstMatchingTypeFromCurrentDomainAssembly(nameof(UtilsMarkerType))
            .Should()
            .Be(typeof(UtilsMarkerType));
    }

    [Fact]
    public void get_referenced_assemblies_includes_root_first_and_core_without_duplicates()
    {
        var assemblies = TypeProvider.GetReferencedAssemblies(typeof(UtilsMarkerType).Assembly);

        assemblies[0].GetName().Name.Should().Be(typeof(UtilsMarkerType).Assembly.GetName().Name);
        assemblies.Should().Contain(a => a == typeof(BuildingBlocks.Core.EventDispatcher).Assembly);
        assemblies.Select(a => a.FullName).Should().OnlyHaveUniqueItems();
    }
}
