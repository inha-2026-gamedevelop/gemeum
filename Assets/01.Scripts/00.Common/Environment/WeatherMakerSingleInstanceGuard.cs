// Unity
using UnityEngine;

namespace Geumeum.Common.Environment
{
    // WeatherMaker 자신은 중복 구동체를 감지해도 에러 로그만 남기고 정리하지 않는다
    // (WeatherMakerScript.EnsureInstance는 먼저 등록된 쪽을 유지한다). 씬마다 프리팹을 두는
    // 구조라 전환할 때마다 구동체가 쌓이므로 여기서 하나만 남긴다
    /// <summary> WeatherMaker 구동체를 세션에 하나만 남긴다. 씬이 바뀌어도 파괴되지 않는다 </summary>
    [DefaultExecutionOrder(EXECUTION_ORDER)]
    public class WeatherMakerSingleInstanceGuard : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 같은 오브젝트의 WeatherMaker 스크립트보다 먼저 깨어나야 중복을 미리 끌 수 있다
        private const int EXECUTION_ORDER = -100;

        [Tooltip("켜면 이미 살아 있는 구동체를 버리고 이쪽으로 갈아 끼운다. 끄면 이쪽을 버린다")]
        [SerializeField] private bool _replaceExisting = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 살아 있는 구동체. 없으면 null </summary>
        public static WeatherMakerSingleInstanceGuard Current { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((Current != null) && (Current != this))
            {
                if (!_replaceExisting)
                {
                    Discard(gameObject);
                    return;
                }

                Discard(Current.gameObject);
            }

            Current = this;

            // DontDestroyOnLoad는 루트만 받는다. WeatherMaker도 부모가 있으면 에러를 낸다
            if (transform.parent != null)
            {
                transform.SetParent(null, true);
            }

            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Current != this)
            {
                return;
            }

            Current = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 정적 상태를 초기화한다 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            Current = null;
        }

        /// <summary> 구동체를 버린다. Destroy는 프레임 끝까지 지연되므로 먼저 꺼서 OnEnable을 막는다 </summary>
        private static void Discard(GameObject target)
        {
            target.SetActive(false);
            Destroy(target);
        }
    }
}
