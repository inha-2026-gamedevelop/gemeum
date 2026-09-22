using Geumeum.Common;
using Mirror;
using NUnit.Framework;
using UnityEngine;

namespace Geumeum.Network.Tests
{
    public sealed class ParticipantDirectoryTests
    {
        private bool _wasActive;
        private GameObject _first;
        private GameObject _second;

        [SetUp]
        public void SetUp()
        {
            _wasActive = NetworkServer.active;
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, true);
            ParticipantDirectory.ResetStatic();
            _first = new GameObject("ParticipantA", typeof(NetworkIdentity));
            _second = new GameObject("ParticipantB", typeof(NetworkIdentity));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_first);
            Object.DestroyImmediate(_second);
            ParticipantDirectory.ResetStatic();
            typeof(NetworkServer).GetProperty(nameof(NetworkServer.active)).SetValue(null, _wasActive);
        }

        [Test]
        public void OwnerlessBotAndSameNumberHumanHaveSeparateEntries()
        {
            Assert.IsTrue(ParticipantDirectory.TryAllocateBot(out var bot));
            var human = new ParticipantId(bot.Value);
            Assert.IsTrue(ParticipantDirectory.Register(bot, _first.GetComponent<NetworkIdentity>()));
            Assert.IsTrue(ParticipantDirectory.Register(human, _second.GetComponent<NetworkIdentity>()));
            Assert.AreEqual(2, ParticipantDirectory.Count);
            Assert.IsTrue(ParticipantDirectory.TryGet(bot, out var actor));
            Assert.IsNull(actor.connectionToClient);
            Assert.AreSame(_first, actor.gameObject);
        }

        [Test]
        public void DuplicateCannotReplaceLiveActorOrRemoveItsEntry()
        {
            var id = new ParticipantId(12UL);
            var first = _first.GetComponent<NetworkIdentity>();
            var second = _second.GetComponent<NetworkIdentity>();
            Assert.IsTrue(ParticipantDirectory.Register(id, first));
            Assert.IsFalse(ParticipantDirectory.Register(id, second));
            ParticipantDirectory.Unregister(id, second);
            Assert.IsTrue(ParticipantDirectory.TryGet(id, out var actor));
            Assert.AreSame(first, actor);
            ParticipantDirectory.Unregister(id, first);
            Assert.IsFalse(ParticipantDirectory.TryGet(id, out _));
        }

        [Test]
        public void AllocationDoesNotReuseDespawnedIdsAndInvalidKindIsRejected()
        {
            Assert.IsTrue(ParticipantDirectory.TryAllocateBot(out var first));
            Assert.IsTrue(ParticipantDirectory.TryAllocateBot(out var next));
            Assert.AreNotEqual(first, next);
            Assert.IsFalse(new ParticipantId((ParticipantKind)255, 1UL).IsValid);
            Assert.IsFalse(new ParticipantId(ParticipantKind.Bot, 0UL).IsValid);
        }
    }
}
