// Unity
using UnityEngine;

// 서드파티
using DigitalRuby.WeatherMaker;

namespace Geumeum.Art
{
    public enum BossArenaPhase
    {
        Shrine     = 1,
        Underworld = 2,
    }

    /// <summary> 보스 아레나의 페이즈 연출을 갈아 끼운다 </summary>
    public class BossArenaPhaseView : MonoBehaviour
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

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 페이즈 연출을 적용한다, 판정에 관여하지 않으므로 클라이언트에서 그대로 호출한다 </summary>
        public void ShowPhase(BossArenaPhase phase)
        {
            bool isShrine = (phase == BossArenaPhase.Shrine);

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

        [ContextMenu("1페이즈 미리보기")]
        private void PreviewShrine() { ShowPhase(BossArenaPhase.Shrine); }

        [ContextMenu("2페이즈 미리보기")]
        private void PreviewUnderworld() { ShowPhase(BossArenaPhase.Underworld); }
    }
}
