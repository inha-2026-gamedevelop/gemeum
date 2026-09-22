// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Combat;

namespace Geumeum.UI
{
    /// <summary>
    /// 락온한 대상의 중앙에 표식을 띄운다. 락온 자체는 LockOnController가 소유하고,
    /// 여기서는 이미 확정된 대상의 좌표를 화면 좌표로 옮겨 그리기만 한다
    ///
    /// 내 화면의 표현이라 서버와 무관하다. 다른 사람의 락온은 그리지 않는다
    /// </summary>
    public sealed class LockOnReticle : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("표식")]
        [Tooltip("대상 중앙에 놓을 점. 앵커와 피벗을 중앙으로 맞춘다")]
        [SerializeField] private RectTransform _dot = null;

        [Tooltip("점이 올라갈 캔버스. 비워두면 부모에서 찾는다")]
        [SerializeField] private Canvas _canvas = null;

        private RectTransform    _canvasRect  = null;
        private Camera           _camera      = null;
        private PlayerStatus     _boundStatus = null;
        private LockOnController _lockOn      = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }

            if (_canvas != null)
            {
                _canvasRect = _canvas.transform as RectTransform;
            }
        }

        private void OnDisable()
        {
            _boundStatus = null;
            _lockOn      = null;
            Show(false);
        }

        private void Update()
        {
            ResolveLocalPlayer();

            if ((_lockOn == null) || (!_lockOn.IsLockedOn))
            {
                Show(false);
                return;
            }

            DrawAt(_lockOn.TargetAimPoint);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 로컬 플레이어는 네트워크 스폰 타이밍에 늦게 생기고 재접속으로 바뀔 수 있다
        private void ResolveLocalPlayer()
        {
            PlayerStatus local = PlayerStatus.Local;

            if (_boundStatus == local)
            {
                return;
            }

            _boundStatus = local;
            _lockOn      = (local != null) ? local.GetComponent<LockOnController>() : null;
        }

        private void DrawAt(Vector3 worldPoint)
        {
            if ((_dot == null) || (_canvasRect == null))
            {
                return;
            }

            if ((_camera == null) || (!_camera.isActiveAndEnabled))
            {
                _camera = Camera.main;
            }

            if (_camera == null)
            {
                Show(false);
                return;
            }

            Vector3 screenPoint = _camera.WorldToScreenPoint(worldPoint);

            // 카메라 뒤로 넘어간 대상은 화면 좌표가 반대편으로 뒤집혀 엉뚱한 자리에 찍힌다
            if (screenPoint.z <= 0f)
            {
                Show(false);
                return;
            }

            Vector2 localPoint;
            bool isInside = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRect, screenPoint, null, out localPoint);

            if (!isInside)
            {
                Show(false);
                return;
            }

            Show(true);
            _dot.anchoredPosition = localPoint;
        }

        private void Show(bool isVisible)
        {
            if ((_dot == null) || (_dot.gameObject.activeSelf == isVisible))
            {
                return;
            }

            _dot.gameObject.SetActive(isVisible);
        }
    }
}
