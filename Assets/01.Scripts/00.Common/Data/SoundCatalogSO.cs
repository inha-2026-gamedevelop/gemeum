// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Audio;

// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Data
{
    /// <summary> 키 하나가 어떤 클립을 어느 믹서 그룹으로 내보내는지의 표 </summary>
    [CreateAssetMenu(fileName = "SoundCatalogSO", menuName = "Geumeum/SoundCatalogSO")]
    public class SoundCatalogSO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct Entry
        {
            public SoundKey     Key;
            public SoundChannel Channel;

            /// <summary> 이 키의 클립 후보. 여러 개면 재생마다 하나를 무작위로 고른다 </summary>
            public AudioClip[] Clips;

            /// <summary> 위치를 갖지 않는 소리인지 </summary>
            public bool Is2D;

            [Range(0f, 1f)]
            public float Volume;

            /// <summary> 재생마다 피치를 이 값만큼 위아래로 흔든다 </summary>
            [Range(0f, 0.5f)]
            public float PitchJitter;

            /// <summary> 같은 발신자가 같은 키를 다시 재생하기까지의 최소 간격(초) </summary>
            public float MinInterval;

            /// <summary> 후보 중 하나를 고른다, 비어 있으면 null </summary>
            public AudioClip PickClip()
            {
                if ((Clips == null) || (Clips.Length == 0))
                {
                    return null;
                }

                if (Clips.Length == 1)
                {
                    return Clips[0];
                }

                return Clips[UnityEngine.Random.Range(0, Clips.Length)];
            }
        }

        [Serializable]
        public struct BgmEntry
        {
            public BgmTrack  Track;
            public AudioClip Clip;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("믹서 - GeumeumMixer.mixer")]
        [SerializeField] private AudioMixer      _mixer;
        [SerializeField] private AudioMixerGroup _bgm;
        [SerializeField] private AudioMixerGroup _localCritical;
        [SerializeField] private AudioMixerGroup _localNormal;
        [SerializeField] private AudioMixerGroup _remote;
        [SerializeField] private AudioMixerGroup _world;

        [Header("원격 구분 (SOUND_DESIGN 4.2)")]
        [SerializeField] private float _remotePitchOffset = -0.05f;

        [Header("BGM")]
        [SerializeField] private BgmEntry[] _bgmEntries;

        [SerializeField] private float _bgmCrossFadeSeconds = 1.5f;

        [SerializeField] private float _killCueSilenceSeconds = 3f;

        [Header("SFX 엔트리")]
        [SerializeField] private Entry[] _entries;

        private Dictionary<SoundKey, Entry> _lookup;

        /****************************************
        *              Properties
        ****************************************/

        public AudioMixer      Mixer => _mixer;
        public AudioMixerGroup Bgm   => _bgm;

        public float RemotePitchOffset    => _remotePitchOffset;
        public float BgmCrossFadeSeconds  => _bgmCrossFadeSeconds;
        public float KillCueSilenceSeconds => _killCueSilenceSeconds;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnValidate()
        {
            _lookup = null;
        }

        private void OnEnable()
        {
            _lookup = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 키에 해당하는 엔트리를 찾는다, 등록되지 않았으면 false </summary>
        public bool TryGetEntry(SoundKey key, out Entry entry)
        {
            EnsureLookup();
            return _lookup.TryGetValue(key, out entry);
        }

        /// <summary> 이 소리가 나갈 믹서 그룹을 고른다 </summary>
        public AudioMixerGroup ResolveGroup(SoundChannel channel, bool isLocalPlayer)
        {
            if (channel == SoundChannel.World)
            {
                return _world;
            }

            if (!isLocalPlayer)
            {
                return _remote;
            }

            return (channel == SoundChannel.PlayerCritical) ? _localCritical : _localNormal;
        }

        /// <summary> BGM 트랙의 클립을 찾는다, 조달 전이면 null </summary>
        public AudioClip GetBgmClip(BgmTrack track)
        {
            if (_bgmEntries == null)
            {
                return null;
            }

            for (int i = 0; i < _bgmEntries.Length; ++i)
            {
                if (_bgmEntries[i].Track == track)
                {
                    return _bgmEntries[i].Clip;
                }
            }

            return null;
        }

        /// <summary> 효과음 클립만 모은다 </summary>
        public void CollectSfxClips(List<AudioClip> clips)
        {
            if (_entries == null)
            {
                return;
            }

            for (int i = 0; i < _entries.Length; ++i)
            {
                AudioClip[] candidates = _entries[i].Clips;

                if (candidates == null)
                {
                    continue;
                }

                for (int j = 0; j < candidates.Length; ++j)
                {
                    if (candidates[j] != null)
                    {
                        clips.Add(candidates[j]);
                    }
                }
            }
        }

        /// <summary> 비어 있는 믹서 참조 이름을 모은다. GameDB 부트 검사가 쓴다 </summary>
        public void CollectMissingGroups(List<string> missing)
        {
            if (_mixer == null)
            {
                missing.Add("SoundCatalog.Mixer");
            }

            if (_bgm == null)
            {
                missing.Add("SoundCatalog.BGM");
            }

            if (_localCritical == null)
            {
                missing.Add("SoundCatalog.Local_Critical");
            }

            if (_localNormal == null)
            {
                missing.Add("SoundCatalog.Local_Normal");
            }

            if (_remote == null)
            {
                missing.Add("SoundCatalog.SFX_Remote");
            }

            if (_world == null)
            {
                missing.Add("SoundCatalog.SFX_World");
            }
        }

        private void EnsureLookup()
        {
            if (_lookup != null)
            {
                return;
            }

            _lookup = new Dictionary<SoundKey, Entry>();

            if (_entries == null)
            {
                return;
            }

            for (int i = 0; i < _entries.Length; ++i)
            {
                SoundKey key = _entries[i].Key;

                if (key == SoundKey.None)
                {
                    continue;
                }

                // 키가 겹치면 어느 쪽이 이길지 인스펙터 순서에 달리게 되므로 조용히 넘기지 않는다
                if (_lookup.ContainsKey(key))
                {
                    Debug.LogError($"[SoundCatalogSO] 키가 중복됐다: {key}, 뒤의 엔트리를 무시한다", this);
                    continue;
                }

                _lookup.Add(key, _entries[i]);
            }
        }
    }
}
