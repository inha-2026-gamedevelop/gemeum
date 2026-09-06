// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Network
{
    /// <summary>
    /// 이 오브젝트를 세션 내내 살린다. 씬이 바뀌어도 파괴되지 않는다.
    ///
    /// 런 매니저와 전환 소유자가 씬마다 새로 생기면 런 시계, 참가자 그릇, 전환 세대가
    /// 전환할 때마다 초기화된다. 런당 전환이 다섯 번이라 다섯 번 다 잃는다.
    /// 이름으로 중복을 막는 이유는 같은 이름의 오브젝트가 두 씬에 있으면
    /// 나중 것이 먼저 것의 정적 참조를 덮어쓰기 때문이다
    /// </summary>
    public class SessionPersistence : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly Dictionary<string, SessionPersistence> G_Instances =
            new Dictionary<string, SessionPersistence>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            SessionPersistence existing;
            if ((G_Instances.TryGetValue(gameObject.name, out existing)) && (existing != null) && (existing != this))
            {
                Destroy(gameObject);
                return;
            }

            G_Instances[gameObject.name] = this;

            // DontDestroyOnLoad는 루트에만 적용되고 자식이면 조용히 실패한다
            if (transform.parent != null)
            {
                transform.SetParent(null, true);
            }

            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            SessionPersistence existing;
            if (!G_Instances.TryGetValue(gameObject.name, out existing))
            {
                return;
            }

            if (existing != this)
            {
                return;
            }

            G_Instances.Remove(gameObject.name);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 도메인 리로드를 끄고 재생할 때 이전 판의 목록이 남지 않게 한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            G_Instances.Clear();
        }
    }
}
