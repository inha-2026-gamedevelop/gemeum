// Unity
using UnityEngine;

// Project
using Geumeum.Common;

namespace Geumeum.Art
{
    /// <summary>Returns particle and trail presentation objects to the local pool.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LocalPoolRoot))]
    public sealed class PooledVisualLifetime : MonoBehaviour, IPoolable
    {
        [SerializeField, Min(0.01f)] private float _lifetimeSeconds = 1f;

        private ParticleSystem[] _particles;
        private TrailRenderer[] _trails;
        private float _remainingSeconds;
        private bool _isRented;
        private bool _particlesPaused;
        private uint _leaseGeneration;

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
                return;
            }

            ResumeParticles();

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
            _remainingSeconds = LifetimeSeconds;
            ResetAndPlayParticles(0f);
            if (RunTimeLocator.Current.IsPaused)
            {
                PauseParticles();
            }
        }

        public void OnReturnToPool()
        {
            _isRented = false;
            _particlesPaused = false;
            _remainingSeconds = 0f;
            StopAndClearVisuals();
        }

        /// <summary>Seeks late cues without replaying the elapsed beginning.</summary>
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

            ResetAndPlayParticles(LifetimeSeconds * progress);
            if (RunTimeLocator.Current.IsPaused)
            {
                PauseParticles();
            }
        }

        /// <summary>Idempotent cancellation for exact action-cue ownership.</summary>
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
            _trails = GetComponentsInChildren<TrailRenderer>(true);
        }

        private void ResetAndPlayParticles(float elapsedSeconds)
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
        }
    }
}
