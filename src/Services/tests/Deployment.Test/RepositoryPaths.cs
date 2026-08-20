namespace Deployment.Test;

public static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string DockerComposeFile =>
        Path.Combine(Root, "deployments", "docker-compose", "docker-compose.yaml");

    public static string ServicesDockerfile => Path.Combine(Root, "src", "Services", "Dockerfile");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test base directory.");
    }
}
