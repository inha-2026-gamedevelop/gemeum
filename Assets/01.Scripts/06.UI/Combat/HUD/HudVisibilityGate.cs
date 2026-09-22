// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.UI
{
    /// <summary>
    /// 관리 창이 열려 있는 동안, 그리고 런이 정산으로 넘어간 뒤 HUD를 숨긴다.
    /// GameObject를 끄지 않고 Canvas만 끄므로, 숨어 있는 동안에도 바인딩과 값 갱신은 계속된다.
    ///
    /// HUD는 SessionManagers 안에 있어 DontDestroyOnLoad로 Result 씬까지 따라간다.
    /// 끄지 않으면 결과창 위에 전투 HUD가 남고, 시간 슬롯이 멈춘 필드 시계로 되돌아간다
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    [DisallowMultipleComponent]
    public sealed class HudVisibilityGate : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private Canvas _canvas;

        // 관리 창 상태는 이벤트로 받고, 단계는 매 프레임 읽는다. 둘 중 하나라도 참이면 숨긴다
        private bool _isManagementOpen;

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
            _isManagementOpen = ((FunctionalManagementPanel.Instance != null) &&
                FunctionalManagementPanel.Instance.IsVisible);
            Apply();
        }

        private void OnDisable()
        {
            FunctionalManagementPanel.VisibilityChanged -= HandleManagementVisibilityChanged;

            // 숨긴 채로 끝나면 다음에 켤 때 HUD가 사라진 것처럼 보인다
            _isManagementOpen = false;
            SetVisible(true);
        }

        /// <summary>
        /// 단계는 이벤트가 없어 매 프레임 읽는다. enum 비교 한 번이라 비용은 사실상 없다.
        /// RunManager가 단계 변경 이벤트를 공개하면 관리 창처럼 구독으로 바꾼다
        /// </summary>
        private void Update()
        {
            Apply();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void HandleManagementVisibilityChanged(bool isManagementOpen)
        {
            _isManagementOpen = isManagementOpen;
            Apply();
        }

        private void Apply()
        {
            SetVisible((!_isManagementOpen) && (!IsRunFinished()));
        }

        /// <summary>
        /// 정산 단계인지. 씬이 아니라 단계로 판단한다 -
        /// 런이 끝나도 Result 씬 전환이 한 프레임 늦게 오는 경우가 있다
        /// </summary>
        private static bool IsRunFinished()
        {
            RunManager run = RunManager.LocalInstance;
            return ((run != null) && (run.Phase == RunPhase.Result));
        }

        private void SetVisible(bool isVisible)
        {
            // 매 프레임 같은 값을 쓰면 Canvas가 불필요하게 다시 그려진다
            if ((_canvas == null) || (_canvas.enabled == isVisible))
            {
                return;
            }

            _canvas.enabled = isVisible;
        }
    }
}
