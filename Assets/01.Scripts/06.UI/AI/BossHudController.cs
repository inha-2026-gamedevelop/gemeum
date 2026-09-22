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

        // 그로기가 끝나면 서버가 저항을 최대치로 한 번에 되돌린다. 그대로 그리면 한 프레임 만에
        // 빈 바가 꽉 찬 바로 바뀌어 회복했다는 것이 눈에 남지 않는다. 차오르는 데 걸리는 시간이다
        private const float GROGGY_REFILL_SECONDS = 1.5f;

        // 기상하는 프레임에는 기상 패턴이 함께 시작돼 화면이 한 번 크게 끊긴다. 그 프레임의 경과 시간을
        // 그대로 실으면 차오르는 과정이 통째로 건너뛰어지므로, 한 프레임이 옮길 양을 묶는다
        private const float GROGGY_REFILL_MAX_STEP_SECONDS = 1f / 30f;

        // 따라갈 기준이 아직 없다는 표시. 비율은 0 이상이라 음수와 겹치지 않는다
        private const float RATIO_UNSET = -1f;

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

        // 지금 화면에 그려져 있는 그로기 비율
        private float _shownGroggyRatio = RATIO_UNSET;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _nextSearchTime   = 0f;
            _shownGroggyRatio = RATIO_UNSET;
            Show(false);
        }

        private void OnDisable()
        {
            _boss             = null;
            _bossController   = null;
            _shownGroggyRatio = RATIO_UNSET;
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

                // 다른 보스로 바뀌었을 수 있다. 앞 판의 비율에서 이어 차오르지 않게 한다
                _shownGroggyRatio = RATIO_UNSET;
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
        /// 남은 저항을 그린다. 0이면 그로기 상태라 빈 바가 정상이다.
        /// 보스가 스폰된 직후에는 서버가 최대치를 아직 넣지 않아 0으로 내려오는데,
        /// 그 짧은 구간도 빈 바로 보이는 것이 "저항이 남았다"고 잘못 읽히는 것보다 낫다.
        ///
        /// 차오르는 쪽만 시간을 들여 따라간다. 깎이는 쪽은 때린 결과를 바로 보여줘야 하므로 즉시 맞춘다
        /// </summary>
        private void ApplyGroggyFill()
        {
            float max    = (GameDB.IsInitialized) ? GameDB.Boss.GroggyGaugeMax : 0f;
            float gauge  = (_bossController == null) ? 0f : _bossController.GroggyGauge;
            float wanted = (max > 0f) ? Mathf.Clamp01(gauge / max) : 0f;

            if ((_shownGroggyRatio <= RATIO_UNSET) || (wanted <= _shownGroggyRatio))
            {
                _shownGroggyRatio = wanted;
            }
            else
            {
                // 시간 배율을 늦추는 연출 중에도 바는 같은 속도로 찬다
                float delta = Mathf.Min(Time.unscaledDeltaTime, GROGGY_REFILL_MAX_STEP_SECONDS);
                float step  = delta / GROGGY_REFILL_SECONDS;

                _shownGroggyRatio = Mathf.Min(_shownGroggyRatio + step, wanted);
            }

            SetFillRatio(_groggyFill, _shownGroggyRatio);
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
            SetFillRatio(fill, (max > 0f) ? Mathf.Clamp01(current / max) : 0f);
        }

        private static void SetFillRatio(Image fill, float ratio)
        {
            if (fill == null)
            {
                return;
            }

            if (!Mathf.Approximately(fill.fillAmount, ratio))
            {
                fill.fillAmount = ratio;
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
