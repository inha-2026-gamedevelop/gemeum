namespace Geumeum.Common
{
    public static partial class Constants
    {
        /// <summary> 전투 계약값. 판정 창, 피해량 같은 밸런스 수치는 CombatConfigSO에 있다 </summary>
        public static class Combat
        {
            public const int WEAPON_KIND_COUNT = 3; // 무기 배분, 옵션 풀 배열 크기와 연동
            public const int ENEMY_KIND_COUNT  = 3; // 구성 비율 배열 크기와 연동
            public const int ITEM_GRADE_COUNT  = 4; // 드랍 가중치 배열 크기와 연동
            public const int FIELD_LEVEL_COUNT = 4; // 0은 개활, 3은 보스 보상

            public const int START_LEVEL = 1;  // 런 시작 레벨, 초기 최대 HP를 레벨 곡선에서 찾는 키
            public const int MAX_LEVEL   = 10; // 레벨 곡선 길이와 특성 포인트 총합이 여기에 묶인다

            public const int   HIT_QUERY_BUFFER_SIZE   = 16;   // NonAlloc 물리 쿼리 재사용 버퍼 크기
            public const int   MELEE_COMBO_STAGE_COUNT = 3;    // 근접 콤보 단계 수(애니메이터의 Attack1~3과 짝을 이룬다)
            public const float DAMAGE_EPSILON        = 0.001f; // 부동소수 오차로 HP가 계속 갱신되는 것을 막는다
            public const float HEALTH_EPSILON        = 0.001f; // 이 이하면 사망으로 취급
        }
    }
}
