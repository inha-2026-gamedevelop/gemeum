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
    /// 리슨 서버라 호스트가 3인분을 지므로, 호스트와 클라이언트를 같은 구간에서 비교해야 부하를 판단할 수 있다
    /// </summary>
    public class FrameRecorder : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD

        /****************************************
        *                Fields
        ****************************************/

        private const int OVERLAY_MARGIN = 10;
        private const int OVERLAY_WIDTH  = 320;
        private const int OVERLAY_HEIGHT = 90;

        [Header("단축키")]
        [SerializeField] private KeyCode _writeKeyCode   = KeyCode.F9;
        [SerializeField] private KeyCode _overlayKeyCode = KeyCode.F10;

        private float[] _frameSeconds; // 프레임당 소요 시간
        private byte[]  _framePhase;   // 같은 인덱스의 프레임이 속한 RunPhase
        private float[] _sortScratch;  // 백분위 계산용 재사용 버퍼

        private int        _sampleCount = 0;
        private int        _warmupLeft  = Constants.Diagnostics.WARMUP_FRAMES;
        private bool       _isFull      = false;
        private RunManager _runManager  = null;

        private float  _autosaveTimer = 0f;
        private float  _overlayTimer  = 0f;
        private string _overlayText   = "";
        private bool   _showOverlay   = true;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            // 게임 루프에서 할당이 일어나지 않도록 여기서 한 번만 잡는다
            _frameSeconds = new float[Constants.Diagnostics.MAX_FRAME_SAMPLES];
            _framePhase   = new byte[Constants.Diagnostics.MAX_FRAME_SAMPLES];
            _sortScratch  = new float[Constants.Diagnostics.MAX_FRAME_SAMPLES];
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

        private void Record()
        {
            if (_isFull)
            {
                return;
            }

            // 씬 로드 직후의 튀는 프레임을 표본에서 뺀다
            if (_warmupLeft > 0)
            {
                --_warmupLeft;
                return;
            }

            _frameSeconds[_sampleCount] = Time.unscaledDeltaTime;
            _framePhase[_sampleCount]   = (byte) ResolvePhase();
            ++_sampleCount;

            if (_sampleCount >= _frameSeconds.Length)
            {
                _isFull = true;
                Debug.LogWarning("[FrameRecorder] 표본이 가득 차 기록을 멈춘다", this);
            }
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

            float currentMs = Time.unscaledDeltaTime * 1000f;
            string state    = (_warmupLeft > 0) ? ("워밍업 " + _warmupLeft) : ("표본 " + _sampleCount);
            string nl = Environment.NewLine;
            _overlayText = ResolveRole() + " · " + ResolvePhase()
                + nl + currentMs.ToString("F1") + " ms  (" + (1000f / Mathf.Max(currentMs, 0.001f)).ToString("F0") + " FPS)"
                + nl + state
                + nl + Screen.width + "x" + Screen.height + "   F9 저장 · F10 표시";
        }

        /// <summary> 지금까지 모은 표본을 CSV로 남긴다 </summary>
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
            csv.AppendLine("# 기록시각," + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            csv.AppendLine("구간,프레임수,평균ms,p50ms,p95ms,p99ms,최악ms,평균FPS,예산초과비율");

            foreach (RunPhase phase in Enum.GetValues(typeof(RunPhase)))
            {
                AppendPhaseRow(csv, phase);
            }

            AppendRow(csv, "전체", 0, _sampleCount);

            string path = Path.Combine(Application.persistentDataPath,
                Constants.Diagnostics.FRAME_LOG_PREFIX + "_" + role + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
            File.WriteAllText(path, csv.ToString());

            Debug.Log($"[FrameRecorder] {_sampleCount}프레임 기록을 남겼다: {path}");
        }

        private void AppendPhaseRow(StringBuilder csv, RunPhase phase)
        {
            int count = 0;
            for (int i = 0; i < _sampleCount; ++i)
            {
                if (_framePhase[i] == (byte) phase)
                {
                    _sortScratch[count] = _frameSeconds[i];
                    ++count;
                }
            }

            if (count <= 0)
            {
                return;
            }

            AppendRow(csv, phase.ToString(), (byte) phase, count, true);
        }

        // filtered가 true면 _sortScratch에 이미 모아둔 count개를 쓴다
        private void AppendRow(StringBuilder csv, string label, byte phase, int count, bool filtered = false)
        {
            if (!filtered)
            {
                for (int i = 0; i < count; ++i)
                {
                    _sortScratch[i] = _frameSeconds[i];
                }
            }

            float total  = 0f;
            int   overBudget = 0;
            for (int i = 0; i < count; ++i)
            {
                total += _sortScratch[i];
                if ((_sortScratch[i] * 1000f) > Constants.Diagnostics.FRAME_BUDGET_60_MS)
                {
                    ++overBudget;
                }
            }

            Array.Sort(_sortScratch, 0, count);

            float averageMs = (total / count) * 1000f;
            csv.AppendLine(label + "," + count
                + "," + averageMs.ToString("F2")
                + "," + PercentileMs(count, Constants.Diagnostics.PERCENTILE_MEDIAN).ToString("F2")
                + "," + PercentileMs(count, Constants.Diagnostics.PERCENTILE_HIGH).ToString("F2")
                + "," + PercentileMs(count, Constants.Diagnostics.PERCENTILE_WORST).ToString("F2")
                + "," + (_sortScratch[count - 1] * 1000f).ToString("F2")
                + "," + (1000f / averageMs).ToString("F1")
                + "," + ((float) overBudget / count).ToString("P1"));
        }

        // _sortScratch가 오름차순으로 정렬된 상태를 전제한다
        private float PercentileMs(int count, float ratio)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt(ratio * (count - 1)), 0, count - 1);
            return _sortScratch[index] * 1000f;
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
