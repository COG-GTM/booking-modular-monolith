using System.Reflection;

namespace BuildingBlocks.EFCore;

public interface ISeedManager
{
    Task ExecuteSeedAsync(Assembly assembly);
    Task ExecuteTestSeedAsync();
}
