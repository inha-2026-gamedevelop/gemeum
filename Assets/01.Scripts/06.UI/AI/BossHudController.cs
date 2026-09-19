// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using Mirror;
using TMPro;

// 프로젝트 내부
using Geumeum.AI;
using Geumeum.Data;

namespace Geumeum.UI
{
    /// <summary>
    /// 보스 바(체력 · 그로기 저항)와 광폭화 경고를 스폰된 보스에 연결한다.
    /// 보스가 없거나 죽은 동안에는 바를 통째로 숨기므로 필드 씬에서는 저절로 보이지 않는다.
    ///
    /// 최대 체력과 그로기 최대치는 서버에서만 확정되는 값이라 클라이언트에서는 0이다.
    /// 서버도 밸런스 값을 그대로 쓰므로(BossStatus.server.cs · BossController.server.cs)
    /// 여기서도 같은 값을 읽어 비율을 만든다 - 서버는 원본 하나만 보내고 표현은 클라가 계산한다
    /// </summary>
    public sealed class BossHudController : MonoBehaviour
    {
        /****************************************
        *                 Types
        ****************************************/

        private enum EnrageState
        {
            None    = 0,
            Warning = 1,
            Enraged = 2,
        }

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

        [Tooltip("그로기 저항 채움. Image Type이 Filled여야 한다. 0이 되는 순간 보스가 그로기에 들어간다")]
        [SerializeField] private Image _groggyFill = null;

        [Header("광폭화 경고")]
        [Tooltip("광폭화 예고·발동 문구. 비워 두면 경고를 표시하지 않는다")]
        [SerializeField] private TMP_Text _enrageLabel = null;

        [SerializeField] private string _enrageWarningText = "광폭화가 임박했다";
        [SerializeField] private string _enragedText       = "광폭화";

        [SerializeField] private Color _enrageWarningColor = new Color(1f, 0.82f, 0.35f, 1f);
        [SerializeField] private Color _enragedColor       = new Color(1f, 0.3f, 0.3f, 1f);

        private BossStatus     _boss           = null;
        private BossController _bossController = null;
        private float          _nextSearchTime = 0f;

        // 같은 문구를 매 프레임 다시 적으면 TMP가 메시를 다시 만든다
        private EnrageState _shownEnrage = EnrageState.None;

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
            _boss           = null;
            _bossController = null;
            Show(false);
        }

        private void Update()
        {
            if ((_boss == null) || _boss.IsDead)
            {
                _boss           = null;
                _bossController = null;

                if (Time.unscaledTime < _nextSearchTime)
                {
                    return;
                }

                _nextSearchTime = Time.unscaledTime + SEARCH_SECONDS;
                _boss           = FindBoss(out _bossController);

                if (_boss == null)
                {
                    Show(false);
                    return;
                }
            }

            Show(true);
            ApplyHealthFill(_boss.Health);
            ApplyGroggyFill();
            ApplyEnrage();
        }

        /****************************************
        *                Methods
        ****************************************/

        // 체력·그로기가 SyncVar라 클라이언트도 보스의 값을 읽는다. 훅이 없어 값은 여기서 직접 본다
        private static BossStatus FindBoss(out BossController controller)
        {
            controller = null;

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
                    identity.TryGetComponent(out controller);
                    return status;
                }
            }

            return null;
        }

        private void ApplyHealthFill(float health)
        {
            float max = (GameDB.IsInitialized) ? GameDB.Boss.MaxHealth : 0f;
            SetFill(_hpFill, health, max);
        }

        /// <summary>
        /// 남은 저항을 그대로 그린다. 0이면 그로기 상태라 빈 바가 정상이다.
        /// 보스가 스폰된 직후에는 서버가 최대치를 아직 넣지 않아 0으로 내려오는데,
        /// 그 짧은 구간도 빈 바로 보이는 것이 "저항이 남았다"고 잘못 읽히는 것보다 낫다
        /// </summary>
        private void ApplyGroggyFill()
        {
            float max   = (GameDB.IsInitialized) ? GameDB.Boss.GroggyGaugeMax : 0f;
            float gauge = (_bossController == null) ? 0f : _bossController.GroggyGauge;

            SetFill(_groggyFill, gauge, max);
        }

        private void ApplyEnrage()
        {
            if (_enrageLabel == null)
            {
                return;
            }

            EnrageState wanted = ResolveEnrageState();
            if (wanted == _shownEnrage)
            {
                return;
            }

            _shownEnrage = wanted;

            if (wanted == EnrageState.None)
            {
                _enrageLabel.text = string.Empty;
                return;
            }

            bool isEnraged = (wanted == EnrageState.Enraged);

            _enrageLabel.color = (isEnraged) ? _enragedColor : _enrageWarningColor;
            _enrageLabel.text  = (isEnraged) ? _enragedText : _enrageWarningText;
        }

        private EnrageState ResolveEnrageState()
        {
            if (_bossController == null)
            {
                return EnrageState.None;
            }

            // 발동이 예고를 덮는다. 둘 다 켜져 있으면 이미 광폭화한 것이다
            if (_bossController.IsEnraged)
            {
                return EnrageState.Enraged;
            }

            return (_bossController.IsEnrageWarning) ? EnrageState.Warning : EnrageState.None;
        }

        private static void SetFill(Image fill, float current, float max)
        {
            if (fill == null)
            {
                return;
            }

            float wanted = (max > 0f) ? Mathf.Clamp01(current / max) : 0f;

            if (!Mathf.Approximately(fill.fillAmount, wanted))
            {
                fill.fillAmount = wanted;
            }
        }

        private void Show(bool isVisible)
        {
            if ((_root == null) || (_root.activeSelf == isVisible))
            {
                return;
            }

            if (!isVisible)
            {
                _shownEnrage = EnrageState.None; // 다음에 켤 때 지난 판의 문구가 남지 않게 한다

                if (_enrageLabel != null)
                {
                    _enrageLabel.text = string.Empty;
                }
            }

            _root.SetActive(isVisible);
        }
    }
}
