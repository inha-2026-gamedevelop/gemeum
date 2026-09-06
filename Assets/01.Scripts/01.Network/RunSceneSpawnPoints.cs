// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary>
    /// 이 씬에 도착한 참가자를 지정된 자리로 옮긴다.
    ///
    /// 씬을 옮기면 플레이어는 떠나온 씬의 좌표를 그대로 들고 온다.
    /// 아레나 밖이나 지형 아래에 서 있게 되므로 도착 시점에 한 번 자리를 잡아 준다.
    /// 자리가 인원보다 적으면 앞에서부터 돌려 쓴다
    /// </summary>
    public class RunSceneSpawnPoints : MonoBehaviour, IRunSceneBinding
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("도착 지점. 비어 있으면 자식 Transform을 순서대로 쓴다")]
        [SerializeField] private Transform[] _spawnPoints;

        private readonly List<Transform> _resolved = new List<Transform>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            ResolveSpawnPoints();
            RunSceneDirector.Register(this);
        }

        private void OnDisable()
        {
            RunSceneDirector.Unregister(this);
        }

        /****************************************
        *           Interface Members
        ****************************************/

        public void Bind(in SceneTransitionSnapshot transition)
        {
            if (!NetworkServer.active)
            {
                return;
            }

            if (_resolved.Count == 0)
            {
                Debug.LogError("[RunSceneSpawnPoints] 도착 지점이 하나도 없다", this);
                return;
            }

            int index = 0;
            foreach (KeyValuePair<int, NetworkConnectionToClient> pair in NetworkServer.connections)
            {
                NetworkConnectionToClient connection = pair.Value;
                if ((connection == null) || (connection.identity == null))
                {
                    continue;
                }

                IPlayerRunLifecycle lifecycle = connection.identity.GetComponent<IPlayerRunLifecycle>();
                if (lifecycle == null)
                {
                    continue;
                }

                Transform spawn = _resolved[index % _resolved.Count];
                lifecycle.Teleport(spawn.position);
                ++index;
            }
        }

        public void Unbind()
        {
        }

        /****************************************
        *                Methods
        ****************************************/

        private void ResolveSpawnPoints()
        {
            _resolved.Clear();

            if ((_spawnPoints != null) && (_spawnPoints.Length > 0))
            {
                for (int i = 0; i < _spawnPoints.Length; ++i)
                {
                    if (_spawnPoints[i] == null)
                    {
                        continue;
                    }

                    _resolved.Add(_spawnPoints[i]);
                }

                return;
            }

            // 인스펙터를 비워 두면 자식이 곧 목록이다. 마커를 추가할 때 배열을 함께 고치는 것을 잊게 된다
            for (int i = 0; i < transform.childCount; ++i)
            {
                _resolved.Add(transform.GetChild(i));
            }
        }
    }
}
