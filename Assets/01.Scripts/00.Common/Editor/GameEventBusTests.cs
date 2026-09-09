// System
using System;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// Third party
using NUnit.Framework;

namespace Geumeum.Common.Tests
{
    public sealed class GameEventBusTests
    {
        private readonly struct TestEvent
        {
            public int Value { get; }

            public TestEvent(int value)
            {
                Value = value;
            }
        }

        private GameObject   _host;
        private GameEventBus _bus;

        [SetUp]
        public void SetUp()
        {
            GameEventBus.ResetStatic();
            _host = new GameObject("GameEventBusTests", typeof(GameEventBus));
            _bus = _host.GetComponent<GameEventBus>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
            }

            GameEventBus.ResetStatic();
        }

        [Test]
        public void ThrowingFirstObserverStillPublishesToTrailingObserver()
        {
            int trailingValue = 0;
            _bus.Subscribe<TestEvent>(_ =>
            {
                throw new InvalidOperationException("first observer test");
            });
            _bus.Subscribe<TestEvent>(payload => trailingValue = payload.Value);

            LogAssert.Expect(LogType.Error,
                new Regex("\\[GameEventBus\\] TestEvent observer 실패:.*first observer test"));

            Assert.DoesNotThrow(() => _bus.Publish(new TestEvent(7)));
            Assert.AreEqual(7, trailingValue);
        }

        [Test]
        public void UnsubscribeDuringPublishAffectsTheNextPublishOnly()
        {
            int removingObserverCalls = 0;
            int trailingObserverCalls = 0;
            Action<TestEvent> trailingObserver = _ => ++trailingObserverCalls;
            Action<TestEvent> removingObserver = _ =>
            {
                ++removingObserverCalls;
                _bus.Unsubscribe(trailingObserver);
            };

            _bus.Subscribe(removingObserver);
            _bus.Subscribe(trailingObserver);

            _bus.Publish(new TestEvent(1));
            _bus.Publish(new TestEvent(2));

            Assert.AreEqual(2, removingObserverCalls);
            Assert.AreEqual(1, trailingObserverCalls);
        }
    }
}
