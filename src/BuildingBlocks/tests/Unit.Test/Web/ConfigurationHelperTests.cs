using BuildingBlocks.Web;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Unit.Test.WebTests;

public class ConfigurationHelperTests
{
    [Fact]
    public void get_configuration_reads_appsettings_from_the_given_base_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bb-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "appsettings.json"), """{"WebHelper": { "Key": "from-file" }}""");

            var config = ConfigurationHelper.GetConfiguration(dir);

            config["WebHelper:Key"].Should().Be("from-file");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void get_configuration_throws_when_appsettings_json_is_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"bb-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var act = () => ConfigurationHelper.GetConfiguration(dir);

            act.Should().Throw<FileNotFoundException>();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
