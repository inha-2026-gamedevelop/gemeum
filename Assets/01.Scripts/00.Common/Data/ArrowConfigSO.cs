// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary> 화살 발사각 보정, 유도 수치 </summary>
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

        [Header("장애물")]
        // 화살을 막는 레이어. 비어 있으면 벽을 통과해 뒤의 적을 맞힌다.
        // 판정 대상 마스크(_targetMask)와 분리한다 - 벽은 맞히는 대상이 아니라 멈추는 이유다
        [SerializeField] private LayerMask _worldMask = 0;

        [Header("명중 보조")]
        [SerializeField] private float _unlockedHitRadius = 0.4f; // 발사각 보정 없이 명중을 보조하는 구형 판정 반경(m)

        [Header("락온 - 2026-09-03 폐기, 미사용")]
        // 락온을 넣지 않기로 확정했다(게임 디자인 2.4). 아래 셋은 어디서도 읽지 않는다.
        // 정밀 사격이 같은 문제를 다르게 풀며, 자동 조준이 있으면 정밀 사격의 대가가 무의미해진다
        [SerializeField] private float _aimAssistAngle           = 12f;
        [SerializeField] private float _guidanceDegreesPerSecond = 30f;
        [SerializeField] private float _guidanceDuration         = 0.4f;

        [Header("관통")]
        [SerializeField] private int _basePierceCount = 1;

        /****************************************
        *              Properties
        ****************************************/

        public float ArrowSpeed    => _arrowSpeed;
        public float ArrowLifetime => _arrowLifetime;

        /// <summary> 화살을 막는 레이어. 0이면 장애물 판정을 하지 않는다 </summary>
        public int WorldMask => _worldMask.value;

        public float UnlockedHitRadius => _unlockedHitRadius;

        // 락온 폐기(2026-09-03)로 아래 셋은 미사용이다. 화살 코드를 다음에 손댈 때 함께 정리한다
        public float AimAssistAngle           => _aimAssistAngle;
        public float GuidanceDegreesPerSecond => _guidanceDegreesPerSecond;
        public float GuidanceDuration         => _guidanceDuration;

        public int BasePierceCount => _basePierceCount;
    }
}
