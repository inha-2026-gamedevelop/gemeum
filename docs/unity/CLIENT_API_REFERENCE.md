# 클라이언트 API 안내

로비 무기 선호·준비 요청과 기본 공격 조준 API는 [로비·전투 조작 안내](LOBBY_COMBAT_CONTROLS.md)를 참고한다.

서버 partial(`.server.cs`)은 `Assets/01.Scripts/98.Servers/`에 모으고, 판정·AI 클래스와 공개 요청·조회 창구는 기존 도메인 위치를 유지한다. 클라이언트·UI 코드는 판정 코드를 직접 호출하지 않고 이 계약을 사용한다. 실제 범위와 위치는 [서버·클라이언트 협업](SERVER_CLIENT_WORKFLOW.md#5-현재-적용-범위)을 따른다.

대상: 몬스터·보스 담당과 게임플레이 시스템·UI 담당이 작성하는 표현·입력·UI 코드. 구현 소유는 [협업 경계](SERVER_CLIENT_WORKFLOW.md)를 따른다.
아래는 현재 코드에서 확인한 소비자용 창구다. 서버 내부 판정 메서드를 호출하는 안내가 아니다.

## 1. 호출 흐름

```text
입력·UI → 공개 Request 메서드 → Mirror Command → 서버 검증·판정
표현·UI ← 복제 상태·결과 이벤트 ← SyncVar / RPC
```

공개 메서드라고 모두 클라이언트용은 아니다. 피해·회복·스폰·결과 확정은 서버 경로에서만 실행된다.
호스트에서도 입력·UI는 공개 요청 경로를 사용한다. 서버 기능을 직접 부르는 호스트 전용 지름길을 추가하지 않는다.

## 2. 현재 창구

| 기능 | 호출·조회 | 결과·표현 |
|---|---|---|
| 구매·물약·티켓 | `ShopTransactionService.RequestPurchase`, `RequestUsePotion`, `RequestRerollTicket`, `RequestUpgradeTicket`, 장착 장비 티켓 요청 | `TransactionCompleted`의 RequestId·Rejection·revision 확인 |
| 장비 양도 | `EquipmentTransferService.RequestOffer`, `RequestAccept`, `RequestReject`, `RequestCancel` | `TransferChanged`로 제안 상태·거절 확인 |
| 장착·해제 | 자기 `InventoryController.RequestEquip`, `RequestUnequip` | `EquipResult`, `EquippedWeaponChanged`; 슬롯은 `TryCopyOwnerSlots`로 복사 |
| 특성 | 자기 `TalentController.RequestAllocate`, `RequestReset` | `RevisionChanged`, `AllocationResult`; `TryGetOwnerAllocation`으로 현재 상태 조회 |
| 전투 HUD | `LocalCombatState`의 HP·스태미나·생명 상태 조회 | 이산 상태 이벤트·`ActionRejected`; 초기 표시에는 현재 상태 조회 |
| 몬스터 표현 | `IEnemyPresentationSource.VisualState`, `AttackVariant`, `IsParryWindowVisuallyOpen` 조회 | 같은 이름의 변경 이벤트 구독. 기존 애니메이션·패링 강조도 활성화 때 현재값을 읽는다 |
| 보스 예고 | `IBossTelegraphSource` 계약 조회 | `TelegraphChanged` 구독과 현재 cue 조회; 상세 수명은 서버 API의 보스 예고 절 참고 |
| 상호작용 | `IInteractionRequestSource.RequestInteract(targetNetId)` 요청, `InteractionController.PromptText` 조회 | `Client/InteractionClient`가 G키·후보 탐색을 담당. true는 전송이며 서버 승인이 아니다. 줍기는 인벤토리·월드 상태로 확인 |

실제 사용 예시는 [FunctionalManagementPanel.cs](../../Assets/01.Scripts/06.UI/Systems/FunctionalManagementPanel.cs),
몬스터 표현 예시는 [EnemyAnimationController.cs](../../Assets/01.Scripts/03.AI/Enemy/EnemyAnimationController.cs)에 있다.
세부 인자·타입은 해당 선언을 확인한다. 신규 UI를 위해 서버 내부 `Server*` 메서드를 공개 요청으로 취급하지 않는다.

## 3. 반환값과 화면 상태

- 거래·양도의 요청 메서드는 로컬 RequestId를 반환한다. **0은 전송하지 못함, 양수는 서버 성공을 뜻하지 않음**이다. 결과의 요청 ID·작업 종류·거절 사유를 확인한다.
- 장착·특성 요청은 현재 `void`다. 거래 API처럼 RequestId를 반환한다고 가정하지 않는다. 해당 결과·거절 이벤트와 복제된 상태를 사용한다.
- 가격·잔액·HP·인벤토리를 UI에서 확정하지 않는다. 버튼 클릭 뒤에는 대기를 표시하고 서버 상태로 갱신한다. 미리보기는 표시 값으로만 유지한다.
- revision은 화면이 본 상태의 버전이다. 요청에 그 값을 보내고, 거절되면 최신 상태를 읽는다. 오래된 요청을 자동 반복하지 않는다.
- 서비스를 아직 바인딩하지 못했거나 소유 상태를 읽지 못하면 버튼을 비활성화한다. 연결이 끊어지면 대기를 해제하고 다시 바인딩한다.

## 4. 구독·재접속

자기 플레이어가 바뀌면 이전 객체의 이벤트를 해제하고 새 객체에 구독한다. netId나 컴포넌트 참조를 재접속 이후에도 그대로 사용하지 않는다.
늦게 열린 화면은 이벤트 구독 후 현재 상태를 읽어 초기화한다. 이벤트가 한 번 더 발생할 때까지 빈 화면으로 기다리지 않는다.
`OnDisable`·파괴 시 구독을 해제하고, 다시 켤 때 상태를 읽는다. `GameEventBus`의 서버 발행은 원격 전송이 아니므로 UI 동기화 수단으로 삼지 않는다.

몬스터의 세 표현 상태는 `IEnemyPresentationSource`로 현재값을 읽을 수 있다. 구독 뒤 조회하는 순서를 지켜서 비활성 중 놓친 상태를 복원한다. 이 계약에는 공격 결정·피해 적용 메서드가 없다.

상호작용은 기존 프리팹의 `InteractionController`와 직렬화된 키·탐색 주기 설정을 유지한다. 원본 컴포넌트가 로컬 플레이어에서만 독립 `InteractionClient`를 구동하므로 새 컴포넌트를 씬마다 붙일 필요가 없다. 로컬 플레이어가 종료되면 소비자와 후보 캐시를 해제한다. 같은 키를 처리하는 입력 컴포넌트를 중복 추가하지 않는다.

`DroppedItem.CanInteract`의 원격 반환값은 복제된 장비가 있는지에 따른 후보 표시다. 서버의 생명·거리·비전투·예약 판정을 통과했다는 의미가 아니다. 실제 줍기는 `RequestInteract` 이후 인벤토리와 드랍 소멸 상태로 확인한다.

더 필요한 요청·상태·거절 정보는 팀장(@AidenKim923)에게 요청해 계약부터 추가한다. [서버 API 레퍼런스](SERVER_API_REFERENCE.md)의 서버 내부 호출을 팀원 코드에서 대신 사용하지 않는다.
