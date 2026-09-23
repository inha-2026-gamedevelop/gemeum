using System;
using System.Net;
using System.Reflection;
using NUnit.Framework;

namespace Geumeum.Network.Tests
{
    public sealed class AccountRoomTests
    {
        private static readonly Type Session = typeof(LauncherGameSession);

        [Test]
        public void RoomResponsePreservesUnselectedAndFirstWeaponPreference()
        {
            string json = "{\"room_id\":\"d8240378-2cba-4a70-a965-fb628765c6ad\",\"name\":\"test\","
                + "\"members\":[{\"account_id\":\"1\",\"preference\":null},"
                + "{\"account_id\":\"2\",\"preference\":0}]}";
            MethodInfo parse = Session.GetMethod("ParseRoomJson", BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(typeof(AccountRoom));
            AccountRoom room = (AccountRoom)parse.Invoke(null, new object[] { json });
            Assert.IsNull(room.members[0].preference);
            Assert.AreEqual(0, room.members[1].preference);
        }

        [TestCase("pulse", true)]
        [TestCase("preference", true)]
        [TestCase("ready", true)]
        [TestCase("start", true)]
        [TestCase("../host", false)]
        [TestCase("tickets", false)]
        public void LobbyActionIsAllowlisted(string action, bool expected)
        {
            MethodInfo valid = Session.GetMethod("ValidLobbyAction", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.AreEqual(expected, valid.Invoke(null, new object[] { action }));
        }

        [Test]
        public void MalformedRoomIdentityIsRejectedBeforeConnection()
        {
            MethodInfo validate = Session.GetMethod("ValidateRoomIdentity", BindingFlags.NonPublic | BindingFlags.Static);
            var room = new AccountRoom
            {
                room_id = "../relay",
                name = "test",
                build_id = BuildIdentity.ReleaseId,
                max_players = 3,
                members = Array.Empty<AccountRoomMember>(),
            };
            TargetInvocationException error = Assert.Throws<TargetInvocationException>(
                () => validate.Invoke(null, new object[] { room }));
            Assert.IsInstanceOf<AccountRoomException>(error.InnerException);
            Assert.AreEqual("invalid_response", ((AccountRoomException)error.InnerException).Code);
        }

        [Test]
        public void IncompatibleRoomBuildHasSpecificSafeError()
        {
            MethodInfo validate = Session.GetMethod("ValidateRoomIdentity", BindingFlags.NonPublic | BindingFlags.Static);
            var room = new AccountRoom
            {
                room_id = Guid.NewGuid().ToString(),
                name = "test",
                build_id = "different-build",
                max_players = 3,
                members = Array.Empty<AccountRoomMember>(),
            };
            TargetInvocationException error = Assert.Throws<TargetInvocationException>(
                () => validate.Invoke(null, new object[] { room }));
            Assert.IsInstanceOf<AccountRoomException>(error.InnerException);
            Assert.AreEqual("build_mismatch", ((AccountRoomException)error.InnerException).Code);
        }

        [TestCase(HttpStatusCode.Conflict, "room_conflict")]
        [TestCase(HttpStatusCode.Forbidden, "forbidden")]
        [TestCase(HttpStatusCode.TooManyRequests, "rate_limited")]
        [TestCase(HttpStatusCode.InternalServerError, "server_error")]
        public void HttpErrorsExposeOnlyFixedUserMessages(HttpStatusCode status, string code)
        {
            MethodInfo map = Session.GetMethod("RoomRequestError", BindingFlags.NonPublic | BindingFlags.Static);
            var error = (AccountRoomException)map.Invoke(null, new object[] { status });
            Assert.AreEqual(code, error.Code);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error.Message));
        }
    }
}
