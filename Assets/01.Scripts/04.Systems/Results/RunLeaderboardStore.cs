// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary> 서버 확정 Standard 승리 결과를 호스트 로컬 JSON에 중복 없이 기록한다 </summary>
    public static class RunLeaderboardStore
    {
        [Serializable]
        private sealed class RecordFile
        {
            public int SchemaVersion = CURRENT_SCHEMA_VERSION;
            public List<RunResultSnapshot> Records = new List<RunResultSnapshot>();
        }

        private const int CURRENT_SCHEMA_VERSION = 2;
        private const int LOCK_RETRY_COUNT = 100;
        private const int LOCK_RETRY_DELAY_MILLISECONDS = 25;
        private const int WRITE_RETRY_COUNT = 4;
        private const int WRITE_RETRY_DELAY_MILLISECONDS = 25;
        public const string FILE_NAME = "run-leaderboard.json";

        public static bool TryStore(string path, in RunResultSnapshot result, out string failure)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                failure = "기록 경로가 비어 있다";
                return false;
            }

            if ((!result.HasAuthoritativeFacts) || (!result.HasRecordKey) ||
                (!result.IsLeaderboardEligible))
            {
                failure = "리더보드에 기록할 수 없는 결과다";
                return false;
            }

            if (result.ScoreRuleVersion <= 0)
            {
                failure = "점수 규칙 판이 없는 승리 결과는 기록할 수 없다";
                return false;
            }

            if ((!Enum.IsDefined(typeof(RunEvaluationGrade), result.EvaluationGrade)) ||
                (result.EvaluationGrade == RunEvaluationGrade.None))
            {
                failure = "유효한 승리 등급이 없는 결과는 기록할 수 없다";
                return false;
            }

            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(path);
            }
            catch (Exception exception)
            {
                failure = $"리더보드 기록 경로가 올바르지 않다: {exception.Message}";
                return false;
            }

            if (!TryAcquireExclusiveLock(normalizedPath, out FileStream writeLock, out failure))
            {
                return false;
            }

            using (writeLock)
            {
                if (!TryLoad(normalizedPath, out RecordFile file, out failure))
                {
                    return false;
                }

                for (int i = 0; i < file.Records.Count; ++i)
                {
                    RunResultSnapshot stored = file.Records[i];
                    if ((stored.SessionIdValue == result.SessionIdValue) &&
                        (stored.RunIdValue == result.RunIdValue) &&
                        (stored.ResultRevision == result.ResultRevision))
                    {
                        failure = null;
                        return true;
                    }
                }

                file.Records.Add(result);
                return TryWriteAtomically(normalizedPath, file, out failure);
            }
        }

        public static bool TryLoadResults(string path, out RunResultSnapshot[] results,
            out string failure)
        {
            results = Array.Empty<RunResultSnapshot>();
            if (!TryLoad(path, out RecordFile file, out failure))
            {
                return false;
            }

            results = file.Records.ToArray();
            return true;
        }

        public static bool TryLoadResults(string path, RunPartyKind partyKind,
            out RunResultSnapshot[] results, out string failure)
        {
            if (!TryLoadResults(path, out RunResultSnapshot[] all, out failure))
            {
                results = Array.Empty<RunResultSnapshot>();
                return false;
            }

            results = Array.FindAll(all, result => result.PartyKind == partyKind);
            return true;
        }

        private static bool TryLoad(string path, out RecordFile file, out string failure)
        {
            file = new RecordFile();
            failure = null;

            if (!File.Exists(path))
            {
                return true;
            }

            try
            {
                string json = File.ReadAllText(path);
                RecordFile loaded = JsonUtility.FromJson<RecordFile>(json);
                if ((loaded == null) ||
                    ((loaded.SchemaVersion != 1) && (loaded.SchemaVersion != CURRENT_SCHEMA_VERSION)) ||
                    (loaded.Records == null))
                {
                    failure = "지원하지 않는 리더보드 기록 형식이다";
                    return false;
                }

                if (loaded.SchemaVersion == 1)
                {
                    for (int i = 0; i < loaded.Records.Count; ++i)
                    {
                        RunResultSnapshot legacy = loaded.Records[i];
                        legacy.PartyKind = RunPartyKind.UnknownLegacy;
                        if (legacy.Participants != null)
                        {
                            for (int j = 0; j < legacy.Participants.Length; ++j)
                            {
                                legacy.Participants[j].ParticipantKind = ParticipantKind.Human;
                            }
                        }

                        loaded.Records[i] = legacy;
                    }
                }

                loaded.SchemaVersion = CURRENT_SCHEMA_VERSION;
                file = loaded;
                return true;
            }
            catch (Exception exception)
            {
                failure = $"리더보드 기록을 읽지 못했다: {exception.Message}";
                return false;
            }
        }

        private static bool TryWriteAtomically(string path, RecordFile file, out string failure)
        {
            failure = null;
            string json;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                json = JsonUtility.ToJson(file, true);
            }
            catch (Exception exception)
            {
                failure = $"리더보드 기록을 저장하지 못했다: {exception.Message}";
                return false;
            }

            for (int attempt = 0; attempt < WRITE_RETRY_COUNT; ++attempt)
            {
                string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
                try
                {
                    File.WriteAllText(temporaryPath, json);

                    if (File.Exists(path))
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                    else
                    {
                        File.Move(temporaryPath, path);
                    }

                    return true;
                }
                catch (IOException exception)
                {
                    TryDeleteTemporary(temporaryPath);
                    if (attempt == (WRITE_RETRY_COUNT - 1))
                    {
                        failure = $"리더보드 기록을 저장하지 못했다: {exception.Message}";
                        return false;
                    }

                    Thread.Sleep(WRITE_RETRY_DELAY_MILLISECONDS);
                }
                catch (Exception exception)
                {
                    TryDeleteTemporary(temporaryPath);
                    failure = $"리더보드 기록을 저장하지 못했다: {exception.Message}";
                    return false;
                }
            }

            failure = "리더보드 기록을 저장하지 못했다";
            return false;
        }

        /// <summary> 여러 로컬 프로세스가 같은 파일에 쓸 때 읽기-추가-교체 전체를 직렬화한다 </summary>
        private static bool TryAcquireExclusiveLock(string path, out FileStream writeLock,
            out string failure)
        {
            writeLock = null;
            failure   = null;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
            catch (Exception exception)
            {
                failure = $"리더보드 기록 디렉터리를 준비하지 못했다: {exception.Message}";
                return false;
            }

            string lockPath = path + ".lock";
            for (int attempt = 0; attempt < LOCK_RETRY_COUNT; ++attempt)
            {
                try
                {
                    writeLock = new FileStream(lockPath, FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None);
                    return true;
                }
                catch (IOException exception)
                {
                    if (attempt == (LOCK_RETRY_COUNT - 1))
                    {
                        failure = $"리더보드 기록 잠금을 얻지 못했다: {exception.Message}";
                        return false;
                    }

                    Thread.Sleep(LOCK_RETRY_DELAY_MILLISECONDS);
                }
                catch (Exception exception)
                {
                    failure = $"리더보드 기록 잠금을 열지 못했다: {exception.Message}";
                    return false;
                }
            }

            failure = "리더보드 기록 잠금을 얻지 못했다";
            return false;
        }

        private static void TryDeleteTemporary(string temporaryPath)
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[RunLeaderboardStore] 임시 기록을 지우지 못했다: {exception.Message}");
            }
        }
    }
}
