// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Combat
{
    /// <summary> 소유 클라이언트가 서버에 보내는 패링 시도 하나. Mirror Weaver 기본 직렬화 대상이다 </summary>
    public struct ParryRequest
    {
        public uint           RequestId;
        public ParryWindowKey ObservedWindow;
        public double         ClientEstimatedServerTime;
    }
}
