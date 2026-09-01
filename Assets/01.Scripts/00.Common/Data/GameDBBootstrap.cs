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

        private void Awake()
        {
            GameDatabaseSO database = Resources.Load<GameDatabaseSO>(Constants.ResourcePath.GAME_DB);

            if (database == null)
            {
                Debug.LogError($"[GameDBBootstrap] Resources/{Constants.ResourcePath.GAME_DB}.asset을 찾지 못했다, 게임을 진행할 수 없다", this);
                enabled = false;
                return;
            }

            database.CollectMissingReferences(_missingReferences);
            if (_missingReferences.Count > 0)
            {
                Debug.LogError($"[GameDBBootstrap] GameDB의 참조가 비어 있다: {string.Join(", ", _missingReferences)}", database);
                enabled = false;
                return;
            }

            GameDB.Initialize(database);
        }
    }
}
