// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> IAimAssistTarget의 기본 구현. 붙인 Transform 기준 로컬 오프셋 하나로 조준점을 만든다 </summary>
    public class AimAssistPoint : MonoBehaviour, IAimAssistTarget
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float GIZMO_RADIUS = 0.12f; // 씬 뷰에서 조준점을 알아볼 최소 크기

        [Tooltip("붙인 오브젝트 기준 로컬 오프셋(m). 잡귀는 2m 캡슐 기준 가슴 높이인 (0, 1.1, 0)이 시작값이다")]
        [SerializeField] private Vector3 _localOffset = new Vector3(0f, 1.1f, 0f);

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 겨눌 월드 좌표 </summary>
        public Vector3 AimPoint => transform.TransformPoint(_localOffset);

        /****************************************
        *              Unity Event
        ****************************************/

#if UNITY_EDITOR

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(AimPoint, GIZMO_RADIUS);
        }

#endif
    }
}
