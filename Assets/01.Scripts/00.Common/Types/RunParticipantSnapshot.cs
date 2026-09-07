// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary> 씬 전환과 재접속 사이에 참가자의 값을 담아 두는 그릇 </summary>
    public class RunParticipantSnapshot
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 그릇의 형식 번호. 필드 구성이 바뀌면 올리고, 다른 번호의 값은 복구하지 않는다 </summary>
        public const int CURRENT_SCHEMA_VERSION = 1;

        /****************************************
        *              Properties
        ****************************************/

        // 헤더 - 이 값이 어긋나면 나머지를 읽지 않는다
        public int           SchemaVersion { get; set; } = CURRENT_SCHEMA_VERSION;
        public RunId         RunId         { get; set; }
        public ParticipantId Participant   { get; set; }
        public RunSceneId    SceneId       { get; set; }
        public SceneRevision SceneRevision { get; set; }

        /// <summary> 몇 번째 캡처인지. 늦게 도착한 옛 캡처가 새 값을 덮지 않게 한다 </summary>
        public uint CaptureRevision { get; set; }

        // 역할
        public CharacterKind Character { get; set; }
        public WeaponKind    Weapon    { get; set; }

        // 자원과 생명
        public float                   Health    { get; set; }
        public float                   Stamina   { get; set; }
        public float                   Spirit    { get; set; }
        public PlayerLifeSnapshotState LifeState { get; set; }

        /// <summary> 검 전용 상태. 무기를 바꾸면 0으로 돌아간다 </summary>
        public float ParryGauge { get; set; }

        // 남은 시간은 절대 시각이 아니라 남은 양으로 담는다.
        // 절대 시각으로 담으면 멈춰 있는 동안에도 만료가 다가온다
        public double SpectateRemainingSeconds  { get; set; }
        public double SwallowedRemainingSeconds { get; set; }

        // 성장
        public int Level { get; set; }
        public int Experience { get; set; }

        /// <summary> 지금 들고 있는 무기의 확정 값 </summary>
        public WeaponCombatSnapshot EquippedWeapon { get; set; }

        // TODO(민성): 인벤토리 4칸과 특성 배분은 Systems 데이터 모델 확정 후 추가

        // 위치
        public Vector3 Position { get; set; }
        public float   YawDegrees { get; set; }

        public double InvincibleUntilServerTime { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이 그릇을 지금 런의 이 참가자에게 부어도 되는지 </summary>
        public bool Matches(RunId runId, ParticipantId participant)
        {
            return ((SchemaVersion == CURRENT_SCHEMA_VERSION) &&
                    (RunId == runId) &&
                    (Participant == participant) &&
                    (participant.IsValid));
        }
    }
}
