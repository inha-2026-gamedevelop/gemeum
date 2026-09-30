// Unity
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Data;

namespace Geumeum.UI
{
    /// <summary>
    /// 좌하단 HP, MP(신력), SP(스태미나) 바를 로컬 플레이어의 PlayerStatus에 바인딩한다
    /// PlayerStatus.Local은 네트워크 스폰 타이밍에 늦게 생기므로, 생길 때까지 기다렸다가
    /// 딱 한 번 구독하고 그 뒤로는 이벤트로만 갱신한다. 다른 플레이어의 값은 절대 읽지 않는다
    /// HP는 잡귀 체력 바와 같은 잔상을 남긴다 - 깎인 만큼 밝게 남았다가 지연 뒤 따라 내려간다
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string HP_CHIP_NAME = "Chip";

        [SerializeField] private Image _hpFill;
        [SerializeField] private Image _mpFill;
        [SerializeField] private Image _spFill;

        [Tooltip("HP 잔상 붓 획. 비우면 HP 채움 그림을 쓴다 - 채움 그림이 이미 빨개서 색을 입혀도 잔상 색이 나오지 않는다")]
        [SerializeField] private Sprite _hpChipSprite;

        private PlayerStatus _boundStatus;

        // 잔상 막대는 HP 채움을 복제해 바로 뒤에 깐다 - 붓 획 모양이 같아야 겹쳐 보이지 않는다
        private Image              _hpChip;
        private EnemyHealthBarTrail _hpTrail = EnemyHealthBarTrail.Create(1f);
        private bool                _hasHpRatio;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _hpChip = CreateChip(_hpFill, _hpChipSprite);
        }

        private void Update()
        {
            if (_boundStatus != PlayerStatus.Local)
            {
                if (_boundStatus != null)
                {
                    Unbind(); // Local이 바뀌었다, 재접속 등. 기존 구독부터 정리한다
                }

                if (PlayerStatus.Local != null)
                {
                    Bind(PlayerStatus.Local);
                }
            }

            TickHpChip();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Bind(PlayerStatus status)
        {
            _boundStatus = status;

            _boundStatus.HealthChanged  += OnHealthChanged;
            _boundStatus.SpiritChanged  += OnSpiritChanged;
            _boundStatus.StaminaChanged += OnStaminaChanged;

            // 새 플레이어의 첫 값은 깎인 것이 아니다. 잔상 없이 그 값에서 시작한다
            _hasHpRatio = false;
            ApplyChipColor(); // Awake 때는 GameDB가 아직 없을 수 있어 바인딩 때 칠한다
            _boundStatus.RaiseAll(); // 뒤늦게 구독했으니 지금 값을 한 번 받아온다
        }

        private void Unbind()
        {
            if (_boundStatus == null)
            {
                return;
            }

            _boundStatus.HealthChanged  -= OnHealthChanged;
            _boundStatus.SpiritChanged  -= OnSpiritChanged;
            _boundStatus.StaminaChanged -= OnStaminaChanged;

            _boundStatus = null;
        }

        private void OnHealthChanged(float current, float max)
        {
            float ratio = EnemyHealthBarRules.Ratio(current, max);
            if (!_hasHpRatio)
            {
                _hasHpRatio = true;
                _hpTrail    = EnemyHealthBarTrail.Create(ratio);
            }
            else
            {
                _hpTrail.SetRatio(ratio, Time.unscaledTime, ResolveChipDelaySeconds());
            }

            ApplyHp();
        }

        private void OnSpiritChanged(float current, float max)  => SetFill(_mpFill, current, max);
        private void OnStaminaChanged(float current, float max) => SetFill(_spFill, current, max);

        private static void SetFill(Image image, float current, float max)
        {
            if (image == null)
            {
                return;
            }

            image.fillAmount = (max > 0f) ? Mathf.Clamp01(current / max) : 0f;
        }

        /// <summary> 잔상이 남아 있을 때만 줄인다. 다 따라왔으면 할 일이 없다 </summary>
        private void TickHpChip()
        {
            if (!_hpTrail.HasChip)
            {
                return;
            }

            _hpTrail.Tick(Time.unscaledTime, Time.unscaledDeltaTime, ResolveChipDrainPerSecond());
            ApplyHp();
        }

        private void ApplyHp()
        {
            if (_hpFill != null)
            {
                _hpFill.fillAmount = _hpTrail.Health;
            }

            if (_hpChip != null)
            {
                _hpChip.fillAmount = _hpTrail.Chip;
            }
        }

        // 잡귀 체력 바와 같은 값을 쓴다. 설정을 못 읽으면 잔상 없이 바로 맞춘다
        private static float ResolveChipDelaySeconds()
        {
            return GameDB.IsInitialized && (GameDB.Presentation != null)
                ? GameDB.Presentation.EnemyHealthBarChipDelaySeconds
                : 0f;
        }

        private static float ResolveChipDrainPerSecond()
        {
            return GameDB.IsInitialized && (GameDB.Presentation != null)
                ? GameDB.Presentation.EnemyHealthBarChipDrainPerSecond
                : 0f;
        }

        /// <summary> 잔상 색은 잡귀 체력 바와 같다 </summary>
        private void ApplyChipColor()
        {
            if ((_hpChip != null) && GameDB.IsInitialized && (GameDB.Presentation != null))
            {
                _hpChip.color = GameDB.Presentation.EnemyHealthBarChipColor;
            }
        }

        /// <summary> 채움 이미지를 복제해 바로 뒤에 깐다 </summary>
        private static Image CreateChip(Image fill, Sprite sprite)
        {
            if (fill == null)
            {
                return null;
            }

            Transform fillTransform = fill.transform;
            Image chip = Instantiate(fill, fillTransform.parent);
            chip.name = HP_CHIP_NAME;
            if (sprite != null)
            {
                chip.sprite = sprite;
            }

            chip.transform.SetSiblingIndex(fillTransform.GetSiblingIndex());
            chip.fillAmount = fill.fillAmount;
            return chip;
        }
    }
}
