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
    /// <summary>
    /// 런 구간별 프레임 시간을 모아 CSV로 남긴다.
    /// 리슨 서버라 호스트가 3인분을 지므로, 호스트와 클라이언트를 같은 구간에서 비교해야 부하를 판단할 수 있다.
    /// 프레임 시간은 표본 배열이 아니라 히스토그램에 누적한다 - 저장 비용이 측정 시간에 비례하면
    /// 자동 저장이 스스로 프레임을 끊어 측정값을 오염시킨다
    /// </summary>
    public class FrameRecorder : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *                Fields
        ****************************************/

        private const int OVERLAY_MARGIN = 10;
        private const int OVERLAY_WIDTH  = 320;
        private const int OVERLAY_HEIGHT = 105;

        [Header("단축키")]
        [SerializeField] private KeyCode _writeKeyCode   = KeyCode.F9;
        [SerializeField] private KeyCode _overlayKeyCode = KeyCode.F10;

        [Header("측정 조건")]
        [Tooltip("같은 PC에서 여러 인스턴스를 비교하려면 상한을 맞춰야 한다. 0이면 상한 없음")]
        [SerializeField] private int _targetFrameRate = Constants.Diagnostics.TARGET_FRAME_RATE;

        private int[][]  _phaseBuckets;      // [RunPhase][버킷] 프레임 수
        private int[]    _mergedBuckets;     // 전체 행을 만들 때만 쓰는 재사용 버퍼
        private long[]   _phaseFrames;       // 구간별 프레임 수
        private double[] _phaseTotalSeconds; // 구간별 누적 프레임 시간
        private float[]  _phaseWorstSeconds; // 구간별 최악 프레임, 히스토그램 양자화와 무관하게 실측을 남긴다
        private long[]   _phaseOverBudget;   // 구간별 예산 초과 프레임 수

        private int  _bucketCount = 0;
        private int  _phaseCount  = 0;
        private long _sampleCount = 0;
        private int  _warmupLeft  = Constants.Diagnostics.WARMUP_FRAMES;

        private RunManager _runManager = null;

        private float  _autosaveTimer = 0f;
        private float  _overlayTimer  = 0f;
        private string _overlayText   = "";
        private bool   _showOverlay   = true;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _phaseCount  = Enum.GetValues(typeof(RunPhase)).Length;
            _bucketCount = Mathf.CeilToInt(Constants.Diagnostics.HISTOGRAM_MAX_MS / Constants.Diagnostics.HISTOGRAM_BUCKET_MS);

            // 게임 루프에서 할당이 일어나지 않도록 여기서 한 번만 잡는다
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
        }

        private void Update()
        {
            Record();
            HandleAutosave();
            RefreshOverlay();
            HandleKeys();
        }

        // 화면에 보이지 않으면 기록이 도는지 알 수 없다, 개발 빌드에서만 뜬다
        private void OnGUI()
        {
            if (!_showOverlay)
            {
                return;
            }

            GUI.Label(new Rect(OVERLAY_MARGIN, OVERLAY_MARGIN, OVERLAY_WIDTH, OVERLAY_HEIGHT), _overlayText);
        }

        private void OnApplicationQuit()
        {
            // 표본이 0이어도 로그는 남긴다, 아무 흔적이 없으면 왜 안 됐는지 알 수 없다
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

        // 상한이 없으면 한 PC의 인스턴스들이 서로 자원을 뺏어 네트워크 비용과 렌더링 경쟁이 섞인다
        private void ApplyFrameRateCap()
        {
            if (_targetFrameRate <= 0)
            {
                return;
            }

            QualitySettings.vSyncCount  = 0; // vSync가 켜져 있으면 targetFrameRate가 무시된다
            Application.targetFrameRate = _targetFrameRate;
        }

        private void Record()
        {
            // 씬 로드 직후의 튀는 프레임을 표본에서 뺀다
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
                bucket = _bucketCount - 1; // 마지막 버킷이 초과분을 받는다, 최악값은 따로 실측으로 남긴다
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

        // RunManager는 서버에도 클라이언트에도 있는 씬 오브젝트다, 조회는 한 번만 한다
        private RunPhase ResolvePhase()
        {
            if (_runManager == null)
            {
                _runManager = FindAnyObjectByType<RunManager>(FindObjectsInactive.Exclude);
            }

            return (_runManager == null) ? RunPhase.Lobby : _runManager.Phase;
        }

        // RunPhase에 새 값이 끼어들어도 배열 밖을 짚지 않게 한다
        private int ResolvePhaseIndex()
        {
            int index = (int) ResolvePhase();
            return ((index < 0) || (index >= _phaseCount)) ? 0 : index;
        }

        private void HandleKeys()
        {
            if (Input.GetKeyDown(_writeKeyCode))
            {
                Write();
            }

            if (Input.GetKeyDown(_overlayKeyCode))
            {
                _showOverlay = !_showOverlay;
            }
        }

        // 비정상 종료로 측정을 통째로 잃지 않도록 주기적으로 덮어쓴다
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
            if (!_showOverlay)
            {
                return;
            }

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
                + nl + Screen.width + "x" + Screen.height + "   F9 저장, F10 표시";
        }

        /// <summary> 지금까지 누적한 통계를 CSV로 남긴다. 비용은 측정 시간과 무관하다 </summary>
        public void Write()
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
                Constants.Diagnostics.FRAME_LOG_PREFIX + "_" + role + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
            File.WriteAllText(path, csv.ToString());

            Debug.Log($"[FrameRecorder] {_sampleCount}프레임 기록을 남겼다: {path}");
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

        /// <summary>
        /// 누적 히스토그램을 앞에서부터 훑어 목표 순위가 들어 있는 버킷을 찾는다.
        /// 버킷 수가 고정이라 측정 시간이 길어져도 비용이 늘지 않는다
        /// </summary>
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
