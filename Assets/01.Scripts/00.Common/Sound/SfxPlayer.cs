// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Audio;

// Third party
using Mirror;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.Common
{
    /// <summary> 효과음 재생 진입점. 클립, 믹서 그룹, 중복 제거를 여기서 정한다. 클라이언트 로컬 표현이고 서버에서 호출하지 않는다 </summary>
    public static class SfxPlayer
    {
        /****************************************
        *                Types
        ****************************************/

        /// <summary> 마지막 재생의 흔적. 검증용이고 게임 로직이 읽지 않는다 </summary>
        [System.Serializable]
        public struct PlayRecord
        {
            public SoundKey Key;
            public uint     EmitterId;
            public bool     IsLocalPlayer;
            public string   GroupName;
            public float    Pitch;
            public bool     Is2D;
            public Vector3  Position;
            public float    MinDistance;
            public float    MaxDistance;
            public int      Frame;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const string ROOT_NAME = "@SfxPlayer";

        private static AudioSource[] _sources;
        private static int           _nextSource;

        // 키, 로컬 여부, 발신자를 함께 잡는다
        private static readonly Dictionary<long, float> _lastPlayTime  = new Dictionary<long, float>();
        private static readonly Dictionary<long, int>   _lastPlayFrame = new Dictionary<long, int>();

        private static readonly HashSet<SoundKey> _warnedKeys = new HashSet<SoundKey>();

        private static readonly List<AudioClip> _preloadBuffer = new List<AudioClip>();

        private static bool _warnedNoCatalog;
        private static bool _isPaused;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막으로 재생한 것. 무엇이 어느 그룹으로 나갔는지 밖에서 확인하는 유일한 창구다 </summary>
        public static PlayRecord Last { get; private set; }

        /// <summary> 지금까지 실제로 재생한 횟수. 중복 제거에 걸린 요청은 세지 않는다 </summary>
        public static int PlayedCount { get; private set; }

        /// <summary> 전체 효과음이 멈춰 있는가 </summary>
        public static bool IsPaused => _isPaused;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 효과음을 재생한다. 개인 알림은 소유자만 듣고 원격 플레이어 효과음은 위치와 카탈로그 거리로 감쇠한다. emitterId로 주체별 중복을 제거한다 </summary>
        public static void Play(SoundKey key, Vector3 position, bool isLocalPlayer, uint emitterId = 0)
        {
            PlayInternal(key, position, isLocalPlayer, emitterId);
        }

        /// <summary> 위치가 없는 호출부용. UI, 결계 경고처럼 낸 자리가 없는 소리에 쓴다 </summary>
        public static void Play(SoundKey key, bool isLocalPlayer, uint emitterId = 0)
        {
            PlayInternal(key, Vector3.zero, isLocalPlayer, emitterId);
        }

        /// <summary> 재생 중인 효과음을 전부 멈춘다 </summary>
        public static void PauseAll()
        {
            if ((_isPaused) || (_sources == null))
            {
                return;
            }

            _isPaused = true;

            for (int i = 0; i < _sources.Length; ++i)
            {
                if (_sources[i] != null)
                {
                    _sources[i].Pause();
                }
            }
        }

        /// <summary> 멈춘 효과음을 그 자리에서 재개한다 </summary>
        public static void ResumeAll()
        {
            if ((!_isPaused) || (_sources == null))
            {
                return;
            }

            _isPaused = false;

            for (int i = 0; i < _sources.Length; ++i)
            {
                if (_sources[i] != null)
                {
                    _sources[i].UnPause();
                }
            }
        }

        /// <summary> 효과음 클립을 미리 디코딩한다 </summary>
        public static void Preload()
        {
            SoundCatalogSO catalog = GetCatalog();

            if (catalog == null)
            {
                return;
            }

            _preloadBuffer.Clear();
            catalog.CollectSfxClips(_preloadBuffer);

            for (int i = 0; i < _preloadBuffer.Count; ++i)
            {
                AudioClip clip = _preloadBuffer[i];

                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    clip.LoadAudioData();
                }
            }

            _preloadBuffer.Clear();
        }

        /// <summary> 도메인 리로드 OFF 대비 정적 상태 초기화 </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatic()
        {
            _sources    = null;
            _nextSource = 0;

            _lastPlayTime.Clear();
            _lastPlayFrame.Clear();
            _warnedKeys.Clear();
            _preloadBuffer.Clear();

            _warnedNoCatalog = false;
            _isPaused        = false;

            Last        = default;
            PlayedCount = 0;
        }

        private static void PlayInternal(SoundKey key, Vector3 position, bool isLocalPlayer, uint emitterId)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (!CanPlayForListener(key, isLocalPlayer, NetworkServer.active, NetworkClient.active))
            {
                return;
            }

            if (_isPaused)
            {
                return;
            }

            SoundCatalogSO catalog = GetCatalog();

            if (catalog == null)
            {
                return;
            }

            SoundCatalogSO.Entry entry;

            if (!catalog.TryGetEntry(key, out entry))
            {
                // 키마다 한 번만 경고한다
                if (_warnedKeys.Add(key))
                {
                    Debug.LogWarning($"[SfxPlayer] SoundCatalog에 등록되지 않은 키다: {key}");
                }

                return;
            }

            AudioClip clip = entry.PickClip();

            if (clip == null)
            {
                return;
            }

            if (IsDuplicate(key, isLocalPlayer, emitterId, entry.MinInterval))
            {
                return;
            }

            AudioSource source = NextSource();

            if (source == null)
            {
                return;
            }

            // 풀을 빼앗을 때 이전 OneShot을 먼저 끈다. 살아 있는 소리를 새 위치/2D 설정으로 옮기면 안 된다.
            source.Stop();
            ConfigureSource(source, catalog, entry, position, isLocalPlayer);

            source.PlayOneShot(clip, entry.Volume);

            ++PlayedCount;

            Last = new PlayRecord
            {
                Key           = key,
                EmitterId     = emitterId,
                IsLocalPlayer = isLocalPlayer,
                GroupName     = (source.outputAudioMixerGroup == null) ? "(none)" : source.outputAudioMixerGroup.name,
                Pitch         = source.pitch,
                Is2D          = source.spatialBlend == 0f,
                Position      = source.transform.position,
                MinDistance   = source.minDistance,
                MaxDistance   = source.maxDistance,
                Frame         = Time.frameCount,
            };
        }

        private static bool CanPlayForListener(SoundKey key, bool isLocalPlayer, bool isServer, bool isClient)
        {
            if ((isServer) && (!isClient))
            {
                return false;
            }

            return (!IsOwnerOnly(key)) || (isLocalPlayer);
        }

        private static bool IsOwnerOnly(SoundKey key)
        {
            return (key == SoundKey.StaminaEmpty) || (key == SoundKey.PerfectDodge);
        }

        private static void ConfigureSource(AudioSource source, SoundCatalogSO catalog,
            SoundCatalogSO.Entry entry, Vector3 position, bool isLocalPlayer)
        {
            bool isRemote = (!isLocalPlayer) && (entry.Channel != SoundChannel.World);
            // 개인 알림은 화면의 신호다. 원격 전투음은 데이터가 2D여도 월드 위치를 유지한다.
            bool is2D = IsOwnerOnly(entry.Key) || ((entry.Is2D) && (!isRemote));
            float pitch = 1f + entry.PitchOffset + Random.Range(-entry.PitchJitter, entry.PitchJitter);

            if (isRemote)
            {
                pitch += catalog.RemotePitchOffset;
            }

            source.outputAudioMixerGroup = catalog.ResolveGroup(entry.Channel, isLocalPlayer);
            source.pitch                 = Mathf.Max(0.01f, pitch);
            source.spatialBlend          = (is2D) ? 0f : 1f;
            source.transform.position    = (is2D) ? Vector3.zero : position;
            source.rolloffMode           = AudioRolloffMode.Linear;
            source.minDistance           = catalog.SfxMinDistance;
            source.maxDistance           = catalog.SfxMaxDistance;
        }

        // 같은 발신자의 같은 소리는 한 프레임에 한 번만 내고, MinInterval 간격도 함께 본다
        private static bool IsDuplicate(SoundKey key, bool isLocalPlayer, uint emitterId, float minInterval)
        {
            long id = ((long)emitterId << 32) | (uint)(((int)key * 2) + (isLocalPlayer ? 1 : 0));

            int lastFrame;

            if ((_lastPlayFrame.TryGetValue(id, out lastFrame)) && (lastFrame == Time.frameCount))
            {
                return true;
            }

            if (minInterval > 0f)
            {
                float lastTime;

                if ((_lastPlayTime.TryGetValue(id, out lastTime)) && ((Time.unscaledTime - lastTime) < minInterval))
                {
                    return true;
                }
            }

            _lastPlayFrame[id] = Time.frameCount;
            _lastPlayTime[id]  = Time.unscaledTime;

            return false;
        }

        private static SoundCatalogSO GetCatalog()
        {
            if (!GameDB.IsInitialized)
            {
                if (!_warnedNoCatalog)
                {
                    _warnedNoCatalog = true;
                    Debug.LogWarning("[SfxPlayer] GameDB 초기화 전에 재생을 요청했다, 이 요청은 무시된다");
                }

                return null;
            }

            return GameDB.Sound;
        }

        // 놀고 있는 소스를 먼저 준다
        private static AudioSource NextSource()
        {
            EnsureSources();

            for (int i = 0; i < _sources.Length; ++i)
            {
                int index = (_nextSource + i) % _sources.Length;

                if (!_sources[index].isPlaying)
                {
                    _nextSource = (index + 1) % _sources.Length;
                    return _sources[index];
                }
            }

            // 전부 울리는 중이면 가장 오래전에 집었던 소스를 다시 쓴다
            AudioSource oldest = _sources[_nextSource];
            _nextSource = (_nextSource + 1) % _sources.Length;

            return oldest;
        }

        private static void EnsureSources()
        {
            if ((_sources != null) && (_sources[0] != null))
            {
                return;
            }

            GameObject root = new GameObject(ROOT_NAME);
            Object.DontDestroyOnLoad(root);

            _sources    = new AudioSource[Constants.Sound.SFX_SOURCE_COUNT];
            _nextSource = 0;

            for (int i = 0; i < Constants.Sound.SFX_SOURCE_COUNT; ++i)
            {
                GameObject child = new GameObject($"Source_{i}");
                child.transform.SetParent(root.transform, false);

                AudioSource source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop        = false;
                source.rolloffMode = AudioRolloffMode.Linear;

                source.dopplerLevel = 0f;

                _sources[i] = source;
            }
        }
    }
}
