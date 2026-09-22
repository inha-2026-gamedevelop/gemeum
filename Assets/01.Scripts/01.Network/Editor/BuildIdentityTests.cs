// Unity
using UnityEngine;
using UnityEngine.TestTools;

// Third party
using NUnit.Framework;

namespace Geumeum.Network.Tests
{
    public sealed class BuildIdentityTests
    {
        private bool _previousAllow;

        [SetUp]
        public void SetUp()
        {
            _previousAllow = BuildIdentity.EditorAllowMismatch;
            BuildIdentity.EditorAllowMismatch = false;
        }

        [TearDown]
        public void TearDown()
        {
            BuildIdentity.EditorAllowMismatch = _previousAllow;
        }

        [Test]
        public void EditorRunsUseTheEditorReleaseId()
        {
            Assert.AreEqual(BuildIdentity.EDITOR_RELEASE_ID, BuildIdentity.ReleaseId);
        }

        [Test]
        public void SameReleaseIdIsCompatible()
        {
            Assert.IsTrue(BuildIdentity.IsCompatible(BuildIdentity.EDITOR_RELEASE_ID, "클라", out string reason));
            Assert.IsNull(reason);
        }

        [TestCase("win64-20260919-1432")]
        [TestCase("dev")]
        [TestCase("")]
        [TestCase(null)]
        public void DifferentReleaseIdIsRejected(string other)
        {
            Assert.IsFalse(BuildIdentity.IsCompatible(other, "클라", out string reason));
            StringAssert.Contains("빌드가 다르다", reason);
        }

        [Test]
        public void UnknownNeverMatchesEvenItself()
        {
            // 정체성 파일이 없는 빌드 둘이 서로 붙으면 안 된다. 에디터에서는 내 값이 editor라 이 경로를 비교로만 확인한다
            Assert.IsFalse(BuildIdentity.IsCompatible(BuildIdentity.UNKNOWN_RELEASE_ID, "클라", out _));
        }

        [Test]
        public void EditorAllowMismatchPassesWithWarning()
        {
            BuildIdentity.EditorAllowMismatch = true;
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("불일치 허용"));

            Assert.IsTrue(BuildIdentity.IsCompatible("win64-20260919-1432", "클라", out string reason));
            StringAssert.Contains("빌드가 다르다", reason);
        }

        [Test]
        public void SerializedShapeMatchesThePackagerContract()
        {
            string json = BuildIdentity.Serialize(new BuildIdentity.Data
            {
                releaseId       = "win64-20260919-1432",
                displayVersion  = "0.1",
                protocolVersion = BuildIdentity.PROTOCOL_VERSION,
                contentVersion  = "a1b2c3d4e5f6",
            });

            Assert.AreEqual(
                "{\"releaseId\":\"win64-20260919-1432\",\"displayVersion\":\"0.1\",\"protocolVersion\":1,\"contentVersion\":\"a1b2c3d4e5f6\"}",
                json);
        }
    }
}
