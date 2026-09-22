// Unity
using UnityEngine;
using UnityEngine.AI;

namespace Geumeum.AI
{
    /// <summary>
    /// 씬에 배치되는 잡귀 거점 마커. 위치는 고정이고 구성(종류, 수)은 SpawnManager가 매번 랜덤으로 정한다
    /// </summary>
    public class SpawnCluster : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private float _scatterRadius = 3f; // 거점 안에서 개체마다 흩어지는 반경

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 결계 반경 안인지, SpawnManager가 단계 전환마다 갱신한다. 기본값은 안(범위 내)이다 </summary>
        public bool IsInRange { get; set; } = true;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 거점 안의 스폰 지점 하나를 고른다, NavMesh 위 지점을 찾지 못하면 거점 중심을 반환한다 </summary>
        public Vector3 GetRandomPoint()
        {
            Vector2 offset = Random.insideUnitCircle * _scatterRadius;
            Vector3 point  = transform.position + new Vector3(offset.x, 0f, offset.y);

            if (NavMesh.SamplePosition(point, out NavMeshHit hit, _scatterRadius + 1f, NavMesh.AllAreas))
            {
                return hit.position;
            }

            return transform.position;
        }
    }
}
