// Unity
using UnityEngine;

// Third party
using DigitalRuby.WeatherMaker;

// Project
using Geumeum.Common;
using Geumeum.Common.Environment;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Art
{
    public enum BossArenaPhase
    {
        Shrine     = 1,
        Underworld = 2,
    }

    /// <summary>권위 보스 phase snapshot을 아레나의 날씨와 재질 표현으로 옮긴다</summary>
    public class BossArenaPhaseView : MonoBehaviour, IRunSceneBinding
    {
        private const float DEFAULT_PHASE_TRANSITION_SECONDS = 6f;
        private const float DEFAULT_SHRINE_SMOOTHNESS        = 0.35f;
        private const float DEFAULT_UNDERWORLD_SMOOTHNESS    = 0.9f;

        private static readonly int G_Smoothness = Shader.PropertyToID("_Smoothness");

        [Header("페이즈 루트")]
        [SerializeField] private GameObject _phase1Root;
        [SerializeField] private GameObject _phase2Root;

        [Header("공용")]
        [SerializeField] private WeatherMakerFullScreenFogScript _fog;
        [SerializeField] private WeatherMakerSkySphereScript     _sky;
        [SerializeField] private Renderer                        _floorRenderer;
        [SerializeField] private GameObject                      _weatherMakerMoon;

        [Header("1페이즈 - 저승 서당")]
        [SerializeField] private WeatherMakerFullScreenFogProfileScript _fogProfileShrine;
        [SerializeField] private WeatherMakerSkyProfileScript           _skyProfileShrine;
        [SerializeField] private Material                               _floorMaterialShrine;

        [Header("2페이즈 - 원혼 물결")]
        [SerializeField] private WeatherMakerFullScreenFogProfileScript _fogProfileUnderworld;
        [SerializeField] private WeatherMakerSkyProfileScript           _skyProfileUnderworld;
        [SerializeField] private Material                               _floorMaterialUnderworld;

        private IBossPhaseSource _phaseSource;
        private BossPhaseSnapshot _snapshot;
        private Renderer _moonRenderer;
        private Material _originalFloorMaterial;
        private Material _floorMaterialInstance;
        private Material _floorMaterialTemplate;
        private WeatherMakerFullScreenFogProfileScript _fogProfileInstance;
        private bool _isBound;
        private bool _hasSnapshot;
        private bool _isLocatorSubscribed;
        private bool _warnedMissingSmoothness;

        public BossArenaPhase CurrentPhase { get; private set; } = BossArenaPhase.Shrine;

        private void Awake()
        {
            if (_floorRenderer != null)
            {
                _originalFloorMaterial = _floorRenderer.sharedMaterial;
            }

            ResolveWeatherMaker();
        }

        private void OnEnable()
        {
            RunSceneDirector.Register(this);
            SubscribePhaseLocator();
        }

        private void Update()
        {
            if ((!_isBound) || (!_hasSnapshot) ||
                (CurrentPhase != BossArenaPhase.Underworld))
            {
                return;
            }

            ApplyFloorTransition(BossArenaPhaseRules.ResolveTransitionProgress(
                in _snapshot,
                RunTimeLocator.Current.GameplayElapsedSeconds,
                ResolveTransitionSeconds()));
            ApplyFogTransition(BossArenaPhaseRules.ResolveTransitionProgress(
                in _snapshot,
                RunTimeLocator.Current.GameplayElapsedSeconds,
                ResolveTransitionSeconds()));
        }

        private void OnDisable()
        {
            UnsubscribePhaseLocator();
            RunSceneDirector.Unregister(this);
            Unbind();
            ReleaseFloorMaterial();
            ReleaseFogProfile();
        }

        public void Bind(in SceneTransitionSnapshot transition)
        {
            _isBound = true;
            SubscribePhaseLocator();
            AttachPhaseSource(BossPhaseSourceLocator.Current);

            if (_phaseSource == null)
            {
                ApplyImmediatePhase(BossArenaPhase.Shrine);
            }
        }

        public void Unbind()
        {
            _isBound = false;
            _hasSnapshot = false;
            DetachPhaseSource();
            UnsubscribePhaseLocator();
        }

        private void SubscribePhaseLocator()
        {
            if (_isLocatorSubscribed)
            {
                return;
            }

            BossPhaseSourceLocator.Changed += HandlePhaseSourceChanged;
            _isLocatorSubscribed = true;
        }

        private void UnsubscribePhaseLocator()
        {
            if (!_isLocatorSubscribed)
            {
                return;
            }

            BossPhaseSourceLocator.Changed -= HandlePhaseSourceChanged;
            _isLocatorSubscribed = false;
        }

        /// <summary>개발 미리보기에서 권위 snapshot 없이 즉시 페이즈를 바꾼다</summary>
        public void ShowPhase(BossArenaPhase phase)
        {
            ApplyImmediatePhase(phase);
        }

        private void HandlePhaseSourceChanged(IBossPhaseSource source)
        {
            if (_isBound)
            {
                AttachPhaseSource(source);
            }
        }

        private void AttachPhaseSource(IBossPhaseSource source)
        {
            if (ReferenceEquals(_phaseSource, source))
            {
                if (source != null)
                {
                    ApplySnapshot(source.PhaseSnapshot, true);
                }

                return;
            }

            DetachPhaseSource();
            _phaseSource = source;
            if (_phaseSource == null)
            {
                return;
            }

            _phaseSource.PhaseChanged += HandlePhaseChanged;
            ApplySnapshot(_phaseSource.PhaseSnapshot, true);
        }

        private void DetachPhaseSource()
        {
            if (_phaseSource != null)
            {
                _phaseSource.PhaseChanged -= HandlePhaseChanged;
                _phaseSource = null;
            }
        }

        private void HandlePhaseChanged(BossPhaseSnapshot snapshot)
        {
            ApplySnapshot(snapshot, false);
        }

        private void ApplySnapshot(BossPhaseSnapshot snapshot, bool force)
        {
            if (snapshot.Revision == 0u)
            {
                return;
            }

            if ((!force) && _hasSnapshot && (snapshot.Revision == _snapshot.Revision))
            {
                return;
            }

            BossArenaPhase phase = BossArenaPhaseRules.ResolvePhase(in snapshot);
            bool phaseChanged = (!_hasSnapshot) || (phase != CurrentPhase);
            _snapshot = snapshot;
            _hasSnapshot = true;

            if (!phaseChanged)
            {
                return;
            }

            if (phase == BossArenaPhase.Shrine)
            {
                ApplyImmediatePhase(phase);
                return;
            }

            PlayPhaseShiftOneShot(in snapshot);

            float progress = BossArenaPhaseRules.ResolveTransitionProgress(
                in snapshot,
                RunTimeLocator.Current.GameplayElapsedSeconds,
                ResolveTransitionSeconds());
            ApplyPhaseState(phase);
            PrepareFogTransition();
            ApplyFogTransition(progress);
            ApplyFloorTransition(progress);
        }

        private void ApplyImmediatePhase(BossArenaPhase phase)
        {
            ApplyPhaseState(phase);
            ApplyFogImmediate(phase);
            ApplyFloorTransition((phase == BossArenaPhase.Shrine) ? 0f : 1f);
        }

        private void ApplyPhaseState(BossArenaPhase phase)
        {
            bool isShrine = (phase == BossArenaPhase.Shrine);
            CurrentPhase = phase;
            ResolveWeatherMaker();

            if (_phase1Root != null)
            {
                _phase1Root.SetActive(isShrine);
            }

            if (_phase2Root != null)
            {
                _phase2Root.SetActive(!isShrine);
            }

            if (_sky != null)
            {
                _sky.SkySphereProfile = isShrine ? _skyProfileShrine : _skyProfileUnderworld;
            }

            if (_moonRenderer != null)
            {
                _moonRenderer.enabled = isShrine;
            }

            EnsureFloorMaterial(isShrine ? _floorMaterialShrine : _floorMaterialUnderworld);
        }

        private void ApplyFogImmediate(BossArenaPhase phase)
        {
            if (_fog == null)
            {
                return;
            }

            ReleaseFogProfile();
            WeatherMakerFullScreenFogProfileScript profile =
                (phase == BossArenaPhase.Shrine) ? _fogProfileShrine : _fogProfileUnderworld;
            if (profile == null)
            {
                return;
            }

            _fog.FogProfile = profile;
        }

        private void PrepareFogTransition()
        {
            if ((_fog == null) || (_fogProfileUnderworld == null))
            {
                return;
            }

            ReleaseFogProfile();
            _fogProfileInstance = Instantiate(_fogProfileUnderworld);
            _fogProfileInstance.name = _fogProfileUnderworld.name + " (BossArena Runtime)";
            _fog.FogProfile = _fogProfileInstance;
        }

        private void ApplyFogTransition(float progress)
        {
            if ((_fogProfileInstance == null) || (_fogProfileShrine == null) ||
                (_fogProfileUnderworld == null))
            {
                return;
            }

            float t = Mathf.Clamp01(progress);
            _fogProfileInstance.FogDensity = Mathf.Lerp(
                _fogProfileShrine.FogDensity, _fogProfileUnderworld.FogDensity, t);
            _fogProfileInstance.FogFactorMultiplier = Mathf.Lerp(
                _fogProfileShrine.FogFactorMultiplier,
                _fogProfileUnderworld.FogFactorMultiplier, t);
            _fogProfileInstance.FogColor = Color.Lerp(
                _fogProfileShrine.FogColor, _fogProfileUnderworld.FogColor, t);
            _fogProfileInstance.FogEmissionColor = Color.Lerp(
                _fogProfileShrine.FogEmissionColor,
                _fogProfileUnderworld.FogEmissionColor, t);
            _fogProfileInstance.MaxFogFactor = Mathf.Lerp(
                _fogProfileShrine.MaxFogFactor,
                _fogProfileUnderworld.MaxFogFactor, t);
            _fogProfileInstance.DitherLevel = Mathf.Lerp(
                _fogProfileShrine.DitherLevel,
                _fogProfileUnderworld.DitherLevel, t);
            _fogProfileInstance.FogNoiseVelocity = Vector3.Lerp(
                _fogProfileShrine.FogNoiseVelocity,
                _fogProfileUnderworld.FogNoiseVelocity, t);
            _fogProfileInstance.FogNoiseVelocityRotation = Vector4.Lerp(
                _fogProfileShrine.FogNoiseVelocityRotation,
                _fogProfileUnderworld.FogNoiseVelocityRotation, t);
            _fogProfileInstance.FogNoiseAdder = Mathf.Lerp(
                _fogProfileShrine.FogNoiseAdder,
                _fogProfileUnderworld.FogNoiseAdder, t);
            _fogProfileInstance.FogNoiseMultiplier = Mathf.Lerp(
                _fogProfileShrine.FogNoiseMultiplier,
                _fogProfileUnderworld.FogNoiseMultiplier, t);
            _fogProfileInstance.FogNoiseSampleCount = Mathf.RoundToInt(Mathf.Lerp(
                _fogProfileShrine.FogNoiseSampleCount,
                _fogProfileUnderworld.FogNoiseSampleCount, t));
            _fogProfileInstance.FogNoiseScale = Mathf.Lerp(
                _fogProfileShrine.FogNoiseScale,
                _fogProfileUnderworld.FogNoiseScale, t);
            _fogProfileInstance.FogShadowBrightness = Mathf.Lerp(
                _fogProfileShrine.FogShadowBrightness,
                _fogProfileUnderworld.FogShadowBrightness, t);
            _fogProfileInstance.FogShadowDecay = Mathf.Lerp(
                _fogProfileShrine.FogShadowDecay,
                _fogProfileUnderworld.FogShadowDecay, t);
            _fogProfileInstance.FogShadowDither = Mathf.Lerp(
                _fogProfileShrine.FogShadowDither,
                _fogProfileUnderworld.FogShadowDither, t);
            _fogProfileInstance.FogShadowMaxRayLength = Mathf.Lerp(
                _fogProfileShrine.FogShadowMaxRayLength,
                _fogProfileUnderworld.FogShadowMaxRayLength, t);
            _fogProfileInstance.FogShadowMultiplier = Mathf.Lerp(
                _fogProfileShrine.FogShadowMultiplier,
                _fogProfileUnderworld.FogShadowMultiplier, t);
            _fogProfileInstance.FogShadowPower = Mathf.Lerp(
                _fogProfileShrine.FogShadowPower,
                _fogProfileUnderworld.FogShadowPower, t);
            _fogProfileInstance.FogShadowSampleCount = Mathf.RoundToInt(Mathf.Lerp(
                _fogProfileShrine.FogShadowSampleCount,
                _fogProfileUnderworld.FogShadowSampleCount, t));
        }

        private void ReleaseFogProfile()
        {
            if (_fogProfileInstance == null)
            {
                return;
            }

            if ((_fog != null) && (_fog.FogProfile == _fogProfileInstance))
            {
                _fog.FogProfile = _fogProfileUnderworld;
            }

            if (Application.isPlaying)
            {
                Destroy(_fogProfileInstance);
            }
            else
            {
                DestroyImmediate(_fogProfileInstance);
            }

            _fogProfileInstance = null;
        }

        private void ApplyFloorTransition(float progress)
        {
            if (_floorMaterialInstance == null)
            {
                return;
            }

            if (!_floorMaterialInstance.HasProperty(G_Smoothness))
            {
                if (!_warnedMissingSmoothness)
                {
                    _warnedMissingSmoothness = true;
                    Debug.LogWarning("[BossArenaPhaseView] 바닥 재질에 _Smoothness 프로퍼티가 없다", this);
                }

                return;
            }

            _floorMaterialInstance.SetFloat(G_Smoothness,
                Mathf.Lerp(ResolveShrineSmoothness(), ResolveUnderworldSmoothness(),
                    Mathf.Clamp01(progress)));
        }

        private static float ResolveTransitionSeconds()
        {
            PresentationConfigSO config = ResolvePresentationConfig();
            return (config == null)
                ? DEFAULT_PHASE_TRANSITION_SECONDS
                : config.ArenaPhaseTransitionSeconds;
        }

        private static float ResolveShrineSmoothness()
        {
            PresentationConfigSO config = ResolvePresentationConfig();
            return (config == null)
                ? DEFAULT_SHRINE_SMOOTHNESS
                : config.ArenaShrineSmoothness;
        }

        private static float ResolveUnderworldSmoothness()
        {
            PresentationConfigSO config = ResolvePresentationConfig();
            return (config == null)
                ? DEFAULT_UNDERWORLD_SMOOTHNESS
                : config.ArenaUnderworldSmoothness;
        }

        private static PresentationConfigSO ResolvePresentationConfig()
        {
            return GameDB.IsInitialized ? GameDB.Presentation : null;
        }

        private static void PlayPhaseShiftOneShot(in BossPhaseSnapshot snapshot)
        {
            PresentationConfigSO config = ResolvePresentationConfig();
            if (config == null)
            {
                return;
            }

            double elapsed = RunTimeLocator.Current.GameplayElapsedSeconds -
                snapshot.PhaseChangedGameplayTime;
            if ((elapsed < 0d) ||
                (elapsed > config.LateOneShotToleranceSeconds))
            {
                return;
            }

            SfxPlayer.Play(SoundKey.BossPhaseShift, false);
        }

        private void EnsureFloorMaterial(Material template)
        {
            if ((_floorRenderer == null) || (template == null))
            {
                return;
            }

            if ((_floorMaterialInstance != null) &&
                (_floorMaterialTemplate == template))
            {
                return;
            }

            ReleaseFloorMaterial();
            _floorMaterialTemplate = template;
            _floorMaterialInstance = new Material(template)
            {
                name = template.name + " (BossArena Runtime)",
            };
            _floorRenderer.sharedMaterial = _floorMaterialInstance;
        }

        private void ReleaseFloorMaterial()
        {
            if (_floorMaterialInstance == null)
            {
                return;
            }

            if ((_floorRenderer != null) &&
                (_floorRenderer.sharedMaterial == _floorMaterialInstance))
            {
                _floorRenderer.sharedMaterial = _originalFloorMaterial;
            }

            if (Application.isPlaying)
            {
                Destroy(_floorMaterialInstance);
            }
            else
            {
                DestroyImmediate(_floorMaterialInstance);
            }

            _floorMaterialInstance = null;
            _floorMaterialTemplate = null;
        }

        private void ResolveWeatherMaker()
        {
            WeatherMakerSingleInstanceGuard guard =
                WeatherMakerSingleInstanceGuard.Current;
            if (guard != null)
            {
                Transform root = guard.transform;
                _fog = root.GetComponentInChildren<WeatherMakerFullScreenFogScript>(true);
                _sky = root.GetComponentInChildren<WeatherMakerSkySphereScript>(true);
                _weatherMakerMoon = null;

                WeatherMakerCelestialObjectScript[] liveCelestials =
                    root.GetComponentsInChildren<WeatherMakerCelestialObjectScript>(true);
                for (int i = 0; i < liveCelestials.Length; ++i)
                {
                    if (liveCelestials[i].name == "Moon")
                    {
                        _weatherMakerMoon = liveCelestials[i].gameObject;
                        break;
                    }
                }
            }
            else
            {
                // Active-only fallback cannot bind the duplicate root that the guard discarded
                _fog = FindAnyObjectByType<WeatherMakerFullScreenFogScript>();
                _sky = FindAnyObjectByType<WeatherMakerSkySphereScript>();
                if (_weatherMakerMoon == null)
                {
                    WeatherMakerCelestialObjectScript[] celestials =
                        FindObjectsByType<WeatherMakerCelestialObjectScript>(
                            FindObjectsInactive.Exclude);
                    for (int i = 0; i < celestials.Length; ++i)
                    {
                        if (celestials[i].name == "Moon")
                        {
                            _weatherMakerMoon = celestials[i].gameObject;
                            break;
                        }
                    }
                }
            }

            _moonRenderer = (_weatherMakerMoon == null)
                ? null
                : _weatherMakerMoon.GetComponent<Renderer>();
        }

        [ContextMenu("1페이즈 미리보기")]
        private void PreviewShrine() { ShowPhase(BossArenaPhase.Shrine); }

        [ContextMenu("2페이즈 미리보기")]
        private void PreviewUnderworld() { ShowPhase(BossArenaPhase.Underworld); }
    }

    public static class BossArenaPhaseRules
    {
        public static BossArenaPhase ResolvePhase(in BossPhaseSnapshot snapshot)
        {
            return (snapshot.PhaseIndex >= 2)
                ? BossArenaPhase.Underworld
                : BossArenaPhase.Shrine;
        }

        public static float ResolveTransitionProgress(in BossPhaseSnapshot snapshot,
            double gameplayTime, float duration)
        {
            if ((snapshot.PhaseIndex < 2) || (duration <= 0f))
            {
                return (snapshot.PhaseIndex < 2) ? 0f : 1f;
            }

            if (!IsFinite(gameplayTime) ||
                !IsFinite(snapshot.PhaseChangedGameplayTime))
            {
                return 1f;
            }

            return Mathf.Clamp01((float) ((gameplayTime -
                snapshot.PhaseChangedGameplayTime) / duration));
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }
    }
}
