// Unity
using UnityEngine;

// Third party
using NUnit.Framework;

// Project
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art.Tests
{
    /// <summary> 프레임 독립 셰이크와 카메라 연출 가중치 </summary>
    public sealed class CameraShakeTests
    {
        private const float FULL = 0.999f;

        [Test]
        public void LowFrameRateHighFrequencyStillReachesPeaksAndAlternates()
        {
            // 15fps에 30Hz면 한 프레임에 극값 네 개를 지나간다
            var oscillator = PeakPreservingOscillator.StartAtPeak(true);
            float previous = oscillator.Current;

            for (int i = 0; i < 30; ++i)
            {
                float value = oscillator.Step(30f, 1f / 15f);

                Assert.GreaterOrEqual(Mathf.Abs(value), FULL, $"{i}번째 프레임이 극값을 놓쳤다");
                Assert.AreNotEqual(Mathf.Sign(previous), Mathf.Sign(value), $"{i}번째 프레임이 한쪽으로 쏠렸다");

                previous = value;
            }
        }

        [Test]
        public void PlainSamplingCanLoseTheWholeShake()
        {
            // 이 기법이 왜 필요한지 남긴다 - 30fps에 15Hz를 영점에서 그냥 샘플링하면 매 프레임 0이다
            float maximum = 0f;

            for (int i = 1; i <= 30; ++i)
            {
                maximum = Mathf.Max(maximum, Mathf.Abs(Mathf.Sin(15f * (i / 30f) * Mathf.PI * 2f)));
            }

            Assert.Less(maximum, 0.01f);

            var oscillator = PeakPreservingOscillator.StartAtPeak(true);
            for (int i = 0; i < 30; ++i)
            {
                Assert.GreaterOrEqual(Mathf.Abs(oscillator.Step(15f, 1f / 30f)), FULL);
            }
        }

        [Test]
        public void HighFrameRateFollowsTheSineWithoutJumps()
        {
            var oscillator = PeakPreservingOscillator.StartAtPeak(false);
            float previous = oscillator.Current;
            float maximum  = 0f;

            for (int i = 0; i < 240; ++i)
            {
                float value = oscillator.Step(5f, 1f / 240f);

                Assert.Less(Mathf.Abs(value - previous), 0.2f, $"{i}번째 프레임이 튀었다");

                maximum  = Mathf.Max(maximum, Mathf.Abs(value));
                previous = value;
            }

            Assert.GreaterOrEqual(maximum, FULL);
        }

        [Test]
        public void EvenNumberOfSkippedPeaksIsFlipped()
        {
            // 한 프레임에 정확히 한 주기(극값 두 개)를 지나면 실제 위상은 같은 부호로 돌아온다. 강제로 뒤집어야 한다
            var oscillator = PeakPreservingOscillator.StartAtPeak(true);

            float first  = oscillator.Step(10f, 0.1f);
            float second = oscillator.Step(10f, 0.1f);

            Assert.AreEqual(-1f, first);
            Assert.AreEqual(1f, second);
        }

        [Test]
        public void EffectWeightBlendsInHoldsAndReturns()
        {
            var spec = new CameraConfigSO.CameraEffectSpec { BlendIn = 0.1f, Hold = 0.2f, BlendOut = 0.3f };

            Assert.AreEqual(0f, CameraEffectPlayer.EvaluateWeight(spec, 0f), 1e-4f);
            Assert.AreEqual(0.5f, CameraEffectPlayer.EvaluateWeight(spec, 0.05f), 1e-4f);
            Assert.AreEqual(1f, CameraEffectPlayer.EvaluateWeight(spec, 0.2f), 1e-4f);
            Assert.AreEqual(0.5f, CameraEffectPlayer.EvaluateWeight(spec, 0.45f), 1e-4f);
            Assert.AreEqual(0f, CameraEffectPlayer.EvaluateWeight(spec, 0.6f), 1e-4f);
            Assert.AreEqual(0f, CameraEffectPlayer.EvaluateWeight(spec, 1f), 1e-4f);
        }

        [Test]
        public void ShippedShakesDifferInShape()
        {
            // 종류별 모양(방향·박자·감쇠)이 서로 달라야 같은 흔들림으로 안 느껴진다
            CameraConfigSO config = ScriptableObject.CreateInstance<CameraConfigSO>();

            try
            {
                CameraConfigSO.ShakeSpec[] specs =
                {
                    config.ResolveHitShake(HitStrength.Light), config.ShakeHitTaken, config.ShakeParrySuccess,
                    config.ShakeCritical, config.ShakeGroggyEnter, config.ShakePartBreak,
                };

                for (int i = 0; i < specs.Length; ++i)
                {
                    Assert.Greater(specs[i].Frequency, 0f, $"{i}번 셰이크 박자가 비었다");
                    Assert.Greater(specs[i].DecayPower, 0f, $"{i}번 셰이크 감쇠가 비었다");

                    for (int j = i + 1; j < specs.Length; ++j)
                    {
                        bool same = (specs[i].Direction == specs[j].Direction) &&
                                    Mathf.Approximately(specs[i].Frequency, specs[j].Frequency) &&
                                    Mathf.Approximately(specs[i].DecayPower, specs[j].DecayPower);

                        Assert.IsFalse(same, $"{i}번과 {j}번 셰이크 모양이 같다");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
