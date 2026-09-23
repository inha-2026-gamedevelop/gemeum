using UnityEngine;

namespace Geumeum.Art
{
    /// <summary>Deterministic inward particles, sampled on the presentation clock.</summary>
    public sealed class BowChargeVfxView : MonoBehaviour
    {
        [SerializeField] private ParticleSystem _particles;
        [SerializeField] private MeshRenderer _core;
        [SerializeField, Min(0.01f)] private float _radius = 0.72f;
        private ParticleSystem.Particle[] _buffer;
        private MaterialPropertyBlock _block;
        public void ResetVisual()
        {
            if (_particles != null)
            {
                _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            if (_core != null)
            {
                _core.enabled = false;
            }
        }
        private void OnDisable() => ResetVisual();
        public void Evaluate(float progress, float elapsed)
        {
            if (_buffer == null)
            {
                _buffer = new ParticleSystem.Particle[24];
            }

            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }

            progress = Mathf.Clamp01(progress);
            if (_core != null)
            {
                _core.enabled = true;
                _core.transform.localScale = Vector3.one * Mathf.Lerp(0.045f, 0.1f, progress);
                _block.SetFloat("_Age", elapsed);
                _block.SetFloat("_Opacity", 0.35f + progress * 0.65f);
                _block.SetFloat("_Dissolve", 0f);
                _core.SetPropertyBlock(_block);
            }
            if (_particles == null)
            {
                return;
            }

            _particles.Pause(true);
            for (int i = 0; i < _buffer.Length; ++i)
            {
                float seed = BowLaserVfxView.Hash(i + 51);
                float phase = Mathf.Repeat(elapsed * (0.9f + seed * 0.45f) + seed, 1f);
                float a = i * 2.399963f;
                Vector3 direction = new Vector3(Mathf.Cos(a), Mathf.Sin(a), (seed - 0.5f) * 0.65f).normalized;
                float radius = _radius * (1f - phase) * (0.45f + progress * 0.55f);
                _buffer[i] = new ParticleSystem.Particle { position = direction * radius, velocity = -direction * (0.3f + progress), startSize = Mathf.Lerp(0.012f, 0.034f, progress) * Mathf.Sin(phase * Mathf.PI), startColor = new Color(1f, 0.12f + phase * 0.32f, 0.26f + phase * 0.25f, Mathf.Sin(phase * Mathf.PI)), startLifetime = 1f, remainingLifetime = 1f, randomSeed = (uint)(i + 1) };
            }
            _particles.SetParticles(_buffer, _buffer.Length);
        }
    }
}
