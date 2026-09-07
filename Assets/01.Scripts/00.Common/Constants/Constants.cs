namespace Geumeum.Common
{
    /// <summary> 코드 계약값 모음. 밸런스 수치는 GameDB에 둔다 </summary>
    public static partial class Constants
    {
        /// <summary> 씬 이름 </summary>
        public static class Scene
        {
            // 런타임 흐름. 빌드 목록 0~6번이고 RunSceneCatalog가 RunSceneId와 잇는다
            public const string BOOT          = "Boot";
            public const string TITLE         = "Title";
            public const string LOBBY         = "Lobby";
            public const string FIELD         = "Field";
            public const string BOSS_ENTRANCE = "BossEntrance";
            public const string BOSS_ARENA    = "Boss_Arena";
            public const string RESULT        = "Result";

            public const string MAIN       = "Main";
            public const string DEV_COMBAT = "Dev_Combat";
            public const string DEV_AI     = "Dev_AI";
            public const string DEV_SYSTEM = "Dev_System";
            public const string DEV_ART    = "Dev_Art";
        }

        /// <summary> 레이어 이름 </summary>
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
