// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 프로젝트 내부
using Geumeum.AI;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.UI
{
    /// <summary>
    /// 맞은 잡귀 머리 위에 체력 바를 잠깐 띄우는 화면 층. 각자 화면의 표현이다
    /// 서버가 확정해 동기화한 체력(EnemyStatus.Health, HealthDecreased)만 보므로 누가 때렸든 세 화면에 같은 바가 뜬다
    /// 잡귀는 LocalEnemyPresentationRegistry로 찾는다. 보스는 등록되지 않아 보스 HUD와 겹치지 않는다
    /// 씬에 배치하지 않는다. 게임 시작 때 DontDestroyOnLoad 캔버스 하나를 스스로 만든다(DamageNumberLayer와 같은 방식)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyHealthBarLayer : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        // 등록된 잡귀 하나. 스폰·디스폰 때만 만들거나 풀로 돌린다
        private sealed class Target
        {
            /// <summary> HealthDecreased에 거는 처리기. 대상 객체마다 한 번만 만든다 </summary>
            public readonly Action Handler;

            public EnemyHealthBarLayer        Owner;
            public IDamagePresentationSource  Source;
            public EnemyStatus                Status;
            public Transform                  Transform;
            public int                        Id;

            /// <summary> 이 화면이 본 가장 높은 체력. 서버 최대 체력을 모르는 원격 화면의 비율 분모 </summary>
            public float PeakHealth;

            /// <summary> 직전에 본 체력. 새 바의 잔상 시작점 </summary>
            public float LastHealth;

            /// <summary> 발밑에서 바까지의 높이(m). 처음 맞을 때 한 번 잰다 </summary>
            public float HeadHeight;
            public bool  HasHeadHeight;

            /// <summary> 떠 있는 바의 칸. 없으면 NO_SLOT </summary>
            public int Slot;

            public Target()
            {
                Handler = HandleHealthDecreased;
                Slot    = NO_SLOT;
            }

            private void HandleHealthDecreased()
            {
                if (Owner != null)
                {
                    Owner.HandleHealthDecreased(this);
                }
            }
        }

        // 풀 한 칸의 화면 부품. 게임 중에는 만들거나 버리지 않는다
        private sealed class Bar
        {
            public GameObject    Root;
            public RectTransform Rect;
            public CanvasGroup   Group;
            public Image         Background;
            public RectTransform Track;
            public RectTransform ChipRect;
            public Image         Chip;
            public RectTransform FillRect;
            public Image         Fill;

            public EnemyHealthBarTrail Trail;
            public Target              Target;
            public float               LastHitTime;

            // 같은 값을 다시 쓰지 않도록 마지막으로 화면에 옮긴 값을 기억한다
            public float AppliedHealth;
            public float AppliedChip;
            public float AppliedScale;
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string OBJECT_NAME = "@EnemyHealthBars";
        private const string ROOT_NAME   = "Root";
        private const string BAR_NAME    = "EnemyHealthBar";
        private const string TRACK_NAME  = "Track";
        private const string CHIP_NAME   = "Chip";
        private const string FILL_NAME   = "Fill";

        // 피격 섬광(-20)보다 위, 데미지 숫자(-10)·HUD(0)보다 아래
        private const int CANVAS_SORTING_ORDER = -15;

        // 설정의 px 값은 1080p 기준이다. 캔버스 단위를 1920x1080에 맞춰 해상도에 비례시킨다
        private const float REFERENCE_WIDTH  = 1920f;
        private const float REFERENCE_HEIGHT = 1080f;
        private const float MATCH_HEIGHT     = 1f;

        private const int NO_SLOT = -1;

        // 등록 목록의 처음 크기. 한 판에 살아 있는 잡귀 수보다 넉넉하면 된다
        private const int TARGET_CAPACITY = 64;

        // 아직 화면에 옮긴 적 없다는 표시. 비율·배율은 0 이상이라 음수와 겹치지 않는다
        private const float UNAPPLIED = -1f;

        /****************************************
        *                Fields
        ****************************************/

        private static EnemyHealthBarLayer _instance;

        private readonly List<Target>  _targets    = new List<Target>(TARGET_CAPACITY);
        private readonly Stack<Target> _targetPool = new Stack<Target>(TARGET_CAPACITY);

        private Canvas             _canvas;
        private RectTransform      _root;
        private EnemyHealthBarBook _book;
        private Bar[]              _bars;
        private Camera             _camera;
        private int                _nextTargetId = 0;
        private bool               _isReady      = false;

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
                Debug.LogError("[EnemyHealthBarLayer] GameDB.Presentation이 없어 잡귀 체력 바 층을 만들지 않는다");
                return;
            }

            GameObject host = new GameObject(OBJECT_NAME, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<EnemyHealthBarLayer>();
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

            LocalEnemyPresentationRegistry.Registered   += HandleRegistered;
            LocalEnemyPresentationRegistry.Unregistered += HandleUnregistered;
            LocalDamageResultFeed.Cleared               += ClearAll;
            EnemyHealthBarSettings.EnabledChanged       += HandleEnabledChanged;
            SceneManager.sceneLoaded                    += HandleSceneLoaded;

            _isReady = true;

            // 이 층보다 먼저 스폰된 잡귀를 따라잡는다
            IReadOnlyList<IDamagePresentationSource> sources = LocalEnemyPresentationRegistry.Sources;
            for (int i = 0; i < sources.Count; ++i)
            {
                HandleRegistered(sources[i]);
            }

            // 떠 있는 바가 있을 때만 Update를 돌린다
            enabled = false;
        }

        private void OnDestroy()
        {
            if (_isReady)
            {
                LocalEnemyPresentationRegistry.Registered   -= HandleRegistered;
                LocalEnemyPresentationRegistry.Unregistered -= HandleUnregistered;
                LocalDamageResultFeed.Cleared               -= ClearAll;
                EnemyHealthBarSettings.EnabledChanged       -= HandleEnabledChanged;
                SceneManager.sceneLoaded                    -= HandleSceneLoaded;

                // 바 오브젝트는 이 층과 함께 사라지므로 잡귀 알림 구독만 끊는다
                for (int i = 0; i < _targets.Count; ++i)
                {
                    Target target = _targets[i];

                    if (target.Source != null)
                    {
                        target.Source.HealthDecreased -= target.Handler;
                    }

                    target.Owner = null;
                }

                _targets.Clear();
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            if (_book.ActiveCount == 0)
            {
                enabled = false;
                return;
            }

            PresentationConfigSO config = GetConfig();

            if (config == null)
            {
                ClearAll();
                return;
            }

            float now          = Time.unscaledTime;
            float deltaSeconds = Time.unscaledDeltaTime;
            float holdSeconds  = config.EnemyHealthBarChipDelaySeconds;
            float drain        = config.EnemyHealthBarChipDrainPerSecond;
            float visible      = config.EnemyHealthBarVisibleSeconds;
            float fade         = config.EnemyHealthBarFadeSeconds;
            bool  hasCamera    = ResolveCamera();

            for (int i = 0; i < _bars.Length; ++i)
            {
                if (!_book.Get(i).IsActive)
                {
                    continue;
                }

                Bar    bar    = _bars[i];
                Target target = bar.Target;

                if ((target == null) || (!IsPresent(target)))
                {
                    ReleaseSlot(i);
                    continue;
                }

                float health = target.Status.Health;

                if (health <= Constants.Combat.HEALTH_EPSILON)
                {
                    ReleaseSlot(i);
                    continue;
                }

                if (health > target.PeakHealth)
                {
                    target.PeakHealth = health;
                }

                target.LastHealth = health;

                float ratio = EnemyHealthBarRules.Ratio(health, ResolveMaxHealth(target));

                // 알림보다 값이 먼저 보인 경우에도 새 피격으로 친다
                if (ratio < bar.Trail.Health)
                {
                    bar.LastHitTime = now;
                }

                bar.Trail.SetRatio(ratio, now, holdSeconds);
                bar.Trail.Tick(now, deltaSeconds, drain);

                float alpha = EnemyHealthBarRules.Alpha(now, bar.LastHitTime, visible, fade);

                if (alpha <= 0f)
                {
                    ReleaseSlot(i);
                    continue;
                }

                Place(bar, target, alpha, hasCamera, config);
                ApplyFill(bar);
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
                Debug.LogError("[EnemyHealthBarLayer] GameDB.Presentation이 없다", this);
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

            // 바는 이 층의 로컬 좌표로 둔다. 16:9 틀(FixedAspectViewport)로 옮겨져도 계산이 그대로 맞는다
            GameObject rootObject = new GameObject(ROOT_NAME, typeof(RectTransform));
            _root = (RectTransform)rootObject.transform;
            _root.SetParent(transform, false);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.pivot     = new Vector2(0.5f, 0.5f);
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            int capacity = config.EnemyHealthBarMaxOnScreen;
            _book = new EnemyHealthBarBook(capacity);
            _bars = new Bar[capacity];

            for (int i = 0; i < capacity; ++i)
            {
                _bars[i] = CreateBar(i);
            }

            // 런타임 루트 캔버스는 자식 구성을 마친 뒤 16:9 틀에 등록한다(FixedAspectViewport 관례)
            FixedAspectViewport.Register(_canvas);
            return true;
        }

        // 배경·잔상·체력 세 장. 스프라이트 없이 흰 사각형에 색만 입힌다
        private Bar CreateBar(int index)
        {
            GameObject barObject = new GameObject(BAR_NAME + index,
                typeof(RectTransform), typeof(CanvasGroup), typeof(Image));

            RectTransform rect = (RectTransform)barObject.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);

            // 아래 가운데가 머리 위 기준점에 온다
            rect.pivot = new Vector2(0.5f, 0f);

            CanvasGroup group    = barObject.GetComponent<CanvasGroup>();
            group.alpha          = 0f;
            group.interactable   = false;
            group.blocksRaycasts = false;

            Image background         = barObject.GetComponent<Image>();
            background.raycastTarget = false;

            GameObject trackObject = new GameObject(TRACK_NAME, typeof(RectTransform));
            RectTransform track    = (RectTransform)trackObject.transform;
            track.SetParent(rect, false);
            track.anchorMin = Vector2.zero;
            track.anchorMax = Vector2.one;
            track.pivot     = new Vector2(0f, 0.5f);

            // 잔상을 먼저 만들어 체력 막대가 그 위에 그려지게 한다
            Image chip = CreateFill(CHIP_NAME, track);
            Image fill = CreateFill(FILL_NAME, track);

            barObject.SetActive(false);

            return new Bar
            {
                Root          = barObject,
                Rect          = rect,
                Group         = group,
                Background    = background,
                Track         = track,
                ChipRect      = chip.rectTransform,
                Chip          = chip,
                FillRect      = fill.rectTransform,
                Fill          = fill,
                AppliedHealth = UNAPPLIED,
                AppliedChip   = UNAPPLIED,
                AppliedScale  = UNAPPLIED,
            };
        }

        // 왼쪽에 붙어 오른쪽 끝(anchorMax.x)이 비율을 따라가는 막대
        private static Image CreateFill(string name, RectTransform parent)
        {
            GameObject fillObject = new GameObject(name, typeof(RectTransform), typeof(Image));

            RectTransform rect = (RectTransform)fillObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot     = new Vector2(0f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image         = fillObject.GetComponent<Image>();
            image.raycastTarget = false;

            return image;
        }

        private void HandleRegistered(IDamagePresentationSource source)
        {
            EnemyStatus status = source as EnemyStatus;

            if (status == null)
            {
                Debug.LogWarning($"[EnemyHealthBarLayer] 잡귀 목록에 EnemyStatus가 아닌 소스가 들어와 무시한다: {source}", this);
                return;
            }

            if (FindTarget(source) >= 0)
            {
                return;
            }

            Target target = (_targetPool.Count > 0) ? _targetPool.Pop() : new Target();

            target.Owner         = this;
            target.Source        = source;
            target.Status        = status;
            target.Transform     = status.transform;
            target.Id            = ++_nextTargetId;
            target.PeakHealth    = status.Health;
            target.LastHealth    = status.Health;
            target.HeadHeight    = 0f;
            target.HasHeadHeight = false;
            target.Slot          = NO_SLOT;

            source.HealthDecreased += target.Handler;
            _targets.Add(target);
        }

        private void HandleUnregistered(IDamagePresentationSource source)
        {
            int index = FindTarget(source);

            if (index >= 0)
            {
                RemoveTargetAt(index);
            }
        }

        private int FindTarget(IDamagePresentationSource source)
        {
            for (int i = 0; i < _targets.Count; ++i)
            {
                if (_targets[i].Source == source)
                {
                    return i;
                }
            }

            return -1;
        }

        // 순서는 의미가 없으므로 마지막 칸을 끌어와 지운다
        private void RemoveTargetAt(int index)
        {
            Target target = _targets[index];

            if (target.Source != null)
            {
                target.Source.HealthDecreased -= target.Handler;
            }

            ReleaseTarget(target);

            int last = _targets.Count - 1;
            _targets[index] = _targets[last];
            _targets.RemoveAt(last);

            target.Owner     = null;
            target.Source    = null;
            target.Status    = null;
            target.Transform = null;
            _targetPool.Push(target);
        }

        // 동기화된 체력이 줄었다. 누가 때렸든 모든 화면에서 같은 값으로 들어온다
        private void HandleHealthDecreased(Target target)
        {
            if (!IsPresent(target))
            {
                ReleaseTarget(target);
                return;
            }

            float health   = target.Status.Health;
            float previous = target.LastHealth;
            target.LastHealth = health;

            // 처치는 바를 남기지 않는다. 몸이 곧 사라진다
            if (health <= Constants.Combat.HEALTH_EPSILON)
            {
                ReleaseTarget(target);
                return;
            }

            if (!EnemyHealthBarSettings.Enabled)
            {
                return;
            }

            PresentationConfigSO config = GetConfig();

            if (config == null)
            {
                return;
            }

            float maxHealth = ResolveMaxHealth(target);

            if (maxHealth <= 0f)
            {
                return;
            }

            float now         = Time.unscaledTime;
            float ratio       = EnemyHealthBarRules.Ratio(health, maxHealth);
            float holdSeconds = config.EnemyHealthBarChipDelaySeconds;

            if (target.Slot != NO_SLOT)
            {
                Bar current = _bars[target.Slot];
                current.Trail.SetRatio(ratio, now, holdSeconds);
                current.LastHitTime = now;
                _book.Touch(target.Slot);
                enabled = true;
                return;
            }

            EnemyHealthBarAction action = _book.Acquire(target.Id, out int index);
            Bar bar = _bars[index];

            // 상한이라 넘겨받은 칸이면 이전 대상의 연결을 끊는다
            if ((action == EnemyHealthBarAction.Replaced) && (bar.Target != null) && (bar.Target != target))
            {
                bar.Target.Slot = NO_SLOT;
            }

            bar.Target  = target;
            target.Slot = index;

            EnsureHeadHeight(target, config);

            // 직전 체력에서 잔상을 시작해 이번에 깎인 만큼이 밝게 남는다
            bar.Trail       = EnemyHealthBarTrail.Create(EnemyHealthBarRules.Ratio(previous, maxHealth));
            bar.LastHitTime = now;
            bar.Trail.SetRatio(ratio, now, holdSeconds);

            Show(bar, target, config);
            enabled = true;
        }

        private void Show(Bar bar, Target target, PresentationConfigSO config)
        {
            float border = config.EnemyHealthBarBorder;

            bar.Rect.sizeDelta   = new Vector2(config.EnemyHealthBarWidth, config.EnemyHealthBarHeight);
            bar.Track.offsetMin  = new Vector2(border, border);
            bar.Track.offsetMax  = new Vector2(-border, -border);
            bar.Background.color = config.EnemyHealthBarBackgroundColor;
            bar.Chip.color       = config.EnemyHealthBarChipColor;
            bar.Fill.color       = config.EnemyHealthBarFillColor;

            bar.AppliedHealth = UNAPPLIED;
            bar.AppliedChip   = UNAPPLIED;
            bar.AppliedScale  = UNAPPLIED;

            // 새로 맞은 바가 위에 그려지게 한다
            bar.Rect.SetAsLastSibling();
            bar.Root.SetActive(true);

            Place(bar, target, 1f, ResolveCamera(), config);
            ApplyFill(bar);
        }

        // 머리 위 점을 매 프레임 화면에 투영한다. 카메라 뒤·게임 화면 밖이면 시간은 흐르되 보이지 않게 둔다
        private void Place(Bar bar, Target target, float alpha, bool hasCamera, PresentationConfigSO config)
        {
            if (!hasCamera)
            {
                bar.Group.alpha = 0f;
                return;
            }

            Vector3 worldPoint = target.Transform.position;
            worldPoint.y += target.HeadHeight;

            Vector3 screenPoint = _camera.WorldToScreenPoint(worldPoint);

            if ((screenPoint.z <= 0f) ||
                (!_camera.pixelRect.Contains(new Vector2(screenPoint.x, screenPoint.y))) ||
                (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screenPoint, null, out Vector2 localPoint)))
            {
                bar.Group.alpha = 0f;
                return;
            }

            bar.Rect.anchoredPosition = localPoint;

            float scale = EnemyHealthBarRules.DistanceScale(screenPoint.z,
                config.EnemyHealthBarScaleReferenceDistance, config.EnemyHealthBarMinScale);

            if (scale != bar.AppliedScale)
            {
                bar.AppliedScale    = scale;
                bar.Rect.localScale = new Vector3(scale, scale, 1f);
            }

            bar.Group.alpha = alpha;
        }

        // 값이 바뀐 막대만 오른쪽 끝을 옮긴다
        private static void ApplyFill(Bar bar)
        {
            float health = bar.Trail.Health;
            float chip   = bar.Trail.Chip;

            if (health != bar.AppliedHealth)
            {
                bar.AppliedHealth      = health;
                bar.FillRect.anchorMax = new Vector2(health, 1f);
            }

            if (chip != bar.AppliedChip)
            {
                bar.AppliedChip        = chip;
                bar.ChipRect.anchorMax = new Vector2(chip, 1f);
            }
        }

        // 몸 콜라이더(없으면 렌더러) 윗면 + 여유. 처음 맞을 때 한 번만 찾는다
        private static void EnsureHeadHeight(Target target, PresentationConfigSO config)
        {
            if (target.HasHeadHeight)
            {
                return;
            }

            target.HasHeadHeight = true;

            float top   = float.NaN;
            float baseY = target.Transform.position.y;

            if (target.Status.TryGetComponent(out Collider body) && body.enabled)
            {
                top = body.bounds.max.y;
            }
            else
            {
                Renderer renderer = target.Status.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    top = renderer.bounds.max.y;
                }
            }

            float height = top - baseY;

            target.HeadHeight = (float.IsNaN(height) || (height <= 0f))
                ? config.EnemyHealthBarFallbackHeight
                : height + config.EnemyHealthBarHeadMargin;
        }

        private static float ResolveMaxHealth(Target target)
        {
            // 호스트는 서버 값을 안다. 원격 화면은 0이라 본 가장 높은 체력(스폰 때 가득 찬 값)을 쓴다
            return EnemyHealthBarRules.ReferenceMax(target.Status.MaxHealth, target.PeakHealth);
        }

        private static bool IsPresent(Target target)
        {
            return (target.Status != null) && target.Status.isActiveAndEnabled;
        }

        private bool ResolveCamera()
        {
            if ((_camera == null) || (!_camera.isActiveAndEnabled))
            {
                _camera = Camera.main;
            }

            return (_camera != null);
        }

        private void ReleaseTarget(Target target)
        {
            if (target.Slot != NO_SLOT)
            {
                ReleaseSlot(target.Slot);
            }
        }

        private void ReleaseSlot(int index)
        {
            Bar bar = _bars[index];

            if (bar.Target != null)
            {
                bar.Target.Slot = NO_SLOT;
                bar.Target      = null;
            }

            bar.Group.alpha = 0f;
            bar.Root.SetActive(false);
            _book.Release(index);
        }

        private void ClearAll()
        {
            if (_book == null)
            {
                return;
            }

            for (int i = 0; i < _bars.Length; ++i)
            {
                if (_book.Get(i).IsActive)
                {
                    ReleaseSlot(i);
                }
            }

            _book.Clear();
            enabled = false;
        }

        private void HandleEnabledChanged(bool isEnabled)
        {
            if (!isEnabled)
            {
                ClearAll();
            }
        }

        // 씬 전환이면 이전 씬의 바를 즉시 거두고, 정리 알림 없이 사라진 잡귀를 목록에서 뺀다. 추가 로드는 같은 판이므로 두고 본다
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
            {
                return;
            }

            ClearAll();

            for (int i = _targets.Count - 1; i >= 0; --i)
            {
                if (_targets[i].Status == null)
                {
                    RemoveTargetAt(i);
                }
            }
        }

        private static PresentationConfigSO GetConfig()
        {
            return GameDB.IsInitialized ? GameDB.Presentation : null;
        }
    }
}
