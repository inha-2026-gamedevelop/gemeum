// Unity
using UnityEngine;
using UnityEngine.EventSystems;

// 서드파티
using Mirror;

// 프로젝트 내부
using Geumeum.Common;
using Geumeum.Data;
using Geumeum.Network;

namespace Geumeum.Combat
{
    /// <summary>
    /// 방울 R의 설치 조준. 로컬 소유자 화면에서만 돌아가는 상태이고,
    /// 실제 설치 가능 여부는 서버가 같은 규칙으로 다시 판정한다
    ///
    /// 이 상태가 활성인 동안 공격, 치유, 락온 입력은 여기서 먼저 소비된다 -
    /// 확인 클릭이 평타로, 취소 클릭이 치유로 새지 않아야 한다
    /// </summary>
    public partial class PlayerAttack
    {
        /// <summary> 서버 응답을 이 시간 안에 못 받으면 조준을 다시 쓸 수 있게 푼다(초) </summary>
        private const double PLACEMENT_RESPONSE_TIMEOUT = 2d;

        private bool    _placementAiming;
        private bool    _placementValid;
        private Vector3 _placementPoint;
        private bool    _placementAwaitingServer;
        private double  _placementRequestDeadline;
        private double  _nextPreviewRefresh;
        private uint _placementRun, _placementScene, _placementRequest;
        private bool _serverPlacementAiming;

        // 소유자 화면이 서버와 같은 반경을 그리도록 서버가 확정해 내려 준다
        [SyncVar] private float _previewRadius;
        [SyncVar] private float _previewCost = 15f;

        public bool    IsPlacingField        => _placementAiming;
        public bool    IsPlacementValid      => _placementAiming && _placementValid;
        public Vector3 PlacementPoint        => _placementPoint;
        public bool    IsPlacementPending    => _placementAwaitingServer;

        public bool BlocksTalentMutation => _serverPlacementAiming || _placementAiming ||
            HasGrantedFieldEffects || EchoDiscountRemaining > 0f || CounterMarks > 0 || HasCombatTalentToken ||
            (_status != null && (_status.IsDead || _status.IsSwallowed || _status.IsHitReacting)) ||
            IsActionInProgress(NetworkTime.time) || (_roll != null && _roll.IsRolling) ||
            (_parry != null && _parry.IsActionInProgress(NetworkTime.time)) ||
            (_abilityHeal != null && _abilityHeal.IsActionInProgress(NetworkTime.time));

        /// <summary> 미리보기에 쓸 반경. 서버가 아직 안 내려 줬으면 기본값을 쓴다 </summary>
        public float PlacementPreviewRadius =>
            (_previewRadius > 0f) ? _previewRadius
            : ((GameDB.IsInitialized && (GameDB.Combat != null)) ? GameDB.Combat.BellFieldRadius : 0f);

        /****************************************
        *              Local Input
        ****************************************/

        private void UpdateBellPlacement()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            bool eligible = IsPlacementEligible();
            if (_placementAiming && (!eligible || !LocalPlacementAim.IsActive))
            {
                EndPlacement();
                return;
            }

            if (!_placementAiming)
            {
                if (Input.GetKeyDown(KeyCode.R)) { RequestBeginBellPlacement(); }

                return;
            }

            // 수락된 요청이 실제 행동으로 이어졌으면 조준을 닫는다
            if (_placementAwaitingServer)
            {
                if (IsWeaponSkillActive)
                {
                    _placementAwaitingServer = false;
                    EndPlacement();
                    return;
                }

                if (NetworkTime.time >= _placementRequestDeadline)
                {
                    _placementAwaitingServer = false;
                }
            }

            // 취소는 어느 쪽이든 조준만 닫는다. Esc는 이번 입력을 여기서 쓰고
            // 다음 Esc부터 기존 메뉴 동작으로 돌아간다
            if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Mouse1) ||
                Input.GetKeyDown(KeyCode.Escape))
            {
                EndPlacement();
                return;
            }

            UpdatePlacementPoint();

            if (!Input.GetKeyDown(KeyCode.Mouse0))
            {
                return;
            }

            // UI 버튼 위의 클릭으로는 설치하지 않는다
            if ((EventSystem.current != null) &&
                EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            // 대기 중 추가 클릭도 소비한다. 두 번 눌러 두 번 설치되지 않는다
            if (_placementAwaitingServer)
            {
                return;
            }

            if (!_placementValid)
            {
                ActionRejectionChat.Show(ActionRejection.InvalidInput, placement: true);
                LocalCombatState.RaiseActionRejected(ActionRejection.InvalidInput);
                return;
            }

            _placementRequestDeadline = NetworkTime.time + PLACEMENT_RESPONSE_TIMEOUT;
            RequestBellFieldPlacement(_placementPoint);
        }

        private bool IsPlacementEligible()
        {
            if ((!NetworkClient.active) || (!LocalGameplayInputGate.AllowsGameplayInput) ||
                (!GameDB.IsInitialized) || (GameDB.Combat == null) ||
                (_weapon == null) || (!_weapon.HasWeapon) || (_weapon.Weapon != WeaponKind.Bell) ||
                (_status == null) || _status.IsDead || _status.IsSwallowed || _status.IsHitReacting ||
                ((_roll != null) && _roll.IsRolling))
            {
                return false;
            }

            RunManager       run   = RunManager.LocalInstance;
            RunSceneDirector scene = RunSceneDirector.LocalInstance;
            if ((run == null) || (scene == null) || run.IsPaused ||
                scene.State != RunTransitionState.Idle ||
                !PlayerActionGate.CanAct(run.Phase, _status.LifeState, run.IsPaused) ||
                (_placementAiming && (run.RunId.Value != _placementRun || scene.Revision.Value != _placementScene)))
            {
                return false;
            }

            // 조준에 들어가지 못하는 이유는 진입 시점에만 본다. 이미 조준 중이면
            // 쿨다운이 돌기 시작했다고 화면을 닫아 버리지 않는다
            return true;
        }

        /// <summary> R 입력과 검증 도구가 같은 로컬 진입/실패 안내를 사용한다. 설치 판정은 서버가 한다 </summary>
        public bool RequestBeginBellPlacement()
        {
            if (!isLocalPlayer || !LocalGameplayInputGate.AllowsGameplayInput ||
                _weapon == null || !_weapon.HasWeapon || _weapon.Weapon != WeaponKind.Bell)
            {
                return false;
            }
            if (_placementAiming) { return true; }
            if (!IsPlacementEligible())
            {
                ActionRejectionChat.Show(ActionRejection.State, usesSpirit: true);
                return false;
            }
            BeginPlacement();
            return _placementAiming;
        }

        private void BeginPlacement()
        {
            if (WeaponSkillCooldownRemaining > 0f)
            {
                ActionRejectionChat.Show(ActionRejection.Busy, cooldown: true);
                LocalCombatState.RaiseActionRejected(ActionRejection.Busy);
                return;
            }

            if (IsActionInProgress(NetworkTime.time))
            {
                ActionRejectionChat.Show(ActionRejection.Busy);
                LocalCombatState.RaiseActionRejected(ActionRejection.Busy);
                return;
            }

            if (_status.Spirit < ResolveLocalFieldCost())
            {
                ActionRejectionChat.Show(ActionRejection.Resource, usesSpirit: true);
                LocalCombatState.RaiseActionRejected(ActionRejection.Resource);
                return;
            }

            _placementAiming         = true;
            _placementRun = RunManager.LocalInstance.RunId.Value;
            _placementScene = RunSceneDirector.LocalInstance.Revision.Value;
            CmdPlacementAiming(true);
            _placementValid          = false;
            _placementAwaitingServer = false;
            LocalPlacementAim.Begin();
            UpdatePlacementPoint();
        }

        private void EndPlacement()
        {
            if (_placementAiming && isLocalPlayer && NetworkClient.active) { CmdPlacementAiming(false); }
            _placementAiming         = false;
            _placementValid          = false;
            _placementAwaitingServer = false;
            LocalPlacementAim.End();
        }

        /// <summary> 커서가 가리키는 지면을 서버와 같은 규칙으로 미리 판정한다 </summary>
        private void UpdatePlacementPoint()
        {
            _placementValid = false;
            Camera camera = Camera.main;
            if ((camera == null) || (GameDB.Arrow == null))
            {
                return;
            }

            float range = GameDB.Combat.Skill(WeaponKind.Bell).Range;
            Ray   ray   = camera.ScreenPointToRay(Input.mousePosition);
            float reach = range + GameDB.Combat.SkillGroundProbeDepth +
                Vector3.Distance(camera.transform.position, transform.position);

            if (!Physics.Raycast(ray, out RaycastHit hit, reach, GameDB.Arrow.WorldMask,
                    QueryTriggerInteraction.Ignore))
            {
                // 지면을 못 맞히면 발밑 높이의 평면에 떨어뜨려 어디를 가리키는지라도 보여 준다
                Plane ground = new Plane(Vector3.up, transform.position);
                if (!ground.Raycast(ray, out float distance))
                {
                    return;
                }

                _placementPoint = ray.GetPoint(distance);
                return;
            }

            _placementPoint = hit.point;
            _placementValid = TryResolveFieldPlacement(hit.point, out Vector3 resolved);
            if (_placementValid)
            {
                _placementPoint = resolved;
            }
        }

        private float ResolveLocalFieldCost()
        {
            float cost = _previewCost;
            if ((_loadoutProvider != null) && (_status != null) &&
                _loadoutProvider.TryBuildLoadout(_status.Participant, out CombatStatsSnapshot stats) &&
                stats.IsValid)
            {
                cost = stats.ResolveSpiritCost(GameDB.Combat.Skill(WeaponKind.Bell).Cost, stats.SkillSpiritCostReduction);
            }

            return cost;
        }

        /****************************************
        *              Server Side
        ****************************************/

        [Command]
        private void CmdPlacementAiming(bool active)
        {
            _serverPlacementAiming = active && _weapon != null && _weapon.HasWeapon &&
                _weapon.Weapon == WeaponKind.Bell && _status != null && !_status.IsDead && !_status.IsSwallowed;
        }

        [TargetRpc]
        private void TargetPlacementResult(uint request, ActionRejection rejection)
        {
            if (!_placementAiming || request != _placementRequest) { return; }
            _placementAwaitingServer = false;
            if (rejection == ActionRejection.None) { EndPlacement(); return; }
            _clientMoveLockUntilTime = 0;
            ActionRejectionChat.Show(rejection, usesSpirit: true,
                cooldown: WeaponSkillCooldownRemaining > 0f, placement: true);
            LocalCombatState.RaiseActionRejected(rejection);
            if (rejection != ActionRejection.InvalidInput || !IsPlacementEligible()) { EndPlacement(); }
        }

        /// <summary>
        /// 소유자 화면의 미리보기 반경을 서버 값으로 맞춘다
        /// 원격 클라이언트는 자기 장비/특성을 서버 계산으로 알 수 없어서 이 값이 필요하다
        /// </summary>
        [Server]
        private void ServerRefreshFieldPreview()
        {
            if ((_weapon == null) || (!_weapon.HasWeapon) || (_weapon.Weapon != WeaponKind.Bell) ||
                (!GameDB.IsInitialized) || (GameDB.Combat == null))
            {
                _previewRadius = 0f;
                return;
            }

            if (AbilityTime < _nextPreviewRefresh)
            {
                return;
            }

            _nextPreviewRefresh = AbilityTime + 1d;
            if ((_loadoutProvider != null) &&
                _loadoutProvider.TryBuildLoadout(ResolveParticipantId(), out CombatStatsSnapshot stats) &&
                stats.IsValid)
            {
                _previewRadius = stats.FieldRadius;
                _previewCost = stats.ResolveSpiritCost(GameDB.Combat.Skill(WeaponKind.Bell).Cost, stats.SkillSpiritCostReduction);
                return;
            }

            _previewRadius = GameDB.Combat.BellFieldRadius;
        }
    }
}
