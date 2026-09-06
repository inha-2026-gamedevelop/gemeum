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

        /// <summary> 마지막으로 적용한 페이즈. ShowPhase를 한 번도 부르지 않았으면 씬에 저장된 값과 같다 </summary>
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

        /// <summary>
        /// 이 씬에 도착하면 아레나의 연출을 건다.
        ///
        /// WeatherMaker 구동체는 진입 씬에만 있고 세션 내내 산다. 씬마다 바뀌는 것은
        /// 인스턴스가 아니라 프로파일이라, 도착 시점에 이 아레나의 값을 밀어 넣는다
        /// </summary>
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

        /// <summary> 페이즈 연출을 적용한다, 판정에 관여하지 않으므로 클라이언트에서 그대로 호출한다 </summary>
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

            // 원반만 숨긴다. 광원은 두 페이즈 모두 켜 둔다 - 물리기반 하늘이 이 광원으로
            // 산란을 계산하므로 끄면 하늘이 통째로 검어진다 (실측: 하늘 휘도 49 -> 3)
            if (_moonRenderer != null)
            {
                _moonRenderer.enabled = isShrine;
            }
        }

        /// <summary>
        /// 살아남은 WeatherMaker 구동체를 다시 잡는다.
        ///
        /// WeatherMaker는 세션 싱글턴이라 이 씬이 들고 온 것이 지워지고 앞 씬의 것이 남을 수 있다.
        /// 그때 인스펙터 참조는 파괴된 오브젝트를 가리키므로 안개와 하늘이 통째로 적용되지 않는다.
        /// 초기화 1회가 아니라 전환마다 확인하는 이유는 살아남는 쪽이 씬마다 달라지기 때문이다
        /// </summary>
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

            // 주야 프로파일은 여기서 대입하지 않는다. 구동체에 이미 직렬화돼 있고,
            // 런타임에 다시 넣으면 프로파일이 한 번 더 복제되면서 계산된 시각이 초기화된다
            // (실측: TimeOfDay 0 인데 Category 가 Night 이 아니라 Day 가 됐다)
        }

        [ContextMenu("1페이즈 미리보기")]
        private void PreviewShrine() { ShowPhase(BossArenaPhase.Shrine); }

        [ContextMenu("2페이즈 미리보기")]
        private void PreviewUnderworld() { ShowPhase(BossArenaPhase.Underworld); }
    }
}
