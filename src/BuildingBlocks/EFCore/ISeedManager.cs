namespace BuildingBlocks.EFCore;

public interface ISeedManager
{
    Task ExecuteSeedAsync();

    // Runs only the IDataSeeder implementations declared in the given module assembly.
    Task ExecuteSeedAsync(System.Reflection.Assembly moduleAssembly);
    Task ExecuteTestSeedAsync();
}