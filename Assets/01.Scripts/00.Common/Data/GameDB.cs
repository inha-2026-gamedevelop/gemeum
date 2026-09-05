// Unity
using UnityEngine;

namespace Geumeum.Data
{
    /// <summary>
    /// 밸런스 데이터 정적 접근점.
    /// 개별 컴포넌트가 SO를 SerializeField로 물지 않고 전부 여기로 읽어야 원본이 하나로 유지된다
    /// </summary>
    public static class GameDB
    {
        /****************************************
        *                Fields
        ****************************************/

        private static GameDatabaseSO _database;

        /****************************************
        *              Properties
        ****************************************/

        public static bool IsInitialized => (_database != null);

        public static CombatConfigSO Combat => Require().Combat;
        public static ArrowConfigSO  Arrow  => Require().Arrow;

        public static EnemySO        Enemy   => Require().Enemy;
        public static BarrierPhaseSO Barrier => Require().Barrier;
        public static SpawnConfigSO  Spawn   => Require().Spawn;
        public static BossPatternSO  Boss    => Require().Boss;

        public static EquipmentSO  Equipment  => Require().Equipment;
        public static DropTableSO  DropTable  => Require().DropTable;
        public static ShopSO       Shop       => Require().Shop;
        public static LevelCurveSO LevelCurve => Require().LevelCurve;
        public static TalentTreeSO Talent     => Require().Talent;
        public static ScoreRuleSO  Score      => Require().Score;

        public static DayCycleSO     DayCycle => Require().DayCycle;
        public static SoundCatalogSO Sound    => Require().Sound;
        public static CameraConfigSO Camera   => Require().Camera;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 루트 DB를 등록한다. GameDBBootstrap만 호출한다 </summary>
        public static void Initialize(GameDatabaseSO database)
        {
            _database = database;
        }

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _database = null;
        }

        // Awake에서 읽는 실수를 조용한 null 대신 예외로 즉시 드러낸다
        private static GameDatabaseSO Require()
        {
            if (_database == null)
            {
                throw new System.InvalidOperationException(
                    "[GameDB] 초기화 전에 접근했다. @Managers에 GameDBBootstrap이 있는지, 그리고 Awake가 아니라 Start, OnStartServer 이후에 읽는지 확인할 것");
            }

            return _database;
        }
    }
}
