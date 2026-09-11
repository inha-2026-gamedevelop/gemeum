// System
using System;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Network;

namespace Geumeum.Systems
{
    /// <summary> 동기화된 결과를 각 클라이언트의 로컬 기록으로 옮기는 재시도 가능한 창구 </summary>
    [RequireComponent(typeof(RunManager))]
    public sealed partial class RunResultRecorder : NetworkBehaviour
    {
        [SerializeField] private string _recordPathOverride = null;

        private RunManager _runManager = null;
        private readonly List<RunResultSnapshot> _pending = new List<RunResultSnapshot>();
        private string _lastFailure = null;

        public bool HasPendingRecord => (_pending.Count > 0);
        public string LastFailure => _lastFailure;

        private void Awake()
        {
            _runManager = GetComponent<RunManager>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TryParseRecordPathArgument(Environment.GetCommandLineArgs(),
                out string commandLinePath))
            {
                _recordPathOverride = commandLinePath;
            }
#endif
        }

        /// <summary> 개발 M2 프로세스가 실제 사용자 기록과 분리된 경로를 지정하는 인자를 읽는다 </summary>
        public static bool TryParseRecordPathArgument(string[] arguments, out string path)
        {
            const string option = "-result-record-path";
            path = null;

            if (arguments == null)
            {
                return false;
            }

            for (int i = 0; i < arguments.Length; ++i)
            {
                if (!string.Equals(arguments[i], option, StringComparison.Ordinal))
                {
                    continue;
                }

                if ((i + 1 >= arguments.Length) ||
                    string.IsNullOrWhiteSpace(arguments[i + 1]))
                {
                    return false;
                }

                path = arguments[i + 1];
                return true;
            }

            return false;
        }

        public bool TryFlushPending()
        {
            if (!HasPendingRecord)
            {
                return true;
            }

            string path = string.IsNullOrWhiteSpace(_recordPathOverride)
                ? Path.Combine(Application.persistentDataPath, RunLeaderboardStore.FILE_NAME)
                : _recordPathOverride;
            while (_pending.Count > 0)
            {
                RunResultSnapshot result = _pending[0];
                if (!RunLeaderboardStore.TryStore(path, result, out _lastFailure))
                {
                    Debug.LogError($"[RunResultRecorder] {_lastFailure}", this);
                    return false;
                }

                _pending.RemoveAt(0);
            }

            _lastFailure = null;
            return true;
        }

        private void HandleResultChanged(RunResultSnapshot result)
        {
            if ((!result.HasAuthoritativeFacts) || (!result.IsLeaderboardEligible))
            {
                return;
            }

            if (!result.HasAuthoritativeScore)
            {
                _lastFailure = "권위 점수가 없는 승리 결과는 기록하지 않는다";
                Debug.LogError($"[RunResultRecorder] {_lastFailure}", this);
                return;
            }

            for (int i = 0; i < _pending.Count; ++i)
            {
                RunResultSnapshot pending = _pending[i];
                if ((pending.SessionIdValue == result.SessionIdValue) &&
                    (pending.RunIdValue == result.RunIdValue) &&
                    (pending.ResultRevision == result.ResultRevision))
                {
                    return;
                }
            }

            _pending.Add(result);
            TryFlushPending();
        }
    }
}
