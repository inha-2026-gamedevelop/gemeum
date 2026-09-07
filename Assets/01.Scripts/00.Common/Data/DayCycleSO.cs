// System
using System;

// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary> 실시간과 게임 내 시각의 매핑, 시간대별 조명 프리셋 </summary>
    [CreateAssetMenu(fileName = "DayCycleSO", menuName = "Geumeum/DayCycleSO")]
    public class DayCycleSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct LightingPreset
        {
            public float   GameHour; // 0~24
            public Color   SunColor;
            public float   SunIntensity;
            public Vector3 SunEulerAngles;
            public Color   FogColor;
            public float   FogDensity;
            public Color   AmbientColor;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("시각 매핑")]
        [SerializeField] private float _startGameHour          = 8f;   // 런 시작 시각
        [SerializeField] private float _realSecondsPerGameHour = 150f; // 런 길이를 바꾸면 같이 조정한다

        // TODO(민성): 조명 프리셋 값은 미정, 아트 파트가 톤 확정 후 채운다
        [Header("조명 프리셋")]
        [SerializeField] private LightingPreset[] _presets;

        /****************************************
        *              Properties
        ****************************************/

        public float StartGameHour          => _startGameHour;
        public float RealSecondsPerGameHour => _realSecondsPerGameHour;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 경과 시간(초)을 게임 내 시각(0~24)으로 환산한다 </summary>
        public float ToGameHour(float elapsedSeconds)
        {
            float hour = _startGameHour + (elapsedSeconds / _realSecondsPerGameHour);
            return Mathf.Repeat(hour, 24f);
        }

        /// <summary> 게임 내 시각을 감싸는 두 프리셋과 보간 비율을 돌려준다, 프리셋이 없으면 false </summary>
        public bool TryGetPresetPair(float gameHour, out LightingPreset from, out LightingPreset to, out float t)
        {
            from = default;
            to   = default;
            t    = 0f;

            if ((_presets == null) || (_presets.Length == 0))
            {
                return false;
            }

            if (_presets.Length == 1)
            {
                from = _presets[0];
                to   = _presets[0];
                return true;
            }

            for (int i = 0; i < _presets.Length - 1; ++i)
            {
                if ((gameHour >= _presets[i].GameHour) && (gameHour <= _presets[i + 1].GameHour))
                {
                    from = _presets[i];
                    to   = _presets[i + 1];

                    float span = to.GameHour - from.GameHour;
                    t = (span <= 0f) ? 0f : ((gameHour - from.GameHour) / span);
                    return true;
                }
            }

            from = _presets[_presets.Length - 1];
            to   = _presets[_presets.Length - 1];
            return true;
        }
    }
}
