// 프로젝트 내부
using Geumeum.Common;

namespace Geumeum.Systems
{
    /// <summary> 거래 헤더 순서와 도메인 거부 사유를 일관되게 판정한다 </summary>
    public static class TransactionRequestRules
    {
        /// <summary> 현재 서버 수명과 요청 헤더가 같은지 검사한다 </summary>
        public static TransactionRejection ValidateHeader(in TransactionRequestHeader header,
            TransactionOperation expectedOperation, RunId currentRunId,
            SceneRevision currentSceneRevision, uint currentBindingGeneration,
            bool isPaused, bool isTransitioning)
        {
            if ((!header.IsValid) || (header.Operation != expectedOperation))
            {
                return TransactionRejection.InvalidRequest;
            }

            if ((header.RunIdValue != currentRunId.Value) ||
                (header.NetworkObjectGenerationValue != currentBindingGeneration))
            {
                return TransactionRejection.StaleRequest;
            }

            if (header.SceneRevisionValue != currentSceneRevision.Value)
            {
                return TransactionRejection.SceneMismatch;
            }

            if (isPaused || isTransitioning)
            {
                return TransactionRejection.Paused;
            }

            return TransactionRejection.None;
        }

        /// <summary> uint 요청 번호가 이전 번호보다 순방향인지 검사한다 </summary>
        public static bool IsNewer(uint requestId, uint previousRequestId)
        {
            // 클라이언트도 MaxValue에서 발급을 멈춘다. 서버가 wrap을 허용하면
            // 오래된 작은 ID를 새 요청으로 되살릴 수 있으므로 단조 증가만 받는다.
            return ((requestId != 0u) && (requestId > previousRequestId));
        }

        /// <summary> 인벤토리 규칙 결과를 거래 결과로 바꾼다 </summary>
        public static TransactionRejection FromInventory(InventoryResult result)
        {
            switch (result)
            {
                case InventoryResult.Success:
                    return TransactionRejection.None;
                case InventoryResult.Full:
                case InventoryResult.StackFull:
                    return TransactionRejection.InventoryFull;
                case InventoryResult.StaleRevision:
                case InventoryResult.ItemChanged:
                    return TransactionRejection.StaleInventoryRevision;
                case InventoryResult.RevisionExhausted:
                    return TransactionRejection.RevisionExhausted;
                case InventoryResult.InvalidState:
                    return TransactionRejection.InvalidState;
                default:
                    return TransactionRejection.InvalidItem;
            }
        }

        /// <summary> 공통 상호작용 결과를 거래 결과로 바꾼다 </summary>
        public static TransactionRejection FromInteraction(InteractionResult result)
        {
            switch (result)
            {
                case InteractionResult.Success:
                    return TransactionRejection.None;
                case InteractionResult.TooFar:
                    return TransactionRejection.OutOfRange;
                case InteractionResult.Dead:
                    return TransactionRejection.Dead;
                case InteractionResult.InCombat:
                    return TransactionRejection.InCombat;
                case InteractionResult.WrongScene:
                    return TransactionRejection.SceneMismatch;
                case InteractionResult.Unavailable:
                    return TransactionRejection.ShopClosed;
                case InteractionResult.RunNotActive:
                    return TransactionRejection.InvalidState;
                default:
                    return TransactionRejection.InvalidRequest;
            }
        }
    }
}
