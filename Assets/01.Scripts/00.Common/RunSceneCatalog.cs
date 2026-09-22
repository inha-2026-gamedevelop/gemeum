namespace Geumeum.Common
{
    /// <summary>
    /// 런 흐름의 씬 역할과 실제 씬 이름을 잇는다
    ///
    /// Mirror에 넘길 때는 문자열이 필요한데, 호출부마다 문자열을 적으면 씬 이름이 바뀌는 순간
    /// 컴파일 에러 없이 런타임에 실패한다. 변환을 한 곳에 모아 그 실패를 여기서만 낸다
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
        /// 도착한 뒤 참가자 전투 상태를 되부어야 하는 씬인지
        /// 결과 씬은 정산만 보여주므로 되부을 이유가 없고, 그 판단을 씬마다 흩어 두면 규칙이 갈라진다
        /// </summary>
        public static bool ShouldRestoreParticipants(RunSceneId sceneId)
        {
            return ((sceneId == RunSceneId.Field) ||
                    (sceneId == RunSceneId.BossEntrance) ||
                    (sceneId == RunSceneId.Boss));
        }

        /// <summary>
        /// 이 phase가 있어야 하는 씬. 씬을 바꾸지 않는 phase면 false다
        ///
        /// 결계 축소 세 단계는 전부 Field에 머문다. 축소는 씬 교체가 아니라 같은 씬 안의 반경 변화다
        /// Boss phase 자체의 목적지는 Boss_Arena다. 정식 전실 경유는 phase가 아니라
        /// BossEntranceState를 키로 쓰는 TryGetSceneForBossEntrance가 결정한다
        /// </summary>
        public static bool TryGetSceneForPhase(RunPhase phase, out RunSceneId sceneId)
        {
            switch (phase)
            {
                case RunPhase.Lobby:
                    sceneId = RunSceneId.Lobby;
                    return true;
                case RunPhase.Farming:
                case RunPhase.Shrink1:
                case RunPhase.Shrink2:
                case RunPhase.Shrink3:
                    sceneId = RunSceneId.Field;
                    return true;
                case RunPhase.Boss:
                    sceneId = RunSceneId.Boss;
                    return true;
                case RunPhase.Result:
                    sceneId = RunSceneId.Result;
                    return true;
                default:
                    sceneId = RunSceneId.None;
                    return false;
            }
        }

        /// <summary>
        /// 정식 보스 입구의 다음 씬. 전실은 RunPhase가 아니므로 입구 상태와 현재 씬을 함께 본다
        /// </summary>
        public static bool TryGetSceneForBossEntrance(BossEntranceState state, RunSceneId currentScene,
            out RunSceneId sceneId)
        {
            if ((state == BossEntranceState.TransitionRequested) && (currentScene == RunSceneId.Field))
            {
                sceneId = RunSceneId.BossEntrance;
                return true;
            }

            if (((state == BossEntranceState.TransitionRequested) || (state == BossEntranceState.Entering)) &&
                (currentScene == RunSceneId.BossEntrance))
            {
                sceneId = RunSceneId.Boss;
                return true;
            }

            sceneId = RunSceneId.None;
            return false;
        }
    }
}
