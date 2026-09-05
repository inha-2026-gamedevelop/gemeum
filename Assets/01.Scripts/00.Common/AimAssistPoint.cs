// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// IAimAssistTarget의 기본 구현. 붙인 Transform 기준 로컬 오프셋 하나로 조준점을 만든다.
    ///
    /// 부위마다 하나씩 붙인다. 보스 몸통과 꼬리 세 덩이가 각각 별개의 락온 대상이므로
    /// 콜라이더가 있는 오브젝트마다 자기 조준점을 들고 있어야 한다 -
    /// 하나만 붙이면 꼬리를 잠가도 카메라와 화살이 몸통을 본다.
    ///
    /// 이것이 없는 대상은 락온이 콜라이더의 bounds.center로 폴백하므로 동작은 한다.
    /// 다만 캡슐 중심이 배 높이라 화살이 배로 가고, 부위 구분도 생기지 않는다
    /// </summary>
    // 조준점은 표현이 아니라 판정에 쓰인다(화살 유도가 서버 판정이다).
    // 그래서 서버와 클라이언트가 같은 값을 내야 하고, 여기에 화면 전용 값을 섞으면 안 된다.
    // 로컬 오프셋 하나면 양쪽이 같은 Transform에서 같은 좌표를 만든다
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

        /// <summary>
        /// 겨눌 월드 좌표. 스케일이 걸린 모델에서도 눈으로 맞춘 높이가 유지되도록
        /// TransformPoint로 변환한다 - 회전과 스케일을 함께 반영한다
        /// </summary>
        public Vector3 AimPoint => transform.TransformPoint(_localOffset);

        /****************************************
        *              Unity Event
        ****************************************/

#if UNITY_EDITOR

        // 값을 눈으로 맞춰야 하는 종류라 씬 뷰에서 보이게 한다. 빌드에는 들어가지 않는다
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(AimPoint, GIZMO_RADIUS);
        }

#endif
    }
}
