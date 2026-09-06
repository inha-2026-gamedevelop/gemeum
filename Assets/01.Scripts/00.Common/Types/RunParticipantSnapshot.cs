// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 씬 전환과 재접속 사이에 참가자의 값을 담아 두는 그릇.
    ///
    /// 디스크 세이브가 아니라 서버 메모리 안의 값이다. 런이 끝나면 버린다.
    /// 평소 현재 값의 주인은 각 서버 컴포넌트이고, 이 그릇은 담고 붓는 순간에만 주인이 된다 -
    /// 양쪽이 매 틱 각자 갱신하면 원본이 둘이 되어 어느 쪽이 맞는지 알 수 없게 된다.
    ///
    /// 값 타입이 아니라 클래스인 이유는 배열을 담고 부분적으로 채워 나가기 때문이다.
    /// 네트워크로 보내지 않으므로 Mirror 직렬화 제약을 받지 않는다
    /// </summary>
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

        // TODO(민성): 인벤토리 4칸과 특성 배분은 Systems가 자기 데이터 모델을 확정한 뒤
        // 같은 그릇에 담는다. 여기서 먼저 모양을 정하면 그쪽 설계를 앞질러 못 박게 된다.
        // 팀 공용 노잣돈은 참가자 값이 아니라 런 값이라 여기 넣지 않는다

        // 위치
        public Vector3 Position { get; set; }
        public float   YawDegrees { get; set; }

        // 짧은 실제 시간 상태는 남은 양이 아니라 절대 만료 시각으로 담는다.
        // 몇 분 멈췄다 돌아왔는데 무적이 그대로 남아 있는 쪽이 오히려 이상하다
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
