// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Geumeum.Art
{
    /// <summary>
    /// Field에 있는 동안만 게임 메인 카메라의 URP 후처리를 켜고, 지역 Volume 레이어(Ignore Raycast)를 volumeLayerMask에 더한다.
    /// 필드 루트가 꺼지거나 파괴되면 자신이 쓴 값이 그대로일 때만 캡처한 원래 값으로 되돌린다
    /// </summary>
    [DisallowMultipleComponent]
    public class FieldRegionalPostProcessing : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int DEFAULT_LAYER = 0;
        private const int VOLUME_LAYER = 2;

        [SerializeField] private float _retryInterval = 0.25f;
        [SerializeField] private int _maxAttempts = 40;

        // 같은 카메라를 두 인스턴스가 동시에 붙잡지 않게 한다
        private static FieldRegionalPostProcessing G_Owner;

        private Coroutine _applyRoutine;
        private Camera _camera;
        private UniversalAdditionalCameraData _data;
        private bool _addedData;
        private bool _previousPostProcessing;
        private int _previousMask;
        private int _appliedMask;
        private bool _isApplied;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            if (_applyRoutine == null)
            {
                _applyRoutine = StartCoroutine(ApplyWhenCameraReady());
            }
        }

        private void OnDisable()
        {
            if (_applyRoutine != null)
            {
                StopCoroutine(_applyRoutine);
                _applyRoutine = null;
            }

            Restore();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 씬 전환 직후 카메라가 아직 없을 수 있어 제한된 횟수만 다시 찾는다. 찾은 뒤에는 탐색하지 않는다 </summary>
        private IEnumerator ApplyWhenCameraReady()
        {
            var wait = new WaitForSecondsRealtime(_retryInterval);
            for (int attempt = 0; attempt < _maxAttempts; ++attempt)
            {
                if (TryApply())
                {
                    _applyRoutine = null;
                    yield break;
                }

                yield return wait;
            }

            _applyRoutine = null;
            Debug.LogWarning("[FieldRegionalPostProcessing] 게임 메인 카메라를 찾지 못해 지역 후처리를 켜지 않았다");
        }

        private bool TryApply()
        {
            if ((G_Owner != null) && (G_Owner != this))
            {
                return true;
            }

            Camera target = ResolveGameCamera();
            if (target == null)
            {
                return false;
            }

            _camera = target;
            _addedData = !target.TryGetComponent(out _data);
            if (_addedData)
            {
                _data = target.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            _previousPostProcessing = _data.renderPostProcessing;
            _previousMask = _data.volumeLayerMask.value;
            _appliedMask = _previousMask | (1 << DEFAULT_LAYER) | (1 << VOLUME_LAYER);
            _data.renderPostProcessing = true;
            _data.volumeLayerMask = _appliedMask;
            _isApplied = true;
            G_Owner = this;
            return true;
        }

        /// <summary> CameraRig가 붙은 게임 카메라를 우선한다. 없으면 MainCamera 태그 카메라. 렌더 텍스처 카메라는 제외한다 </summary>
        private static Camera ResolveGameCamera()
        {
            CameraRig rig = FindAnyObjectByType<CameraRig>();
            Camera camera = (rig != null) ? rig.GetComponent<Camera>() : Camera.main;
            if ((camera == null) || (camera.cameraType != CameraType.Game) || (camera.targetTexture != null))
            {
                return null;
            }

            return camera;
        }

        private void Restore()
        {
            if (!_isApplied)
            {
                return;
            }

            _isApplied = false;
            if (G_Owner == this)
            {
                G_Owner = null;
            }

            // 카메라가 먼저 파괴됐으면 되돌릴 대상이 없다
            if ((_camera == null) || (_data == null))
            {
                return;
            }

            // 다른 시스템이 그 사이 값을 바꿨다면 그 값을 존중한다
            bool postUntouched = _data.renderPostProcessing;
            bool maskUntouched = _data.volumeLayerMask.value == _appliedMask;
            if (postUntouched)
            {
                _data.renderPostProcessing = _previousPostProcessing;
            }

            if (maskUntouched)
            {
                _data.volumeLayerMask = _previousMask;
            }

            // 직접 추가한 URP 데이터도 제거하지 않는다. 다른 시스템이 다른 필드(depth·AA·stack 등)를 바꿨을 수 있어 공유 카메라에 남긴다
            _data = null;
            _camera = null;
        }
    }
}
