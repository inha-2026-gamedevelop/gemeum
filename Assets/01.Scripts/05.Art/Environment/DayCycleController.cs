// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// Third party
using DigitalRuby.WeatherMaker;

// Project
using Geumeum.Common;
using Geumeum.Common.Environment;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Art
{
    public enum DayCycleSceneMode
    {
        Auto  = 0,
        Field = 1,
        Boss  = 2,
    }

    /// <summary>권위 런 게임 시간을 WeatherMaker의 하루 시간으로 옮기는 유일한 writer.</summary>
    public class DayCycleController : MonoBehaviour, IRunSceneBinding
    {
        [SerializeField] private DayCycleSceneMode _mode = DayCycleSceneMode.Auto;
        [SerializeField] private WeatherMakerDayNightCycleManagerScript _dayNightCycle;
        [SerializeField] private WeatherMakerFullScreenFogProfileScript _fieldFogProfile;
        [SerializeField] private WeatherMakerSkyProfileScript _fieldSkyProfile;

        private static WeatherMakerFullScreenFogProfileScript G_CapturedFieldFogProfile;
        private static WeatherMakerSkyProfileScript G_CapturedFieldSkyProfile;

        private DayCycleSceneMode _boundMode;
        private WeatherMakerFullScreenFogScript _fog;
        private WeatherMakerSkySphereScript _sky;
        private Renderer _moonRenderer;
        private float _previousSpeed;
        private float _previousNightSpeed;
        private bool _hasCapturedSpeed;
        private bool _isBound;
        private bool _hasAppliedTime;
        private bool _hasAppliedFieldProfiles;

        public float LastAppliedGameHour { get; private set; }

        private void Awake()
        {
            ResolveDayNightCycle();
        }

        private void OnEnable()
        {
            _hasAppliedFieldProfiles = false;
            _isBound = true;
            _boundMode = ResolveActiveMode();
            RunSceneDirector.Register(this);
            ClaimClock();
            ApplyTime();
        }

        private void Start()
        {
            // 모든 scene object의 Awake가 끝난 뒤 한 번 더 찾는다. 이후에는 매 frame 탐색하지 않는다.
            ResolveDayNightCycle();
            ClaimClock();
            ApplyTime();
        }

        private void Update()
        {
            ApplyTime();
        }

        private void OnDisable()
        {
            RunSceneDirector.Unregister(this);
            _isBound = false;
            RestoreClockSpeed();
        }

        public void Bind(in SceneTransitionSnapshot transition)
        {
            _boundMode = ResolveMode(transition.ToScene);
            _isBound = true;
            _hasAppliedFieldProfiles = false;
            ResolveDayNightCycle();
            ClaimClock();
            ApplyTime();
        }

        public void Unbind()
        {
            _isBound = false;
        }

        private void ApplyTime()
        {
            if ((!_isBound) || (!GameDB.IsInitialized))
            {
                return;
            }

            if (_dayNightCycle == null)
            {
                return;
            }

            IRunTimeSource runTime = RunTimeLocator.Current;
            if (runTime.IsPaused && _hasAppliedTime)
            {
                return;
            }

            DayCycleSO config = GameDB.DayCycle;
            if (config == null)
            {
                return;
            }

            DayCycleSceneMode mode = (_mode == DayCycleSceneMode.Auto)
                ? _boundMode
                : _mode;
            if ((mode == DayCycleSceneMode.Field) && !_hasAppliedFieldProfiles)
            {
                ApplyFieldProfiles();
            }

            float gameHour = DayCycleRules.ResolveGameHour(
                mode, runTime.FieldElapsedSeconds, config);

            ClaimClock();
            _dayNightCycle.TimeOfDay = gameHour * 3600f;
            LastAppliedGameHour = gameHour;
            _hasAppliedTime = true;
        }

        private void ResolveDayNightCycle()
        {
            WeatherMakerSingleInstanceGuard guard =
                WeatherMakerSingleInstanceGuard.Current;
            if (guard != null)
            {
                _dayNightCycle = guard.GetComponentInChildren<WeatherMakerDayNightCycleManagerScript>(true);
                _fog = guard.GetComponentInChildren<WeatherMakerFullScreenFogScript>(true);
                _sky = guard.GetComponentInChildren<WeatherMakerSkySphereScript>(true);
                ResolveMoonRenderer(guard.transform);
                return;
            }

            // Active-only fallback cannot capture a discarded inactive duplicate root.
            _dayNightCycle = FindAnyObjectByType<WeatherMakerDayNightCycleManagerScript>();
            _fog = FindAnyObjectByType<WeatherMakerFullScreenFogScript>();
            _sky = FindAnyObjectByType<WeatherMakerSkySphereScript>();
        }

        private void ApplyFieldProfiles()
        {
            ResolveDayNightCycle();

            if ((_fieldFogProfile == null) && (G_CapturedFieldFogProfile == null) &&
                (_fog != null) && (_fog.FogProfile != null))
            {
                G_CapturedFieldFogProfile = _fog.FogProfile;
            }

            if ((_fieldSkyProfile == null) && (G_CapturedFieldSkyProfile == null) &&
                (_sky != null) && (_sky.SkySphereProfile != null))
            {
                G_CapturedFieldSkyProfile = _sky.SkySphereProfile;
            }

            WeatherMakerFullScreenFogProfileScript fogProfile =
                (_fieldFogProfile != null) ? _fieldFogProfile : G_CapturedFieldFogProfile;
            WeatherMakerSkyProfileScript skyProfile =
                (_fieldSkyProfile != null) ? _fieldSkyProfile : G_CapturedFieldSkyProfile;

            if ((_fog != null) && (fogProfile != null))
            {
                _fog.FogProfile = fogProfile;
            }

            if ((_sky != null) && (skyProfile != null))
            {
                _sky.SkySphereProfile = skyProfile;
            }

            if (_moonRenderer != null)
            {
                _moonRenderer.enabled = true;
            }

            _hasAppliedFieldProfiles = true;
        }

        private void ResolveMoonRenderer(Transform weatherRoot)
        {
            _moonRenderer = null;
            if (weatherRoot == null)
            {
                return;
            }

            WeatherMakerCelestialObjectScript[] celestials =
                weatherRoot.GetComponentsInChildren<WeatherMakerCelestialObjectScript>(true);
            for (int i = 0; i < celestials.Length; ++i)
            {
                if (celestials[i].name == "Moon")
                {
                    _moonRenderer = celestials[i].GetComponent<Renderer>();
                    return;
                }
            }
        }

        private void ClaimClock()
        {
            if (_dayNightCycle == null)
            {
                return;
            }

            if (!_hasCapturedSpeed)
            {
                _previousSpeed = _dayNightCycle.Speed;
                _previousNightSpeed = _dayNightCycle.NightSpeed;
                _hasCapturedSpeed = true;
            }

            // WeatherMaker의 자체 누적을 끄고 이 컴포넌트만 TimeOfDay를 쓴다.
            _dayNightCycle.Speed = 0f;
            _dayNightCycle.NightSpeed = 0f;
        }

        private void RestoreClockSpeed()
        {
            if ((!_hasCapturedSpeed) || (_dayNightCycle == null))
            {
                return;
            }

            _dayNightCycle.Speed = _previousSpeed;
            _dayNightCycle.NightSpeed = _previousNightSpeed;
            _hasCapturedSpeed = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            G_CapturedFieldFogProfile = null;
            G_CapturedFieldSkyProfile = null;
        }

        private DayCycleSceneMode ResolveActiveMode()
        {
            if (_mode != DayCycleSceneMode.Auto)
            {
                return _mode;
            }

            if (RunSceneDirector.LocalInstance != null)
            {
                DayCycleSceneMode directorMode = ResolveMode(
                    RunSceneDirector.LocalInstance.CurrentScene);
                if (directorMode != DayCycleSceneMode.Auto)
                {
                    return directorMode;
                }
            }

            return ResolveMode(RunSceneCatalog.FromSceneName(
                SceneManager.GetActiveScene().name));
        }

        private static DayCycleSceneMode ResolveMode(RunSceneId scene)
        {
            if (scene == RunSceneId.Boss)
            {
                return DayCycleSceneMode.Boss;
            }

            if (scene == RunSceneId.Field)
            {
                return DayCycleSceneMode.Field;
            }

            return DayCycleSceneMode.Auto;
        }
    }

    public static class DayCycleRules
    {
        private const float BOSS_GAME_HOUR      = 0f;
        private const float FIELD_END_GAME_HOUR = 20f;

        public static float ResolveGameHour(DayCycleSceneMode mode,
            double fieldElapsedSeconds, DayCycleSO config)
        {
            if (mode == DayCycleSceneMode.Boss)
            {
                return BOSS_GAME_HOUR;
            }

            if (config == null)
            {
                return 0f;
            }

            float startHour = Mathf.Clamp(config.StartGameHour, 0f, FIELD_END_GAME_HOUR);
            float secondsPerHour = config.RealSecondsPerGameHour;
            if ((secondsPerHour <= 0f) || double.IsNaN(fieldElapsedSeconds) ||
                double.IsInfinity(fieldElapsedSeconds))
            {
                return startHour;
            }

            float elapsed = (float) System.Math.Max(0d, fieldElapsedSeconds);
            return Mathf.Clamp(startHour + (elapsed / secondsPerHour),
                startHour, FIELD_END_GAME_HOUR);
        }
    }
}
