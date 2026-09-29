// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.AI
{
    /// <summary>
    /// 보스의 Physics 기반 타겟 스캔만 담당하는 순수 C# 클래스. MonoBehaviour도
    /// NetworkBehaviour도 아니다 - Update 콜백이나 네트워크 동기화 대상 상태가 없고,
    /// BossController가 생성 시점(Awake)에 직접 만들어 필드로 소유한다.
    ///
    /// ResolvePrimaryTarget과 CollectAliveTargets는 기존 RefreshTarget/
    /// ServerCollectAliveTargets의 로직을 각각 그대로 옮긴 것이다. 겉보기엔 비슷해도
    /// 실제로는 서로 다른 필터링을 한다(아래 각 메서드 주석 참고) - 이 차이는 의도적으로
    /// 보존했고, 이번 추출에서 통일하지 않았다
    /// </summary>
    public sealed class BossTargetingSystem
    {
        private const float TARGET_SCAN_RADIUS      = 30f;  // 보스 사거리들보다 넉넉히 잡는다
        private const int   TARGET_SCAN_BUFFER_SIZE = Constants.Network.MAX_PLAYERS * 4;

        private readonly Transform  _self;
        private readonly int        _playerLayerMask;
        private readonly Collider[] _scanBuffer = new Collider[TARGET_SCAN_BUFFER_SIZE];

        /// <summary>
        /// playerLayerMask는 호출부(BossController.Awake)가 이미 계산해 둔 값을 그대로
        /// 넘겨받는다 - 이 클래스가 레이어마스크를 다시 계산하지 않는다
        /// </summary>
        public BossTargetingSystem(Transform self, int playerLayerMask)
        {
            _self            = self;
            _playerLayerMask = playerLayerMask;
        }

        /// <summary>
        /// 기존 RefreshTarget의 Physics 스캔·필터·거리 비교를 그대로 옮겼다. 가장 가까운
        /// 생존(비삼켜짐) 대상 하나만 고른다. 의도적으로 유지한 기존 동작:
        /// - IDamageable이 없는 Player 레이어 콜라이더는 걸러지지 않고 후보로 남는다
        /// - NetworkIdentity 상위 정규화를 하지 않는다(콜라이더 자신의 Transform을 그대로 씀)
        /// - 중복 제거를 하지 않는다(애초에 "가장 가까운 하나"만 고르므로 필요 없음)
        /// </summary>
        public void ResolvePrimaryTarget(out Transform target, out IDamageable damageable)
        {
            int count = Physics.OverlapSphereNonAlloc(_self.position, TARGET_SCAN_RADIUS,
                _scanBuffer, _playerLayerMask);

            Transform   nearest           = null;
            IDamageable nearestDamageable = null;
            float       nearestDistance   = float.MaxValue;

            for (int i = 0; i < count; ++i)
            {
                _scanBuffer[i].TryGetComponent(out IDamageable candidateDamageable);
                if ((candidateDamageable != null) &&
                    (candidateDamageable.IsDead || ((candidateDamageable is IBossSwallowTarget swallowed) &&
                        swallowed.IsSwallowed)))
                {
                    continue; // 관전 또는 삼켜진 플레이어는 새 공격 대상으로 삼지 않는다
                }

                float distance = Vector3.Distance(_self.position, _scanBuffer[i].transform.position);
                if (distance < nearestDistance)
                {
                    nearestDistance   = distance;
                    nearest           = _scanBuffer[i].transform;
                    nearestDamageable = candidateDamageable;
                }
            }

            target     = nearest;
            damageable = nearestDamageable;
        }

        /// <summary>
        /// 기존 ServerCollectAliveTargets를 그대로 옮겼다. ResolvePrimaryTarget과 달리:
        /// - NetworkIdentity 상위 정규화를 한다(한 플레이어의 콜라이더 여러 개를 하나로 묶음)
        /// - 정규화된 Transform 기준으로 중복을 제거한다
        /// - 거리 정렬 없이 조건을 만족하는 대상을 destination 용량까지 순서대로 채운다
        /// </summary>
        public int CollectAliveTargets(Transform[] destination)
        {
            if ((destination == null) || (destination.Length == 0))
            {
                return 0;
            }

            int hitCount = Physics.OverlapSphereNonAlloc(_self.position, TARGET_SCAN_RADIUS,
                _scanBuffer, _playerLayerMask);
            int count = 0;
            for (int i = 0; (i < hitCount) && (count < destination.Length); ++i)
            {
                NetworkIdentity identity = _scanBuffer[i].GetComponentInParent<NetworkIdentity>();
                Transform candidate = (identity == null)
                    ? _scanBuffer[i].transform
                    : identity.transform;
                if (candidate.TryGetComponent(out IDamageable damageable) &&
                    (damageable.IsDead || ((damageable is IBossSwallowTarget swallowed) &&
                        swallowed.IsSwallowed)))
                {
                    continue;
                }

                bool duplicate = false;
                for (int j = 0; j < count; ++j)
                {
                    if (destination[j] == candidate)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    destination[count] = candidate;
                    ++count;
                }
            }

            return count;
        }
    }
}
