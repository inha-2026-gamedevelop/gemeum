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
        [SerializeField] private float _arrowSpeed    = 30f; // m/s
        [SerializeField] private float _arrowLifetime = 3f;  // 미명중 시 소멸까지(초)

        [Header("장애물")]
        // 화살을 막는 레이어. 비어 있으면 벽을 통과해 뒤의 적을 맞힌다
        [SerializeField] private LayerMask _worldMask = (1 << 0) | (1 << 8) | (1 << 15) | (1 << 16) | (1 << 17);

        [Header("명중 보조")]
        [SerializeField] private float _unlockedHitRadius = 0.4f; // 발사각 보정 없이 명중을 보조하는 구형 판정 반경(m)

        [Header("락온 보정과 유도")]
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

        [SerializeField] private int _maxPierceCount = 8;
        public int MaxPierceCount => Mathf.Max(1, _maxPierceCount);
        public int BasePierceCount => _basePierceCount;
    }
}
