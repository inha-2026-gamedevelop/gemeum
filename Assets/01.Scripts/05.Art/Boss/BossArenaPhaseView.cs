// Unity
using UnityEngine;

// 서드파티
using DigitalRuby.WeatherMaker;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.Art
{
    public enum BossArenaPhase
    {
        Shrine     = 1,
        Underworld = 2,
    }

    /// <summary> 보스 아레나의 페이즈 연출을 갈아 끼운다 </summary>
    public class BossArenaPhaseView : MonoBehaviour, IRunSceneBinding
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("페이즈 루트")]
        [SerializeField] private GameObject _phase1Root;
        [SerializeField] private GameObject _phase2Root;

        [Header("공용 - 페이즈마다 값만 바뀐다")]
        [SerializeField] private WeatherMakerFullScreenFogScript _fog;
        [SerializeField] private WeatherMakerSkySphereScript     _sky;
        [SerializeField] private Renderer                        _floorRenderer;
        [SerializeField] private GameObject                      _weatherMakerMoon;


        [Header("1페이즈 - 산속 사당")]
        [SerializeField] private WeatherMakerFullScreenFogProfileScript _fogProfileShrine;
        [SerializeField] private WeatherMakerSkyProfileScript           _skyProfileShrine;
        [SerializeField] private Material                               _floorMaterialShrine;

        [Header("2페이즈 - 저승 물길")]
        [SerializeField] private WeatherMakerFullScreenFogProfileScript _fogProfileUnderworld;
        [SerializeField] private WeatherMakerSkyProfileScript           _skyProfileUnderworld;
        [SerializeField] private Material                               _floorMaterialUnderworld;

        private Renderer _moonRenderer;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막으로 적용한 페이즈 </summary>
        public BossArenaPhase CurrentPhase { get; private set; } = BossArenaPhase.Shrine;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_weatherMakerMoon == null)
            {
                return;
            }

            _moonRenderer = _weatherMakerMoon.GetComponent<Renderer>();
        }

        private void OnEnable()
        {
            RunSceneDirector.Register(this);
        }

        private void OnDisable()
        {
            RunSceneDirector.Unregister(this);
        }

        /****************************************
        *           Interface Members
        ****************************************/

        /// <summary> 이 씬에 도착하면 아레나의 연출을 적용한다 </summary>
        public void Bind(in SceneTransitionSnapshot transition)
        {
            ShowPhase(CurrentPhase);
        }

        public void Unbind()
        {
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 페이즈 연출을 적용한다 </summary>
        public void ShowPhase(BossArenaPhase phase)
        {
            bool isShrine = (phase == BossArenaPhase.Shrine);
            CurrentPhase  = phase;

            ResolveWeatherMaker();

            if (_phase1Root != null)
            {
                _phase1Root.SetActive(isShrine);
            }

            if (_phase2Root != null)
            {
                _phase2Root.SetActive(!isShrine);
            }

            if (_fog != null)
            {
                _fog.FogProfile = isShrine ? _fogProfileShrine : _fogProfileUnderworld;
            }

            if (_sky != null)
            {
                _sky.SkySphereProfile = isShrine ? _skyProfileShrine : _skyProfileUnderworld;
            }

            // TODO(민성): 전환 6초 동안 smoothness 를 0.35 에서 0.9 로 보간한다. 지금은 즉시 교체다
            if (_floorRenderer != null)
            {
                var floorMaterial = isShrine ? _floorMaterialShrine : _floorMaterialUnderworld;
                if (floorMaterial != null)
                {
                    _floorRenderer.sharedMaterial = floorMaterial;
                }
            }

            // 원반만 숨기고 광원은 그대로 켜 둔다
            if (_moonRenderer != null)
            {
                _moonRenderer.enabled = isShrine;
            }
        }

        /// <summary> 살아남은 WeatherMaker 구동체를 다시 찾아 연결한다 </summary>
        private void ResolveWeatherMaker()
        {
            if (_fog == null)
            {
                _fog = FindAnyObjectByType<WeatherMakerFullScreenFogScript>(FindObjectsInactive.Include);
            }

            if (_sky == null)
            {
                _sky = FindAnyObjectByType<WeatherMakerSkySphereScript>(FindObjectsInactive.Include);
            }

            if (_weatherMakerMoon == null)
            {
                WeatherMakerCelestialObjectScript[] celestials =
                    FindObjectsByType<WeatherMakerCelestialObjectScript>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < celestials.Length; ++i)
                {
                    if (celestials[i].name != "Moon")
                    {
                        continue;
                    }

                    _weatherMakerMoon = celestials[i].gameObject;
                    _moonRenderer     = _weatherMakerMoon.GetComponent<Renderer>();
                }
            }

            if (_moonRenderer == null)
            {
                if (_weatherMakerMoon != null)
                {
                    _moonRenderer = _weatherMakerMoon.GetComponent<Renderer>();
                }
            }

            // 주야 프로파일은 여기서 대입하지 않는다
        }

        [ContextMenu("1페이즈 미리보기")]
        private void PreviewShrine() { ShowPhase(BossArenaPhase.Shrine); }

        [ContextMenu("2페이즈 미리보기")]
        private void PreviewUnderworld() { ShowPhase(BossArenaPhase.Underworld); }
    }
}
