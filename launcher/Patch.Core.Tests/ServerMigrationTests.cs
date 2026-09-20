using Geumeum.Launcher;
using Xunit;

namespace Geumeum.Patch.Tests;

public sealed class ServerMigrationTests
{
    [Theory]
    [InlineData("http://169.212.47.146:8765", true)]
    [InlineData("http://169.212.47.146:8765/", true)]
    [InlineData("http://localhost:8765", false)]
    [InlineData("http://169.212.47.146:8765/custom", false)]
    [InlineData("https://custom.example.com", false)]
    [InlineData("", false)]
    public void OnlyLegacyPcAddressMigrates(string oldUrl, bool expected)
    {
        var settings = new LauncherSettings { ServerUrl = oldUrl, InstallRoot = "existing-game", Channel = "internal" };
        Assert.Equal(expected, settings.MigrateLegacyServer("https://pub-test.r2.dev"));
        Assert.Equal(expected ? "" : oldUrl, settings.ServerUrl);
        Assert.Equal("existing-game", settings.InstallRoot);
        Assert.Equal("internal", settings.Channel);
        Assert.False(settings.MigrateLegacyServer("https://pub-test.r2.dev"));
    }
}
