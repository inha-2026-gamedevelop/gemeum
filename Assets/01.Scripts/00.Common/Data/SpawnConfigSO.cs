// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary>
    /// 구간별 동시 존재 수, 구성 비율, 리스폰 주기.
    /// 스폰이 없는 구간은 항목 자체를 두지 않는다, 결계 데이터와 달리 전 구간을 채우지 않아도 정상이다
    /// </summary>
    [CreateAssetMenu(fileName = "SpawnConfigSO", menuName = "Geumeum/SpawnConfigSO")]
    public class SpawnConfigSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct PhaseEntry
        {
            public RunPhase Phase;
            public int      ConcurrentMin;
            public int      ConcurrentMax;
            public float    RespawnInterval; // 0이면 리스폰 없음(초)
            public int      RatioHeokkaebi;
            public int      RatioChanggwi;
            public int      RatioAekgwi;

            // 경험치와 노잣돈 총량의 기준값, 고치면 레벨 곡선과 경제를 다시 계산할 것
            public int      TargetKillMin;
            public int      TargetKillMax;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("거점")]
        // 균등 분포는 이동 내내 싸우게 되어 피로하다, 거점형이어야 칠지 넘어갈지 판단이 생긴다
        [SerializeField] private int _clusterSizeMin = 3;
        [SerializeField] private int _clusterSizeMax = 5;

        [Header("구간")]
        [SerializeField] private PhaseEntry[] _phases;

        /****************************************
        *              Properties
        ****************************************/

        public int ClusterSizeMin => _clusterSizeMin;
        public int ClusterSizeMax => _clusterSizeMax;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 구간별 스폰 설정을 찾는다.
        /// false는 데이터 오류가 아니라 그 구간에 스폰이 없다는 뜻이다, Shrink3와 Boss가 여기 해당한다
        /// </summary>
        public bool TryGetPhase(RunPhase phase, out PhaseEntry entry)
        {
            for (int i = 0; i < _phases.Length; ++i)
            {
                if (_phases[i].Phase == phase)
                {
                    entry = _phases[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }
    }
}
