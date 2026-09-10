// System
using System;
using System.IO;
using System.Text;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Network
{
    /// <summary> 런 구간별 프레임 시간을 모아 CSV로 남긴다 </summary>
    public class FrameRecorder : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *                Fields
        ****************************************/

        private const string PANEL_ID    = "frame";
        private const string PANEL_TITLE = "프레임 기록";

        [Header("단축키")]
        // F9는 GDebugOverlay의 로그 비우기와, F1은 호스트 시작과 겹친다.
        // F1~F12가 전부 차 있어 F키 밖으로 뺐다
        [SerializeField] private KeyCode _writeKeyCode = KeyCode.Insert;

        [Header("측정 조건")]
        [Tooltip("같은 PC에서 여러 인스턴스를 비교하려면 상한을 맞춰야 한다. 0이면 상한 없음")]
        [SerializeField] private int _targetFrameRate = Constants.Diagnostics.TARGET_FRAME_RATE;

        private int[][]  _phaseBuckets;      // [RunPhase][버킷] 프레임 수
        private int[]    _mergedBuckets;     // 전체 행을 만들 때 쓰는 버퍼
        private long[]   _phaseFrames;       // 구간별 프레임 수
        private double[] _phaseTotalSeconds; // 구간별 누적 프레임 시간
        private float[]  _phaseWorstSeconds; // 구간별 최악 프레임
        private long[]   _phaseOverBudget;   // 구간별 예산 초과 프레임 수

        private int  _bucketCount = 0;
        private int  _phaseCount  = 0;
        private long _sampleCount = 0;
        private int  _warmupLeft  = Constants.Diagnostics.WARMUP_FRAMES;

        private RunManager _runManager = null;
        private string     _instanceKey = null;

        private float  _autosaveTimer = 0f;
        private float  _overlayTimer = 0f;
        private string _overlayText  = "";

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _instanceKey = Guid.NewGuid().ToString("N");
            _phaseCount  = Enum.GetValues(typeof(RunPhase)).Length;
            _bucketCount = Mathf.CeilToInt(Constants.Diagnostics.HISTOGRAM_MAX_MS / Constants.Diagnostics.HISTOGRAM_BUCKET_MS);

            _phaseBuckets = new int[_phaseCount][];
            for (int i = 0; i < _phaseCount; ++i)
            {
                _phaseBuckets[i] = new int[_bucketCount];
            }

            _mergedBuckets     = new int[_bucketCount];
            _phaseFrames       = new long[_phaseCount];
            _phaseTotalSeconds = new double[_phaseCount];
            _phaseWorstSeconds = new float[_phaseCount];
            _phaseOverBudget   = new long[_phaseCount];

            ApplyFrameRateCap();

            DevPanelHost.Register(PANEL_ID, PANEL_TITLE, Build, Handle);
        }

        private void OnDestroy()
        {
            DevPanelHost.Unregister(PANEL_ID);
        }

        private void Update()
        {
            Record();
            HandleAutosave();
            RefreshOverlay();
            HandleKeys();
        }

        /// <summary>
        /// 측정값은 브라우저 개발 패널에서 본다.
        /// 화면 왼쪽 위에 고정으로 겹쳐 그리던 것을 옮긴 것이다 -
        /// 로비 UI와 다른 개발 표시가 같은 자리에서 서로를 덮었다
        /// </summary>
        private void Build(DevPanelBuilder builder)
        {
            builder.Label(_overlayText);
            builder.Button("write", $"지금 파일로 저장 ({_writeKeyCode})");
        }

        private void Handle(DevCommand command)
        {
            if (command.Control == "write")
            {
                Write(true);
            }
        }

        private void OnApplicationQuit()
        {
            if (_sampleCount <= 0)
            {
                Debug.LogWarning($"[FrameRecorder] 표본 없이 종료했다 (워밍업 {Constants.Diagnostics.WARMUP_FRAMES}프레임을 넘기지 못함)");
                return;
            }

            Write();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void ApplyFrameRateCap()
        {
            if (_targetFrameRate <= 0)
            {
                return;
            }

            QualitySettings.vSyncCount  = 0;
            Application.targetFrameRate = _targetFrameRate;
        }

        private void Record()
        {
            if (_warmupLeft > 0)
            {
                --_warmupLeft;
                return;
            }

            float frameSeconds = Time.unscaledDeltaTime;
            float frameMs      = frameSeconds * 1000f;
            int   phaseIndex   = ResolvePhaseIndex();

            int bucket = (int) (frameMs / Constants.Diagnostics.HISTOGRAM_BUCKET_MS);
            if (bucket < 0)
            {
                bucket = 0;
            }
            else if (bucket >= _bucketCount)
            {
                bucket = _bucketCount - 1;
            }

            ++_phaseBuckets[phaseIndex][bucket];
            ++_phaseFrames[phaseIndex];
            _phaseTotalSeconds[phaseIndex] += frameSeconds;

            if (frameSeconds > _phaseWorstSeconds[phaseIndex])
            {
                _phaseWorstSeconds[phaseIndex] = frameSeconds;
            }

            if (frameMs > Constants.Diagnostics.FRAME_BUDGET_60_MS)
            {
                ++_phaseOverBudget[phaseIndex];
            }

            ++_sampleCount;
        }

        private RunPhase ResolvePhase()
        {
            if (_runManager == null)
            {
                _runManager = RunManager.LocalInstance;
            }

            return (_runManager == null) ? RunPhase.Lobby : _runManager.Phase;
        }

        private int ResolvePhaseIndex()
        {
            int index = (int) ResolvePhase();
            return ((index < 0) || (index >= _phaseCount)) ? 0 : index;
        }

        private void HandleKeys()
        {
            if (Input.GetKeyDown(_writeKeyCode))
            {
                Write(true);
            }

        }

        private void HandleAutosave()
        {
            if (_sampleCount <= 0)
            {
                return;
            }

            _autosaveTimer += Time.unscaledDeltaTime;
            if (_autosaveTimer < Constants.Diagnostics.AUTOSAVE_INTERVAL_SECONDS)
            {
                return;
            }

            _autosaveTimer = 0f;
            Write();
        }

        private void RefreshOverlay()
        {
            _overlayTimer += Time.unscaledDeltaTime;
            if (_overlayTimer < Constants.Diagnostics.OVERLAY_REFRESH_SECONDS)
            {
                return;
            }

            _overlayTimer = 0f;

            float  currentMs = Time.unscaledDeltaTime * 1000f;
            string state     = (_warmupLeft > 0) ? ("워밍업 " + _warmupLeft) : ("표본 " + _sampleCount);
            string cap       = (_targetFrameRate > 0) ? (_targetFrameRate + " FPS 상한") : "상한 없음";
            string nl        = Environment.NewLine;

            _overlayText = ResolveRole() + ", " + ResolvePhase()
                + nl + currentMs.ToString("F1") + " ms  (" + (1000f / Mathf.Max(currentMs, 0.001f)).ToString("F0") + " FPS)"
                + nl + state + ", " + cap
                + nl + Screen.width + "x" + Screen.height + "   " + _writeKeyCode + " 저장";
        }

        /// <summary> 지금까지 누적한 통계를 CSV로 남긴다 </summary>
        public void Write(bool announce = false)
        {
            if (_sampleCount <= 0)
            {
                Debug.LogWarning("[FrameRecorder] 남길 표본이 없다", this);
                return;
            }

            string role = ResolveRole();
            var    csv  = new StringBuilder();

            csv.AppendLine("# 역할," + role);
            csv.AppendLine("# 해상도," + Screen.width + "x" + Screen.height);
            csv.AppendLine("# 표본," + _sampleCount);
            csv.AppendLine("# 워밍업 제외," + Constants.Diagnostics.WARMUP_FRAMES + "프레임");
            csv.AppendLine("# 프레임 상한," + ((_targetFrameRate > 0) ? (_targetFrameRate + " FPS") : "없음"));
            csv.AppendLine("# 백분위 해상도," + Constants.Diagnostics.HISTOGRAM_BUCKET_MS.ToString("F2") + "ms");
            csv.AppendLine("# 기록시각," + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            csv.AppendLine("구간,프레임수,평균ms,p50ms,p95ms,p99ms,최악ms,평균FPS,예산초과비율");

            long   totalFrames     = 0;
            double totalSeconds    = 0.0;
            float  totalWorst      = 0f;
            long   totalOverBudget = 0;

            Array.Clear(_mergedBuckets, 0, _bucketCount);

            foreach (RunPhase phase in Enum.GetValues(typeof(RunPhase)))
            {
                int index = (int) phase;
                if ((index < 0) || (index >= _phaseCount) || (_phaseFrames[index] <= 0))
                {
                    continue;
                }

                AppendRow(csv, phase.ToString(), _phaseBuckets[index], _phaseFrames[index],
                    _phaseTotalSeconds[index], _phaseWorstSeconds[index], _phaseOverBudget[index]);

                int[] buckets = _phaseBuckets[index];
                for (int i = 0; i < _bucketCount; ++i)
                {
                    _mergedBuckets[i] += buckets[i];
                }

                totalFrames     += _phaseFrames[index];
                totalSeconds    += _phaseTotalSeconds[index];
                totalOverBudget += _phaseOverBudget[index];

                if (_phaseWorstSeconds[index] > totalWorst)
                {
                    totalWorst = _phaseWorstSeconds[index];
                }
            }

            AppendRow(csv, "전체", _mergedBuckets, totalFrames, totalSeconds, totalWorst, totalOverBudget);

            string path = Path.Combine(Application.persistentDataPath,
                Constants.Diagnostics.FRAME_LOG_PREFIX + "_" + role + "_" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "_" + _instanceKey + ".csv");

            try
            {
                File.WriteAllText(path, csv.ToString());
            }
            catch (UnauthorizedAccessException exception)
            {
                Debug.LogError(
                    $"[FrameRecorder] CSV 기록 권한이 없다: {path}, {exception.Message}", this);
                return;
            }
            catch (IOException exception)
            {
                Debug.LogError(
                    $"[FrameRecorder] CSV 기록 중 파일 오류가 발생했다: {path}, {exception.Message}",
                    this);
                return;
            }

            if (announce)
            {
                Debug.Log($"[FrameRecorder] {_sampleCount}프레임 기록을 남겼다: {path}");
            }
        }

        private void AppendRow(StringBuilder csv, string label, int[] buckets, long frames,
            double totalSeconds, float worstSeconds, long overBudget)
        {
            if (frames <= 0)
            {
                return;
            }

            float averageMs = (float) ((totalSeconds / frames) * 1000.0);

            csv.AppendLine(label + "," + frames
                + "," + averageMs.ToString("F2")
                + "," + PercentileMs(buckets, frames, Constants.Diagnostics.PERCENTILE_MEDIAN).ToString("F2")
                + "," + PercentileMs(buckets, frames, Constants.Diagnostics.PERCENTILE_HIGH).ToString("F2")
                + "," + PercentileMs(buckets, frames, Constants.Diagnostics.PERCENTILE_WORST).ToString("F2")
                + "," + (worstSeconds * 1000f).ToString("F2")
                + "," + (1000f / averageMs).ToString("F1")
                + "," + ((float) overBudget / frames).ToString("P1"));
        }

        /// <summary> 누적 히스토그램을 앞에서부터 훑어 목표 순위가 들어 있는 버킷을 찾는다 </summary>
        private float PercentileMs(int[] buckets, long frames, float ratio)
        {
            long targetRank = (long) Mathf.Round(ratio * (frames - 1));
            long cumulative = 0;

            for (int i = 0; i < _bucketCount; ++i)
            {
                cumulative += buckets[i];
                if (cumulative > targetRank)
                {
                    return (i + 0.5f) * Constants.Diagnostics.HISTOGRAM_BUCKET_MS;
                }
            }

            return Constants.Diagnostics.HISTOGRAM_MAX_MS;
        }

        private string ResolveRole()
        {
            if ((NetworkServer.active) && (NetworkClient.active))
            {
                return "호스트";
            }

            if (NetworkServer.active)
            {
                return "서버";
            }

            if (NetworkClient.active)
            {
                return "클라이언트";
            }

            return "단독";
        }

#endif
    }
}
