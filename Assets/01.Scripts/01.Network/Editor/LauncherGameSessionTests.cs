using System.IO;
using NUnit.Framework;

namespace Geumeum.Network.Tests
{
    public sealed class LauncherGameSessionTests
    {
        [TestCase("http://example.com")]
        [TestCase("https://example.com/api")]
        [TestCase("https://user:password@example.com")]
        [TestCase("https://example.com/?token=secret")]
        [TestCase("http://127.0.0.2")]
        public void UnsafeOriginIsRejected(string origin)
        {
            Assert.Throws<IOException>(() => LauncherGameSession.ValidateOrigin(origin, true));
        }

        [Test]
        public void HttpRequiresDevelopmentBuild()
        {
            Assert.Throws<IOException>(() => LauncherGameSession.ValidateOrigin("http://127.0.0.1:8766", false));
            Assert.IsTrue(LauncherGameSession.ValidateOrigin("http://127.0.0.1:8766", true).IsLoopback);
        }

        [TestCase(null)]
        [TestCase("short")]
        [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\r\n")]
        public void TokenHeaderInjectionIsRejected(string value)
        {
            Assert.IsFalse(LauncherGameSession.ValidToken(value));
        }

        [Test]
        public void OpaqueTokenAndPipeNameAreAccepted()
        {
            Assert.IsTrue(LauncherGameSession.ValidToken(new string('a', 43)));
            Assert.IsTrue(GameSessionPipeClient.IsValidName("geumeum-account-" + new string('0', 32)));
            Assert.IsFalse(GameSessionPipeClient.IsValidName("other-pipe"));
        }
    }
}
