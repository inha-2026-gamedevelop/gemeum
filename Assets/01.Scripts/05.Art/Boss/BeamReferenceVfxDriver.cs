using UnityEngine;
using UnityEngine.VFX;

namespace Geumeum.Art
{
    /// <summary>붉은 흑심 빔의 재생 단계. 바깥에서 현재 단계를 읽을 수 있다.</summary>
    public enum BeamPhase
    {
        Idle = 0,
        Charging = 1,
        Firing = 2,
        Cancelling = 3
    }

    /// <summary>Beam_Reference.vfx에 충전·발사·종료를 보내는 표현 전용 구동기.
    /// 시간축은 전부 이 스크립트가 소유하고 그래프에는 프레임마다 형태·밝기만 넣는다.
    /// 지속시간·길이·반경·색은 인스펙터와 런타임 주입 양쪽으로 바꿀 수 있고 바깥값이 항상 이긴다.
    /// 판정·피해·사거리·네트워크는 다루지 않는다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VisualEffect))]
    public sealed class BeamReferenceVfxDriver : MonoBehaviour
    {
        [Header("재생")]
        [Tooltip("활성화될 때 충전부터 전체 타임라인을 한 번 재생한다. 게임 연결에서는 끈다")]
        [SerializeField] private bool _playOnEnable = true;
        [Tooltip("잔재가 사라질 때까지 기다리는 여유 시간(초)")]
        [SerializeField, Min(0f)] private float _tailDuration = 0.62f;

        [Header("지속시간(초)")]
        [Tooltip("ChargeStart부터 Fire까지. 참조 실측 1.80-0.733")]
        [SerializeField, Min(0.01f)] private float _chargeDuration = 1.067f;
        [Tooltip("ChargeStart부터 충전 정점 섬광 이벤트까지. 참조 1.50초에 섬광이 떠 있도록 스폰 지연을 앞당김")]
        [SerializeField, Min(0f)] private float _chargePeakTime = 0.715f;
        [Tooltip("빔이 끝점까지 뻗는 시간. 참조 실측 1.833-1.967")]
        [SerializeField, Min(0.01f)] private float _revealDuration = 0.167f;
        [Tooltip("드러남 이후 최대 상태 유지 시간. 참조 실측 1.967-2.70")]
        [SerializeField, Min(0.01f)] private float _fireDuration = 0.733f;
        [Tooltip("수축·소멸 시간. 참조 실측 2.70-3.10")]
        [SerializeField, Min(0.01f)] private float _fadeDuration = 0.40f;
        [Tooltip("Cancel이 들어왔을 때 기여분을 지우는 시간")]
        [SerializeField, Min(0.01f)] private float _cancelDuration = 0.12f;

        [Header("형태(m)")]
        [Tooltip("시작점에서 끝점까지의 길이. SetEndpoints로 주입하면 덮어쓴다")]
        [SerializeField, Min(0.5f)] private float _beamLength = 16f;
        [Tooltip("흑심 최대 반경")]
        [SerializeField, Min(0.01f)] private float _beamRadius = 0.50f;
        [Tooltip("흑심 반경에 곱하는 붉은 외피 반경 배수")]
        [SerializeField, Min(1f)] private float _sheathRadiusScale = 3.8f;
        [Tooltip("흑심 길이 대비 외피 길이 배수. 끝단에서 외피가 흑심보다 조금 더 나간다")]
        [SerializeField, Min(0.5f)] private float _sheathLengthScale = 1.03f;
        [Tooltip("시작점 구체 반경")]
        [SerializeField, Min(0.01f)] private float _orbRadius = 0.85f;
        [Tooltip("충전 구체 최대 반경")]
        [SerializeField, Min(0.01f)] private float _chargeOrbRadius = 1.20f;

        [Header("색·밝기")]
        [Tooltip("흑심 불투명도. 1이면 배경을 완전히 가린다")]
        [SerializeField, Range(0f, 1f)] private float _coreOpacity = 0.97f;
        [Tooltip("붉은 외피 기본색. HDR 선형값이라 R만 크게 두면 중심이 백색으로 블룸한다")]
        [SerializeField, ColorUsage(false, true)] private Color _edgeColor = new Color(1f, 0.10f, 0.085f);
        [SerializeField, Min(0f)] private float _edgeIntensity = 4.5f;
        [Tooltip("빔 몸통을 감싸는 검붉은 외피 색(HDR 선형). 참조 실측: 축 위 (59,2,5)/255 sRGB, 바닥보다 어둡다")]
        [SerializeField, ColorUsage(false, true)] private Color _sheathColor = new Color(2.2f, 0.02f, 0.22f);
        [Tooltip("구체 내부 밝기. 참조 충전 구체는 중심이 어둡고(0), 발사 뒤 시작점 구체는 내부가 붉게 찬다")]
        [SerializeField, Min(0f)] private float _chargeOrbInner = 0f;
        [SerializeField, Min(0f)] private float _fireOrbInner = 0.38f;
        [Tooltip("구체 둘레 붉은 후광의 지름 배수(구체 지름 대비)와 충전 중 세기. 참조 1.0~1.4초는 후광이 크다")]
        [SerializeField, Min(0f)] private float _glowScale = 2.7f;
        [SerializeField, Min(0f)] private float _chargeGlowIntensity = 1.8f;
        [Tooltip("충전 정점 섬광과 점화 섬광의 수명(초). 참조: 정점 1.50~1.60, 점화 1.80~1.87")]
        [SerializeField, Min(0.01f)] private float _peakFlashLifetime = 0.24f;
        [SerializeField, Min(0.01f)] private float _fireFlashLifetime = 0.07f;
        [Tooltip("연무 알파. 어두운 배경에서 실루엣을 만드는 어두운 요소")]
        [SerializeField, Range(0f, 1f)] private float _smokeOpacity = 0.42f;

        [Header("2차 입자")]
        [SerializeField, Min(0f)] private float _sparkRate = 150f;
        [SerializeField, Min(0f)] private float _wispRate = 11f;
        [Tooltip("유지 중 끝단 터짐을 다시 보내는 간격(초)")]
        [SerializeField, Min(0.02f)] private float _endBurstInterval = 0.10f;
        [SerializeField] private uint _seed = 20260923;

        [Header("크기 비율")]
        [Tooltip("충전 정점 섬광 지름 = 충전 구체 반경 x 이 값. 참조 1.50초 백색 구체 지름 약 350px")]
        [SerializeField, Min(0f)] private float _peakFlashScale = 7.0f;
        [Tooltip("점화 섬광 지름 = 시작 구체 반경 x 이 값. 참조 1.80초")]
        [SerializeField, Min(0f)] private float _fireFlashScale = 10f;
        [Tooltip("충전·유지 중 궤도 광륜 반경 = 구체 반경 x 이 값")]
        [SerializeField, Min(0f)] private float _chargeArcScale = 1.1f;
        [SerializeField, Min(0f)] private float _fireArcScale = 1.55f;
        [Tooltip("끝단 검은 덩어리 크기 = 흑심 반경 x 이 값")]
        [SerializeField, Min(0f)] private float _endInkScale = 3.4f;
        [Tooltip("점화 화염 혀 크기(m). 참조 1.9초 혀 길이 약 200px")]
        [SerializeField, Min(0.01f)] private float _ignitionFlameSize = 2.8f;
        [Tooltip("끝단 붉은 파편 크기(m)")]
        [SerializeField, Min(0.01f)] private float _tipSparkSize = 1.4f;
        [Tooltip("끝단 터짐 파편의 기본 속도(로컬 m/s)")]
        [SerializeField] private Vector3 _endBurstVelocity = new Vector3(0f, 1.2f, 1.5f);

        [Header("검은 잉크 획")]
        [Tooltip("충전 구체가 무너져 검은 획을 남기는 충전 진행도(0~1). 참조 1.567초 = 0.78")]
        [SerializeField, Range(0f, 1f)] private float _chargeInkProgress = 0.78f;
        [Tooltip("충전 붕괴 때 구체 자리에 남는 검은 곡선 획 수와 크기(m). 참조 1.57~1.77초")]
        [SerializeField, Min(0)] private int _chargeInkCount = 4;
        [SerializeField, Min(0.01f)] private float _chargeInkSize = 1.6f;
        [Tooltip("구체가 검은 초승달 획으로 무너지는 빔 곡선 진행도. 참조 2.767초 = 0.78")]
        [SerializeField, Range(0f, 1f)] private float _dissolveInkProgress = 0.73f;
        [SerializeField, Min(0)] private int _dissolveInkCount = 8;
        [SerializeField, Min(0.01f)] private float _dissolveInkSize = 1.3f;
        [Tooltip("잉크 획 수명(초): 끝단 덩어리 / 충전 잔재 / 소멸 초승달. 참조 초승달은 2.77~2.97초")]
        [SerializeField, Min(0.02f)] private float _endInkLifetime = 0.45f;
        [SerializeField, Min(0.02f)] private float _chargeInkLifetime = 0.34f;
        [SerializeField, Min(0.02f)] private float _dissolveInkLifetime = 0.34f;
        [Tooltip("점화 화염 혀의 기본 방향 속도(로컬 m/s). 참조는 구체 위와 뒤쪽으로 뻗는다")]
        [SerializeField] private Vector3 _ignitionFlameVelocity = new Vector3(-3.5f, 4.5f, -3.5f);

        [Header("충전 곡선 — x는 ChargeStart부터 Fire까지를 0~1로 정규화")]
        [SerializeField]
        private AnimationCurve _chargeOrbSize = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.12f, 0.26f), new Keyframe(0.25f, 0.62f),
            new Keyframe(0.45f, 0.86f), new Keyframe(0.64f, 0.97f), new Keyframe(0.70f, 1f),
            new Keyframe(0.76f, 0.58f), new Keyframe(0.82f, 0.16f), new Keyframe(0.88f, 0f),
            new Keyframe(1f, 0f));

        [Tooltip("충전 중 붉은 후광. 구체가 무너진 뒤(참조 1.63~1.77초)에도 시전자 주변이 붉게 남는다")]
        [SerializeField]
        private AnimationCurve _chargeGlowAlpha = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.14f, 0f), new Keyframe(0.25f, 0.8f), new Keyframe(0.70f, 1f),
            new Keyframe(0.80f, 0.40f), new Keyframe(0.88f, 0.10f), new Keyframe(1f, 0f));

        [SerializeField]
        private AnimationCurve _chargeOrbAlpha = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.11f, 0f), new Keyframe(0.20f, 0.85f),
            new Keyframe(0.68f, 1f), new Keyframe(0.76f, 0.80f), new Keyframe(0.86f, 0f),
            new Keyframe(1f, 0f));

        [Header("빔 곡선 — x는 드러남 0~0.15, 유지 0.15~0.75, 소멸 0.75~1")]
        [SerializeField]
        private AnimationCurve _coreLength = new AnimationCurve(
            new Keyframe(0f, 0.04f), new Keyframe(0.07f, 0.62f), new Keyframe(0.15f, 1f),
            new Keyframe(1f, 1f));

        [SerializeField]
        private AnimationCurve _coreWidth = new AnimationCurve(
            new Keyframe(0f, 0.45f), new Keyframe(0.09f, 1.18f), new Keyframe(0.20f, 1f),
            new Keyframe(0.50f, 0.88f), new Keyframe(0.75f, 0.52f), new Keyframe(0.90f, 0.18f),
            new Keyframe(1f, 0.04f));

        [SerializeField]
        private AnimationCurve _coreAlpha = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.78f, 1f), new Keyframe(0.90f, 0.55f),
            new Keyframe(0.95f, 0.12f), new Keyframe(1f, 0f));

        [SerializeField]
        private AnimationCurve _sheathWidth = new AnimationCurve(
            new Keyframe(0f, 0.22f), new Keyframe(0.11f, 1.30f), new Keyframe(0.24f, 1.05f),
            new Keyframe(0.55f, 0.95f), new Keyframe(0.75f, 0.72f), new Keyframe(0.92f, 0.30f),
            new Keyframe(1f, 0.05f));

        [SerializeField]
        private AnimationCurve _sheathAlpha = new AnimationCurve(
            new Keyframe(0f, 0.85f), new Keyframe(0.10f, 1f), new Keyframe(0.22f, 1f), new Keyframe(0.50f, 0.45f),
            new Keyframe(0.75f, 0.32f), new Keyframe(0.90f, 0.08f), new Keyframe(0.97f, 0.02f), new Keyframe(1f, 0f));

        [SerializeField]
        private AnimationCurve _orbSize = new AnimationCurve(
            new Keyframe(0f, 0.25f), new Keyframe(0.09f, 1.15f), new Keyframe(0.22f, 1f),
            new Keyframe(0.76f, 0.95f), new Keyframe(0.80f, 0.70f), new Keyframe(0.84f, 0.20f),
            new Keyframe(0.88f, 0f), new Keyframe(1f, 0f));

        [SerializeField]
        private AnimationCurve _orbAlpha = new AnimationCurve(
            new Keyframe(0f, 0.40f), new Keyframe(0.07f, 1f), new Keyframe(0.77f, 1f),
            new Keyframe(0.82f, 0.50f), new Keyframe(0.86f, 0f), new Keyframe(1f, 0f));

        [Tooltip("유지 구간의 광륜 호. 참조는 발사 뒤 궤도 호가 거의 보이지 않는다")]
        [SerializeField]
        private AnimationCurve _fireArcAlpha = new AnimationCurve(
            new Keyframe(0f, 0.30f), new Keyframe(0.10f, 0.34f), new Keyframe(0.55f, 0.22f),
            new Keyframe(0.80f, 0.10f), new Keyframe(0.94f, 0f), new Keyframe(1f, 0f));

        [Tooltip("끝단 위로 찢어지는 외피 붓자국. 참조 2.0~2.3초에 가장 크다")]
        [SerializeField]
        private AnimationCurve _flagAlpha = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.14f, 0.9f), new Keyframe(0.30f, 1f), new Keyframe(0.55f, 0.45f),
            new Keyframe(0.78f, 0.15f), new Keyframe(0.90f, 0f), new Keyframe(1f, 0f));

        [Tooltip("시작점 붉은 후광. 구체보다 늦게 사라진다")]
        [SerializeField]
        private AnimationCurve _glowAlpha = new AnimationCurve(
            new Keyframe(0f, 0.6f), new Keyframe(0.08f, 1f), new Keyframe(0.78f, 1f),
            new Keyframe(0.88f, 0.70f), new Keyframe(0.94f, 0.10f), new Keyframe(1f, 0f));


        private const string EVENT_CHARGE_START = "ChargeStart";
        private const string EVENT_CHARGE_PEAK = "ChargePeak";
        private const string EVENT_FIRE = "Fire";
        private const string EVENT_FIRE_FLASH = "FireFlash";
        private const string EVENT_BEAM_TIP = "BeamTip";
        private const string EVENT_INK = "Ink";
        private const string EVENT_IGNITE = "Ignite";
        private const string EVENT_TIP_SPARK = "TipSpark";
        private const string EVENT_STOP = "Stop";
        private const string EVENT_CANCEL = "Cancel";

        private const float REVEAL_PHASE_END = 0.15f;
        private const float SUSTAIN_PHASE_END = 0.75f;

        private static readonly int G_Position = Shader.PropertyToID("position");
        private static readonly int G_Size = Shader.PropertyToID("size");
        private static readonly int G_Lifetime = Shader.PropertyToID("lifetime");
        private static readonly int G_TexIndex = Shader.PropertyToID("texIndex");
        private static readonly int G_Color = Shader.PropertyToID("color");
        private static readonly int G_Velocity = Shader.PropertyToID("velocity");
        private static readonly int G_SpawnCount = Shader.PropertyToID("spawnCount");
        private static readonly int G_BeamLength = Shader.PropertyToID("BeamLength");
        private static readonly int G_CoreScale = Shader.PropertyToID("CoreScale");
        private static readonly int G_CoreAlpha = Shader.PropertyToID("CoreAlpha");
        private static readonly int G_SheathScale = Shader.PropertyToID("SheathScale");
        private static readonly int G_SheathAlpha = Shader.PropertyToID("SheathAlpha");
        private static readonly int G_EdgeColor = Shader.PropertyToID("EdgeColor");
        private static readonly int G_BodyFade = Shader.PropertyToID("BodyFade");
        private static readonly int G_CoreOpacity = Shader.PropertyToID("CoreOpacity");
        private static readonly int G_SheathColor = Shader.PropertyToID("SheathColor");
        private static readonly int G_OrbInner = Shader.PropertyToID("OrbInner");
        private static readonly int G_GlowSize = Shader.PropertyToID("GlowSize");
        private static readonly int G_GlowAlpha = Shader.PropertyToID("GlowAlpha");
        private static readonly int G_BeamAxisY = Shader.PropertyToID("BeamAxisY");
        private static readonly int G_FlagAlpha = Shader.PropertyToID("FlagAlpha");
        private static readonly int G_OrbSize = Shader.PropertyToID("OrbSize");
        private static readonly int G_OrbAlpha = Shader.PropertyToID("OrbAlpha");
        private static readonly int G_ArcAlpha = Shader.PropertyToID("ArcAlpha");
        private static readonly int G_SparkRate = Shader.PropertyToID("SparkRate");
        private static readonly int G_WispRate = Shader.PropertyToID("WispRate");
        private static readonly int G_SmokeOpacity = Shader.PropertyToID("SmokeOpacity");

        private VisualEffect _vfx;
        private VFXEventAttribute _attribute;

        private BeamPhase _phase = BeamPhase.Idle;
        private float _time;
        private float _cancelTime;
        private bool _peakSent;
        private bool _autoFire;
        private bool _stopRequested;
        private float _stopTime = -1f;
        private float _nextTipBurst;
        private int _runId;
        private int _activeRunId = -1;
        private float _bodyFade = 1f;
        private float _lastChargeT;
        private float _lastFireP;
        private BeamPhase _cancelledFrom = BeamPhase.Idle;
        private bool _chargeInkSent;
        private bool _dissolveInkSent;

        /// <summary>현재 재생 단계.</summary>
        public BeamPhase Phase => _phase;

        /// <summary>현재 단계 안에서의 경과 시각(초).</summary>
        public float ElapsedTime => _time;

        /// <summary>드러남 + 유지 + 소멸을 합한 발사 구간 길이(초).</summary>
        public float FirePhaseDuration => _revealDuration + _fireDuration + _fadeDuration;

        /// <summary>충전부터 잔재 정리까지의 전체 길이(초).</summary>
        public float TotalDuration => _chargeDuration + FirePhaseDuration + _tailDuration;

        /// <summary>시작점에서 끝점까지의 길이(m).</summary>
        public float BeamLength
        {
            get => _beamLength;
            set => _beamLength = Mathf.Max(0.5f, value);
        }

        /// <summary>흑심 최대 반경(m).</summary>
        public float BeamRadius
        {
            get => _beamRadius;
            set => _beamRadius = Mathf.Max(0.01f, value);
        }

        /// <summary>시작점 구체 반경(m).</summary>
        public float OrbRadius
        {
            get => _orbRadius;
            set => _orbRadius = Mathf.Max(0.01f, value);
        }

        /// <summary>흑심 불투명도(0~1).</summary>
        public float CoreOpacity
        {
            get => _coreOpacity;
            set => _coreOpacity = Mathf.Clamp01(value);
        }

        /// <summary>붉은 외피 기본색(HDR 선형).</summary>
        public Color EdgeColor
        {
            get => _edgeColor;
            set => _edgeColor = value;
        }

        /// <summary>외피 밝기 배수.</summary>
        public float EdgeIntensity
        {
            get => _edgeIntensity;
            set => _edgeIntensity = Mathf.Max(0f, value);
        }

        /// <summary>고정 시드. 같은 값이면 같은 무작위 분포가 나온다.</summary>
        public uint Seed
        {
            get => _seed;
            set => _seed = value;
        }

        private void Awake() => Cache();

        private void OnEnable()
        {
            Cache();
            ApplyStaticParameters();
            if (_playOnEnable)
            {
                PlayFull();
            }
        }

        private void OnDisable() => ResetAll();

        private void Update()
        {
            if (_phase == BeamPhase.Idle)
            {
                return;
            }
            if (_vfx != null && _vfx.pause)
            {
                return;
            }
            _time += Time.deltaTime;
            switch (_phase)
            {
                case BeamPhase.Charging:
                    TickCharge();
                    break;
                case BeamPhase.Firing:
                    TickFire();
                    break;
                case BeamPhase.Cancelling:
                    TickCancel();
                    break;
            }
        }

        private void Cache()
        {
            if (_vfx == null)
            {
                _vfx = GetComponent<VisualEffect>();
            }
        }

        private VFXEventAttribute Attribute()
        {
            if (_attribute == null && _vfx != null)
            {
                _attribute = _vfx.CreateVFXEventAttribute();
            }
            return _attribute;
        }

        /// <summary>충전부터 소멸까지 참조 타임라인 그대로 한 번 재생한다. 독립 미리보기용이다.</summary>
        public void PlayFull()
        {
            BeginCharge();
            _autoFire = true;
        }

        /// <summary>시작·끝점을 월드 좌표로 주입한다. 본체 트랜스폼과 빔 길이를 함께 맞춘다.</summary>
        public void SetEndpoints(Vector3 worldStart, Vector3 worldEnd)
        {
            Vector3 delta = worldEnd - worldStart;
            float length = delta.magnitude;
            transform.position = worldStart;
            if (length > 1e-4f)
            {
                transform.rotation = Quaternion.LookRotation(delta / length, Vector3.up);
                _beamLength = length;
            }
        }

        /// <summary>충전·발사·소멸 지속시간을 한 번에 주입한다. 바깥값이 인스펙터 값을 덮는다.</summary>
        public void SetDurations(float chargeDuration, float revealDuration, float fireDuration, float fadeDuration)
        {
            _chargeDuration = Mathf.Max(0.01f, chargeDuration);
            _chargePeakTime = Mathf.Clamp(_chargePeakTime, 0f, _chargeDuration);
            _revealDuration = Mathf.Max(0.01f, revealDuration);
            _fireDuration = Mathf.Max(0.01f, fireDuration);
            _fadeDuration = Mathf.Max(0.01f, fadeDuration);
        }

        /// <summary>충전을 시작한다. 자동 발사는 하지 않는다.</summary>
        public void BeginCharge()
        {
            Cache();
            if (_vfx == null)
            {
                return;
            }
            ResetRuntime();
            _vfx.pause = false;
            _vfx.resetSeedOnPlay = false;
            _vfx.startSeed = _seed;
            _vfx.Reinit();
            ApplyStaticParameters();
            _phase = BeamPhase.Charging;
            ++_runId;
            _activeRunId = -1;
            WriteChargeParameters(0f);
            VFXEventAttribute attr = Attribute();
            attr.SetVector3(G_Position, Vector3.zero);
            attr.SetFloat(G_Size, _chargeOrbRadius * _chargeArcScale);
            // 후광이 충전 끝까지 남아야 하므로 충전 구체 입자는 충전 전체를 산다. 구체 자체는 곡선이 먼저 끈다.
            attr.SetFloat(G_Lifetime, Mathf.Max(0.05f, _chargeDuration + 0.05f));
            _vfx.SendEvent(EVENT_CHARGE_START, attr);
        }

        /// <summary>빔을 발사한다. 같은 실행 ID를 다시 받으면 무시한다.</summary>
        public void Fire(int runId)
        {
            Cache();
            if (_vfx == null || runId == _activeRunId)
            {
                return;
            }
            _activeRunId = runId;
            _phase = BeamPhase.Firing;
            _time = 0f;
            _stopRequested = false;
            _stopTime = -1f;
            _nextTipBurst = _revealDuration;
            ApplyStaticParameters();
            WriteFireParameters(0f);
            VFXEventAttribute attr = Attribute();
            attr.SetVector3(G_Position, Vector3.zero);
            attr.SetFloat(G_Size, _orbRadius * _fireArcScale);
            attr.SetFloat(G_Lifetime, FirePhaseDuration + _tailDuration);
            _vfx.SendEvent(EVENT_FIRE, attr);
            attr.SetFloat(G_Size, _ignitionFlameSize);
            attr.SetVector3(G_Velocity, _ignitionFlameVelocity);
            _vfx.SendEvent(EVENT_IGNITE, attr);
            attr.SetFloat(G_Size, _orbRadius * _fireFlashScale);
            attr.SetFloat(G_Lifetime, _fireFlashLifetime);
            attr.SetFloat(G_TexIndex, 1f);
            _vfx.SendEvent(EVENT_FIRE_FLASH, attr);
        }

        /// <summary>현재 실행 ID를 하나 올려 발사한다.</summary>
        public void Fire() => Fire(++_runId);

        /// <summary>새 방출을 멈추고 잔재를 감쇠시킨다. 이미 살아 있는 입자는 자기 수명대로 사라진다.</summary>
        public void StopBeam()
        {
            if (_vfx == null || _phase != BeamPhase.Firing)
            {
                return;
            }
            if (_stopRequested)
            {
                return;
            }
            _stopRequested = true;
            _stopTime = _time;
            _vfx.SendEvent(EVENT_STOP);
        }

        /// <summary>빠르게 소멸시키고 이 효과의 기여분을 해제한다.</summary>
        public void Cancel()
        {
            Cache();
            if (_vfx == null || _phase == BeamPhase.Idle)
            {
                return;
            }
            _vfx.SendEvent(EVENT_CANCEL);
            _cancelledFrom = _phase;
            _phase = BeamPhase.Cancelling;
            _cancelTime = 0f;
            _time = 0f;
        }

        /// <summary>일시정지. VFX 시뮬레이션과 곡선 시계를 함께 멈춘다.</summary>
        public void SetPaused(bool paused)
        {
            Cache();
            if (_vfx != null)
            {
                _vfx.pause = paused;
            }
        }

        /// <summary>모든 입자·예약·기여분을 지우고 대기 상태로 되돌린다. 풀 반환 시 호출한다.</summary>
        public void ResetAll()
        {
            ResetRuntime();
            if (_vfx == null)
            {
                return;
            }
            _vfx.pause = false;
            _vfx.SetFloat(G_BodyFade, 1f);
            _vfx.SetFloat(G_CoreAlpha, 0f);
            _vfx.SetFloat(G_SheathAlpha, 0f);
            _vfx.SetFloat(G_FlagAlpha, 0f);
            _vfx.SetFloat(G_OrbAlpha, 0f);
            _vfx.SetFloat(G_GlowAlpha, 0f);
            _vfx.SetFloat(G_ArcAlpha, 0f);
            _vfx.Reinit();
        }

        private void ResetRuntime()
        {
            _phase = BeamPhase.Idle;
            _time = 0f;
            _cancelTime = 0f;
            _peakSent = false;
            _autoFire = false;
            _stopRequested = false;
            _stopTime = -1f;
            _nextTipBurst = 0f;
            _activeRunId = -1;
            _bodyFade = 1f;
            _lastChargeT = 0f;
            _lastFireP = 0f;
            _cancelledFrom = BeamPhase.Idle;
            _chargeInkSent = false;
            _dissolveInkSent = false;
        }

        private void ApplyStaticParameters()
        {
            if (_vfx == null)
            {
                return;
            }
            _vfx.SetFloat(G_BeamLength, _beamLength);
            _vfx.SetVector3(G_EdgeColor, new Vector3(_edgeColor.r, _edgeColor.g, _edgeColor.b) * _edgeIntensity);
            _vfx.SetFloat(G_SparkRate, _sparkRate);
            _vfx.SetFloat(G_WispRate, _wispRate);
            _vfx.SetFloat(G_SmokeOpacity, _smokeOpacity);
            _vfx.SetFloat(G_BodyFade, _bodyFade);
            _vfx.SetFloat(G_CoreOpacity, _coreOpacity);
            _vfx.SetVector3(G_SheathColor, new Vector3(_sheathColor.r, _sheathColor.g, _sheathColor.b));
        }

        private void TickCharge()
        {
            float t = Mathf.Clamp01(_time / _chargeDuration);
            WriteChargeParameters(t);
            if (!_peakSent && _time >= _chargePeakTime)
            {
                _peakSent = true;
                VFXEventAttribute attr = Attribute();
                attr.SetVector3(G_Position, Vector3.zero);
                attr.SetFloat(G_Size, _chargeOrbRadius * _peakFlashScale);
                attr.SetFloat(G_Lifetime, _peakFlashLifetime);
                attr.SetFloat(G_TexIndex, 0f);
                _vfx.SendEvent(EVENT_CHARGE_PEAK, attr);
            }
            if (!_chargeInkSent && t >= _chargeInkProgress)
            {
                _chargeInkSent = true;
                SendInk(Vector3.zero, _chargeInkCount, _chargeInkSize, 2f, _chargeInkLifetime);
            }
            if (_time >= _chargeDuration)
            {
                if (_autoFire)
                {
                    Fire(++_runId);
                }
                else
                {
                    _phase = BeamPhase.Idle;
                }
            }
        }

        /// <summary>검은 잉크 획을 즉시 내보낸다. Ink 이벤트는 Initialize에 바로 연결돼 spawnCount가 개수다.</summary>
        private void SendInk(Vector3 localPosition, int count, float size, float texIndex, float lifetime)
        {
            if (_vfx == null || count <= 0)
            {
                return;
            }
            VFXEventAttribute attr = Attribute();
            attr.SetVector3(G_Position, localPosition);
            attr.SetFloat(G_SpawnCount, count);
            attr.SetFloat(G_Size, size);
            attr.SetFloat(G_TexIndex, texIndex);
            attr.SetVector3(G_Color, Vector3.zero);
            attr.SetVector3(G_Velocity, Vector3.zero);
            attr.SetFloat(G_Lifetime, lifetime);
            _vfx.SendEvent(EVENT_INK, attr);
        }

        private void WriteChargeParameters(float t)
        {
            if (_vfx == null)
            {
                return;
            }
            _lastChargeT = t;
            float size = Mathf.Max(0f, _chargeOrbSize.Evaluate(t)) * _chargeOrbRadius * 2f;
            float alpha = Mathf.Clamp01(_chargeOrbAlpha.Evaluate(t)) * _bodyFade;
            _vfx.SetFloat(G_OrbSize, size);
            _vfx.SetFloat(G_OrbAlpha, alpha);
            _vfx.SetFloat(G_OrbInner, _chargeOrbInner);
            _vfx.SetFloat(G_GlowSize, _chargeOrbRadius * 2f * _glowScale);
            _vfx.SetFloat(G_GlowAlpha, Mathf.Clamp01(_chargeGlowAlpha.Evaluate(t) * _chargeGlowIntensity) * _bodyFade);
            _vfx.SetFloat(G_ArcAlpha, alpha);
            _vfx.SetFloat(G_CoreAlpha, 0f);
            _vfx.SetFloat(G_SheathAlpha, 0f);
            _vfx.SetFloat(G_FlagAlpha, 0f);
            _vfx.SetVector3(G_CoreScale, Vector3.zero);
            _vfx.SetVector3(G_SheathScale, Vector3.zero);
        }

        private void TickFire()
        {
            float p = PhaseProgress(_time);
            WriteFireParameters(p);
            if (!_dissolveInkSent && p >= _dissolveInkProgress)
            {
                _dissolveInkSent = true;
                SendInk(Vector3.zero, _dissolveInkCount, _dissolveInkSize, 2f, _dissolveInkLifetime);
            }
            if (!_stopRequested && _time >= _nextTipBurst && p < SUSTAIN_PHASE_END)
            {
                _nextTipBurst = _time + _endBurstInterval;
                VFXEventAttribute attr = Attribute();
                attr.SetVector3(G_Position, new Vector3(0f, 0f, _beamLength * Mathf.Clamp01(_coreLength.Evaluate(p))));
                attr.SetFloat(G_Size, _beamRadius * _endInkScale);
                attr.SetFloat(G_TexIndex, 0f);
                attr.SetVector3(G_Color, Vector3.zero);
                attr.SetVector3(G_Velocity, _endBurstVelocity);
                attr.SetFloat(G_Lifetime, _endInkLifetime);
                _vfx.SendEvent(EVENT_BEAM_TIP, attr);
                attr.SetFloat(G_Size, _tipSparkSize);
                _vfx.SendEvent(EVENT_TIP_SPARK, attr);
            }
            if (p >= 1f && _time >= FirePhaseEndTime() + _tailDuration)
            {
                ResetAll();
            }
        }

        private float FirePhaseEndTime()
        {
            if (_stopRequested)
            {
                return _stopTime + _fadeDuration;
            }
            return FirePhaseDuration;
        }

        /// <summary>실제 지속시간과 무관하게 드러남 0~0.15, 유지 0.15~0.75, 소멸 0.75~1로 정규화한다.
        /// 곡선을 다시 그리지 않고도 지속시간만 바꿀 수 있다.</summary>
        private float PhaseProgress(float t)
        {
            if (t < _revealDuration)
            {
                return REVEAL_PHASE_END * (t / _revealDuration);
            }
            if (_stopRequested && t >= _stopTime)
            {
                float sustainAtStop = Mathf.Min(SUSTAIN_PHASE_END,
                    REVEAL_PHASE_END + (SUSTAIN_PHASE_END - REVEAL_PHASE_END)
                    * ((_stopTime - _revealDuration) / _fireDuration));
                float k = Mathf.Clamp01((t - _stopTime) / _fadeDuration);
                return Mathf.Lerp(sustainAtStop, 1f, k);
            }
            float sustainEnd = _revealDuration + _fireDuration;
            if (t < sustainEnd)
            {
                return REVEAL_PHASE_END + (SUSTAIN_PHASE_END - REVEAL_PHASE_END)
                    * ((t - _revealDuration) / _fireDuration);
            }
            return Mathf.Clamp01(SUSTAIN_PHASE_END + (1f - SUSTAIN_PHASE_END)
                * ((t - sustainEnd) / _fadeDuration));
        }

        private void WriteFireParameters(float p)
        {
            if (_vfx == null)
            {
                return;
            }
            _lastFireP = p;
            _vfx.SetFloat(G_BeamAxisY, transform.position.y);
            float length = _beamLength * Mathf.Clamp01(_coreLength.Evaluate(p));
            float coreWidth = _beamRadius * Mathf.Max(0f, _coreWidth.Evaluate(p));
            float sheathWidth = _beamRadius * _sheathRadiusScale * Mathf.Max(0f, _sheathWidth.Evaluate(p));
            _vfx.SetVector3(G_CoreScale, new Vector3(coreWidth, coreWidth, length));
            _vfx.SetVector3(G_SheathScale, new Vector3(sheathWidth, sheathWidth, length * _sheathLengthScale));
            _vfx.SetFloat(G_CoreAlpha, Mathf.Clamp01(_coreAlpha.Evaluate(p)) * _bodyFade);
            _vfx.SetFloat(G_SheathAlpha, Mathf.Clamp01(_sheathAlpha.Evaluate(p)) * _bodyFade);
            _vfx.SetFloat(G_FlagAlpha, Mathf.Clamp01(_flagAlpha.Evaluate(p)) * _bodyFade);
            _vfx.SetFloat(G_OrbSize, Mathf.Max(0f, _orbSize.Evaluate(p)) * _orbRadius * 2f);
            _vfx.SetFloat(G_OrbInner, _fireOrbInner);
            _vfx.SetFloat(G_GlowSize, _orbRadius * 2f * _glowScale);
            _vfx.SetFloat(G_GlowAlpha, Mathf.Clamp01(_glowAlpha.Evaluate(p)) * _bodyFade);
            _vfx.SetFloat(G_OrbAlpha, Mathf.Clamp01(_orbAlpha.Evaluate(p)) * _bodyFade);
            _vfx.SetFloat(G_ArcAlpha, Mathf.Clamp01(_fireArcAlpha.Evaluate(p)));
        }

        private void TickCancel()
        {
            _cancelTime += Time.deltaTime;
            float k = Mathf.Clamp01(_cancelTime / _cancelDuration);
            _bodyFade = 1f - k;
            if (_vfx != null)
            {
                _vfx.SetFloat(G_BodyFade, _bodyFade);
                if (_cancelledFrom == BeamPhase.Charging)
                {
                    WriteChargeParameters(_lastChargeT);
                }
                else if (_cancelledFrom == BeamPhase.Firing)
                {
                    WriteFireParameters(_lastFireP);
                }
            }
            if (k >= 1f)
            {
                ResetAll();
            }
        }
    }
}
