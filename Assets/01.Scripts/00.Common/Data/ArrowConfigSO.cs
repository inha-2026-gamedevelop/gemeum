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
        // 판정 대상 마스크(_targetMask)와 분리한다 - 벽은 맞히는 대상이 아니라 멈추는 이유다.
        //
        // 2026-09-05까지 0이라 장애물 판정이 통째로 꺼져 있었다. 코드는 완성돼 있었고
        // ArrowProjectile.TryFindWorldBlock이 "마스크가 0이면 무시"로 조기 반환하고 있었다.
        // Main 씬 실측 기준 지형은 Default(0), 바위 393개는 PW_Object_* 세 층에 있다
        // (Ground(8)은 콜라이더 0개). 플레이어는 넣지 않는다 - 코옵에서 팀원이 화살을 막으면 괴롭다
        [SerializeField] private LayerMask _worldMask = (1 << 0) | (1 << 8) | (1 << 15) | (1 << 16) | (1 << 17);

        [Header("명중 보조")]
        [SerializeField] private float _unlockedHitRadius = 0.4f; // 발사각 보정 없이 명중을 보조하는 구형 판정 반경(m)

        [Header("락온 보정과 유도")]
        // 2026-09-05 정정 - 이 항목은 2026-09-03에 "폐기, 미사용"으로 적혀 있었으나 같은 날 저녁
        // 락온이 부활하면서 되살아났고, 2026-09-06에 실제로 소비하는 코드가 붙었다.
        // 폐기 사유였던 "정밀 사격과 배타적"이 정밀 사격의 층이 바뀌면서 사라졌다
        // -> 카메라와 락온 12장, 화살 조준 보정 5.4, 5.5
        //
        // 12도가 탐색 반각 35도와 다른 이유는 탐색됐다고 발사 보정까지 허용되는 것이 아니어서다.
        // 유도는 락온이 걸려 있을 때만 붙고, 0.4초 동안 총 12도까지만 휜다
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

        /// <summary> 발사 순간 조준점 쪽으로 당길 수 있는 최대 각도(도). PlayerAttack이 release 틱에 본다 </summary>
        public float AimAssistAngle           => _aimAssistAngle;
        public float GuidanceDegreesPerSecond => _guidanceDegreesPerSecond;
        public float GuidanceDuration         => _guidanceDuration;

        public int BasePierceCount => _basePierceCount;
    }
}
