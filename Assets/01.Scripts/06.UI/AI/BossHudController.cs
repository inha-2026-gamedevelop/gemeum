// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.AI;
using Geumeum.Data;

namespace Geumeum.UI
{
    /// <summary>
    /// 보스 체력 바를 스폰된 보스에 연결한다. 보스가 없거나 죽은 동안에는 바를 숨긴다.
    ///
    /// 최대 체력은 서버에서만 확정되는 값이라 클라이언트에서는 0이다.
    /// 서버도 밸런스 값을 그대로 쓰므로(BossStatus.server.cs) 여기서도 같은 값을 읽어 비율을 만든다
    /// </summary>
    public sealed class BossHudController : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 매 프레임 스폰 목록을 훑으면 그 자체가 부하다. 찾는 일만 주기로 돌린다
        private const float SEARCH_SECONDS = 0.5f;

        [Header("바")]
        [Tooltip("보스 바 전체. 보스가 없는 동안 통째로 숨긴다")]
        [SerializeField] private GameObject _root = null;

        [Tooltip("체력 채움. Image Type이 Filled여야 한다")]
        [SerializeField] private Image _hpFill = null;

        private BossStatus _boss           = null;
        private float      _nextSearchTime = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _nextSearchTime = 0f;
            Show(false);
        }

        private void OnDisable()
        {
            _boss = null;
            Show(false);
        }

        private void Update()
        {
            if ((_boss == null) || _boss.IsDead)
            {
                _boss = null;

                if (Time.unscaledTime < _nextSearchTime)
                {
                    return;
                }

                _nextSearchTime = Time.unscaledTime + SEARCH_SECONDS;
                _boss           = FindBoss();

                if (_boss == null)
                {
                    Show(false);
                    return;
                }
            }

            Show(true);
            ApplyFill(_boss.Health);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 체력이 SyncVar라 클라이언트도 보스의 값을 읽는다. 훅이 없어 값은 여기서 직접 본다
        private static BossStatus FindBoss()
        {
            Dictionary<uint, NetworkIdentity> spawned = (NetworkClient.active)
                ? NetworkClient.spawned
                : NetworkServer.spawned;

            if (spawned == null)
            {
                return null;
            }

            foreach (NetworkIdentity identity in spawned.Values)
            {
                if (identity == null)
                {
                    continue;
                }

                if (identity.TryGetComponent(out BossStatus status) && (!status.IsDead))
                {
                    return status;
                }
            }

            return null;
        }

        private void ApplyFill(float health)
        {
            if (_hpFill == null)
            {
                return;
            }

            float max = (GameDB.IsInitialized) ? GameDB.Boss.MaxHealth : 0f;
            float wanted = (max > 0f) ? Mathf.Clamp01(health / max) : 0f;

            if (!Mathf.Approximately(_hpFill.fillAmount, wanted))
            {
                _hpFill.fillAmount = wanted;
            }
        }

        private void Show(bool isVisible)
        {
            if ((_root == null) || (_root.activeSelf == isVisible))
            {
                return;
            }

            _root.SetActive(isVisible);
        }
    }
}
