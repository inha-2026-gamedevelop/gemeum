// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Pool;

namespace Geumeum.Common
{
    /// <summary>
    /// 로컬 표현 오브젝트 전용 프리팹별 풀.
    /// 네트워크 개체는 수명을 서버가 소유하므로 여기에 등록하지 않는다 - Mirror spawn handler로 따로 다룬다
    /// </summary>
    public class LocalObjectPoolService : PersistentSingleton<LocalObjectPoolService>
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string POOL_ROOT_NAME     = "[Pooled]";
        private const int    LAZY_POOL_MAX_SIZE = 32;

        // 프리팹 하나에 풀 하나
        private readonly Dictionary<GameObject, ObjectPool<GameObject>> _pools = new Dictionary<GameObject, ObjectPool<GameObject>>();

        // 반납할 때 이 인스턴스가 어느 풀 것인지 알아야 한다
        private readonly Dictionary<GameObject, GameObject> _prefabOf = new Dictionary<GameObject, GameObject>();

        // 대여, 반납마다 GetComponent를 부르면 연출이 잦을수록 비용이 쌓인다
        private readonly Dictionary<GameObject, IPoolable> _poolableOf = new Dictionary<GameObject, IPoolable>();

        // 같은 인스턴스를 두 번 반납하면 풀에 중복이 생겨 두 곳에서 동시에 쓰이게 된다
        private readonly HashSet<GameObject> _rented = new HashSet<GameObject>();

        private Transform _poolRoot;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnSingletonAwake()
        {
            GameObject root = new GameObject(POOL_ROOT_NAME);
            root.transform.SetParent(transform);
            _poolRoot = root.transform;
        }

        protected override void OnSingletonDestroy()
        {
            foreach (ObjectPool<GameObject> pool in _pools.Values)
            {
                pool.Clear();
            }

            _pools.Clear();
            _prefabOf.Clear();
            _poolableOf.Clear();
            _rented.Clear();
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary>
        /// 프리팹의 풀을 만들고 초기 개수만큼 미리 생성한다.
        /// LocalPoolRoot가 붙은 프리팹만 받는다
        /// </summary>
        public void Prewarm(GameObject prefab, int initialCapacity, int maxSize)
        {
            if (prefab == null)
            {
                Debug.LogError("[LocalObjectPoolService] 프리팹이 비어 있어 풀을 만들 수 없다", this);
                return;
            }

            if (initialCapacity < 0)
            {
                Debug.LogError($"[LocalObjectPoolService] {prefab.name} 초기 용량이 음수다", this);
                return;
            }

            if (maxSize < initialCapacity)
            {
                Debug.LogError($"[LocalObjectPoolService] {prefab.name} 상한이 초기 용량보다 작다", this);
                return;
            }

            if (_pools.ContainsKey(prefab))
            {
                Debug.LogError($"[LocalObjectPoolService] {prefab.name} 프리팹이 이미 등록되어 있다", this);
                return;
            }

            if (!IsPoolable(prefab))
            {
                return;
            }

            ObjectPool<GameObject> pool = CreatePool(prefab, initialCapacity, maxSize);
            _pools.Add(prefab, pool);

            // 미리 만들어 두지 않으면 첫 사용에서 Instantiate 비용이 그대로 나온다
            for (int i = 0; i < initialCapacity; ++i)
            {
                pool.Release(CreateInstance(prefab));
            }
        }

        /// <summary> 풀에서 하나 꺼내 배치한다. 등록되지 않은 프리팹이면 그 자리에서 풀을 만든다 </summary>
        public GameObject Rent(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null)
            {
                Debug.LogError("[LocalObjectPoolService] 프리팹이 비어 있어 대여할 수 없다", this);
                return null;
            }

            if (!_pools.TryGetValue(prefab, out ObjectPool<GameObject> pool))
            {
                // Boot를 거치지 않는 Dev 씬 직접 실행에서도 동작해야 한다. 프리워밍은 최적화이지 동작 조건이 아니다
                Debug.LogWarning($"[LocalObjectPoolService] {prefab.name} 프리팹이 프리워밍되지 않아 지연 생성한다", this);

                if (!IsPoolable(prefab))
                {
                    return null;
                }

                pool = CreatePool(prefab, 0, LAZY_POOL_MAX_SIZE);
                _pools.Add(prefab, pool);
            }

            GameObject instance = pool.Get();

            // 활성화 전에 배치해야 첫 프레임을 엉뚱한 자리에서 그리지 않는다
            instance.transform.SetParent(null, false);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);

            _rented.Add(instance);

            if (_poolableOf.TryGetValue(instance, out IPoolable poolable))
            {
                poolable.OnRentFromPool();
            }

            return instance;
        }

        /// <summary> 인스턴스를 풀로 되돌린다. 이 풀이 만든 것이 아니면 false </summary>
        public bool TryReturn(GameObject instance)
        {
            if (instance == null)
            {
                Debug.LogError("[LocalObjectPoolService] 반납 대상이 비어 있다", this);
                return false;
            }

            if (!_prefabOf.TryGetValue(instance, out GameObject prefab))
            {
                // 소유하지 않은 것을 풀에 넣으면 다른 프리팹의 인스턴스가 섞여 나간다
                Debug.LogError($"[LocalObjectPoolService] {instance.name} 은 이 풀이 만든 인스턴스가 아니다", instance);
                Destroy(instance);
                return false;
            }

            if (!_rented.Remove(instance))
            {
                Debug.LogError($"[LocalObjectPoolService] {instance.name} 을 두 번 반납했다", instance);
                return false;
            }

            if (_poolableOf.TryGetValue(instance, out IPoolable poolable))
            {
                // 비활성화 전에 불러야 코루틴을 스스로 멈출 수 있다
                poolable.OnReturnToPool();
            }

            instance.SetActive(false);
            instance.transform.SetParent(_poolRoot, false);

            _pools[prefab].Release(instance);
            return true;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>
        /// 풀에 받아도 되는 프리팹인가. 금지 목록이 아니라 허용 표식으로 판별한다 -
        /// 서버가 수명을 가진 개체는 NetworkIdentity가 없어도 있으므로 빠뜨릴 수 있다
        /// </summary>
        private bool IsPoolable(GameObject prefab)
        {
            if (prefab.TryGetComponent(out LocalPoolRoot _))
            {
                return true;
            }

            Debug.LogError(
                $"[LocalObjectPoolService] {prefab.name} 에 LocalPoolRoot가 없어 풀에 넣지 않는다. "
                + "네트워크 개체라면 Mirror spawn handler로 따로 다룬다", prefab);

            return false;
        }

        private ObjectPool<GameObject> CreatePool(GameObject prefab, int initialCapacity, int maxSize)
        {
            return new ObjectPool<GameObject>(
                createFunc:      () => CreateInstance(prefab),
                actionOnGet:     null,
                actionOnRelease: null,
                actionOnDestroy: DestroyInstance,
                collectionCheck: false, // 중복 반납은 _rented가 먼저 잡고 원인을 알려 준다
                defaultCapacity: Mathf.Max(1, initialCapacity),
                maxSize:         Mathf.Max(1, maxSize));
        }

        private GameObject CreateInstance(GameObject prefab)
        {
            GameObject instance = Instantiate(prefab, _poolRoot);
            instance.SetActive(false);

            _prefabOf.Add(instance, prefab);

            // 인스턴스마다 한 번만 찾는다
            if (instance.TryGetComponent(out IPoolable poolable))
            {
                _poolableOf.Add(instance, poolable);
            }

            return instance;
        }

        // 풀 상한을 넘겨 실제로 파괴될 때 호출된다. 등록을 남기면 죽은 키가 계속 쌓인다
        private void DestroyInstance(GameObject instance)
        {
            _prefabOf.Remove(instance);
            _poolableOf.Remove(instance);
            _rented.Remove(instance);

            Destroy(instance);
        }
    }
}
