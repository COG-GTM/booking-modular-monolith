using MapsterMapper;
using Xunit;

namespace Unit.Test.Common;

[CollectionDefinition(nameof(UnitTestFixture))]
public class FixtureCollection : ICollectionFixture<UnitTestFixture> { }

public class UnitTestFixture
{
    public UnitTestFixture()
    {
        Mapper = MapperFactory.Create();
    }

    public IMapper Mapper { get; }
}
