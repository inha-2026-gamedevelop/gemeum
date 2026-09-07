// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 패링 판정 테스트 전용 더미 공격자 </summary>
    public class ParryTestDummy : NetworkBehaviour, IParryable
    {
        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("이 키를 누르면 씬의 모든 플레이어에게 테스트 창을 연다")]
        [SerializeField] private KeyCode _triggerKey = KeyCode.F2;

        [Tooltip("창 길이(초). IParryable.ParryWindowDuration으로 노출된다")]
        [SerializeField] private float _windowDuration = 0.3f;

        [Tooltip("트리거 순간부터 타격까지 걸리는 시간(초). 텔레그래프 + 창 길이보다 충분히 길어야 창이 거부되지 않는다")]
        [SerializeField] private float _impactDelay = 1.5f;

        private uint _nextWindowId = 0;

        /****************************************
        *              Properties
        ****************************************/

        public float ParryWindowDuration => _windowDuration;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if ((!isServer) || (!Input.GetKeyDown(_triggerKey)))
            {
                return;
            }

            TriggerAllTargets();
        }

        /****************************************
        *              IParryable
        ****************************************/

        public void OnParried(uint parriedByNetId)
        {
            Debug.Log($"[ParryTestDummy] netId={parriedByNetId}에게 패링당했다", this);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void TriggerAllTargets()
        {
            ++_nextWindowId;

            double now                = NetworkTime.time;
            double telegraphStartTime = now;
            double impactTime         = now + _impactDelay;

            ParryWindowKey key = new ParryWindowKey(netId, _nextWindowId);

            var targets = FindObjectsByType<ParryController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < targets.Length; ++i)
            {
                bool opened = targets[i].OpenParryWindow(this, key, telegraphStartTime, impactTime);
                Debug.Log($"[ParryTestDummy] {targets[i].name}에 테스트 창 {(opened ? "열림" : "거부됨")}, impact까지 {_impactDelay:F2}초", this);
            }
        }
    }
}
