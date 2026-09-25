// System
using System.Collections;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.AI
{
    /// <summary>
    /// 잡귀 패링 창 강조. 판정에는 관여하지 않는 표현 전용 컴포넌트 - 이슈 #52: 기존에는
    /// 공격 모션 시작과 동시에 창이 열린 것처럼 보여 실제 판정 구간(모션 중반)과 신호가 어긋났다
    ///
    /// 타이밍 섬광(COMBAT_STYLE_DESIGN S4)과 한 신호로 통합했다. 둘이 따로 나오지 않는다
    /// - 로컬 플레이어가 대상: ParryController가 받은 확정 창(로드아웃 보너스 포함)으로 강조하고,
    ///   창 시작에 패링 가능 반짝임 + 소리를 얹는다. 늦게 받았으면 남은 구간만 강조하고 반짝임, 소리는 없다
    /// - 비대상: 확정 창을 받지 못하므로 EnemyController.ParryWindowVisuallyOpenChanged(근사, 보너스 미반영)로
    ///   강조한다. 근사 구간이 열리는 순간에 절반 세기 반짝임과 원격 그룹 소리를 한 번 내고, 이후는 어두운 금색이다.
    ///   근사 구간이 이미 열린 뒤에 켜졌으면 지난 반짝임, 소리를 새로 내지 않는다
    /// Telegraph 진입 자체는 창 시작으로 쓰지 않는다
    /// </summary>
    [RequireComponent(typeof(EnemyController))]
    public sealed class EnemyParryWindowFlash : MonoBehaviour
    {
        private static readonly Color PARRY_WINDOW_COLOR = new Color(1f, 0.92f, 0.4f, 1f); // 옅은 금색, 예고 발광과 구분되는 강조색

        private IEnemyPresentationSource _controller;
        private NetworkIdentity          _identity;
        private ParryFlashTint           _tint;

        // 근사 강조(SyncVar)가 켜져 있는가
        private bool _isApproximateOpen;

        // 이번 공격은 로컬 플레이어가 대상이라 확정 창이 신호를 맡는다. 켜져 있는 동안 근사 강조를 무시한다
        private bool _exactClaimsAttack;

        // 이번 근사 구간의 비대상 반짝임, 소리를 냈거나 늦게 받아 건너뛰었다. 근사 구간이 닫힐 때 푼다
        private bool      _approximateFlashConsumed;
        private double    _approximateFlashStart;
        private Coroutine _approximateRoutine;

        private bool           _hasExactWindow;
        private ParryWindowKey _exactKey;
        private double         _exactStart;
        private double         _exactEnd;
        private bool           _exactRemainingOnly;
        private Coroutine      _exactRoutine;

        private void Awake()
        {
            _controller = GetComponent<EnemyController>();
            _identity   = GetComponent<NetworkIdentity>();
            _tint       = new ParryFlashTint(transform, includeInactive: true, meshRenderersOnly: false);
        }

        private void OnEnable()
        {
            _controller.ParryWindowVisuallyOpenChanged += HandleParryWindowVisuallyOpenChanged;
            LocalParryWindowFeed.Opened  += HandleExactWindowOpened;
            LocalParryWindowFeed.Closed  += HandleExactWindowClosed;
            LocalParryWindowFeed.Cleared += HandleExactWindowCleared;

            // 확정 창을 먼저 줍는다. 근사 강조보다 먼저 대상 여부를 알아야 비대상 강조가 섞이지 않는다
            if (LocalParryWindowFeed.TryGetActive(NetworkTime.time, out ParryWindowSignal active))
            {
                HandleExactWindowOpened(active);
            }

            // 켜지기 전에 열린 근사 구간은 시작 시각을 모른다. 남은 구간만 강조하고 반짝임, 소리는 없다
            ApplyApproximateState(_controller.IsParryWindowVisuallyOpen, isLate: true);
        }

        private void OnDisable()
        {
            _controller.ParryWindowVisuallyOpenChanged -= HandleParryWindowVisuallyOpenChanged;
            LocalParryWindowFeed.Opened  -= HandleExactWindowOpened;
            LocalParryWindowFeed.Closed  -= HandleExactWindowClosed;
            LocalParryWindowFeed.Cleared -= HandleExactWindowCleared;

            StopExactWindow();
            StopApproximateFlash();
            _exactClaimsAttack        = false;
            _isApproximateOpen        = false;
            _approximateFlashConsumed = false;
            _tint.Clear();
        }

        /****************************************
        *        Approximate (non-target)
        ****************************************/

        private void HandleParryWindowVisuallyOpenChanged(bool isOpen)
        {
            ApplyApproximateState(isOpen, isLate: false);
        }

        private void ApplyApproximateState(bool isOpen, bool isLate)
        {
            _isApproximateOpen = isOpen;

            if (!isOpen)
            {
                // 근사 구간이 끝났다. 확정 창도 끝났으면 이번 공격의 대상 표시를 푼다
                if (!_hasExactWindow)
                {
                    _exactClaimsAttack = false;
                }

                _approximateFlashConsumed = false; // 다음 공격의 근사 구간은 다시 반짝인다
                RefreshApproximate();
                return;
            }

            if (isLate)
            {
                _approximateFlashConsumed = true;
            }

            RefreshApproximate();
        }

        private void RefreshApproximate()
        {
            // 확정 창이 도는 동안에는 그쪽이 색을 소유한다
            if (_hasExactWindow)
            {
                StopApproximateFlash();
                return;
            }

            if ((!_isApproximateOpen) || (_exactClaimsAttack))
            {
                StopApproximateFlash();
                _tint.Clear();
                return;
            }

            // 로컬 플레이어가 없으면(에디터 검증, 접속 전) 대상/비대상을 가를 기준이 없다. 기존 금색 강조를 그대로 쓴다
            if ((NetworkClient.localPlayer == null) || (!GameDB.IsInitialized))
            {
                StopApproximateFlash();
                _tint.ApplySolid(PARRY_WINDOW_COLOR);
                return;
            }

            // 같은 근사 구간에서 반짝임, 소리는 한 번뿐이다. 도는 중이면 코루틴이 색을 소유한다
            if (!_approximateFlashConsumed)
            {
                _approximateFlashConsumed = true;
                StartApproximateFlash();
                return;
            }

            if (_approximateRoutine != null)
            {
                return;
            }

            // 비대상 근사 강조 - 대상 강조와 같은 금색을 어둡게 해 "내 창"과 구분한다
            _tint.ApplySolid(ParryFlashTint.ForAudience(PARRY_WINDOW_COLOR, false,
                GameDB.Presentation.ParryFlashNonTargetScale));
        }

        private void StartApproximateFlash()
        {
            StopApproximateFlash();

            // 서버 창 시작 시각을 받지 못하므로 근사 구간이 열렸다는 동기화 도착 시각을 시작으로 쓴다
            _approximateFlashStart = NetworkTime.time;
            ParryFlashSound.Play(ParryFlashKind.Parryable, false, transform.position,
                (_identity != null) ? _identity.netId : 0u);
            _approximateRoutine = StartCoroutine(RunApproximateFlash());
        }

        private IEnumerator RunApproximateFlash()
        {
            PresentationConfigSO presentation = GameDB.Presentation;
            float nonTargetScale = presentation.ParryFlashNonTargetScale;
            Color hold           = ParryFlashTint.ForAudience(PARRY_WINDOW_COLOR, false, nonTargetScale);
            Color flash          = ParryFlashTint.ForAudience(presentation.ParryFlashParryableColor, false, nonTargetScale);
            float shapeSeconds   = ParryFlashTiming.ShapeSeconds(ParryFlashKind.Parryable, presentation.ParryFlashSeconds);

            while (true)
            {
                float blink = ParryFlashTiming.EvaluateShape(ParryFlashKind.Parryable,
                    NetworkTime.time - _approximateFlashStart, shapeSeconds);
                if (blink <= 0f)
                {
                    break;
                }

                _tint.ApplySolid(Color.Lerp(hold, flash, ParryFlashTiming.AudienceWeight(blink, false, nonTargetScale)));
                yield return null;
            }

            _approximateRoutine = null;
            _tint.ApplySolid(hold);
        }

        private void StopApproximateFlash()
        {
            if (_approximateRoutine != null)
            {
                StopCoroutine(_approximateRoutine);
                _approximateRoutine = null;
            }
        }

        /****************************************
        *            Exact (target)
        ****************************************/

        private void HandleExactWindowOpened(ParryWindowSignal signal)
        {
            if ((_identity == null) || (signal.Key.AttackerNetId != _identity.netId))
            {
                return;
            }

            if ((_hasExactWindow) && (_exactKey == signal.Key))
            {
                return;
            }

            double now = NetworkTime.time;

            // 닫힌 창은 표시하지 않는다
            if (now >= signal.EndServerTime)
            {
                return;
            }

            StopExactWindow();
            StopApproximateFlash();

            _exactClaimsAttack  = true;
            _hasExactWindow     = true;
            _exactKey           = signal.Key;
            _exactStart         = signal.StartServerTime;
            _exactEnd           = signal.EndServerTime;
            _exactRemainingOnly = (now > signal.StartServerTime); // 늦게 받았다 - 지난 시작 반짝임은 재생하지 않는다

            _tint.Clear(); // 비대상 근사 강조가 켜져 있었으면 걷어낸다
            _exactRoutine = StartCoroutine(RunExactWindow());
        }

        private void HandleExactWindowClosed(ParryWindowKey key)
        {
            if ((_hasExactWindow) && (_exactKey == key))
            {
                FinishExactWindow();
            }
        }

        private void HandleExactWindowCleared()
        {
            if (_hasExactWindow)
            {
                FinishExactWindow();
            }
        }

        private IEnumerator RunExactWindow()
        {
            bool soundPlayed = _exactRemainingOnly;
            bool holdApplied = false; // 반짝임이 끝난 뒤 금색은 한 번만 덮고 매 프레임 다시 쓰지 않는다

            while (true)
            {
                double now = NetworkTime.time;
                if (now >= _exactEnd)
                {
                    break;
                }

                if (now < _exactStart)
                {
                    yield return null;
                    continue;
                }

                if (!soundPlayed)
                {
                    soundPlayed = true;
                    ParryFlashSound.Play(ParryFlashKind.Parryable, true, transform.position,
                        (_identity != null) ? _identity.netId : 0u);
                }

                if (!holdApplied)
                {
                    bool isBlinking = TryResolveBlinkColor(now, out Color blinkColor);
                    _tint.ApplySolid(isBlinking ? blinkColor : PARRY_WINDOW_COLOR);
                    holdApplied = !isBlinking;
                }

                yield return null;
            }

            _exactRoutine = null;
            FinishExactWindow();
        }

        // 창 동안 금색 강조, 시작 직후에는 패링 가능 반짝임을 얹는다. 반짝임이 끝났으면 false
        private bool TryResolveBlinkColor(double now, out Color color)
        {
            color = PARRY_WINDOW_COLOR;
            if ((_exactRemainingOnly) || (!GameDB.IsInitialized))
            {
                return false;
            }

            PresentationConfigSO presentation = GameDB.Presentation;
            float blink = ParryFlashTiming.EvaluateShape(ParryFlashKind.Parryable, now - _exactStart,
                ParryFlashTiming.ShapeSeconds(ParryFlashKind.Parryable, presentation.ParryFlashSeconds));
            if (blink <= 0f)
            {
                return false;
            }

            color = Color.Lerp(PARRY_WINDOW_COLOR, presentation.ParryFlashParryableColor, blink);
            return true;
        }

        private void FinishExactWindow()
        {
            StopExactWindow();
            _tint.Clear();

            // 근사 강조가 아직 켜져 있으면 같은 공격의 늦은 SyncVar다. 꺼질 때까지 대상 표시를 유지한다
            if (!_isApproximateOpen)
            {
                _exactClaimsAttack = false;
            }
        }

        private void StopExactWindow()
        {
            if (_exactRoutine != null)
            {
                StopCoroutine(_exactRoutine);
                _exactRoutine = null;
            }

            _hasExactWindow = false;
            _exactKey       = default;
        }
    }
}
