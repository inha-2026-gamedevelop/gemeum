// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary> 화살 발사각 보정 · 유도 수치 </summary>
    [CreateAssetMenu(fileName = "ArrowConfigSO", menuName = "Geumeum/ArrowConfigSO")]
    public class ArrowConfigSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("비행")]
        // TODO(민성): 기본 비행 속도는 미정, 플레이 테스트로 확정할 것
        [SerializeField] private float _arrowSpeed    = 30f; // m/s
        [SerializeField] private float _arrowLifetime = 3f;  // 미명중 시 소멸까지(초)

        [Header("락온")]
        [SerializeField] private float _aimAssistAngle           = 12f;  // 이 각도 이내면 대상 방향으로 보정(도)
        [SerializeField] private float _guidanceDegreesPerSecond = 30f;  // 비행 중 최대 회전 속도(도/초)
        [SerializeField] private float _guidanceDuration         = 0.4f; // 지나면 직진으로 전환(초)

        [Header("비락온")]
        [SerializeField] private float _unlockedHitRadius = 0.4f; // 발사각 보정 없이 명중을 보조하는 구형 판정 반경(m)

        [Header("관통")]
        [SerializeField] private int _basePierceCount = 1;

        /****************************************
        *              Properties
        ****************************************/

        public float ArrowSpeed    => _arrowSpeed;
        public float ArrowLifetime => _arrowLifetime;

        public float AimAssistAngle           => _aimAssistAngle;
        public float GuidanceDegreesPerSecond => _guidanceDegreesPerSecond;
        public float GuidanceDuration         => _guidanceDuration;

        public float UnlockedHitRadius => _unlockedHitRadius;

        public int BasePierceCount => _basePierceCount;
    }
}
