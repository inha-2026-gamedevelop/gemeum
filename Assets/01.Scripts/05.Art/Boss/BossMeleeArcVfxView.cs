// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.AI;

namespace Geumeum.Art
{
    /// <summary>
    /// BossController.MeleeArcImpact 신호를 받아 타격 지점에 VFX 프리팹을 생성했다가 지운다.
    /// 판정에는 관여하지 않는 표현 전용 러프 연출 — 이슈 검증용으로 ConeBurst 한 템플릿만 먼저 붙였다.
    /// 정식 프로덕션 파이프라인(PresentationConfigSO·풀링)을 거치지 않는 임시 구현이다
    /// </summary>
    [RequireComponent(typeof(BossController))]
    public sealed class BossMeleeArcVfxView : MonoBehaviour
    {
        [SerializeField] private GameObject _vfxPrefab;
        [SerializeField] private float      _vfxLifetimeSeconds = 2f;

        private BossController _controller;

        private void Awake()
        {
            _controller = GetComponent<BossController>();
        }

        private void OnEnable()
        {
            _controller.MeleeArcImpact += HandleMeleeArcImpact;
        }

        private void OnDisable()
        {
            _controller.MeleeArcImpact -= HandleMeleeArcImpact;
        }

        private void HandleMeleeArcImpact(Vector3 origin, Vector3 forward)
        {
            if (_vfxPrefab == null)
            {
                return;
            }

            // 소스 VFX(SB_Shockwave_Cone)의 정면 축이 반대라, forward를 그대로 쓰면 반대로 나간다
            GameObject instance = Instantiate(_vfxPrefab, origin, Quaternion.LookRotation(-forward));
            Destroy(instance, _vfxLifetimeSeconds);
        }
    }
}
