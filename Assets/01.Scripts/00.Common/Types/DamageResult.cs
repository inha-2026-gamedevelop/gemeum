// Unity
using UnityEngine;

namespace Geumeum.Common
{
    /// <summary>
    /// 서버가 HP를 실제로 깎은 뒤 확정한 피해 한 건(COMBAT_STYLE_DESIGN 8.1)
    /// 서버가 공격자 소유 클라이언트에게만 보낸다. 표현(히트스톱·움찔·숫자)은 이 값을 소비하고 판정을 다시 하지 않는다
    /// Mirror가 직렬화하므로 공개 필드만 둔다
    /// </summary>
    public struct DamageResult
    {
        /// <summary> 부위가 없는 대상(본체)을 뜻하는 PartIndex </summary>
        public const sbyte NO_PART = -1;

        /// <summary> 서버 세션 안에서 늘어나기만 하는 사건 번호. 수신 쪽 중복 제거 키 </summary>
        public uint EventId;

        /// <summary> 가해자 netId </summary>
        public uint AttackerNetId;

        /// <summary> 가해 행동의 실행 번호. 한 공격이 여러 적을 맞힌 것을 묶는다. 모르면 0 </summary>
        public uint ExecutionId;

        public DamageSourceKind Source;

        /// <summary> 종류별 보조값. 받아베기는 소비한 표식 수, 잔향은 단계 </summary>
        public byte Detail;

        /// <summary> 맞은 대상의 netId. 보스 꼬리·원혼은 본체와 같은 netId일 수 있어 PartIndex로 가른다 </summary>
        public uint TargetNetId;

        /// <summary> 맞은 부위 번호. 본체면 NO_PART </summary>
        public sbyte PartIndex;

        /// <summary> 대상이 보스(본체·부위·원혼)인지 </summary>
        public bool TargetIsBoss;

        /// <summary> 실제로 줄어든 HP. 초과 피해는 빠진 값이다 </summary>
        public float Amount;

        public bool IsCritical;

        /// <summary> 이 피해로 대상이 쓰러졌는지 </summary>
        public bool IsKill;

        /// <summary> 적중 위치(월드). 모르면 대상 중심 </summary>
        public Vector3 HitPoint;

        /// <summary> 가해자에서 대상으로 향하는 수평 방향(정규화). 모르면 zero </summary>
        public Vector3 HitDirection;

        /// <summary> 서버가 확정한 게임 진행 시각(IRunTimeSource.GameplayElapsedSeconds) </summary>
        public double GameplayTime;

        /// <summary>
        /// 서버가 결과를 만든 NetworkTime.time. 수신 쪽 늦은 결과 폐기와 처치 연쇄 창은 이 값으로 잰다
        /// GameplayTime은 일시정지, 필드 시간 상한, 보스 시계 시작 전에 멈추고 클라이언트 쪽 값도 결과와 같이 늦게 오므로 쓰지 않는다
        /// </summary>
        public double ServerTime;
    }
}
