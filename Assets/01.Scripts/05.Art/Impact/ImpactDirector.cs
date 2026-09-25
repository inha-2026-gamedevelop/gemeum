// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Combat;
using Geumeum.Common;
using Geumeum.Data;

namespace Geumeum.Art
{
    /// <summary>
    /// 서버가 확정한 내 피해 결과로 타격 표현을 재생하는 한 곳(COMBAT_STYLE_DESIGN 5, 5.1, S1, S2, S3, S5, S6, S7, S9)
    /// 내 멈춤, 셰이크, 화면 플래시는 공격 실행 단위로 한 번, 대상 멈춤과 움찔은 결과마다 재생한다
    /// 서버가 인정한 극한 회피(S7)는 가장자리 어둡게와 고음만 재생한다. 대상 멈춤, 전역 슬로우모션, 보상은 없다
    /// 동료가 잡귀를 때린 것(LocalObservedHitFeed)은 그 잡귀의 방향 움찔만 재생한다. 멈춤, 셰이크, 플래시는 없다(8.1)
    /// 전부 내 화면의 표현이다. 판정, 경직 시간, 서버 시계는 건드리지 않는다
    /// CombatFeedbackImpl이 같은 GameObject에 붙인다
    /// </summary>
    [DefaultExecutionOrder(EXECUTION_ORDER)]
    [RequireComponent(typeof(CameraRig))]
    public sealed class ImpactDirector : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        // 다른 LateUpdate(애니메이터 뒤 뼈 보정, 카메라 추적)보다 늦게 돌아야 움찔이 덮이지 않는다
        private const int EXECUTION_ORDER = 10000;

        // 한 프레임에 서로 다른 공격 실행이 이보다 많이 오지 않는다
        private const int MAX_EXECUTIONS_PER_FRAME = 32;

        /****************************************
        *                Fields
        ****************************************/

        private readonly ImpactExecutionMerger _merger = new ImpactExecutionMerger(MAX_EXECUTIONS_PER_FRAME);
        private readonly KillChainDamper       _killChain = new KillChainDamper();

        private readonly Dictionary<uint, ImpactTargetView> _targets = new Dictionary<uint, ImpactTargetView>();
        private readonly List<ImpactTargetView>             _targetList = new List<ImpactTargetView>();

        private CameraRig          _rig;
        private ImpactScreenFlash  _flash;

        // 내 캐릭터 캐시. 스폰, 재접속으로 바뀌면 다시 찾는다
        private PlayerStatus              _cachedLocal;
        private PlayerAnimationController _localAnimation;
        private PlayerWeapon              _localWeapon;

        // 대상 멈춤, 움찔 시계(초). 실제 시간으로 흐르되 런 일시정지 중에는 멈춘다 - 공격자 히트스톱과 같은 관례
        private double _clock = 0d;

        // T5 공동 순간이 화면을 쓰는 동안 개인 카메라, 플래시를 더하지 않는다(5.1)
        private double _partyMomentUntil = 0d;

        private bool _loggedOverflow = false;

        // 받아베기 먹 번짐(S6)을 마지막으로 재생한 공격 실행. 한 실행이 여러 적을 맞혀도 한 번만 재생한다
        private uint _lastInkAttackerNetId = 0u;
        private uint _lastInkExecutionId   = 0u;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 늦게 도착해 개인 표현을 버린 결과 수. 누락 통계용 </summary>
        public int LateDiscardCount { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _rig   = GetComponent<CameraRig>();
            _flash = new ImpactScreenFlash(transform);
        }

        private void OnEnable()
        {
            LocalDamageResultFeed.Received += HandleResult;
            LocalDamageResultFeed.Cleared  += HandleCleared;

            LocalPerfectDodgeFeed.Received += HandlePerfectDodge;
            LocalPerfectDodgeFeed.Cleared  += HandlePerfectDodgeCleared;

            LocalObservedHitFeed.Received += HandleObservedHit;
        }

        private void OnDisable()
        {
            LocalDamageResultFeed.Received -= HandleResult;
            LocalDamageResultFeed.Cleared  -= HandleCleared;

            LocalPerfectDodgeFeed.Received -= HandlePerfectDodge;
            LocalPerfectDodgeFeed.Cleared  -= HandlePerfectDodgeCleared;

            LocalObservedHitFeed.Received -= HandleObservedHit;

            ResetAll();
        }

        private void OnDestroy()
        {
            if (_flash != null)
            {
                _flash.Dispose();
            }
        }

        private void LateUpdate()
        {
            float delta = Time.unscaledDeltaTime;
            if (!RunTimeLocator.Current.IsPaused)
            {
                _clock += Mathf.Max(0f, delta);
            }

            _flash.Tick(delta);

            if (!GameDB.IsInitialized)
            {
                _merger.Clear();
                return;
            }

            PlayExecutions();
            TickTargets();
        }

        /****************************************
        *              Public API
        ****************************************/

        /// <summary> 패링 성공. 기존 셰이크는 호출부가 주고 여기서는 T3 화면 플래시만 켠다. 멈춤은 없다(5.1) </summary>
        public void PlayParryFlash()
        {
            if (IsInPartyMoment())
            {
                return;
            }

            PlayTierFlash(ImpactTier.Heavy);
        }

        /// <summary> 그로기 진입, 꼬리 파괴(T5). 파티 전원이 같은 플래시를 보고 그동안 개인 카메라 효과를 더하지 않는다 </summary>
        public void PlayPartyMoment(float shakeSeconds)
        {
            if (!GameDB.IsInitialized)
            {
                return;
            }

            PresentationConfigSO.ImpactTierSpec spec = GameDB.Presentation.GetImpactTier(ImpactTier.Party);
            float holdSeconds = Mathf.Max(spec.ScreenFlashSeconds, Mathf.Max(0f, shakeSeconds));

            _partyMomentUntil = System.Math.Max(_partyMomentUntil, _clock + holdSeconds);
            PlayTierFlash(ImpactTier.Party);
        }

        /****************************************
        *            Result Handling
        ****************************************/

        /// <summary> 결과 하나. 대상 쪽 표현은 바로 걸고 내 쪽 표현은 실행별로 모아 LateUpdate에서 한 번 재생한다 </summary>
        private void HandleResult(DamageResult result)
        {
            if (!GameDB.IsInitialized)
            {
                return;
            }

            PresentationConfigSO presentation = GameDB.Presentation;

            // 늦게 온 결과는 개인 표현만 버린다. 피해와 숫자는 이 경로와 무관하다
            // 서버 시각으로 잰다. 복제된 게임 시각은 결과와 같이 늦게 와서 지연이 보이지 않는다
            if (ImpactRules.IsLateResult(result, NetworkTime.time, presentation.LateResultDiscardSeconds))
            {
                ++LateDiscardCount;
                return;
            }

            TryPlayCounterInk(result, presentation);

            ImpactTier tier = ImpactTierResolver.Resolve(result);
            tier = _killChain.Apply(tier, result, presentation.KillChainWindowSeconds);

            if (tier == ImpactTier.Whiff)
            {
                return;
            }

            PlayTargetSide(result, tier, presentation);

            if (!_merger.Add(result, tier) && !_loggedOverflow)
            {
                _loggedOverflow = true;
                Debug.LogWarning($"[ImpactDirector] 한 프레임 공격 실행이 {_merger.Capacity}개를 넘어 일부 카메라 효과를 생략했다", this);
            }
        }

        /// <summary> 런, 씬이 바뀌었다. 남은 표현을 모두 되돌리고 버린다 </summary>
        private void HandleCleared()
        {
            ResetAll();
        }

        /// <summary>
        /// 표식 3개 받아베기(S6)의 먹 번짐. 한 실행에 한 번, 재생 중이면 겹치지 않는다. 입력은 잠그지 않는다
        /// T5 공동 순간이 화면을 쓰는 동안과 카메라 연출 배수 0에서는 끈다
        /// </summary>
        private void TryPlayCounterInk(in DamageResult result, PresentationConfigSO presentation)
        {
            if (!ImpactRules.ShouldPlayCounterInk(result.Source, result.Detail, result.AttackerNetId,
                    result.ExecutionId, _lastInkAttackerNetId, _lastInkExecutionId))
            {
                return;
            }

            _lastInkAttackerNetId = result.AttackerNetId;
            _lastInkExecutionId   = result.ExecutionId;

            float scale = ResolveEffectScale();
            if ((scale <= 0f) || IsInPartyMoment())
            {
                return;
            }

            _flash.PlayInk(presentation.CounterInkColor, presentation.CounterInkSeconds,
                presentation.CounterInkAlpha * scale);
        }

        /****************************************
        *          Perfect Dodge (S7)
        ****************************************/

        /// <summary>
        /// 서버가 인정한 극한 회피. 1단계는 보상 없이 표현만 - 화면 가장자리를 어둡게, 짧은 고음
        /// 가장자리 어둡게는 단색 UI 오버레이다. 월드 채도를 낮추지 않는다. 적 대상 멈춤과 전역 슬로우모션은 걸지 않는다
        /// 잔상은 이번 단계에서 재생하지 않는다 - 캐릭터 잔상으로 쓸 기존 표현이 없다
        /// </summary>
        private void HandlePerfectDodge(double serverTime)
        {
            if (!GameDB.IsInitialized)
            {
                return;
            }

            PresentationConfigSO presentation = GameDB.Presentation;

            // 늦게 온 알림은 표현을 버린다. 판정은 서버에서 이미 끝났다
            if (ImpactRules.IsLate(NetworkTime.time, serverTime, presentation.LateResultDiscardSeconds))
            {
                ++LateDiscardCount;
                return;
            }

            PlayPerfectDodgeSound();

            float scale = ResolveEffectScale();
            if ((scale <= 0f) || IsInPartyMoment())
            {
                return;
            }

            // 검정 단색으로 가장자리를 어둡게 한다. 색은 "어둡게"라는 뜻 자체라 설정으로 빼지 않는다
            _flash.PlayDarkEdge(Color.black, presentation.PerfectDodgeVignetteSeconds,
                presentation.PerfectDodgeVignetteAlpha * scale);
        }

        private void HandlePerfectDodgeCleared()
        {
            if (_flash != null)
            {
                _flash.StopDarkEdge();
            }
        }

        /// <summary> 회피자 자신의 소리다. 카탈로그에 클립이 없으면 SfxPlayer가 키마다 한 번 경고하고 소리 없이 지나간다 </summary>
        private static void PlayPerfectDodgeSound()
        {
            PlayerStatus local = PlayerStatus.Local;
            if (local == null)
            {
                SfxPlayer.Play(SoundKey.PerfectDodge, true);
                return;
            }

            SfxPlayer.Play(SoundKey.PerfectDodge, local.transform.position, true, local.netId);
        }

        /// <summary> 카메라 연출 배수. 설정이 없으면 1 </summary>
        private static float ResolveEffectScale()
        {
            CameraConfigSO camera = GameDB.Camera;
            return (camera != null) ? camera.EffectScale : 1f;
        }

        /// <summary> 대상 멈춤(S1)과 방향 움찔(S2). 무기 배수는 곱하지 않는다 - 활도 대상은 멈춘다 </summary>
        private void PlayTargetSide(in DamageResult result, ImpactTier tier, PresentationConfigSO presentation)
        {
            ImpactTargetView view = ResolveTarget(result.TargetNetId, result.TargetIsBoss);
            if (view == null)
            {
                return;
            }

            PresentationConfigSO.ImpactTierSpec spec = presentation.GetImpactTier(tier);

            float stopSeconds = result.TargetIsBoss ? spec.BossTargetHitStopSeconds : spec.TargetHitStopSeconds;
            view.RequestStop(_clock, stopSeconds, presentation.TargetHitStopMaxSeconds);

            view.RequestFlinch(_clock, spec.FlinchDegrees, spec.FlinchSeconds, result.HitDirection,
                presentation.FlinchRepeatGuardSeconds);
        }

        /// <summary>
        /// 동료(또는 봇)가 잡귀를 때렸다. 그 잡귀의 방향 움찔만 재생한다(8.1, S2)
        /// 같은 대상의 움찔 반복 방지는 내 결과 경로와 같은 ImpactTargetView가 맡아 둘이 겹쳐 떨리지 않는다
        /// </summary>
        private void HandleObservedHit(LocalObservedHitFeed.ObservedHit hit)
        {
            if (!GameDB.IsInitialized)
            {
                return;
            }

            ImpactTargetView view = ResolveTarget(hit.TargetNetId, false);
            if (view == null)
            {
                return;
            }

            PresentationConfigSO presentation = GameDB.Presentation;
            PresentationConfigSO.ImpactTierSpec spec = presentation.GetImpactTier(hit.Tier);

            view.RequestFlinch(_clock, spec.FlinchDegrees, spec.FlinchSeconds, hit.Direction,
                presentation.FlinchRepeatGuardSeconds);
        }

        private ImpactTargetView ResolveTarget(uint netId, bool isBoss)
        {
            if (_targets.TryGetValue(netId, out ImpactTargetView cached))
            {
                if (!cached.IsGone)
                {
                    return cached;
                }

                RemoveTarget(cached);
            }

            // 사망, 디스폰 뒤 도착한 결과는 대상 표현을 생략한다(8.1)
            if (!NetworkClient.spawned.TryGetValue(netId, out NetworkIdentity identity) || (identity == null))
            {
                return null;
            }

            ImpactTargetView view = new ImpactTargetView(identity, isBoss);
            _targets[netId] = view;
            _targetList.Add(view);
            return view;
        }

        private void RemoveTarget(ImpactTargetView view)
        {
            _targets.Remove(view.NetId);
            _targetList.Remove(view);
        }

        /****************************************
        *           Execution Playback
        ****************************************/

        /// <summary> 이번 프레임에 모인 공격 실행마다 가장 높은 단계로 내 멈춤, 셰이크, 플래시를 한 번 재생한다 </summary>
        private void PlayExecutions()
        {
            if (_merger.Count == 0)
            {
                return;
            }

            PresentationConfigSO presentation = GameDB.Presentation;
            CameraConfigSO       camera       = GameDB.Camera;
            bool                 partyMoment  = IsInPartyMoment();

            RefreshLocal();

            for (int i = 0; i < _merger.Count; ++i)
            {
                ImpactExecutionMerger.Entry entry = _merger.Get(i);
                PresentationConfigSO.ImpactTierSpec spec = presentation.GetImpactTier(entry.Tier);

                PlayAttackerStop(entry, spec, presentation);

                if (partyMoment)
                {
                    continue;
                }

                PlayShake(ImpactRules.ResolveShake(entry.Tier, entry.Result.Source), camera);
                PlayTierFlash(entry.Tier);
            }

            _merger.Clear();
        }

        private void PlayAttackerStop(in ImpactExecutionMerger.Entry entry,
            in PresentationConfigSO.ImpactTierSpec spec, PresentationConfigSO presentation)
        {
            if ((_localAnimation == null) || (_cachedLocal == null) || _cachedLocal.IsDead)
            {
                return;
            }

            WeaponKind fallback = (_localWeapon != null) ? _localWeapon.Weapon : WeaponKind.Sword;
            WeaponKind weapon   = ImpactRules.ResolveWeapon(entry.Result.Source, fallback);

            float seconds = ImpactRules.ResolveAttackerHitStop(entry.Result.Source, weapon,
                spec.AttackerHitStopSeconds, presentation.SwordHitStopScale,
                presentation.BowRangedAttackerHitStopScale, presentation.BellHitStopScale,
                presentation.BowStabHitStopSeconds);

            if (seconds > 0f)
            {
                _localAnimation.PlayHitStop(seconds);
            }
        }

        private void PlayShake(ImpactShakeKind kind, CameraConfigSO camera)
        {
            if ((_rig == null) || (camera == null))
            {
                return;
            }

            switch (kind)
            {
                case ImpactShakeKind.HitLight:
                    _rig.Shake(camera.ResolveHitShake(HitStrength.Light));
                    break;

                case ImpactShakeKind.SwordSkill:
                    _rig.Shake(camera.ShakeSwordSkill);
                    break;

                case ImpactShakeKind.BowStab:
                    _rig.Shake(camera.ShakeBowStab);
                    break;

                case ImpactShakeKind.Critical:
                    _rig.Shake(camera.ShakeCritical);
                    break;

                case ImpactShakeKind.EchoBurst:
                    _rig.Shake(camera.ShakeEchoBurst);
                    break;
            }
        }

        /// <summary> 단계의 화면 가장자리 플래시. 카메라 연출 배수가 0이면 끈다 </summary>
        private void PlayTierFlash(ImpactTier tier)
        {
            if (!GameDB.IsInitialized)
            {
                return;
            }

            CameraConfigSO camera = GameDB.Camera;
            float          scale  = (camera != null) ? camera.EffectScale : 1f;
            if (scale <= 0f)
            {
                return;
            }

            PresentationConfigSO.ImpactTierSpec spec = GameDB.Presentation.GetImpactTier(tier);
            _flash.Play(GameDB.Presentation.ScreenFlashColor, spec.ScreenFlashSeconds, spec.ScreenFlashAlpha * scale);
        }

        private bool IsInPartyMoment()
        {
            return _clock < _partyMomentUntil;
        }

        /// <summary> 내 캐릭터가 바뀌었을 때만 컴포넌트를 다시 찾는다 </summary>
        private void RefreshLocal()
        {
            PlayerStatus local = PlayerStatus.Local;
            if (local == _cachedLocal)
            {
                return;
            }

            _cachedLocal    = local;
            _localAnimation = (local != null) ? local.GetComponent<PlayerAnimationController>() : null;
            _localWeapon    = (local != null) ? local.GetComponent<PlayerWeapon>() : null;
        }

        /****************************************
        *            Target Playback
        ****************************************/

        /// <summary> 대상 멈춤을 재고 움찔을 얹는다. 파괴된 대상의 캐시는 여기서 버린다 </summary>
        private void TickTargets()
        {
            if (_targetList.Count == 0)
            {
                return;
            }

            float cooldown = GameDB.Presentation.TargetHitStopCooldownSeconds;

            for (int i = _targetList.Count - 1; i >= 0; --i)
            {
                ImpactTargetView view = _targetList[i];
                if (view.IsGone)
                {
                    // 파괴된 대상은 되돌릴 애니메이터가 없다
                    _targets.Remove(view.NetId);
                    _targetList.RemoveAt(i);
                    continue;
                }

                view.TickStop(_clock, cooldown);
                view.ApplyFlinch(_clock);
            }
        }

        /// <summary> 멈춘 애니메이터, 젖힌 뼈, 플래시, 대기 중인 실행을 모두 되돌린다 </summary>
        private void ResetAll()
        {
            float cooldown = GameDB.IsInitialized ? GameDB.Presentation.TargetHitStopCooldownSeconds : 0f;

            for (int i = 0; i < _targetList.Count; ++i)
            {
                if (!_targetList[i].IsGone)
                {
                    _targetList[i].RestoreAll(_clock, cooldown);
                }
            }

            _targets.Clear();
            _targetList.Clear();
            _merger.Clear();
            _killChain.Reset();
            _partyMomentUntil = 0d;

            _lastInkAttackerNetId = 0u;
            _lastInkExecutionId   = 0u;

            if (_flash != null)
            {
                _flash.Stop();
            }
        }
    }
}
