// System
using System.Collections;

// Unity
using UnityEngine;

// 프로젝트 내부
using Geumeum.Data;

namespace Geumeum.Common
{
    /// <summary>
    /// BGM 재생. 트랙이 바뀌면 두 소스를 교차 페이드해 이어 붙인다.
    /// 사용자 볼륨은 믹서의 노출 파라미터가 담당하므로 여기서는 페이드 비율만 다룬다
    /// </summary>
    public class BgmPlayer : PersistentSingleton<BgmPlayer>
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OBJECT_NAME = "@BgmPlayer";

        private AudioSource _activeSource;
        private AudioSource _idleSource;

        private Coroutine _coCrossFade;
        private Coroutine _coSilence;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막으로 재생에 성공한 트랙. 되돌아가야 하는 연출이 조회한다 </summary>
        public BgmTrack CurrentTrack { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnSingletonAwake()
        {
            _activeSource = CreateSource("Bgm_A");
            _idleSource   = CreateSource("Bgm_B");
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 씬에 배치하지 않았어도 인스턴스를 보장한다 </summary>
        public static BgmPlayer Ensure()
        {
            return EnsureCreated(OBJECT_NAME);
        }

        /// <summary>
        /// 트랙을 재생한다. 이미 다른 곡이 흐르고 있으면 교차 페이드로 넘어간다.
        /// 클립이 조달 전이면 아무 일도 일어나지 않고 CurrentTrack도 바뀌지 않는다
        /// </summary>
        public void Play(BgmTrack track, bool isLoop = true)
        {
            if (!GameDB.IsInitialized)
            {
                Debug.LogWarning("[BgmPlayer] GameDB 초기화 전에 재생을 요청했다, 이 요청은 무시된다");
                return;
            }

            SoundCatalogSO catalog = GameDB.Sound;
            AudioClip      clip    = catalog.GetBgmClip(track);

            // 조달 전이라는 뜻이고 결함이 아니다
            if (clip == null)
            {
                return;
            }

            StopSilence();

            float duration = catalog.BgmCrossFadeSeconds;

            if ((_activeSource == null) || (!_activeSource.isPlaying) || (duration <= 0f))
            {
                PlayImmediately(clip, isLoop);
            }
            else
            {
                StartCrossFade(clip, isLoop, duration);
            }

            CurrentTrack = track;
        }

        /// <summary>
        /// 지금 흐르는 곡을 끊고, 무음을 둔 뒤 다음 트랙을 재생한다.
        /// 꼬리가 마지막으로 떨어질 때의 무음 3초가 이것이다.
        /// 무음 길이를 넘기지 않으면 SoundCatalogSO의 값을 쓴다
        /// </summary>
        public void PlayAfterSilence(BgmTrack track, float silenceSeconds = -1f)
        {
            if (!GameDB.IsInitialized)
            {
                Debug.LogWarning("[BgmPlayer] GameDB 초기화 전에 재생을 요청했다, 이 요청은 무시된다");
                return;
            }

            if (silenceSeconds < 0f)
            {
                silenceSeconds = GameDB.Sound.KillCueSilenceSeconds;
            }

            StopSilence();
            _coSilence = StartCoroutine(CoPlayAfterSilence(track, silenceSeconds));
        }

        /// <summary> 완전 정지. 진행 중인 페이드와 무음 대기도 함께 끊는다 </summary>
        public void Stop()
        {
            StopCrossFade();
            StopSilence();

            if (_activeSource != null)
            {
                _activeSource.Stop();
            }

            if (_idleSource != null)
            {
                _idleSource.Stop();
            }

            CurrentTrack = BgmTrack.None;
        }

        /// <summary> 일시정지. 이탈자를 기다리는 동안 곡의 위치를 잃지 않는다 </summary>
        public void Pause()
        {
            if (_activeSource != null)
            {
                _activeSource.Pause();
            }

            if (_idleSource != null)
            {
                _idleSource.Pause();
            }
        }

        /// <summary> 일시정지한 곡을 그 자리에서 재개한다 </summary>
        public void UnPause()
        {
            if (_activeSource != null)
            {
                _activeSource.UnPause();
            }

            if (_idleSource != null)
            {
                _idleSource.UnPause();
            }
        }

        private IEnumerator CoPlayAfterSilence(BgmTrack track, float silenceSeconds)
        {
            StopCrossFade();

            if (_activeSource != null)
            {
                _activeSource.Stop();
            }

            if (_idleSource != null)
            {
                _idleSource.Stop();
            }

            CurrentTrack = BgmTrack.None;

            // 히트스톱이나 슬로우모션이 걸려도 무음 길이가 늘어나면 안 된다
            yield return new WaitForSecondsRealtime(silenceSeconds);

            _coSilence = null;
            Play(track);
        }

        private void PlayImmediately(AudioClip clip, bool isLoop)
        {
            StopCrossFade();

            _idleSource.Stop();
            Configure(_activeSource, clip, isLoop, 1f);
        }

        private void StartCrossFade(AudioClip clip, bool isLoop, float duration)
        {
            StopCrossFade();

            _idleSource.Stop();

            AudioSource outgoing = _activeSource;
            AudioSource incoming = _idleSource;

            // 이전 페이드가 중간에 끊겼으면 나가는 소스가 이미 작아져 있다, 그 지점에서 이어 줄인다
            float outgoingGain = outgoing.volume;

            Configure(incoming, clip, isLoop, 0f);

            _activeSource = incoming;
            _idleSource   = outgoing;

            _coCrossFade = StartCoroutine(CoCrossFade(outgoing, incoming, outgoingGain, duration));
        }

        private IEnumerator CoCrossFade(AudioSource outgoing, AudioSource incoming, float outgoingGain, float duration)
        {
            float progress = 0f;

            while (progress < 1f)
            {
                // 일시정지나 슬로우모션에 페이드가 끌려가지 않게 한다
                progress += Time.unscaledDeltaTime / duration;

                float t = Mathf.Clamp01(progress);
                outgoing.volume = (1f - t) * outgoingGain;
                incoming.volume = t;

                yield return null;
            }

            outgoing.Stop();
            outgoing.volume = 0f;

            _coCrossFade = null;
        }

        private void Configure(AudioSource source, AudioClip clip, bool isLoop, float volume)
        {
            // Awake가 GameDB보다 빨랐으면 그룹이 비어 있다. 비면 소리가 Master로 새어 나가 덕킹이 걸리지 않는다
            if (source.outputAudioMixerGroup == null)
            {
                source.outputAudioMixerGroup = GameDB.Sound.Bgm;
            }

            source.clip   = clip;
            source.loop   = isLoop;
            source.volume = volume;
            source.time   = 0f;
            source.Play();
        }

        private void StopCrossFade()
        {
            if (_coCrossFade == null)
            {
                return;
            }

            StopCoroutine(_coCrossFade);
            _coCrossFade = null;
        }

        private void StopSilence()
        {
            if (_coSilence == null)
            {
                return;
            }

            StopCoroutine(_coSilence);
            _coSilence = null;
        }

        private AudioSource CreateSource(string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(transform, false);

            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake  = false;
            source.loop         = true;
            source.volume       = 0f;
            source.spatialBlend = 0f; // BGM은 위치를 갖지 않는다

            if (GameDB.IsInitialized)
            {
                source.outputAudioMixerGroup = GameDB.Sound.Bgm;
            }

            return source;
        }
    }
}
