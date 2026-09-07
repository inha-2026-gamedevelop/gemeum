// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// DontDestroyOnLoad 싱글턴 베이스.
    /// 상속 클래스는 Awake를 직접 만들지 말고 OnSingletonAwake를 오버라이드한다
    /// </summary>
    public abstract class PersistentSingleton<T> : MonoBehaviour where T : PersistentSingleton<T>
    {
        /****************************************
        *                Fields
        ****************************************/

        private static T    _instance;
        private static bool _isQuitting = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary>
        /// 인스턴스 존재 여부, 조회만으로 탐색 비용이 들지 않는다.
        /// Instance와 같은 종료 조건을 본다, 여기서 _isQuitting을 빼면
        /// HasInstance는 true인데 Instance는 null인 구간이 생겨 호출부가 터진다
        /// </summary>
        public static bool HasInstance => ((!_isQuitting) && (_instance != null));

        /// <summary>
        /// 현재 인스턴스, 없으면 null.
        /// 자동 생성하지 않으므로 생성이 필요하면 EnsureCreated를 호출한다
        /// </summary>
        public static T Instance
        {
            get
            {
                // 종료 중에 접근하면 이미 파괴된 오브젝트가 되살아나 씬에 남는다
                if (_isQuitting)
                {
                    return null;
                }

                // 씬 배치본을 붙잡는 1회 폴백, 결과는 캐싱된다
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<T>(FindObjectsInactive.Exclude);
                }

                return _instance;
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        // virtual이 아니다, 상속 클래스가 Awake를 다시 선언하면 컴파일 경고로 드러난다
        protected void Awake()
        {
            if ((_instance != null) && (_instance != this))
            {
                Debug.LogWarning($"[{typeof(T).Name}] 중복 인스턴스를 파괴한다", this);
                Destroy(gameObject);
                return;
            }

            _instance = (T) this;

            // DontDestroyOnLoad는 루트에만 적용되고 자식이면 조용히 실패한다
            if (transform.parent != null)
            {
                Debug.LogWarning($"[{typeof(T).Name}] 자식 오브젝트여서 루트로 분리한다", this);
                transform.SetParent(null);
            }

            DontDestroyOnLoad(gameObject);
            OnSingletonAwake();
        }

        protected void OnDestroy()
        {
            // 중복이어서 파괴되는 경우에는 살아있는 인스턴스를 건드리면 안 된다
            if (_instance != this)
            {
                return;
            }

            OnSingletonDestroy();
            _instance = null;
        }

        protected void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 최초 인스턴스 확정 후 1회 초기화, Awake 대신 여기를 오버라이드한다 </summary>
        protected virtual void OnSingletonAwake() { }

        /// <summary> 인스턴스 파괴 시 정리, 구독 해제 등을 여기서 한다 </summary>
        protected virtual void OnSingletonDestroy() { }

        /// <summary> 씬에 배치되지 않았어도 인스턴스를 보장한다 </summary>
        public static T EnsureCreated(string objectName)
        {
            if (_instance != null)
            {
                return _instance;
            }

            // 생성 전에 씬 배치본이 있는지 확인하는 1회 조회
            T found = FindAnyObjectByType<T>(FindObjectsInactive.Exclude);
            if (found != null)
            {
                _instance = found;
                return _instance;
            }

            // AddComponent가 Awake를 동기 호출하므로 여기서 _instance가 이미 채워진다
            GameObject host = new GameObject(objectName);
            return host.AddComponent<T>();
        }

        /// <summary> 정적 상태 초기화, 도메인 리로드 OFF에서 플레이를 멈춰도 static이 남는 것을 막는다 </summary>
        public static void ResetStatic()
        {
            _instance   = null;
            _isQuitting = false;
        }
    }
}
