using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace Geumeum.Art
{
    /// <summary>할퀴기 한 번의 시각·방향·높이·크기·밝기. 패턴마다 다르게 주입한다.</summary>
    [System.Serializable]
    public struct BossScratchStrike
    {
        [Tooltip("첫 타격을 0초로 둔 발생 시각")]
        public float time;
        [Tooltip("공격 방향을 기준으로 한 좌우 회전(도)")]
        public float yawDegrees;
        [Tooltip("지면 기준 발생 높이(m)")]
        public float heightOffset;
        [Tooltip("전방 밀어내기(m)")]
        public float forwardOffset;
        [Tooltip("기본 반경에 곱하는 배수")]
        public float radiusScale;
        [Tooltip("칼날·코어·잔광의 밝기 배수")]
        public float brightness;

        public static BossScratchStrike Make(float time, float yaw, float height, float radiusScale, float brightness)
        {
            return new BossScratchStrike
            {
                time = time,
                yawDegrees = yaw,
                heightOffset = height,
                forwardOffset = 0f,
                radiusScale = radiusScale,
                brightness = brightness
            };
        }
    }

    /// <summary>BossScratch.vfx에 할퀴기 이벤트를 보내는 표현 전용 구동기. 판정·피해는 포함하지 않는다.
    /// 타격 횟수·간격·반경은 전부 바깥에서 주입한다. 고정 시드와 고정 프레임 간격을 함께 쓰면 같은 결과를 다시 만들 수 있다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VisualEffect))]
    public sealed class BossScratchVfxDriver : MonoBehaviour
    {
        [Header("재생")]
        [SerializeField] private bool _playOnEnable = true;
        [Tooltip("마지막 타격 이후 잔여 입자가 사라지는 데 필요한 여유 시간(초)")]
        [SerializeField, Min(0.1f)] private float _tailDuration = 1.6f;

        [Header("형태")]
        [Tooltip("할퀴기 기본 반경(m). 타격별 radiusScale이 여기에 곱해진다")]
        [SerializeField, Min(0.2f)] private float _radius = 5.25f;
        [SerializeField, Range(0.05f, 4f)] private float _brightness = 1f;
        [SerializeField] private uint _seed = 20260922;

        [Header("타격 구성")]
        [Tooltip("시각 순서대로. 서버 패턴의 타격 횟수·간격에 맞춰 주입한다")]
        [SerializeField]
        private BossScratchStrike[] _strikes =
        {
            new BossScratchStrike { time = 0f,     yawDegrees =  34f, heightOffset = 0.35f, forwardOffset = 0f, radiusScale = 1.00f, brightness = 1.00f },
            new BossScratchStrike { time = 0.817f, yawDegrees = -30f, heightOffset = 0.75f, forwardOffset = 0f, radiusScale = 0.92f, brightness = 0.80f }
        };

        private const string EVENT_SLASH_A = "SlashA";
        private const string EVENT_SLASH_B = "SlashB";

        private static readonly int G_Position = Shader.PropertyToID("position");
        private static readonly int G_AngleY = Shader.PropertyToID("angleY");
        private static readonly int G_Size = Shader.PropertyToID("size");
        private static readonly int G_Color = Shader.PropertyToID("color");

        private VisualEffect _vfx;
        private VFXEventAttribute _attribute;
        private float _time;
        private bool _playing;
        private int _nextStrike;

        /// <summary>구성된 타격 수.</summary>
        public int StrikeCount => _strikes != null ? _strikes.Length : 0;

        /// <summary>마지막 타격 시각 + 꼬리 시간.</summary>
        public float TotalDuration => LastStrikeTime() + _tailDuration;

        /// <summary>자동 재생이 진행 중인지 여부.</summary>
        public bool IsPlaying => _playing;

        /// <summary>첫 타격을 0초로 둔 현재 재생 시각(초).</summary>
        public float ElapsedTime => _time;

        /// <summary>할퀴기 기본 반경(m).</summary>
        public float Radius
        {
            get => _radius;
            set => _radius = Mathf.Max(0.2f, value);
        }

        private void Awake() => Cache();

        private void OnEnable()
        {
            Cache();
            if (_playOnEnable)
            {
                Play();
            }
        }

        private void OnDisable() => Stop();

        private void Update()
        {
            if (!_playing)
            {
                return;
            }
            _time += Time.deltaTime;
            while (_nextStrike < StrikeCount && _time >= _strikes[_nextStrike].time)
            {
                SendStrike(_nextStrike);
                _nextStrike++;
            }
            if (_time >= TotalDuration)
            {
                _playing = false;
            }
        }

        private void Cache()
        {
            if (_vfx == null)
            {
                _vfx = GetComponent<VisualEffect>();
            }
        }

        private float LastStrikeTime()
        {
            float last = 0f;
            for (int i = 0; i < StrikeCount; i++)
            {
                last = Mathf.Max(last, _strikes[i].time);
            }
            return last;
        }

        /// <summary>패턴의 타격 구성을 바깥에서 주입한다. 재생 중이면 다음 Play부터 적용된다.</summary>
        public void SetStrikes(IReadOnlyList<BossScratchStrike> strikes)
        {
            if (strikes == null || strikes.Count == 0)
            {
                return;
            }
            if (_strikes == null || _strikes.Length != strikes.Count)
            {
                _strikes = new BossScratchStrike[strikes.Count];
            }
            for (int i = 0; i < strikes.Count; i++)
            {
                _strikes[i] = strikes[i];
            }
        }

        /// <summary>일정 간격의 N타 구성을 만들어 넣는다. 방향·크기는 타격마다 살짝 어긋나게 한다.</summary>
        public void SetUniformStrikes(int count, float interval, float radius)
        {
            count = Mathf.Max(1, count);
            _radius = Mathf.Max(0.2f, radius);
            var list = new BossScratchStrike[count];
            for (int i = 0; i < count; i++)
            {
                float sign = (i % 2 == 0) ? 1f : -1f;
                list[i] = new BossScratchStrike
                {
                    time = i * Mathf.Max(0.02f, interval),
                    yawDegrees = sign * (26f + (i * 6f)),
                    heightOffset = 0.20f + ((i % 3) * 0.16f),
                    forwardOffset = 0f,
                    radiusScale = 1f - (i * 0.05f),
                    brightness = 1f - (i * 0.10f)
                };
            }
            _strikes = list;
        }

        /// <summary>처음부터 다시 재생한다.</summary>
        public void Play()
        {
            Cache();
            if (_vfx == null)
            {
                return;
            }
            _vfx.pause = false;
            _vfx.resetSeedOnPlay = false;
            _vfx.startSeed = _seed;
            _vfx.Reinit();
            _time = 0f;
            _nextStrike = 0;
            _playing = true;
            while (_nextStrike < StrikeCount && _strikes[_nextStrike].time <= 0f)
            {
                SendStrike(_nextStrike);
                _nextStrike++;
            }
        }

        /// <summary>즉시 중지하고 남은 입자를 정리한다.</summary>
        public void Stop()
        {
            _playing = false;
            _time = 0f;
            _nextStrike = 0;
            if (_vfx == null)
            {
                return;
            }
            _vfx.pause = false;
            _vfx.Reinit();
        }

        private void SendStrike(int index)
        {
            if (_vfx == null)
            {
                return;
            }
            BossScratchStrike strike = _strikes[index];
            if (_attribute == null)
            {
                _attribute = _vfx.CreateVFXEventAttribute();
            }
            _attribute.SetVector3(G_Position, new Vector3(0f, strike.heightOffset, strike.forwardOffset));
            _attribute.SetFloat(G_AngleY, strike.yawDegrees);
            _attribute.SetFloat(G_Size, Mathf.Max(0.05f, _radius * strike.radiusScale));
            float b = Mathf.Max(0f, strike.brightness * _brightness);
            _attribute.SetVector3(G_Color, new Vector3(b, b, b));
            _vfx.SendEvent((index % 2 == 0) ? EVENT_SLASH_A : EVENT_SLASH_B, _attribute);
        }
    }
}
