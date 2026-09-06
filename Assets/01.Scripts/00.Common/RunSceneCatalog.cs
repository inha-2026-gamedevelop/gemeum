namespace Geumeum.Common
{
    /// <summary>
    /// 런 흐름의 씬 역할과 실제 씬 이름을 잇는다.
    ///
    /// Mirror에 넘길 때는 문자열이 필요한데, 호출부마다 문자열을 적으면 씬 이름이 바뀌는 순간
    /// 컴파일 에러 없이 런타임에 실패한다. 변환을 한 곳에 모아 그 실패를 여기서만 낸다.
    /// Mirror에 의존하지 않는 순수 C#이라 데디케이티드로 옮길 때 그대로 쓴다
    /// </summary>
    public static class RunSceneCatalog
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 역할에 대응하는 씬 이름. 대응이 없으면 false이고 name은 빈 문자열이다 </summary>
        public static bool TryGetSceneName(RunSceneId sceneId, out string name)
        {
            switch (sceneId)
            {
                case RunSceneId.Boot:
                    name = Constants.Scene.BOOT;
                    return true;
                case RunSceneId.Title:
                    name = Constants.Scene.TITLE;
                    return true;
                case RunSceneId.Lobby:
                    name = Constants.Scene.LOBBY;
                    return true;
                case RunSceneId.Field:
                    name = Constants.Scene.FIELD;
                    return true;
                case RunSceneId.BossEntrance:
                    name = Constants.Scene.BOSS_ENTRANCE;
                    return true;
                case RunSceneId.Boss:
                    name = Constants.Scene.BOSS_ARENA;
                    return true;
                case RunSceneId.Result:
                    name = Constants.Scene.RESULT;
                    return true;
                default:
                    name = string.Empty;
                    return false;
            }
        }

        /// <summary> 씬 이름에 대응하는 역할. 대응이 없으면 None이다 </summary>
        public static RunSceneId FromSceneName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return RunSceneId.None;
            }

            if (name == Constants.Scene.BOOT)
            {
                return RunSceneId.Boot;
            }

            if (name == Constants.Scene.TITLE)
            {
                return RunSceneId.Title;
            }

            if (name == Constants.Scene.LOBBY)
            {
                return RunSceneId.Lobby;
            }

            if (name == Constants.Scene.FIELD)
            {
                return RunSceneId.Field;
            }

            if (name == Constants.Scene.BOSS_ENTRANCE)
            {
                return RunSceneId.BossEntrance;
            }

            if (name == Constants.Scene.BOSS_ARENA)
            {
                return RunSceneId.Boss;
            }

            if (name == Constants.Scene.RESULT)
            {
                return RunSceneId.Result;
            }

            return RunSceneId.None;
        }

        /// <summary>
        /// 도착한 뒤 참가자 전투 상태를 되부어야 하는 씬인지.
        /// 결과 씬은 정산만 보여주므로 되부을 이유가 없고, 그 판단을 씬마다 흩어 두면 규칙이 갈라진다
        /// </summary>
        public static bool ShouldRestoreParticipants(RunSceneId sceneId)
        {
            return ((sceneId == RunSceneId.Field) ||
                    (sceneId == RunSceneId.BossEntrance) ||
                    (sceneId == RunSceneId.Boss));
        }
    }
}
