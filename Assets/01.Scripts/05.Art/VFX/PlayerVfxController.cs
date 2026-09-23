// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;

// Third party
using Mirror;

// Project
using Geumeum.Common;

namespace Geumeum.Art
{
    [Serializable]
    public sealed class PlayerVfxEntry
    {
        [SerializeField] private string _key;
        [SerializeField] private GameObject _prefab;
        [SerializeField, Min(0), Tooltip("최초 재생 전에 확보할 전체 인스턴스 수. 0이면 선생성을 생략합니다.")]
        private int _initialCapacity = 1;
        [SerializeField, Min(1), Tooltip("Boot에서는 같은 프리팹의 최대 상한으로 등록합니다. 등록 뒤에는 풀 상한을 늘릴 수 없습니다.")]
        private int _maxSize = 1;
        [SerializeField] private Transform _anchor;

        public string Key => _key;
        public GameObject Prefab => _prefab;
        public int InitialCapacity => _initialCapacity;
        public int MaxSize => _maxSize;
        public Transform Anchor => _anchor;
    }

    /// <summary>플레이어별 로컬 VFX 목록, 프리워밍과 대여 수명 관리</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVfxController : MonoBehaviour
    {
        public const string MELEE_SLASH_KEY = "melee_slash";
        public const string BELL_PULSE_KEY = "bell_pulse";
        public const string HEAL_CAST_KEY = "heal_cast";
        public const string PARRY_KEY = "parry";
        public const string BOW_CHARGE_KEY = "bow_charge";
        public const string BOW_LASER_RELEASE_KEY = "bow_laser_release";
        public const string MARK_KEY = "mark";

        [SerializeField] private List<PlayerVfxEntry> _entries = new List<PlayerVfxEntry>();

        private readonly Dictionary<string, PlayerVfxEntry> _validEntries =
            new Dictionary<string, PlayerVfxEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<VisualLease>> _leases =
            new Dictionary<string, List<VisualLease>>(StringComparer.Ordinal);
        private Coroutine _initializeRoutine;
        private LocalObjectPoolService _pool;

        private readonly struct VisualLease
        {
            public readonly PooledVisualLifetime Visual;
            public readonly uint Generation;

            public VisualLease(PooledVisualLifetime visual)
            {
                Visual = visual;
                Generation = visual.LeaseGeneration;
            }
        }

        private void OnEnable()
        {
            BuildEntries();
            if (_initializeRoutine == null)
            {
                _initializeRoutine = StartCoroutine(CoInitialize());
            }
        }

        private void OnDisable()
        {
            if (_initializeRoutine != null)
            {
                StopCoroutine(_initializeRoutine);
                _initializeRoutine = null;
            }

            StopAll();
            _pool = null;
        }

        /// <summary>유효하게 등록한 키의 프리팹을 조회한다. 미등록 키는 조용히 false를 반환한다.</summary>
        public bool TryGetPrefab(string key, out GameObject prefab)
        {
            prefab = null;
            if ((key == null) || !_validEntries.TryGetValue(key, out PlayerVfxEntry entry) ||
                (entry.Prefab == null))
            {
                return false;
            }

            prefab = entry.Prefab;
            return true;
        }

        /// <summary>프리팹 에셋에서도 인스턴스 생성 없이 Boot 프리워밍 목록을 복사한다.</summary>
        public bool CopyPrewarmEntries(List<PlayerVfxEntry> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (_entries == null)
            {
                return true;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            bool valid = true;
            for (int i = 0; i < _entries.Count; ++i)
            {
                PlayerVfxEntry entry = _entries[i];
                if ((entry == null) || string.IsNullOrWhiteSpace(entry.Key) ||
                    !keys.Add(entry.Key) || (entry.Prefab == null) ||
                    (entry.InitialCapacity < 0) || (entry.MaxSize < 1) ||
                    (entry.InitialCapacity > entry.MaxSize) ||
                    (entry.Prefab.GetComponent<LocalPoolRoot>() == null) ||
                    (entry.Prefab.GetComponent<PooledVisualLifetime>() == null))
                {
                    Debug.LogError($"[PlayerVfxController] Boot 목록 {i}가 유효하지 않다", this);
                    valid = false;
                    continue;
                }

                destination.Add(entry);
            }
            return valid;
        }

        /// <summary>등록된 앵커, 없으면 플레이어 위치에서 VFX를 재생한다.</summary>
        public PooledVisualLifetime Play(string key)
        {
            if (!TryResolveEntry(key, out PlayerVfxEntry entry))
            {
                return null;
            }

            Transform origin = entry.Anchor != null ? entry.Anchor : transform;
            return PlayEntry(key, entry, origin.position, origin.rotation);
        }

        /// <summary>지정한 월드 위치에서 재생하고, 앵커가 있으면 그 밑에 붙인다.</summary>
        public PooledVisualLifetime Play(string key, Vector3 position, Quaternion rotation)
        {
            return TryResolveEntry(key, out PlayerVfxEntry entry)
                ? PlayEntry(key, entry, position, rotation)
                : null;
        }

        /// <summary>해당 키로 대여한 모든 VFX를 현재 lease에 한해 정지한다.</summary>
        public bool Stop(string key)
        {
            if (!TryResolveEntry(key, out _))
            {
                return false;
            }

            if (!_leases.TryGetValue(key, out List<VisualLease> leases))
            {
                return false;
            }

            bool stopped = false;
            for (int i = 0; i < leases.Count; ++i)
            {
                VisualLease lease = leases[i];
                if (lease.Visual != null)
                {
                    stopped |= lease.Visual.TryStop(lease.Generation);
                }
            }

            leases.Clear();
            return stopped;
        }

        public void StopAll()
        {
            foreach (List<VisualLease> leases in _leases.Values)
            {
                for (int i = 0; i < leases.Count; ++i)
                {
                    VisualLease lease = leases[i];
                    if (lease.Visual != null)
                    {
                        lease.Visual.TryStop(lease.Generation);
                    }
                }

                leases.Clear();
            }
        }

        private IEnumerator CoInitialize()
        {
            bool shouldPrewarm;
            while (!PresentationPoolRules.TryResolvePrewarm(
                    NetworkServer.active, NetworkClient.active, out shouldPrewarm) ||
                (NetworkServer.active && !NetworkClient.active &&
                    NetworkManager.singleton != null &&
                    NetworkManager.singleton.mode == NetworkManagerMode.Host))
            {
                yield return null;
            }

            if (!shouldPrewarm)
            {
                _initializeRoutine = null;
                yield break;
            }

            while (!LocalObjectPoolService.HasInstance)
            {
                yield return null;
            }

            _pool = LocalObjectPoolService.Instance;
            if (_pool != null)
            {
                foreach (PlayerVfxEntry entry in _validEntries.Values)
                {
                    _pool.EnsurePrewarmed(entry.Prefab, entry.InitialCapacity, entry.MaxSize);
                }
            }

            _initializeRoutine = null;
        }

        private PooledVisualLifetime PlayEntry(string key, PlayerVfxEntry entry,
            Vector3 position, Quaternion rotation)
        {
            if (!isActiveAndEnabled || !NetworkClient.active)
            {
                return null;
            }

            if ((_pool == null) && LocalObjectPoolService.HasInstance)
            {
                _pool = LocalObjectPoolService.Instance;
            }

            if ((_pool == null) ||
                !_pool.EnsurePrewarmed(entry.Prefab, entry.InitialCapacity, entry.MaxSize))
            {
                return null;
            }

            GameObject instance = _pool.Rent(entry.Prefab, position, rotation);
            if (instance == null)
            {
                return null;
            }

            if (entry.Anchor != null)
            {
                instance.transform.SetParent(entry.Anchor, true);
            }

            PooledVisualLifetime visual = instance.GetComponent<PooledVisualLifetime>();
            if (visual == null)
            {
                _pool.TryReturn(instance);
                return null;
            }

            if (!_leases.TryGetValue(key, out List<VisualLease> leases))
            {
                leases = new List<VisualLease>();
                _leases.Add(key, leases);
            }

            // 자동 수명 종료된 과거 대여는 다음 재생 전에 제거한다.
            leases.RemoveAll(lease => lease.Visual == null || !lease.Visual.IsRented ||
                lease.Visual.LeaseGeneration != lease.Generation);
            leases.Add(new VisualLease(visual));
            return visual;
        }

        private bool TryResolveEntry(string key, out PlayerVfxEntry entry)
        {
            if (string.IsNullOrWhiteSpace(key) || !_validEntries.TryGetValue(key, out entry))
            {
                entry = null;
                Debug.LogError($"[PlayerVfxController] 등록되지 않은 VFX 키: '{key}'", this);
                return false;
            }

            return true;
        }

        private void BuildEntries()
        {
            _validEntries.Clear();
            if (_entries == null)
            {
                return;
            }

            for (int i = 0; i < _entries.Count; ++i)
            {
                PlayerVfxEntry entry = _entries[i];
                if ((entry == null) || string.IsNullOrWhiteSpace(entry.Key))
                {
                    Debug.LogError($"[PlayerVfxController] VFX 목록 {i}의 키가 비어 있다", this);
                    continue;
                }

                if (_validEntries.ContainsKey(entry.Key))
                {
                    Debug.LogError($"[PlayerVfxController] 중복 VFX 키: '{entry.Key}'", this);
                    continue;
                }

                if ((entry.Prefab == null) || (entry.InitialCapacity < 0) ||
                    (entry.MaxSize < 1) || (entry.InitialCapacity > entry.MaxSize))
                {
                    Debug.LogError($"[PlayerVfxController] '{entry.Key}' 프리팹 또는 풀 용량이 잘못됐다", this);
                    continue;
                }

                if ((entry.Prefab.GetComponent<LocalPoolRoot>() == null) ||
                    (entry.Prefab.GetComponent<PooledVisualLifetime>() == null))
                {
                    Debug.LogError($"[PlayerVfxController] '{entry.Key}' 프리팹 루트에 LocalPoolRoot와 PooledVisualLifetime이 필요하다", entry.Prefab);
                    continue;
                }

                _validEntries.Add(entry.Key, entry);
            }
        }
    }
}
