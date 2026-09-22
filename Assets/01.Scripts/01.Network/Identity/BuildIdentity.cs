// System
using System;
using System.IO;

// Unity
using UnityEngine;

namespace Geumeum.Network
{
    /// <summary>
    /// 이 실행 파일의 배포 정체성. 빌드 메뉴가 StreamingAssets에 쓰고, 로비와 인증기가 같은 빌드끼리만 붙이는 데 쓴다
    /// 파일 형식은 launcher/Patch.Core의 BuildIdentity와 같아야 한다 - 패키징 도구가 manifest와 대조한다
    /// </summary>
    public static class BuildIdentity
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string FILE_NAME = "build-identity.json";

        /// <summary> Mirror 메시지, 직렬화 계약 버전. 메시지 구조를 바꿀 때만 올린다 </summary>
        public const int PROTOCOL_VERSION = 1;

        /// <summary> 에디터 실행의 식별자. 에디터끼리만 붙는다 </summary>
        public const string EDITOR_RELEASE_ID = "editor";

        /// <summary> 플레이어 빌드인데 정체성 파일이 없거나 읽지 못했을 때. 어떤 배포와도 붙지 않게 한다 </summary>
        public const string UNKNOWN_RELEASE_ID = "unknown";

        /****************************************
        *                Fields
        ****************************************/

        // JsonUtility는 필드 이름을 키로 쓴다. 패키징 도구의 camelCase 계약과 맞추려고 규칙과 다른 이름을 쓴다
        [Serializable]
        public sealed class Data
        {
            public string releaseId;
            public string displayVersion;
            public int    protocolVersion;
            public string contentVersion;
        }

        private static Data _loaded;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 로비 BUILD와 인증 요청 Build에 싣는 값 </summary>
        public static string ReleaseId => Load().releaseId;

        /// <summary> 사람이 읽는 표시용 버전. 비교에 쓰지 않는다 </summary>
        public static string DisplayVersion => Load().displayVersion;

#if UNITY_EDITOR || DEVELOPMENT_BUILD

        private const string ARG_ALLOW_MISMATCH = "-allow-build-mismatch";

        /// <summary> 에디터 메뉴가 켠다. 에디터 호스트에 빌드 클라이언트를 붙일 때 쓴다 </summary>
        public static bool EditorAllowMismatch { get; set; }

        /// <summary> 개발 빌드, 에디터에서만 켤 수 있는 불일치 허용. 릴리스 빌드에는 컴파일되지 않는다 </summary>
        public static bool AllowMismatch
        {
            get
            {
                return (Application.isEditor && EditorAllowMismatch)
                    || (Array.IndexOf(Environment.GetCommandLineArgs(), ARG_ALLOW_MISMATCH) >= 0);
            }
        }

#else

        public static bool AllowMismatch => false;

#endif

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 상대 releaseId가 나와 붙을 수 있는가. 불일치를 허용 중이면 경고만 남기고 true </summary>
        public static bool IsCompatible(string otherReleaseId, string context, out string reason)
        {
            reason = null;
            if (string.Equals(otherReleaseId, ReleaseId, StringComparison.Ordinal)
                && !string.Equals(otherReleaseId, UNKNOWN_RELEASE_ID, StringComparison.Ordinal))
            {
                return true;
            }

            reason = $"빌드가 다르다: {context}={otherReleaseId}, 나={ReleaseId}";
            if (AllowMismatch)
            {
                Debug.LogWarning($"[BuildIdentity] 개발용 불일치 허용으로 통과: {reason}");
                return true;
            }

            return false;
        }

        public static string Serialize(Data data)
        {
            return JsonUtility.ToJson(data, false);
        }

        private static Data Load()
        {
            if (_loaded != null)
            {
                return _loaded;
            }

            string path = Path.Combine(Application.streamingAssetsPath, FILE_NAME);
            _loaded = Read(path);
            Debug.Log($"[BuildIdentity] releaseId={_loaded.releaseId}, version={_loaded.displayVersion}");
            return _loaded;
        }

        private static Data Read(string path)
        {
            if (!File.Exists(path))
            {
                if (Application.isEditor)
                {
                    return Fallback(EDITOR_RELEASE_ID);
                }

                Debug.LogError($"[BuildIdentity] 정체성 파일이 없다, 그믐 빌드 메뉴로 만든 빌드가 아니다: {path}");
                return Fallback(UNKNOWN_RELEASE_ID);
            }

            try
            {
                Data data = JsonUtility.FromJson<Data>(File.ReadAllText(path));
                if ((data == null) || string.IsNullOrEmpty(data.releaseId))
                {
                    Debug.LogError($"[BuildIdentity] releaseId가 비었다: {path}");
                    return Fallback(UNKNOWN_RELEASE_ID);
                }

                return data;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"[BuildIdentity] 정체성 파일을 읽지 못했다: {path}\n{e}");
                return Fallback(UNKNOWN_RELEASE_ID);
            }
        }

        private static Data Fallback(string releaseId)
        {
            return new Data
            {
                releaseId       = releaseId,
                displayVersion  = Application.version,
                protocolVersion = PROTOCOL_VERSION,
                contentVersion  = string.Empty,
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            _loaded = null;
        }
    }
}
