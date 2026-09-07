// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Geumeum.Network
{
    /// <summary> 이 오브젝트를 세션 내내 살린다. 씬이 바뀌어도 파괴되지 않는다 </summary>
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

        /// <summary> 인스턴스 목록을 초기화한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            G_Instances.Clear();
        }
    }
}
