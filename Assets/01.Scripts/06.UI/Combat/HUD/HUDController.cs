// Unity
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.Combat;

namespace Geumeum.UI
{
    /// <summary>
    /// 좌하단 HP, MP(신력), SP(스태미나) 바를 로컬 플레이어의 PlayerStatus에 바인딩한다
    /// PlayerStatus.Local은 네트워크 스폰 타이밍에 늦게 생기므로, 생길 때까지 기다렸다가
    /// 딱 한 번 구독하고 그 뒤로는 이벤트로만 갱신한다. 다른 플레이어의 값은 절대 읽지 않는다
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Image _hpFill;
        [SerializeField] private Image _mpFill;
        [SerializeField] private Image _spFill;

        private PlayerStatus _boundStatus;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if ((_boundStatus != null) && (_boundStatus == PlayerStatus.Local))
            {
                return;
            }

            if (_boundStatus != null)
            {
                Unbind(); // Local이 바뀌었다, 재접속 등. 기존 구독부터 정리한다
            }

            if (PlayerStatus.Local == null)
            {
                return;
            }

            Bind(PlayerStatus.Local);
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

        private void OnHealthChanged(float current, float max)  => SetFill(_hpFill, current, max);
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
    }
}
