#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Mirror;
using Geumeum.AI;
using Geumeum.Combat;
using Geumeum.Common;

namespace Geumeum.Network
{
    public sealed partial class VerificationCommandDriver
    {
        private uint _abilityHold;
        private readonly Dictionary<uint, uint> _abilityHoldsByActor = new Dictionary<uint, uint>();
        [Serializable]
        private sealed class GripVerificationState
        {
            public int character;
            public int weapon;
            public bool active;
            public string parent;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public int weaponModels;
            public Quaternion[] fingerRotations;
            public float bowAxisUp;

            // 구르기 표현 검증용. 세 화면이 같은 방향을 그리는지 비교한다
            public bool isRolling;
            public bool isLockedOn;
            public Vector2 facing;
            public Vector3 visualForward;
            public int animatorState;
        }
        // 네트워크 반격 소비 검증용 공격 시퀀스. 보스 패링 판정 검증을 대체하지 않는다
        private sealed class VerificationCounterSequence : IParryable, IParrySequenceSource
        {
            public uint AttackInstanceId { get; set; }
            public byte RepeatIndex { get; set; }
            public bool IsDeflect => true;
            public bool IsSequenceActive => true;
            public float ParryWindowDuration => .32f;
            public void OnParried(uint defender) { }
        }

        private const BindingFlags AbilityPrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>
        /// 표현 전용 락온 fixture 하나. 서버 판정이나 실제 타겟 검증을 대신하지 않는다
        /// </summary>
        private sealed class RollLockOnFixture
        {
            public GameObject Target;
            public LockOnController Controller;
            public bool ControllerWasEnabled;
        }

        private readonly Dictionary<uint, RollLockOnFixture> _rollLockOnFixtures =
            new Dictionary<uint, RollLockOnFixture>();

        /// <summary> 표현 전용 락온을 풀고 저장해 둔 상태를 되돌린다 </summary>
        private void ReleaseRollLockOnFixture(uint actorNetId, PlayerPresentation presentation)
        {
            if (presentation != null) { presentation.SetLockOnTarget(null); }
            if (!_rollLockOnFixtures.TryGetValue(actorNetId, out RollLockOnFixture fixture))
            { return; }
            if (fixture.Controller != null) { fixture.Controller.enabled = fixture.ControllerWasEnabled; }
            if (fixture.Target != null) { UnityEngine.Object.Destroy(fixture.Target); }
            _rollLockOnFixtures.Remove(actorNetId);
        }

        // 본체 partial에는 OnDisable이 없다. 검증 보조가 남긴 상태만 여기서 되돌린다
        private void OnDisable()
        {
            _abilityHoldsByActor.Clear();
            foreach (var pair in _rollLockOnFixtures)
            {
                RollLockOnFixture fixture = pair.Value;
                if (fixture.Controller != null) { fixture.Controller.enabled = fixture.ControllerWasEnabled; }
                if (fixture.Target != null) { UnityEngine.Object.Destroy(fixture.Target); }
                if (NetworkClient.spawned.TryGetValue(pair.Key, out NetworkIdentity identity) &&
                    (identity != null))
                {
                    var presentation = identity.GetComponent<PlayerPresentation>();
                    if (presentation != null) { presentation.SetLockOnTarget(null); }
                }
            }
            _rollLockOnFixtures.Clear();
        }

        private bool ExecuteAbilityVerification(VerificationCommand command, out string message)
        {
            if (command.verb == "ability_grip_state")
            {
                if (!NetworkClient.spawned.TryGetValue(command.uintValue, out NetworkIdentity target))
                { message = "Client target required"; return false; }
                var character = target.GetComponent<PlayerCharacter>();
                var visual = target.GetComponent<PlayerWeaponVisual>();
                var model = typeof(PlayerWeaponVisual).GetField("_spawnedModel", AbilityPrivate).GetValue(visual) as GameObject;
                var grip = character.ActiveModel == null ? null : character.ActiveModel.GetComponent<Geumeum.Art.PlayerWeaponGrip>();
                if (grip == null || model == null) { message = "Grip and weapon required"; return false; }
                var joints = (Geumeum.Art.PlayerWeaponGrip.FingerJoint[]) typeof(Geumeum.Art.PlayerWeaponGrip)
                    .GetField("_fingerJoints", AbilityPrivate).GetValue(grip);
                int models = 0;
                foreach (Transform bone in character.ActiveModel.GetComponentsInChildren<Transform>())
                {
                    if (bone.name == "Sword(Clone)" || bone.name == "Bow(Clone)" || bone.name == "Bell(Clone)")
                    {
                        ++models;
                    }
                }
                // 구르기 표현 진단. 없으면 0으로 두고 파지 검증 자체는 그대로 진행한다
                var presentation = target.GetComponent<PlayerPresentation>();
                var rollController = target.GetComponent<PlayerRollController>();
                var visualPivot = target.transform.Find("VisualPivot");
                var modelAnimator = target.GetComponentInChildren<Animator>();
                message = JsonUtility.ToJson(new GripVerificationState
                {
                    character = (int) character.Character, weapon = (int) target.GetComponent<PlayerWeapon>().Weapon,
                    active = (bool) typeof(Geumeum.Art.PlayerWeaponGrip).GetField("_isGripActive", AbilityPrivate).GetValue(grip),
                    parent = model.transform.parent.name, localPosition = model.transform.localPosition,
                    localRotation = model.transform.localRotation, weaponModels = models,
                    fingerRotations = Array.ConvertAll(joints, joint => joint.Bone.localRotation),
                    bowAxisUp = Mathf.Abs(model.transform.forward.y),
                    isRolling = rollController != null && rollController.IsRolling,
                    isLockedOn = presentation != null && presentation.IsLockedOn,
                    facing = presentation == null ? Vector2.zero : presentation.FacingDirection,
                    visualForward = visualPivot == null ? Vector3.zero : visualPivot.forward,
                    animatorState = modelAnimator == null ? 0
                        : modelAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash
                });
                return true;
            }
            // 로컬 클라이언트에서 락온 대상만 붙였다 뗀다. 서버 판정과 실제 락온 조작은 건드리지 않고
            // 구르기 방향 표현을 세 화면에서 같은 조건으로 보기 위한 개발 전용 보조다
            if (command.verb == "ability_roll_lockon")
            {
                if (!NetworkClient.spawned.TryGetValue(command.uintValue, out NetworkIdentity rollActorIdentity))
                { message = "Client target required"; return false; }
                var rollPresentation = rollActorIdentity.GetComponent<PlayerPresentation>();
                if (rollPresentation == null) { message = "Presentation required"; return false; }
                if (command.index <= 0)
                {
                    ReleaseRollLockOnFixture(rollActorIdentity.netId, rollPresentation);
                    message = "Lock on cleared/lockedOn=" + rollPresentation.IsLockedOn;
                    return true;
                }

                if (!_rollLockOnFixtures.TryGetValue(rollActorIdentity.netId,
                        out RollLockOnFixture rollFixture))
                {
                    rollFixture = new RollLockOnFixture
                    {
                        Controller = rollActorIdentity.GetComponent<LockOnController>(),
                    };
                    // 원래 enabled는 최초 한 번만 저장한다. 두 번 켜도 복원값이 뒤집히지 않는다
                    rollFixture.ControllerWasEnabled =
                        (rollFixture.Controller != null) && rollFixture.Controller.enabled;
                    _rollLockOnFixtures[rollActorIdentity.netId] = rollFixture;
                }

                // LockOnController.Update -> ApplyPresentation이 매 프레임 자기 _targetTransform(null)으로
                // 표현 대상을 덮어쓴다. 표현만 보는 fixture이므로 켜 두는 동안만 컴포넌트를 멈춘다
                if (rollFixture.Controller != null) { rollFixture.Controller.enabled = false; }

                // NetworkIdentity/Collider/Renderer 없는 로컬 검증 전용 대상이다.
                // 기존 맵의 EnemyStatus 등록 여부에 기대지 않는다
                if (rollFixture.Target == null)
                {
                    rollFixture.Target = new GameObject(
                        "Verification roll lock on target " + rollActorIdentity.netId);
                }

                // 모델을 교체해도 같은 actor를 다시 쓰므로 위치만 갱신한다
                rollFixture.Target.transform.position =
                    rollActorIdentity.transform.position + (Vector3.forward * 10f);
                rollPresentation.SetLockOnTarget(rollFixture.Target.transform);
                message = "Lock on fixture=" + rollFixture.Target.transform.position +
                    "/lockedOn=" + rollPresentation.IsLockedOn +
                    "/facing=" + rollPresentation.FacingDirection;
                return true;
            }
            // Boot를 거치지 않는 개발 맵에서도 운영과 같은 로컬 효과 풀을 명시적으로 준비한다.
            if (command.verb == "ability_vfx_setup")
            {
                if (!NetworkClient.active || !Geumeum.Data.GameDB.IsInitialized)
                { message = "Initialized client required"; return false; }
                if (LocalObjectPoolService.Instance == null)
                {
                    var host = new GameObject("Verification presentation pool");
                    host.AddComponent<LocalObjectPoolService>();
                    host.AddComponent<Geumeum.Art.PresentationPoolPrewarmer>();
                }
                message = "Presentation pool ready"; return true;
            }
            if (command.verb == "ability_placement_begin" || command.verb == "ability_placement_cancel")
            {
                var localAttack = NetworkClient.localPlayer?.GetComponent<PlayerAttack>();
                if (localAttack == null) { message = "Owner required"; return false; }
                typeof(PlayerAttack).GetMethod(command.verb == "ability_placement_begin" ? "BeginPlacement" : "EndPlacement", AbilityPrivate)
                    .Invoke(localAttack, null);
                message = "placement=" + localAttack.IsPlacingField;
                return true;
            }
            if (command.verb == "ability_skill_owner")
            {
                var local = NetworkClient.localPlayer;
                NetworkClient.spawned.TryGetValue(command.uintValue, out NetworkIdentity aimed);
                if (local != null && Camera.main != null && aimed != null)
                {
                    var rig = Camera.main.GetComponent<Geumeum.Art.CameraRig>(); if (rig != null) { rig.enabled = false; }
                    Vector3 forward = aimed.transform.position - local.transform.position; forward.y = 0;
                    Camera.main.transform.rotation = Quaternion.LookRotation(forward.normalized);
                }
                // 방울은 R이 조준만 연다. 검증에서는 커서 대신 대상 위치로 바로 확정한다
                PlayerAttack ownerAttack = local == null ? null : local.GetComponent<PlayerAttack>();
                PlayerWeapon ownerWeapon = local == null ? null : local.GetComponent<PlayerWeapon>();
                bool sent = ownerAttack != null &&
                    (ownerWeapon != null && ownerWeapon.HasWeapon && ownerWeapon.Weapon == WeaponKind.Bell
                        ? aimed != null && ownerAttack.RequestBellFieldPlacement(aimed.transform.position)
                        : ownerAttack.RequestWeaponSkill());
                message = "Owner R Command sent=" + sent; return sent;
            }
            if (command.verb == "ability_visual_state")
            {
                if (!NetworkClient.spawned.TryGetValue(command.uintValue, out NetworkIdentity visualTarget))
                { message = "Client target required"; return false; }
                var presentation = visualTarget.GetComponent<Geumeum.Art.PlayerCombatPresentation>();
                var presentationType = typeof(Geumeum.Art.PlayerCombatPresentation);
                var hand = presentationType.GetField("_effectHand", AbilityPrivate).GetValue(presentation) as Transform;
                var model = presentationType.GetField("_effectModel", AbilityPrivate).GetValue(presentation) as GameObject;
                var charge = presentationType.GetField("_chargeLine", AbilityPrivate).GetValue(presentation) as LineRenderer;
                var weaponVisual = visualTarget.GetComponent<PlayerWeaponVisual>();
                var weaponModel = typeof(PlayerWeaponVisual).GetField("_spawnedModel", AbilityPrivate).GetValue(weaponVisual) as GameObject;
                var accents = presentationType.GetField("_accentMesh", AbilityPrivate).GetValue(presentation) as Mesh;
                var accentRenderer = presentationType.GetField("_accentRenderer", AbilityPrivate).GetValue(presentation) as MeshRenderer;
                message = "model=" + (model == null ? "null" : model.name) +
                    ";hand=" + (hand == null ? "null" : hand.name + "/" + hand.position) +
                    ";weapon=" + (weaponModel == null ? "null" : weaponModel.transform.parent.position.ToString()) +
                    ";focus=" + (charge == null ? "null" : charge.enabled + "/" + charge.bounds.center) +
                    ";actor=" + visualTarget.transform.position +
                    ";accentVertices=" + (accents == null ? 0 : accents.vertexCount) +
                    ";accentsVisible=" + (accentRenderer != null && accentRenderer.enabled);
                var trails = UnityEngine.Object.FindObjectsByType<Geumeum.Art.MeleeSlashView>(FindObjectsInactive.Exclude);
                foreach (var trail in trails)
                {
                    message += ";trail=" + trail.SampleCount + "/vertices=" + (trail.TrailMesh == null ? 0 : trail.TrailMesh.vertexCount) +
                        "/recordFrom=" + typeof(Geumeum.Art.MeleeSlashView).GetField("_recordFrom", AbilityPrivate).GetValue(trail) +
                        "/recordUntil=" + typeof(Geumeum.Art.MeleeSlashView).GetField("_recordUntil", AbilityPrivate).GetValue(trail);
                }
                var bowLeases = presentationType.GetField("_bowLasers", AbilityPrivate)?.GetValue(presentation) as System.Collections.ICollection;
                var bowCharge = presentationType.GetField("_bowCharge", AbilityPrivate)?.GetValue(presentation) as Geumeum.Art.PooledVisualLifetime;
                message += ";bowLasers=" + (bowLeases == null ? 0 : bowLeases.Count) +
                    ";bowCharge=" + (bowCharge != null && bowCharge.IsRented);
                message += ";gameplay=" + RunTimeLocator.Current.GameplayElapsedSeconds;
                return true;
            }
            if (command.verb == "ability_camera")
            {
                Camera camera = Camera.main;
                if (camera == null || !NetworkClient.spawned.TryGetValue(command.uintValue, out NetworkIdentity viewed))
                { message = "Camera and client target required"; return false; }
                var rig = camera.GetComponent<Geumeum.Art.CameraRig>();
                if (command.secondaryIndex == 2 && rig != null)
                { rig.enabled = true; message = "Gameplay camera restored"; return true; }
                if (rig != null) { rig.enabled = false; }
                Vector3 look = viewed.transform.position + Vector3.up;
                camera.transform.position = look + (command.secondaryIndex == 1 ? new Vector3(1.5f, 1.2f, -3f) : new Vector3(3f, 2.5f, -6f));
                camera.transform.LookAt(look);
                camera.ResetWorldToCameraMatrix(); camera.ResetProjectionMatrix();
                message = "Verification camera=" + camera.transform.position + "/target=" + look + "/mask=" + camera.cullingMask;
                return true;
            }
            if (command.verb == "ability_recall")
            {
                var local = NetworkClient.localPlayer;
                bool sent = local != null && local.GetComponent<PlayerAttack>().RequestEchoRecall();
                message = "Owner recall Command sent=" + sent;
                return sent;
            }
            if (!NetworkServer.active) { message = "Host required"; return false; }
            var actors = new List<PlayerStatus>(PlayerStatus.ServerPlayers);
            actors.Sort((a,b) => a.netId.CompareTo(b.netId));
            if (command.verb == "ability_grip_character")
            {
                if (command.index < 0 || command.index >= actors.Count || command.secondaryIndex < 0 || command.secondaryIndex > 2)
                { message = "Valid actor and character required"; return false; }
                // 기본 무기와 전투 loadout은 유지한 채 개발 빌드에서만 모델 교체/원격 파지를 검증한다.
                var character = actors[command.index].GetComponent<PlayerCharacter>();
                character.ServerClearForNewRun();
                bool selected = character.ServerSetCharacter((CharacterKind) command.secondaryIndex);
                message = "Grip character=" + character.Character;
                return selected;
            }
            if (command.verb == "ability_setup")
            {
                var enemies = UnityEngine.Object.FindObjectsByType<EnemyStatus>(FindObjectsInactive.Exclude);
                EnemyStatus target = null;
                float nearest = float.MaxValue;
                foreach (var enemy in enemies)
                {
                    if (enemy.IsDead) { continue; }
                    var controller = enemy.GetComponent<EnemyController>();
                    if (controller != null)
                    {
                        var tick = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), controller,
                            typeof(EnemyController).GetMethod("ServerTick", AbilityPrivate));
                        AuthorityTickScheduler.ServerInstance.Unregister(tick);
                        var agent = enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();
                        if (agent != null && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); }
                    }
                    float distance = (enemy.transform.position - actors[0].transform.position).sqrMagnitude;
                    if (distance < nearest) { nearest = distance; target = enemy; }
                }
                if (target == null) { message = "No living target"; return false; }
                typeof(EnemyStatus).GetField("_health", AbilityPrivate).SetValue(target, 2000f);
                typeof(EnemyStatus).GetField("_maxHealth", AbilityPrivate).SetValue(target, 2000f);
                target.SetDirty();
                for (int i = 0; i < actors.Count; ++i)
                {
                    WeaponKind kind = actors[i].GetComponent<PlayerWeapon>().Weapon;
                    float distance = kind == WeaponKind.Bow ? 5f : 2f;
                    actors[i].GetComponent<PlayerController>().ServerTeleport(target.transform.position + Vector3.back * distance);
                    actors[i].ServerAddSpirit(100f);
                    actors[i].GetComponent<PlayerAttack>().ResetForNewRun();
                }
                Physics.SyncTransforms();
                message = "target=" + target.netId;
                return true;
            }
            if (command.index < 0 || command.index >= actors.Count ||
                !NetworkServer.spawned.TryGetValue(command.uintValue, out NetworkIdentity targetIdentity))
            { message = "Actor index and target required"; return false; }
            var actor = actors[command.index];
            var attack = actor.GetComponent<PlayerAttack>();
            if (command.verb == "ability_arrow_state")
            {
                var fired = (ArrowFireSnapshot)typeof(PlayerAttack).GetField("_burstSnapshot", AbilityPrivate).GetValue(attack);
                var cameraAim = (PrecisionAimRay)typeof(PlayerAttack).GetField("_committedCameraAim", AbilityPrivate).GetValue(attack);
                attack.ServerTryGetWedgePoint(targetIdentity.netId, out Vector3 markPoint);
                message = "origin=" + fired.Origin + ";direction=" + fired.Direction + ";cameraOrigin=" + cameraAim.Origin +
                    ";cameraDirection=" + cameraAim.Direction + ";mark=" + markPoint + ";damage=" + fired.Damage;
                return true;
            }
            // 실제 서버 패링 판정과 기존 RPC를 통과시키되 공격 소스만 검증용으로 제공한다.
            if (command.verb == "ability_parry")
            {
                var parry = actor.GetComponent<ParryController>();
                var source = new VerificationCounterSequence { AttackInstanceId = (uint)command.commandId };
                double now = NetworkTime.time;
                double impact = now + Geumeum.Data.GameDB.Combat.ParryStartupDuration + Constants.Network.PARRY_RESOLUTION_LEAD + .12;
                var key = new ParryWindowKey(targetIdentity.netId, (uint)command.commandId);
                if (!parry.OpenParryWindow(source, key, now - 1, impact))
                { message = "Parry window rejected"; return false; }
                var result = parry.ServerRequestParry((uint)command.commandId);
                if (result == ParryResultCode.Success) { Invoke(nameof(PauseHitTest), .06f); }
                message = result.ToString(); return result == ParryResultCode.Success;
            }
            if (command.verb == "ability_counter_sequence")
            {
                var gauge = actor.GetComponent<ParryGauge>();
                if (gauge == null) { message = "Sword gauge required"; return false; }
                var sequence = new VerificationCounterSequence { AttackInstanceId = (uint)command.commandId };
                for (byte i = 0; i < Mathf.Clamp(command.secondaryIndex, 1, 3); ++i)
                {
                    sequence.RepeatIndex = i;
                    gauge.ServerAddCounter(targetIdentity.netId, (uint)command.commandId + i, sequence, 0);
                }
                message = "counter=" + gauge.CounterMarks; return true;
            }
            if (command.verb == "ability_field_discard")
            { attack.ServerDiscardField(); message = "Field and tail revoked"; return true; }
            if (command.verb == "ability_recall_pose")
            {
                var result = attack.ServerRequestEchoRecall((uint)command.commandId,
                    RunManager.ServerInstance.RunId.Value, RunSceneDirector.ServerInstance.Revision.Value);
                if (result == ActionRejection.None) { Invoke(nameof(PauseHitTest), 0.12f); }
                message = result.ToString(); return result == ActionRejection.None;
            }
            if (command.verb == "ability_reset")
            { attack.ResetForNewRun(); message = "Owner marks cleared"; return true; }
            if (command.verb == "ability_move")
            {
                actor.GetComponent<PlayerController>().ServerTeleport(targetIdentity.transform.position + Vector3.back * command.secondaryIndex);
                message = "Actor placed at requested range"; return true;
            }
            Vector3 point = targetIdentity.GetComponentInChildren<Collider>().bounds.center;
            if (attack.ServerTryGetWedgePoint(targetIdentity.netId, out Vector3 wedge)) { point = wedge; }
            Vector3 origin = actor.transform.position + Vector3.up * 1.2f;
            Vector3 direction = (point - origin).normalized;
            var aim = new PrecisionAimRay { Origin = origin, Direction = direction };
            uint request = (uint)command.commandId;
            if (command.verb == "ability_skill" || command.verb == "ability_skill_cancel")
            {
                // 방울은 설치 좌표가 있어야 한다. 사람과 같은 서버 검증을 타도록
                // 대상 발밑을 후보로 넣고, 실패하면 거부 사유를 그대로 돌려준다
                Vector3 placement = new Vector3(float.NaN, float.NaN, float.NaN);
                PlayerWeapon actorWeapon = actor.GetComponent<PlayerWeapon>();
                if ((actorWeapon != null) && actorWeapon.HasWeapon &&
                    (actorWeapon.Weapon == WeaponKind.Bell))
                { placement = targetIdentity.transform.position; }
                var rejection = attack.ServerRequestWeaponSkill(request, RunManager.ServerInstance.RunId.Value,
                    RunSceneDirector.ServerInstance.Revision.Value, new Vector2(direction.x, direction.z).normalized,
                    placement);
                if (rejection == ActionRejection.None && command.verb == "ability_skill_cancel")
                {
                    actor.TakeDamage(new DamageInfo(1, uint.MaxValue, HitStrength.Light));
                }

                if (rejection == ActionRejection.None && command.secondaryIndex > 0)
                {
                    Invoke(nameof(PauseHitTest), command.secondaryIndex == 1 ? .12f : command.secondaryIndex == 3 ? 1.10f : .31f);
                }

                message = rejection.ToString(); return rejection == ActionRejection.None;
            }
            if (command.verb == "ability_skill_roll")
            {
                var rejection = actor.GetComponent<PlayerRollController>().ServerRequestRoll(request, Vector2.down);
                message = rejection.ToString(); return rejection == ActionRejection.None;
            }
            if (command.verb == "ability_strike")
            {
                var rejection = attack.ServerRequestAttack(request, new Vector2(direction.x, direction.z).normalized, aim);
                if (rejection == ActionRejection.None && command.secondaryIndex > 0)
                { Invoke(nameof(PauseHitTest), command.secondaryIndex > 10 ? command.secondaryIndex / 1000f : command.secondaryIndex == 1 ? 0.22f : 0.29f); }
                message = rejection.ToString(); return rejection == ActionRejection.None;
            }
            if (command.verb == "ability_precision_begin")
            {
                uint hold = ++_abilityHold;
                var rejection = actor.GetComponent<PrecisionShotController>().ServerBeginPrecisionShot(
                    RunManager.ServerInstance.RunId.Value, RunSceneDirector.ServerInstance.Revision.Value, request, hold);
                if (rejection == ActionRejection.None) { _abilityHoldsByActor[actor.netId] = hold; }
                message = rejection.ToString(); return rejection == ActionRejection.None;
            }
            if (command.verb == "ability_precision_release")
            {
                uint hold = _abilityHoldsByActor.TryGetValue(actor.netId, out uint acceptedHold) ? acceptedHold : _abilityHold;
                var decision = actor.GetComponent<PrecisionShotController>().ServerReleasePrecisionShot(
                    RunManager.ServerInstance.RunId.Value, RunSceneDirector.ServerInstance.Revision.Value, request, hold, aim, out ActionRejection rejection);
                if (decision == PrecisionReleaseDecision.Fire && command.secondaryIndex == 1)
                { Invoke(nameof(PauseHitTest), .08f); }
                message = decision + "/" + rejection; return rejection == ActionRejection.None;
            }
            if (command.verb == "ability_recall_cancel")
            {
                var rejection = attack.ServerRequestEchoRecall(request, RunManager.ServerInstance.RunId.Value, RunSceneDirector.ServerInstance.Revision.Value);
                if (rejection == ActionRejection.None)
                { actor.TakeDamage(new DamageInfo(1, uint.MaxValue, HitStrength.Light)); }
                message = rejection + "/reacting=" + actor.IsHitReacting;
                return rejection == ActionRejection.None;
            }
            message = "Unknown ability verification command"; return false;
        }

        [Serializable]
        private struct AbilityEnemySnapshot
        {
            public uint netId;
            public float health;
            public Vector3 position;
        }
    }
}
#endif
