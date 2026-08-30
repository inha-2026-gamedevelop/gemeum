namespace Geumeum.Combat
{
    /// <summary> 단발 요청의 중복·역순 적용을 막는 순수 RequestId 상태 </summary>
    public struct RequestIdTracker
    {
        /****************************************
        *                Fields
        ****************************************/

        private uint _lastAcceptedRequestId;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이전보다 큰 0이 아닌 RequestId만 한 번 수락한다 </summary>
        public bool TryAccept(uint requestId)
        {
            if ((requestId == 0) || (requestId <= _lastAcceptedRequestId))
            {
                return false;
            }

            _lastAcceptedRequestId = requestId;
            return true;
        }
    }
}
