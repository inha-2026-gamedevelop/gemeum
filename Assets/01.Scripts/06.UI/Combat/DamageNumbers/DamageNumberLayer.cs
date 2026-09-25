// Unity
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.UI
{
    /// <summary>
    /// 내가 준 피해를 적중점 근처에 띄우는 화면 층(COMBAT_STYLE_DESIGN S10). 각자 화면의 표현이다
    /// LocalDamageResultFeed.Received(서버가 공격자에게만 보낸 확정 피해)만 소비하고 판정·피해를 다시 계산하지 않는다
    /// 씬에 배치하지 않는다. 게임 시작 때 DontDestroyOnLoad 캔버스 하나를 스스로 만든다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DamageNumberLayer : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        // 풀 한 칸의 화면 부품. 게임 중에는 만들거나 버리지 않는다
        private sealed class Label
        {
            public RectTransform   Rect;
            public CanvasGroup     Group;
            public TextMeshProUGUI Text;

            // 생성 때 한 번 정한 화면 위치. 이후 적을 따라가지 않는다
            public Vector2 Origin;
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string OBJECT_NAME = "@DamageNumbers";
        private const string ROOT_NAME   = "Root";
        private const string LABEL_NAME  = "DamageNumber";

        // HUD 캔버스(RunHUD, sortingOrder 0)보다 아래. 숫자가 HUD를 가리지 않는다
        private const int CANVAS_SORTING_ORDER = -10;

        // 설정의 px 값은 1080p 기준이다. 캔버스 단위를 1920x1080에 맞춰 해상도에 비례시킨다
        private const float REFERENCE_WIDTH  = 1920f;
        private const float REFERENCE_HEIGHT = 1080f;
        private const float MATCH_HEIGHT     = 1f;

        // 글자 상자 크기. 넘침 허용이라 배치 기준점 역할만 한다
        private const float LABEL_WIDTH  = 400f;
        private const float LABEL_HEIGHT = 80f;

        // 설정이 아직 깨어나지 않았을 때만 쓰는 값. 조정은 PresentationConfigSO에서 한다
        private const float FALLBACK_OUTLINE_WIDTH = 0.25f;
        private const float FALLBACK_FADE_START    = 0.6f;

        // 가림 결과를 기억하는 칸 수. 화면 상한(24)보다 넉넉하면 된다
        private const int OCCLUSION_CACHE_CAPACITY = 32;

        /****************************************
        *                Fields
        ****************************************/

        private static DamageNumberLayer _instance;

        private readonly char[] _textBuffer = new char[DamageNumberFormatter.BUFFER_LENGTH];

        private Canvas                _canvas;
        private RectTransform         _root;
        private DamageNumberBook      _book;
        private DamageNumberOcclusion _occlusion;
        private Label[]               _labels;
        private Camera                _camera;
        private Transform             _cameraTransform;
        private TMP_FontAsset         _font;
        private Material              _sharedMaterial;
        private string                _criticalPrefix;
        private string                _criticalSuffix;
        private bool                  _isReady = false;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
        }

        // 씬에 배치하지 않아도 게임 시작 때 한 번 돈다. 서버 전용·배치 모드에는 화면이 없으므로 만들지 않는다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
#if !UNITY_SERVER
            if (Application.isBatchMode || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null))
            {
                return;
            }

            if (_instance != null)
            {
                return;
            }

            if ((!GameDB.IsInitialized) || (GameDB.Presentation == null))
            {
                Debug.LogError("[DamageNumberLayer] GameDB.Presentation이 없어 데미지 숫자 층을 만들지 않는다");
                return;
            }

            GameObject host = new GameObject(OBJECT_NAME, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<DamageNumberLayer>();
#endif
        }

        private void Awake()
        {
            if ((_instance != null) && (_instance != this))
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            if (!Build())
            {
                enabled = false;
                return;
            }

            LocalDamageResultFeed.Received       += HandleReceived;
            LocalDamageResultFeed.Cleared        += ClearAll;
            DamageNumberSettings.EnabledChanged  += HandleEnabledChanged;
            SceneManager.sceneLoaded             += HandleSceneLoaded;

            _isReady = true;

            // 떠 있는 숫자가 있을 때만 Update를 돌린다
            enabled = false;
        }

        private void OnDestroy()
        {
            if (_isReady)
            {
                LocalDamageResultFeed.Received      -= HandleReceived;
                LocalDamageResultFeed.Cleared       -= ClearAll;
                DamageNumberSettings.EnabledChanged -= HandleEnabledChanged;
                SceneManager.sceneLoaded            -= HandleSceneLoaded;
            }

            if (_sharedMaterial != null)
            {
                Destroy(_sharedMaterial);
                _sharedMaterial = null;
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            PresentationConfigSO config = GetConfig();

            if (config == null)
            {
                ClearAll();
                return;
            }

            float now         = Time.unscaledTime;
            float lifetime    = config.DamageNumberLifetimeSeconds;
            float risePixels  = config.DamageNumberRisePixels;
            float guardRadius = config.DamageNumberCenterGuard * _root.rect.height;

            for (int i = 0; i < _labels.Length; ++i)
            {
                ref readonly DamageNumberBook.Slot slot = ref _book.Get(i);

                if (!slot.IsActive)
                {
                    continue;
                }

                float age = (now - slot.SpawnTime) / lifetime;

                if (age >= 1f)
                {
                    Hide(i);
                    _book.Release(i);
                    continue;
                }

                Label label  = _labels[i];
                Vector2 point = label.Origin;
                point.y += DamageNumberPlacement.Rise(age, risePixels);

                label.Rect.anchoredPosition = DamageNumberPlacement.PushOutOfCenter(point, guardRadius);
                label.Group.alpha           = DamageNumberPlacement.Alpha(age, ResolveFadeStart());
            }

            if (_book.ActiveCount == 0)
            {
                enabled = false;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool Build()
        {
            PresentationConfigSO config = GetConfig();

            if (config == null)
            {
                Debug.LogError("[DamageNumberLayer] GameDB.Presentation이 없다", this);
                return false;
            }

            _font = (TMP_Settings.instance != null) ? TMP_Settings.defaultFontAsset : null;

            if (_font == null)
            {
                Debug.LogError("[DamageNumberLayer] TMP Settings의 기본 글꼴이 없어 데미지 숫자를 표시하지 않는다", this);
                return false;
            }

            _canvas              = GetComponent<Canvas>();
            _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CANVAS_SORTING_ORDER;
            _canvas.pixelPerfect = false;

            CanvasScaler scaler        = GetComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight  = MATCH_HEIGHT;

            // 숫자는 이 층의 로컬 좌표로 둔다. 16:9 틀(FixedAspectViewport)로 옮겨져도 계산이 그대로 맞는다
            GameObject rootObject = new GameObject(ROOT_NAME, typeof(RectTransform));
            _root = (RectTransform)rootObject.transform;
            _root.SetParent(transform, false);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.pivot     = new Vector2(0.5f, 0.5f);
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            BuildMaterial();
            ResolveCriticalMark();

            int capacity = config.DamageNumberMaxOnScreen;
            _book      = new DamageNumberBook(capacity);
            _occlusion = new DamageNumberOcclusion(OCCLUSION_CACHE_CAPACITY);
            _labels    = new Label[capacity];

            for (int i = 0; i < capacity; ++i)
            {
                _labels[i] = CreateLabel(i);
            }

            // 런타임 루트 캔버스는 자식 구성을 마친 뒤 16:9 틀에 등록한다(FixedAspectViewport 관례)
            FixedAspectViewport.Register(_canvas);
            return true;
        }

        // 검은 외곽선을 켠 재질 하나를 모든 숫자가 공유한다. 글자마다 재질 인스턴스를 만들지 않는다
        private void BuildMaterial()
        {
            Material source = _font.material;

            if (source == null)
            {
                Debug.LogWarning("[DamageNumberLayer] 기본 글꼴에 재질이 없어 외곽선 없이 표시한다", this);
                return;
            }

            _sharedMaterial      = new Material(source);
            _sharedMaterial.name = source.name + " (DamageNumber)";

            if (!_sharedMaterial.HasProperty(ShaderUtilities.ID_OutlineWidth))
            {
                Debug.LogWarning($"[DamageNumberLayer] {source.shader.name} 셰이더에 외곽선 속성이 없다", this);
                return;
            }

            _sharedMaterial.EnableKeyword(ShaderUtilities.Keyword_Outline);
            _sharedMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth,
                GameDB.IsInitialized ? GameDB.Presentation.DamageNumberOutlineWidth : FALLBACK_OUTLINE_WIDTH);
            _sharedMaterial.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            ShaderUtilities.UpdateShaderRatios(_sharedMaterial);
        }

        // 글꼴에 이미 있는 글자만 본다. tryAddCharacter를 켜면 공유 동적 글꼴 에셋에 글자가 추가되어 에셋 파일이 바뀐다
        private void ResolveCriticalMark()
        {
            bool hasGlyph = _font.HasCharacter(DamageNumberCriticalMark.GLYPH, true, false);
            DamageNumberCriticalMark mark = DamageNumberCriticalMark.Resolve(hasGlyph);

            _criticalPrefix = mark.Prefix;
            _criticalSuffix = mark.Suffix;

            if (mark.IsFallback)
            {
                Debug.Log("[DamageNumberLayer] 기본 글꼴에 치명 표식(◆)이 아직 없어 '!'로 대신한다", this);
            }
        }

        private Label CreateLabel(int index)
        {
            GameObject labelObject = new GameObject(LABEL_NAME + index,
                typeof(RectTransform), typeof(CanvasGroup), typeof(TextMeshProUGUI));

            RectTransform rect = (RectTransform)labelObject.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot     = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(LABEL_WIDTH, LABEL_HEIGHT);

            CanvasGroup group    = labelObject.GetComponent<CanvasGroup>();
            group.alpha          = 0f;
            group.interactable   = false;
            group.blocksRaycasts = false;

            TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
            text.font             = _font;
            if (_sharedMaterial != null)
            {
                text.fontSharedMaterial = _sharedMaterial;
            }
            text.raycastTarget    = false;
            text.richText         = true;
            text.alignment        = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode     = TextOverflowModes.Overflow;
            text.enabled          = false;

            return new Label
            {
                Rect  = rect,
                Group = group,
                Text  = text,
            };
        }

        private void HandleReceived(DamageResult result)
        {
            if ((!DamageNumberSettings.Enabled) || (!DamageNumberFormatter.IsDisplayable(result.Amount)))
            {
                return;
            }

            PresentationConfigSO config = GetConfig();

            if (config == null)
            {
                return;
            }

            float now       = Time.unscaledTime;
            bool  canSpawn  = TryProject(result.HitPoint, out Vector2 origin) && (!IsOccluded(in result, now, config));
            ImpactTier tier = ImpactTierResolver.Resolve(result);
            DamageNumberRules rules = DamageNumberRules.From(config);

            DamageNumberAction action = _book.Add(result, tier, now, rules, canSpawn, out int index);

            switch (action)
            {
                case DamageNumberAction.Merged:
                    RefreshText(index);
                    RefreshScale(index, config);
                    break;

                case DamageNumberAction.Spawned:
                case DamageNumberAction.Replaced:
                    Show(index, origin, config);
                    break;

                default:
                    return;
            }

            enabled = true;
        }

        // 적중점을 생성 시 한 번만 화면에 투영한다. 카메라 뒤·게임 화면 밖이면 새 숫자를 만들지 않는다(지형 가림은 IsOccluded)
        private bool TryProject(Vector3 worldPoint, out Vector2 localPoint)
        {
            localPoint = Vector2.zero;

            if ((_camera == null) || (!_camera.isActiveAndEnabled))
            {
                _camera          = Camera.main;
                _cameraTransform = (_camera != null) ? _camera.transform : null;
            }

            if (_camera == null)
            {
                return false;
            }

            Vector3 screenPoint = _camera.WorldToScreenPoint(worldPoint);

            if (screenPoint.z <= 0f)
            {
                return false;
            }

            if (!_camera.pixelRect.Contains(new Vector2(screenPoint.x, screenPoint.y)))
            {
                return false;
            }

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screenPoint, null, out localPoint);
        }

        /// <summary>
        /// 카메라와 적중점 사이를 지형·바위가 막는지. 투영이 통과한 뒤 생성 시 한 번만 본다(매 프레임 아님)
        /// 같은 대상(부위 포함)의 최근 결과를 다시 쓰고, 프레임 예산을 넘으면 검사 없이 보인다고 본다
        /// </summary>
        private bool IsOccluded(in DamageResult result, float now, PresentationConfigSO config)
        {
            float reuseSeconds = config.DamageNumberOcclusionReuseSeconds;

            if (_occlusion.TryGet(result.TargetNetId, result.PartIndex, now, reuseSeconds, out bool cached))
            {
                return cached;
            }

            if (!_occlusion.TryConsumeBudget(Time.frameCount, config.DamageNumberOcclusionChecksPerFrame))
            {
                return false;
            }

            Vector3 from = _cameraTransform.position;
            bool isOccluded = false;

            if (DamageNumberOcclusion.TryResolveSegmentEnd(from, result.HitPoint,
                config.DamageNumberOcclusionEndInset, out Vector3 end))
            {
                isOccluded = Physics.Linecast(from, end, config.DamageNumberOcclusionMask,
                    QueryTriggerInteraction.Ignore);
            }

            _occlusion.Store(result.TargetNetId, result.PartIndex, now, isOccluded);
            return isOccluded;
        }

        private void Show(int index, Vector2 origin, PresentationConfigSO config)
        {
            ref readonly DamageNumberBook.Slot slot = ref _book.Get(index);
            Label label = _labels[index];

            float spread = config.DamageNumberSpreadPixels;
            origin.x    += Random.Range(-spread, spread);
            label.Origin = origin;

            float guardRadius = config.DamageNumberCenterGuard * _root.rect.height;
            label.Rect.anchoredPosition = DamageNumberPlacement.PushOutOfCenter(origin, guardRadius);

            // 새 숫자가 위에 그려지게 한다
            label.Rect.SetAsLastSibling();

            label.Text.fontSize = config.DamageNumberFontSize;
            label.Text.color    = (slot.Key.IsCritical) ? config.DamageNumberCriticalColor : config.DamageNumberNormalColor;

            RefreshText(index);
            RefreshScale(index, config);

            label.Group.alpha  = 1f;
            label.Text.enabled = true;
        }

        // 값이 바뀔 때만 글자를 다시 쓴다. 고정 버퍼라 문자열을 만들지 않는다
        private void RefreshText(int index)
        {
            ref readonly DamageNumberBook.Slot slot = ref _book.Get(index);

            string prefix = (slot.Key.IsCritical) ? _criticalPrefix : null;
            string suffix = (slot.Key.IsCritical) ? _criticalSuffix : null;
            int length    = DamageNumberFormatter.Write(slot.Amount, prefix, suffix, _textBuffer);

            _labels[index].Text.SetText(_textBuffer, 0, length);
        }

        private void RefreshScale(int index, PresentationConfigSO config)
        {
            ref readonly DamageNumberBook.Slot slot = ref _book.Get(index);

            float scale = DamageNumberRules.ResolveScale(slot.Key.IsCritical, slot.Tier,
                config.DamageNumberCriticalScale, config.DamageNumberTierScaleMax);

            _labels[index].Rect.localScale = new Vector3(scale, scale, 1f);
        }

        private void Hide(int index)
        {
            Label label = _labels[index];
            label.Group.alpha  = 0f;
            label.Text.enabled = false;
        }

        private void ClearAll()
        {
            if (_book == null)
            {
                return;
            }

            _book.Clear();
            _occlusion.Clear();

            for (int i = 0; i < _labels.Length; ++i)
            {
                Hide(i);
            }

            enabled = false;
        }

        private void HandleEnabledChanged(bool isEnabled)
        {
            if (!isEnabled)
            {
                ClearAll();
            }
        }

        // 씬 전환이면 이전 씬의 숫자를 즉시 거둔다. 추가 로드(Additive)는 같은 판이므로 두고 본다
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                ClearAll();
            }
        }

        private static PresentationConfigSO GetConfig()
        {
            return GameDB.IsInitialized ? GameDB.Presentation : null;
        }

        private static float ResolveFadeStart()
        {
            return GameDB.IsInitialized ? GameDB.Presentation.DamageNumberFadeStart : FALLBACK_FADE_START;
        }
    }
}
