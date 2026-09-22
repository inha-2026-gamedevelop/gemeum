// Unity
using UnityEngine;

namespace Geumeum.UI
{
    /// <summary>
    /// 관리 창이 열려 있는 동안 HUD를 숨긴다
    /// GameObject를 끄지 않고 Canvas만 끄므로, 숨어 있는 동안에도 바인딩과 값 갱신은 계속된다
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [DisallowMultipleComponent]
    public sealed class HudVisibilityGate : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private Canvas _canvas;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();
        }

        private void OnEnable()
        {
            FunctionalManagementPanel.VisibilityChanged += HandleManagementVisibilityChanged;

            // 관리 창이 먼저 열린 뒤에 이 화면이 켜질 수 있다. 지금 상태에 맞춰 시작한다
            bool isManagementOpen = ((FunctionalManagementPanel.Instance != null) &&
                FunctionalManagementPanel.Instance.IsVisible);
            Apply(isManagementOpen);
        }

        private void OnDisable()
        {
            FunctionalManagementPanel.VisibilityChanged -= HandleManagementVisibilityChanged;

            // 숨긴 채로 끝나면 다음에 켤 때 HUD가 사라진 것처럼 보인다
            Apply(false);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void HandleManagementVisibilityChanged(bool isManagementOpen)
        {
            Apply(isManagementOpen);
        }

        private void Apply(bool isManagementOpen)
        {
            if (_canvas == null)
            {
                return;
            }

            _canvas.enabled = (!isManagementOpen);
        }
    }
}
