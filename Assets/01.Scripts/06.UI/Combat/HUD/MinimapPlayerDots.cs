// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;

namespace Geumeum.UI
{
    /// <summary>
    /// 미니맵에 파티원 위치를 점으로, 결계를 원으로 그린다. 맵 배경과 회전은 아직 다루지 않는다
    /// 로컬 플레이어를 중앙에 두고 북쪽을 위로 고정하며, 표시 반경 밖은 가장자리에 붙인다
    /// </summary>
    public class MinimapPlayerDots : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 매 프레임 스폰 목록을 다시 훑으면 그 자체가 부하다. 점 위치는 매 프레임 갱신하되 목록만 주기로 만든다
        private const float REFRESH_SECONDS = 0.2f;

        // 결계 밖을 덮는 판의 해상도. 미니맵이 180px라 이보다 크게 잡을 이유가 없다
        private const int DANGER_TEXTURE_SIZE = 256;

        // 원이 이만큼도 안 움직였으면 판을 다시 칠하지 않는다. 축소 30초 동안 매 프레임 칠하면 그것만으로 부하다
        private const float DANGER_REDRAW_EPSILON = 0.5f;

        // 중심 기준으로 anchoredPosition을 계산하므로 프리팹이 무엇이든 앵커를 중앙으로 맞춘다
        private static readonly Vector2 DOT_ANCHOR = new Vector2(0.5f, 0.5f);

        [Header("점")]
        [Tooltip("점 하나짜리 프리팹. Image를 가진 RectTransform이면 된다")]
        [SerializeField] private RectTransform _dotPrefab = null;

        [Header("결계")]
        [Tooltip("결계 밖을 덮을 판. 미니맵 전체에 맞춰 늘려 둔다. 비워두면 결계를 그리지 않는다")]
        [SerializeField] private RawImage _dangerOverlay = null;

        [Tooltip("결계 밖 색. 밖이 위험한 구역이라 여기를 칠한다")]
        [SerializeField] private Color _dangerColor = new Color(0.45f, 0.16f, 0.66f, 0.35f);

        [Tooltip("다음 축소의 목표 원. 비워두면 그리지 않는다")]
        [SerializeField] private RectTransform _targetCircle = null;

        [Header("범위")]
        [Tooltip("미니맵 가장자리가 몇 m를 뜻하는지")]
        [SerializeField] private float _rangeMeters = 200f;

        // 캐릭터 모델의 대표색을 따르되 어두운 맵에서 서로 구분되도록 채도와 명도를 올렸다
        // 무녀는 의상이 거의 검정이라 금색 장신구 쪽을 쓴다. 흰색은 락온 표식이 쓰므로 비워 둔다
        [Header("캐릭터별 색")]
        [SerializeField] private Color _chasaColor   = new Color(0.88f, 0.20f, 0.18f, 1f);
        [SerializeField] private Color _singungColor = new Color(0.35f, 0.62f, 1f, 1f);
        [SerializeField] private Color _munyeoColor  = new Color(1f, 0.78f, 0.25f, 1f);

        [Tooltip("아직 캐릭터가 배정되지 않은 동안 쓴다")]
        [SerializeField] private Color _unknownColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        private readonly List<PlayerStatus> _players = new List<PlayerStatus>(Constants.Player.PARTY_SIZE);

        // 파티 정원만큼만 만들어 두고 돌려 쓴다. 사람이 빠져도 파괴하지 않고 숨기기만 한다
        private readonly List<RectTransform> _dots        = new List<RectTransform>(Constants.Player.PARTY_SIZE);
        private readonly List<Graphic>       _dotGraphics = new List<Graphic>(Constants.Player.PARTY_SIZE);

        private RectTransform _rect;
        private float         _nextRefreshTime = 0f;

        // 판은 한 번 만들어 두고 구멍 위치만 다시 칠한다
        private Texture2D _dangerTexture = null;
        private Color32[] _dangerPixels  = null;
        private Vector2   _dangerHoleCenter = Vector2.zero;
        private float     _dangerHoleRadius = -1f;
        private Color32   _dangerPainted;

        // 점은 지도 배율과 별개로 같은 크기를 유지한다. 지도를 키워도 점까지 커지면 뭉개진다
        private float _dotScale = 1f;

        /// <summary>
        /// 미니맵 가장자리가 몇 m를 뜻하는지. 펼친 지도가 같은 컴포넌트를 쓰면서 범위만 바꾼다
        /// 배율이 달라지면 결계 판을 한 번은 다시 칠해야 하므로 여기서 같이 처리한다
        /// </summary>
        public float RangeMeters
        {
            get => _rangeMeters;
            set
            {
                float wanted = Mathf.Max(value, 0.01f);

                if (Mathf.Approximately(_rangeMeters, wanted))
                {
                    return;
                }

                _rangeMeters      = wanted;
                _dangerHoleRadius = -1f;
            }
        }

        /// <summary>
        /// 점 크기 배율. 지도를 통째로 키우면 점도 같이 커지므로 바깥에서 되돌린다
        /// 프리팹이 들고 있는 크기를 기준으로 곱한다
        /// </summary>
        public float DotScale
        {
            get => _dotScale;
            set
            {
                float wanted = Mathf.Max(value, 0.01f);

                if (Mathf.Approximately(_dotScale, wanted))
                {
                    return;
                }

                _dotScale = wanted;
                ApplyDotScale();
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _rect = transform as RectTransform;

            if (_rect == null)
            {
                Debug.LogError("[MinimapPlayerDots] RectTransform 위에 붙여야 한다", this);
                enabled = false;
                return;
            }

            if (_dotPrefab == null)
            {
                Debug.LogError("[MinimapPlayerDots] 점 프리팹이 비어 있다", this);
                enabled = false;
                return;
            }

            HideDotsFrom(0);
        }

        private void OnDisable()
        {
            _players.Clear();
            _nextRefreshTime = 0f;
            HideDotsFrom(0);

            if (_dangerOverlay != null)
            {
                _dangerOverlay.gameObject.SetActive(false);
            }

            if (_targetCircle != null)
            {
                _targetCircle.gameObject.SetActive(false);
            }

            _dangerHoleRadius = -1f; // 다시 켜졌을 때 한 번은 칠하게 한다
        }

        private void OnDestroy()
        {
            if (_dangerTexture != null)
            {
                Destroy(_dangerTexture);
                _dangerTexture = null;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.unscaledTime + REFRESH_SECONDS;
                RefreshPlayers();
            }

            PlayerStatus local = PlayerStatus.Local;
            if (local == null)
            {
                HideDotsFrom(0);
                return;
            }

            DrawDots(local.transform.position);
            DrawBarrier(local.transform.position);
        }

        /****************************************
        *                Methods
        ****************************************/

        // PartyDebugOverlay와 같은 방식이다. 체력 계열이 SyncVar라 클라이언트도 남의 오브젝트를 읽을 수 있다
        private void RefreshPlayers()
        {
            _players.Clear();

            Dictionary<uint, NetworkIdentity> spawned = (NetworkClient.active)
                ? NetworkClient.spawned
                : NetworkServer.spawned;

            if (spawned == null)
            {
                return;
            }

            foreach (NetworkIdentity identity in spawned.Values)
            {
                if (identity == null)
                {
                    continue;
                }

                if (identity.TryGetComponent(out PlayerStatus status))
                {
                    _players.Add(status);
                }
            }

            // 갱신마다 순서가 바뀌면 어느 점이 누구인지 눈으로 못 따라간다
            _players.Sort(CompareByNetId);
        }

        private static int CompareByNetId(PlayerStatus left, PlayerStatus right)
        {
            return left.netId.CompareTo(right.netId);
        }

        private void DrawDots(Vector3 localPosition)
        {
            float half  = Mathf.Min(_rect.rect.width, _rect.rect.height) * 0.5f;
            float range = Mathf.Max(_rangeMeters, 0.01f);
            int   count = Mathf.Min(_players.Count, Constants.Player.PARTY_SIZE);

            EnsureDotCount(count);

            for (int i = 0; i < count; ++i)
            {
                RectTransform dot = _dots[i];
                if (dot == null)
                {
                    continue;
                }

                PlayerStatus status = _players[i];
                if (status == null)
                {
                    dot.gameObject.SetActive(false);
                    continue;
                }

                Vector3 offset = status.transform.position - localPosition;
                Vector2 planar = new Vector2(offset.x, offset.z) / range;

                // 표시 반경 밖은 잘라내지 않고 가장자리에 붙인다. 어느 쪽에 있는지가 위치보다 중요하다
                if (planar.sqrMagnitude > 1f)
                {
                    planar = planar.normalized;
                }

                dot.gameObject.SetActive(true);
                dot.anchoredPosition = planar * half;

                ApplyDotColor(i, status);
            }

            HideDotsFrom(count);
        }

        // 결계는 서버가 확정한 값을 그대로 읽는다. 판정도 보간도 여기서 하지 않는다
        private void DrawBarrier(Vector3 localPosition)
        {
            IBarrierState state = BarrierStateLocator.Current;

            DrawDanger(state.Center, state.Radius, localPosition);
            DrawTargetCircle(localPosition);
        }

        /// <summary>
        /// 결계 밖을 덮는다. 안쪽이 아니라 바깥이 위험한 구역이라 칠하는 쪽이 반대다
        ///
        /// UI에는 역마스크가 없어서 판 하나를 칠하고 결계 자리만 비운다
        /// 원이 움직인 만큼만 다시 칠하므로 축소 중에도 매 프레임 칠하지 않는다
        /// </summary>
        private void DrawDanger(Vector3 worldCenter, float radius, Vector3 localPosition)
        {
            if (_dangerOverlay == null)
            {
                return;
            }

            // 결계가 없는 단계에서는 반경이 0으로 수렴한다. 이때 판을 깔면 화면 전체가 위험으로 보인다
            if (radius <= 0f)
            {
                _dangerOverlay.gameObject.SetActive(false);
                return;
            }

            float half  = Mathf.Min(_rect.rect.width, _rect.rect.height) * 0.5f;
            float range = Mathf.Max(_rangeMeters, 0.01f);
            float scale = half / range;

            Vector3 offset = worldCenter - localPosition;

            Vector2 holeCenter = new Vector2(offset.x, offset.z) * scale;
            float   holeRadius = radius * scale;

            _dangerOverlay.gameObject.SetActive(true);

            EnsureDangerTexture();

            bool moved = (Vector2.Distance(holeCenter, _dangerHoleCenter) > DANGER_REDRAW_EPSILON)
                      || (Mathf.Abs(holeRadius - _dangerHoleRadius) > DANGER_REDRAW_EPSILON)
                      || (!_dangerPainted.Equals((Color32) _dangerColor));

            if (!moved)
            {
                return;
            }

            _dangerHoleCenter = holeCenter;
            _dangerHoleRadius = holeRadius;
            _dangerPainted    = _dangerColor;

            PaintDanger(holeCenter, holeRadius, half);
        }

        private void EnsureDangerTexture()
        {
            if (_dangerTexture != null)
            {
                return;
            }

            _dangerTexture = new Texture2D(DANGER_TEXTURE_SIZE, DANGER_TEXTURE_SIZE,
                                           TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            _dangerPixels        = new Color32[DANGER_TEXTURE_SIZE * DANGER_TEXTURE_SIZE];
            _dangerOverlay.texture = _dangerTexture;

            // 색은 판이 정하고 RawImage는 그대로 내보낸다. 둘 다 곱해지면 알파가 두 번 먹는다
            _dangerOverlay.color = Color.white;
        }

        /// <summary> 판 전체를 위험 색으로 칠하고 결계 안쪽만 비운다 </summary>
        private void PaintDanger(Vector2 holeCenter, float holeRadius, float half)
        {
            Color32 danger = _dangerColor;
            Color32 clear  = new Color32(danger.r, danger.g, danger.b, 0);

            // 판 한 칸이 미니맵에서 몇 px인지. 구멍 반경을 판 좌표로 옮길 때 쓴다
            float pixelsPerUnit = DANGER_TEXTURE_SIZE / (half * 2f);

            float centerX = (holeCenter.x + half) * pixelsPerUnit;
            float centerY = (holeCenter.y + half) * pixelsPerUnit;
            float radius  = holeRadius * pixelsPerUnit;

            float sqrRadius = radius * radius;

            for (int y = 0; y < DANGER_TEXTURE_SIZE; ++y)
            {
                float dy    = (y + 0.5f) - centerY;
                int   rowTop = y * DANGER_TEXTURE_SIZE;

                for (int x = 0; x < DANGER_TEXTURE_SIZE; ++x)
                {
                    float dx = (x + 0.5f) - centerX;

                    _dangerPixels[rowTop + x] = (((dx * dx) + (dy * dy)) <= sqrRadius) ? clear : danger;
                }
            }

            _dangerTexture.SetPixels32(_dangerPixels);
            _dangerTexture.Apply(false);
        }

        /// <summary>
        /// 다음 축소가 어디로 갈지 미리 보여 준다
        /// 목표 원은 서버가 경고 시점에 확정하므로 그 전에는 그릴 것이 없고,
        /// 축소가 끝나면 현재 원과 겹치므로 지운다
        /// </summary>
        private void DrawTargetCircle(Vector3 localPosition)
        {
            if (_targetCircle == null)
            {
                return;
            }

            BarrierTransitionState snapshot = BarrierStateLocator.TransitionSource.Snapshot;

            // 결계 판정이 쓰는 시계와 같은 것을 본다. 다른 시계를 쓰면 그려진 원과 때리는 원이 어긋난다
            double now = RunTimeLocator.Current.FieldElapsedSeconds;

            if ((now < snapshot.WarningGameplayTime) || (now >= snapshot.EndGameplayTime))
            {
                _targetCircle.gameObject.SetActive(false);
                return;
            }

            DrawCircle(_targetCircle, snapshot.TargetCenter, snapshot.TargetRadius, localPosition);
        }

        private void DrawCircle(RectTransform circle, Vector3 worldCenter, float radius,
                                Vector3 localPosition)
        {
            if (circle == null)
            {
                return;
            }

            // 결계가 없는 단계에서는 반경이 0으로 수렴하므로 이것만으로 가시성 판정이 된다
            if (radius <= 0f)
            {
                circle.gameObject.SetActive(false);
                return;
            }

            float half  = Mathf.Min(_rect.rect.width, _rect.rect.height) * 0.5f;
            float range = Mathf.Max(_rangeMeters, 0.01f);
            float scale = half / range;

            Vector3 offset = worldCenter - localPosition;

            circle.gameObject.SetActive(true);
            circle.anchoredPosition = new Vector2(offset.x, offset.z) * scale;

            float diameter = radius * 2f * scale;
            circle.sizeDelta = new Vector2(diameter, diameter);
        }

        private void HideDotsFrom(int startIndex)
        {
            for (int i = startIndex; i < _dots.Count; ++i)
            {
                if (_dots[i] != null)
                {
                    _dots[i].gameObject.SetActive(false);
                }
            }
        }

        // 필요한 만큼만 늘린다. 파티 정원을 넘겨 만들지 않는다
        private void EnsureDotCount(int wanted)
        {
            int limit = Mathf.Min(wanted, Constants.Player.PARTY_SIZE);

            while (_dots.Count < limit)
            {
                RectTransform dot = Instantiate(_dotPrefab, _rect);
                dot.anchorMin = DOT_ANCHOR;
                dot.anchorMax = DOT_ANCHOR;
                dot.pivot     = DOT_ANCHOR;
                dot.localScale = _dotPrefab.localScale * _dotScale;

                _dots.Add(dot);
                _dotGraphics.Add(dot.GetComponent<Graphic>());
            }
        }

        private void ApplyDotScale()
        {
            Vector3 wanted = _dotPrefab.localScale * _dotScale;

            for (int i = 0; i < _dots.Count; ++i)
            {
                if (_dots[i] != null)
                {
                    _dots[i].localScale = wanted;
                }
            }
        }

        private void ApplyDotColor(int index, PlayerStatus status)
        {
            Graphic graphic = _dotGraphics[index];
            if (graphic == null)
            {
                return;
            }

            Color wanted = ResolveColor(status);
            if (graphic.color != wanted)
            {
                graphic.color = wanted;
            }
        }

        // 캐릭터는 SyncVar라 클라이언트도 남의 것을 읽는다. 로비에서 배정되기 전에는 아직 비어 있다
        private Color ResolveColor(PlayerStatus status)
        {
            if (!status.TryGetComponent(out PlayerCharacter character) || (!character.HasCharacter))
            {
                return _unknownColor;
            }

            switch (character.Character)
            {
                case CharacterKind.Chasa:
                    return _chasaColor;
                case CharacterKind.Singung:
                    return _singungColor;
                case CharacterKind.Munyeo:
                    return _munyeoColor;
                default:
                    return _unknownColor;
            }
        }
    }
}
