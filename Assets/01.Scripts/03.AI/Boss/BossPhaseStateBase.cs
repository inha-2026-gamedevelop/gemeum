// Unity
using UnityEngine;

namespace Geumeum.AI
{
    /// <summary>
    /// 보스 페이즈별 판단 베이스. 서버에서만 Tick되며, 대상 스캔, 텔레그래프 RPC는 BossController가 담당한다.
    /// </summary>
    public abstract class BossPhaseStateBase
    {
        /****************************************
        *                Fields
        ****************************************/

        protected readonly Transform  Self;
        protected readonly BossStatus Status;

        /****************************************
        *              Constructor
        ****************************************/

        protected BossPhaseStateBase(Transform self, BossStatus status)
        {
            Self   = self;
            Status = status;
        }

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 공격 후보 대상, BossController가 주기적으로 채워준다 </summary>
        public Transform Target { get; set; }

        protected bool IsExited { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 틱마다 호출된다, deltaTime은 호출부(BossController)가 넘긴다 </summary>
        public abstract void Tick(float deltaTime);

        /// <summary> 진행 중인 판정, 패링 창, 예고를 정리하고 회복 상태로 돌아간다 </summary>
        public abstract void CancelActiveAttack();

        /// <summary> 서버 틱이 멈춘 동안에도 현재 공격이 실제 windup인지 확인한다 </summary>
        public abstract bool IsAttackWindup(uint attackInstanceId);

        /// <summary> 기상/최종 실패처럼 일반 선택기를 거치지 않는 조건부 패턴을 요청한다 </summary>
        public virtual bool TryBeginForcedAttack(int patternId, int repeatCountOverride)
        {
            return false;
        }

        /// <summary>
        /// 확인용 회전 빔 시각화 대상. directionBuffer에 현재 활성 빔들의 방향(공용 원점 기준)을
        /// 채우고 개수를 돌려준다(2페이즈는 반대편 2줄기라 2개). 실제 VFX가 붙기 전까지만 쓴다.
        /// GGizmo는 서버 틱마다만 다시 그려져 렌더 프레임 사이에 깜빡이므로, BossController가
        /// 매 렌더 프레임(Update) 이 값을 읽어 실제 3D 오브젝트 위치를 갱신한다
        /// </summary>
        public virtual int GetDebugBeams(Vector3[] directionBuffer, out Vector3 origin, out float range)
        {
            origin = default;
            range  = 0f;
            return 0;
        }

        /// <summary>
        /// 확인용 탄막 구체 시각화 대상. buffer에 현재 날아가고 있는 탄환들의 위치를 채우고
        /// 개수를 돌려준다. GGizmo 깜빡임 문제는 위 GetDebugBeams와 같다
        /// </summary>
        public virtual int GetDebugBullets(Vector3[] buffer)
        {
            return 0;
        }

        /// <summary> 페이즈를 종료한다. 종료된 상태는 다시 실행하지 않는다 </summary>
        public void Exit()
        {
            if (IsExited)
            {
                return;
            }

            IsExited = true;
            CancelActiveAttack();
            Target = null;
        }
    }
}
