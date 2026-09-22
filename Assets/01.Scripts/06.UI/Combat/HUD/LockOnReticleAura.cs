// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Geumeum.UI
{
    /// <summary>
    /// 락온 표식 바깥의 번짐을 숨쉬듯 일렁이게 한다
    ///
    /// HUD 캔버스가 ScreenSpaceOverlay라 ParticleSystem은 UI 위에 그려지지 않는다
    /// 그래서 UI 이미지 하나를 여기서 직접 움직인다
    /// </summary>
    public sealed class LockOnReticleAura : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("바깥 번짐")]
        [Tooltip("도트 뒤에 깔리는 번짐. 비워두면 아무것도 하지 않는다")]
        [SerializeField] private RectTransform _glow = null;

        [SerializeField] private Color _glowColor = new Color(1f, 0.99f, 0.96f, 0.38f);

        [Tooltip("초당 회전 각도. 번짐이 고르지 않아 돌리면 일렁이는 느낌이 난다")]
        [SerializeField] private float _spinSpeed = 9f;

        [Tooltip("숨쉬는 주기의 빠르기")]
        [SerializeField] private float _pulseSpeed = 1.7f;

        [Tooltip("숨쉴 때 커지고 작아지는 폭. 0.12면 12퍼센트")]
        [SerializeField, Range(0f, 0.5f)] private float _pulseScale = 0.12f;

        [Tooltip("숨쉴 때 옅어지는 폭")]
        [SerializeField, Range(0f, 1f)] private float _pulseAlpha = 0.25f;

        private Image   _glowImage = null;
        private Vector2 _baseSize  = Vector2.zero;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_glow == null)
            {
                enabled = false;
                return;
            }

            _glowImage = _glow.GetComponent<Image>();
            _baseSize  = _glow.sizeDelta;

            if (_glowImage != null)
            {
                _glowImage.color = _glowColor;
            }
        }

        private void Update()
        {
            // 표식은 게임이 멈춰 있어도 그려진다. 다른 HUD와 같은 시계를 본다
            float time = Time.unscaledTime;
            float wave = Mathf.Sin(time * _pulseSpeed);

            _glow.localRotation = Quaternion.Euler(0f, 0f, time * _spinSpeed);
            _glow.sizeDelta     = _baseSize * (1f + (wave * _pulseScale));

            if (_glowImage == null)
            {
                return;
            }

            Color color = _glowColor;
            color.a *= 1f - (_pulseAlpha * ((wave + 1f) * 0.5f));
            _glowImage.color = color;
        }
    }
}
