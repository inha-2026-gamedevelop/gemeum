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
    /// 미니맵에 파티원 위치를 점으로, 결계를 원으로 그린다. 맵 배경과 회전은 아직 다루지 않는다.
    /// 로컬 플레이어를 중앙에 두고 북쪽을 위로 고정하며, 표시 반경 밖은 가장자리에 붙인다
    /// </summary>
    public class MinimapPlayerDots : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 매 프레임 스폰 목록을 다시 훑으면 그 자체가 부하다. 점 위치는 매 프레임 갱신하되 목록만 주기로 만든다
        private const float REFRESH_SECONDS = 0.2f;

        // 중심 기준으로 anchoredPosition을 계산하므로 프리팹이 무엇이든 앵커를 중앙으로 맞춘다
        private static readonly Vector2 DOT_ANCHOR = new Vector2(0.5f, 0.5f);

        [Header("점")]
        [Tooltip("점 하나짜리 프리팹. Image를 가진 RectTransform이면 된다")]
        [SerializeField] private RectTransform _dotPrefab = null;

        [Header("결계")]
        [Tooltip("결계 원을 그릴 Image. 비워두면 결계를 그리지 않는다")]
        [SerializeField] private RectTransform _barrierCircle = null;

        [Header("범위")]
        [Tooltip("미니맵 가장자리가 몇 m를 뜻하는지")]
        [SerializeField] private float _rangeMeters = 100f;

        [Header("캐릭터별 색")]
        [SerializeField] private Color _chasaColor   = Color.black;
        [SerializeField] private Color _singungColor = Color.white;
        [SerializeField] private Color _munyeoColor  = Color.red;

        [Tooltip("아직 캐릭터가 배정되지 않은 동안 쓴다")]
        [SerializeField] private Color _unknownColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        private readonly List<PlayerStatus> _players = new List<PlayerStatus>(Constants.Player.PARTY_SIZE);

        // 파티 정원만큼만 만들어 두고 돌려 쓴다. 사람이 빠져도 파괴하지 않고 숨기기만 한다
        private readonly List<RectTransform> _dots        = new List<RectTransform>(Constants.Player.PARTY_SIZE);
        private readonly List<Graphic>       _dotGraphics = new List<Graphic>(Constants.Player.PARTY_SIZE);

        private RectTransform _rect;
        private float         _nextRefreshTime = 0f;

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

            if (_barrierCircle != null)
            {
                _barrierCircle.gameObject.SetActive(false);
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
            if (_barrierCircle == null)
            {
                return;
            }

            IBarrierState state  = BarrierStateLocator.Current;
            float         radius = state.Radius;

            // 결계가 없는 단계에서는 반경이 0으로 수렴하므로 이것만으로 가시성 판정이 된다
            if (radius <= 0f)
            {
                _barrierCircle.gameObject.SetActive(false);
                return;
            }

            float half  = Mathf.Min(_rect.rect.width, _rect.rect.height) * 0.5f;
            float range = Mathf.Max(_rangeMeters, 0.01f);
            float scale = half / range;

            Vector3 offset = state.Center - localPosition;

            _barrierCircle.gameObject.SetActive(true);
            _barrierCircle.anchoredPosition = new Vector2(offset.x, offset.z) * scale;

            float diameter = radius * 2f * scale;
            _barrierCircle.sizeDelta = new Vector2(diameter, diameter);
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

                _dots.Add(dot);
                _dotGraphics.Add(dot.GetComponent<Graphic>());
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
