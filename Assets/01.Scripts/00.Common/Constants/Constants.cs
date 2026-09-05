namespace Geumeum.Common
{
    /// <summary> 코드 계약값 모음. 밸런스 수치는 GameDB에 둔다 </summary>
    public static partial class Constants
    {
        /// <summary> 씬 이름 </summary>
        public static class Scene
        {
            public const string MAIN       = "Main";
            public const string DEV_COMBAT = "Dev_Combat";
            public const string DEV_AI     = "Dev_AI";
            public const string DEV_SYSTEM = "Dev_System";
            public const string DEV_ART    = "Dev_Art";
        }

        /// <summary> 레이어 이름 </summary>
        /// <remarks>
        /// ⚠️ 2026-09-05 실측 — <b>이름과 실제 배치가 어긋나 있다.</b> Main 씬 기준
        /// 지형은 GROUND(8)가 아니라 DEFAULT(0)에 있고 GROUND에는 콜라이더가 0개다.
        /// 바위 393개는 전부 WeatherMaker/PW의 PW_Object_Small/Medium/Large(15·16·17)에 있으며,
        /// 그 이름의 Small/Medium/Large는 물리 크기가 아니라 <b>거리 분류</b>다
        /// (Small 평균 11m, Large 평균 1.7m). 장애물 마스크를 만들 때 이름으로 짐작하지 말 것
        /// → 카메라와 락온 설계 7.1
        /// </remarks>
        public static class Layer
        {
            // TODO(민성): 물리 충돌 매트릭스는 아직 전부 켜져 있다, 히트박스 확정 후 정리할 것
            public const string DEFAULT      = "Default";
            public const string GROUND       = "Ground";
            public const string PLAYER       = "Player";
            public const string ENEMY        = "Enemy";
            public const string BOSS         = "Boss";
            public const string PROJECTILE   = "Projectile";
            public const string INTERACTABLE = "Interactable";
        }

        /// <summary> 태그 이름 </summary>
        public static class Tag
        {
            public const string PLAYER         = "Player"; // Unity 내장 태그
            public const string ENEMY          = "Enemy";
            public const string BOSS           = "Boss";
            public const string BARRIER_CENTER = "BarrierCenter";
            public const string SPAWN_CLUSTER  = "SpawnCluster";
        }

        /// <summary> 씬 고정 루트 오브젝트 이름 </summary>
        public static class SceneRoot
        {
            public const string NETWORK  = "@Network";
            public const string MANAGERS = "@Managers";
            public const string STAGE    = "@Stage";
            public const string AI       = "@AI";
            public const string GAMEPLAY = "@Gameplay";
            public const string UI       = "@UI";
            public const string LOCAL    = "@Local";
        }

        /// <summary> Resources 아래 로드 경로 </summary>
        public static class ResourcePath
        {
            public const string GAME_DB = "GameDB";
        }
    }
}
