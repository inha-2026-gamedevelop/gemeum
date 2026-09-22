using Geumeum.Launcher;
using Xunit;

namespace Geumeum.Patch.Tests;

public class SteamLoginStatusTests
{
    [Theory]
    [InlineData(false, 123, true, SteamLoginState.NotRunning)]
    [InlineData(false, null, false, SteamLoginState.NotRunning)]
    [InlineData(true, 0, true, SteamLoginState.SignedOut)]
    [InlineData(true, 123, true, SteamLoginState.SignedIn)]
    [InlineData(true, -1, true, SteamLoginState.SignedIn)]
    [InlineData(true, null, true, SteamLoginState.Unknown)]
    [InlineData(true, 123, false, SteamLoginState.Unknown)]
    [InlineData(true, 0, false, SteamLoginState.Unknown)]
    public void RequiresLiveMatchingSession(bool running, int? user, bool matches, SteamLoginState expected)
    {
        Assert.Equal(expected, SteamLoginStatus.Evaluate(running, user, matches));
    }
}
