// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary>
    /// 루트 DB를 한 번 초기화하고 누락 시 즉시 실패시킨다.
    /// GameDB를 초기화하는 것은 이 컴포넌트 하나뿐이다
    /// </summary>
    [DefaultExecutionOrder(EXECUTION_ORDER)]
    public class GameDBBootstrap : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 다른 컴포넌트보다 먼저 Awake를 돌리기 위한 보조 장치일 뿐이다.
        // 소비 컴포넌트는 여전히 Awake가 아니라 Start, OnStartServer 이후에 GameDB를 읽어야 한다
        private const int EXECUTION_ORDER = -10000;

        private static readonly List<string> _missingReferences = new List<string>();

        /****************************************
        *              Unity Event
        ****************************************/

        // @Managers에는 NetworkIdentity가 있어 Mirror가 서버를 열 때까지 오브젝트를 비활성으로 둔다.
        // 그 위의 Awake는 StartHost보다 늦게 돌아 부트 게이트가 빈 GameDB를 보게 된다.
        // GameDB는 GameObject가 필요 없는 정적 데이터이므로 씬 로드 전에 먼저 채운다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeBeforeSceneLoad()
        {
            TryInitialize(null);
        }

        private void Awake()
        {
            TryInitialize(this);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 씬 로드 전 호출과 Awake가 같은 경로를 쓴다. 이미 채워져 있으면 아무것도 하지 않는다
        private static void TryInitialize(Object context)
        {
            if (GameDB.IsInitialized)
            {
                return;
            }

            GameDatabaseSO database = Resources.Load<GameDatabaseSO>(Constants.ResourcePath.GAME_DB);

            if (database == null)
            {
                Debug.LogError($"[GameDBBootstrap] Resources/{Constants.ResourcePath.GAME_DB}.asset을 찾지 못했다, 게임을 진행할 수 없다", context);
                return;
            }

            database.CollectMissingReferences(_missingReferences);
            if (_missingReferences.Count > 0)
            {
                Debug.LogError($"[GameDBBootstrap] GameDB의 참조가 비어 있다: {string.Join(", ", _missingReferences)}", database);
                return;
            }

            GameDB.Initialize(database);

            // 효과음의 첫 재생이 디코딩 때문에 늦게 나오는 것을 막는다. 클립이 없으면 아무 일도 하지 않는다
            SfxPlayer.Preload();
        }
    }
}
