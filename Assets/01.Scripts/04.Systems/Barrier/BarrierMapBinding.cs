// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary> 필드 씬의 시작점, 필수 랜드마크, 검증할 대체 중심을 결계 prefab에 런타임으로 연결한다 </summary>
    public sealed class BarrierMapBinding : MonoBehaviour
    {
        [Serializable]
        private struct LandmarkEntry
        {
            public BarrierLandmarkKind Kind;
            public Transform           Target;
        }

        [Serializable]
        private struct FallbackEntry
        {
            public RunPhase Phase;
            public Vector3  Center;
        }

        [SerializeField] private Transform       _initialCenter;
        [SerializeField] private LandmarkEntry[] _landmarks = Array.Empty<LandmarkEntry>();
        [SerializeField] private FallbackEntry[] _fallbackCenters = Array.Empty<FallbackEntry>();

        public Vector3 InitialCenter => (_initialCenter == null) ? transform.position : _initialCenter.position;

        private void OnEnable()
        {
            BarrierMapBindingLocator.Register(this);
        }

        private void OnDisable()
        {
            BarrierMapBindingLocator.Unregister(this);
        }

        public bool TryCollectLandmarks(BarrierLandmarkKind required,
            List<Vector3> positions, out BarrierLandmarkKind missing)
        {
            positions.Clear();
            missing = required;

            for (int i = 0; i < _landmarks.Length; ++i)
            {
                LandmarkEntry entry = _landmarks[i];
                if ((entry.Kind == BarrierLandmarkKind.None) ||
                    ((required & entry.Kind) == 0) || (entry.Target == null))
                {
                    continue;
                }

                if ((missing & entry.Kind) == 0)
                {
                    Debug.LogError($"[BarrierMapBinding] {entry.Kind} 랜드마크가 중복 배정됐다", this);
                    return false;
                }

                positions.Add(entry.Target.position);
                missing &= ~entry.Kind;
            }

            return missing == BarrierLandmarkKind.None;
        }

        public void CollectFallbackCenters(RunPhase phase, List<Vector3> centers)
        {
            centers.Clear();
            for (int i = 0; i < _fallbackCenters.Length; ++i)
            {
                if (_fallbackCenters[i].Phase == phase)
                {
                    centers.Add(_fallbackCenters[i].Center);
                }
            }
        }
    }

    public static class BarrierMapBindingLocator
    {
        public static BarrierMapBinding Current { get; private set; }

        public static void Register(BarrierMapBinding binding)
        {
            if (binding == null)
            {
                Debug.LogError("[BarrierMapBindingLocator] null binding을 등록하려 했다");
                return;
            }

            if ((Current != null) && (Current != binding))
            {
                Debug.LogError("[BarrierMapBindingLocator] 활성 필드 binding이 둘 이상이다", binding);
                return;
            }

            Current = binding;
        }

        public static void Unregister(BarrierMapBinding binding)
        {
            if (Current == binding)
            {
                Current = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            Current = null;
        }
    }
}
