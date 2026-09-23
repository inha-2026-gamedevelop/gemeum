// Unity
using UnityEngine;
using UnityEngine.VFX;

// Project
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>Returns particle, trail, and VFX Graph presentation objects to the local pool</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LocalPoolRoot))]
    public sealed class PooledVisualLifetime : MonoBehaviour, IPoolable
    {
        [SerializeField, Min(0.01f)] private float _lifetimeSeconds = 1f;

        private ParticleSystem[] _particles;
        private VisualEffect[] _visualEffects;
        private bool[] _visualEffectsPausedByRun;
        private TrailRenderer[] _trails;
        private IActionCueVisual[] _cueVisuals;
        private float _remainingSeconds;
        private bool _isRented;
        private bool _particlesPaused;
        private bool _automaticReturnSuppressed;
        private uint _leaseGeneration;

        private const float GRAPH_SIMULATION_STEP_SECONDS = 1f / 60f;
        private const int MAX_GRAPH_SIMULATION_STEPS = 1024;

        public float LifetimeSeconds => Mathf.Max(0.01f, _lifetimeSeconds);
        public bool IsRented => _isRented;
        public uint LeaseGeneration => _leaseGeneration;

        private void Awake()
        {
            CacheVisuals();
        }

        private void Update()
        {
            if (!_isRented)
            {
                return;
            }

            if (RunTimeLocator.Current.IsPaused)
            {
                PauseParticles();
                PauseVisualEffects();
                return;
            }

            ResumeParticles();
            ResumeVisualEffects();

            if (_automaticReturnSuppressed)
            {
                return;
            }

            _remainingSeconds -= Time.unscaledDeltaTime;
            if (_remainingSeconds <= 0f)
            {
                TryStop(_leaseGeneration);
            }
        }

        public void OnRentFromPool()
        {
            CacheVisuals();
            _leaseGeneration = (_leaseGeneration == uint.MaxValue)
                ? 1u
                : _leaseGeneration + 1u;
            _isRented = true;
            _particlesPaused = false;
            _automaticReturnSuppressed = false;
            _remainingSeconds = LifetimeSeconds;
            ResetAndPlayVisuals(0f);
            if (RunTimeLocator.Current.IsPaused)
            {
                PauseParticles();
                PauseVisualEffects();
            }
        }

        public void OnReturnToPool()
        {
            _isRented = false;
            _particlesPaused = false;
            _automaticReturnSuppressed = false;
            _remainingSeconds = 0f;
            StopAndClearVisuals();
        }

        /// <summary>Prevents a temporary cook lease from expiring before its visual work finishes</summary>
        public void SetAutomaticReturnSuppressed(bool suppressed)
        {
            if (_isRented)
            {
                _automaticReturnSuppressed = suppressed;
            }
        }

        /// <summary>Seeks late cues without replaying the elapsed beginning</summary>
        public void BeginAtProgress(float normalizedProgress)
        {
            if (!_isRented)
            {
                return;
            }

            float progress = Mathf.Clamp01(normalizedProgress);
            _remainingSeconds = LifetimeSeconds * (1f - progress);
            if (_remainingSeconds <= 0f)
            {
                TryStop(_leaseGeneration);
                return;
            }

            ResetAndPlayVisuals(LifetimeSeconds * progress);
            if (RunTimeLocator.Current.IsPaused)
            {
                PauseParticles();
                PauseVisualEffects();
            }
        }

        /// <summary> 서버 cue 시각표로 스스로 진행하는 연출에 cue를 넘긴다 </summary>
        public void PlayCue(in PlayerActionCue cue, IWeaponBladeSource blade)
        {
            if (!_isRented)
            {
                return;
            }

            for (int i = 0; i < _cueVisuals.Length; ++i)
            {
                _cueVisuals[i].PlayCue(in cue, blade);
            }
        }

        /// <summary> 빌린 쪽이 손을 뗀다. 같은 대여일 때만 전달해 이미 다른 공격에 재사용된 연출을 건드리지 않는다 </summary>
        public void ReleaseCue(uint expectedLeaseGeneration)
        {
            if ((!_isRented) || (expectedLeaseGeneration == 0u) ||
                (expectedLeaseGeneration != _leaseGeneration))
            {
                return;
            }

            for (int i = 0; i < _cueVisuals.Length; ++i)
            {
                _cueVisuals[i].ReleaseCue();
            }
        }

        /// <summary>Idempotent cancellation for exact action-cue ownership</summary>
        public bool TryStop(uint expectedLeaseGeneration)
        {
            if ((!_isRented) || (expectedLeaseGeneration == 0u) ||
                (expectedLeaseGeneration != _leaseGeneration))
            {
                return false;
            }

            _isRented = false;
            LocalObjectPoolService pool = LocalObjectPoolService.Instance;
            if (pool != null)
            {
                return pool.TryReturn(gameObject);
            }

            StopAndClearVisuals();
            gameObject.SetActive(false);
            return true;
        }

        private void CacheVisuals()
        {
            _particles = GetComponentsInChildren<ParticleSystem>(true);
            _visualEffects = GetComponentsInChildren<VisualEffect>(true);
            if ((_visualEffectsPausedByRun == null) ||
                (_visualEffectsPausedByRun.Length != _visualEffects.Length))
            {
                _visualEffectsPausedByRun = new bool[_visualEffects.Length];
            }
            _trails = GetComponentsInChildren<TrailRenderer>(true);
            _cueVisuals = GetComponentsInChildren<IActionCueVisual>(true);
        }

        private void ResetAndPlayVisuals(float elapsedSeconds)
        {
            _particlesPaused = false;
            for (int i = 0; i < _trails.Length; ++i)
            {
                if (_trails[i] != null)
                {
                    _trails[i].Clear();
                }
            }

            for (int i = 0; i < _particles.Length; ++i)
            {
                ParticleSystem particle = _particles[i];
                if (particle == null)
                {
                    continue;
                }

                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (elapsedSeconds > 0f)
                {
                    particle.Simulate(elapsedSeconds, false, true, true);
                }

                particle.Play(false);
            }

            for (int i = 0; i < _visualEffects.Length; ++i)
            {
                _visualEffectsPausedByRun[i] = false;
                VisualEffect effect = _visualEffects[i];
                if ((effect == null) || (effect.visualEffectAsset == null))
                {
                    continue;
                }

                effect.pause = false;
                // Reinit already sends the configured initial event; Play would send it twice.
                effect.Reinit();
                if (elapsedSeconds > 0f)
                {
                    int stepCount = Mathf.Clamp(
                        Mathf.CeilToInt(elapsedSeconds / GRAPH_SIMULATION_STEP_SECONDS),
                        1, MAX_GRAPH_SIMULATION_STEPS);
                    effect.Simulate(elapsedSeconds / stepCount, (uint)stepCount);
                }
            }
        }

        private void PauseParticles()
        {
            if (_particlesPaused || (_particles == null))
            {
                return;
            }

            for (int i = 0; i < _particles.Length; ++i)
            {
                if ((_particles[i] != null) && _particles[i].isPlaying)
                {
                    _particles[i].Pause(false);
                }
            }

            _particlesPaused = true;
        }

        private void ResumeParticles()
        {
            if (!_particlesPaused || (_particles == null))
            {
                return;
            }

            for (int i = 0; i < _particles.Length; ++i)
            {
                if ((_particles[i] != null) && _particles[i].isPaused)
                {
                    _particles[i].Play(false);
                }
            }

            _particlesPaused = false;
        }

        private void PauseVisualEffects()
        {
            for (int i = 0; i < _visualEffects.Length; ++i)
            {
                VisualEffect effect = _visualEffects[i];
                if ((effect != null) && !effect.pause)
                {
                    effect.pause = true;
                    _visualEffectsPausedByRun[i] = true;
                }
            }
        }

        private void ResumeVisualEffects()
        {
            for (int i = 0; i < _visualEffects.Length; ++i)
            {
                if (!_visualEffectsPausedByRun[i])
                {
                    continue;
                }

                VisualEffect effect = _visualEffects[i];
                if (effect != null)
                {
                    effect.pause = false;
                }

                _visualEffectsPausedByRun[i] = false;
            }
        }

        private void StopAndClearVisuals()
        {
            if (_particles != null)
            {
                for (int i = 0; i < _particles.Length; ++i)
                {
                    if (_particles[i] != null)
                    {
                        _particles[i].Stop(false,
                            ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
            }

            if (_trails != null)
            {
                for (int i = 0; i < _trails.Length; ++i)
                {
                    if (_trails[i] != null)
                    {
                        _trails[i].Clear();
                    }
                }
            }

            if (_visualEffects != null)
            {
                for (int i = 0; i < _visualEffects.Length; ++i)
                {
                    _visualEffectsPausedByRun[i] = false;
                    VisualEffect effect = _visualEffects[i];
                    if ((effect == null) || (effect.visualEffectAsset == null))
                    {
                        continue;
                    }

                    effect.pause = false;
                    effect.Stop();
                    // Stop halts spawners; Reinit also discards particles from this lease.
                    effect.Reinit();
                    effect.Stop();
                }
            }
        }
    }
}
