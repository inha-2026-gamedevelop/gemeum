// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary>
    /// 루트 DB. 모든 *SO 참조를 모은다.
    /// Resources/GameDB.asset 하나만 존재하며 GameDBBootstrap이 로드한다
    /// </summary>
    [CreateAssetMenu(fileName = "GameDB", menuName = "Geumeum/GameDatabaseSO")]
    public class GameDatabaseSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("네트워크 · 전투")]
        [SerializeField] private CombatConfigSO _combat;
        [SerializeField] private ArrowConfigSO  _arrow;

        [Header("AI · 보스")]
        [SerializeField] private EnemySO        _enemy;
        [SerializeField] private BarrierPhaseSO _barrier;
        [SerializeField] private SpawnConfigSO  _spawn;
        [SerializeField] private BossPatternSO  _boss;

        [Header("게임플레이 시스템")]
        [SerializeField] private EquipmentSO  _equipment;
        [SerializeField] private DropTableSO  _dropTable;
        [SerializeField] private ShopSO       _shop;
        [SerializeField] private LevelCurveSO _levelCurve;
        [SerializeField] private TalentTreeSO _talent;
        [SerializeField] private ScoreRuleSO  _score;

        [Header("테크니컬 아트 (로컬 전용)")]
        [SerializeField] private DayCycleSO _dayCycle;

        /****************************************
        *              Properties
        ****************************************/

        public CombatConfigSO Combat => _combat;
        public ArrowConfigSO  Arrow  => _arrow;

        public EnemySO        Enemy   => _enemy;
        public BarrierPhaseSO Barrier => _barrier;
        public SpawnConfigSO  Spawn   => _spawn;
        public BossPatternSO  Boss    => _boss;

        public EquipmentSO  Equipment  => _equipment;
        public DropTableSO  DropTable  => _dropTable;
        public ShopSO       Shop       => _shop;
        public LevelCurveSO LevelCurve => _levelCurve;
        public TalentTreeSO Talent     => _talent;
        public ScoreRuleSO  Score      => _score;

        public DayCycleSO DayCycle => _dayCycle;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 비어 있는 참조 이름을 모은다. 누락을 조용히 기본값으로 넘기지 않고 초기화 오류로 중단시키기 위한 검사
        /// </summary>
        public void CollectMissingReferences(List<string> missing)
        {
            missing.Clear();

            if (_combat == null)     { missing.Add(nameof(CombatConfigSO)); }
            if (_arrow == null)      { missing.Add(nameof(ArrowConfigSO)); }
            if (_enemy == null)      { missing.Add(nameof(EnemySO)); }
            if (_barrier == null)    { missing.Add(nameof(BarrierPhaseSO)); }
            if (_spawn == null)      { missing.Add(nameof(SpawnConfigSO)); }
            if (_boss == null)       { missing.Add(nameof(BossPatternSO)); }
            if (_equipment == null)  { missing.Add(nameof(EquipmentSO)); }
            if (_dropTable == null)  { missing.Add(nameof(DropTableSO)); }
            if (_shop == null)       { missing.Add(nameof(ShopSO)); }
            if (_levelCurve == null) { missing.Add(nameof(LevelCurveSO)); }
            if (_talent == null)     { missing.Add(nameof(TalentTreeSO)); }
            if (_score == null)      { missing.Add(nameof(ScoreRuleSO)); }
            if (_dayCycle == null)   { missing.Add(nameof(DayCycleSO)); }
        }
    }
}
