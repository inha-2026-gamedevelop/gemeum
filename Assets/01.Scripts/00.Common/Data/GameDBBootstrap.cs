// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary> 루트 DB를 한 번 초기화하고 누락 시 즉시 실패시킨다 </summary>
    [DefaultExecutionOrder(EXECUTION_ORDER)]
    public class GameDBBootstrap : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int EXECUTION_ORDER = -10000;

        private static readonly List<string> _missingReferences = new List<string>();

        /****************************************
        *              Unity Event
        ****************************************/

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

            SfxPlayer.Preload();
        }
    }
}
