// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

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

        private static bool G_SceneLoadedHooked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            RegisterInstance();
        }

        /// <summary> 씬 로드 이벤트 이후 동적으로 만들어진 인스턴스도 영속화한다 </summary>
        private void Start()
        {
            PersistAfterSceneLoad();
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

        /// <summary> 씬에 배치된 비활성 인스턴스까지 찾아 씬 해시 처리 이후 영속화한다 </summary>
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!scene.IsValid())
            {
                return;
            }

            SessionPersistence[] persistences = CollectScenePersistences(scene);
            for (int persistenceIndex = 0; persistenceIndex < persistences.Length; ++persistenceIndex)
            {
                persistences[persistenceIndex].PersistAfterSceneLoad();
            }
        }

        /// <summary> 씬 root를 한 번 순회해 비활성 영속화 컴포넌트까지 수집한다 </summary>
        private static SessionPersistence[] CollectScenePersistences(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            List<SessionPersistence> persistences = new List<SessionPersistence>();
            for (int rootIndex = 0; rootIndex < roots.Length; ++rootIndex)
            {
                persistences.AddRange(roots[rootIndex].GetComponentsInChildren<SessionPersistence>(true));
            }

            return persistences.ToArray();
        }

        /// <summary> 첫 씬 로드 전에 씬 수명 보존 콜백을 한 번 등록한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoaded()
        {
            if (G_SceneLoadedHooked)
            {
                return;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            G_SceneLoadedHooked = true;
        }

        private bool RegisterInstance()
        {
            SessionPersistence existing;
            if ((G_Instances.TryGetValue(gameObject.name, out existing)) && (existing != null) && (existing != this))
            {
                Destroy(gameObject);
                return false;
            }

            G_Instances[gameObject.name] = this;
            return true;
        }

        private void PersistAfterSceneLoad()
        {
            if (!RegisterInstance())
            {
                return;
            }

            if (gameObject.scene.name == "DontDestroyOnLoad")
            {
                return;
            }

            if (transform.parent != null)
            {
                transform.SetParent(null, true);
            }

            DontDestroyOnLoad(gameObject);
        }

        /// <summary> 씬 로드 이벤트 구독과 인스턴스 목록을 초기화한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            if (G_SceneLoadedHooked)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                G_SceneLoadedHooked = false;
            }

            G_Instances.Clear();
        }
    }
}
