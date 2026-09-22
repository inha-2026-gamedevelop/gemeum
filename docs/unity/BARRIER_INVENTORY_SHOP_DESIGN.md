# 결계 · 인벤토리 · 상점 — 현재 구현

2026-09-22 코드 기준. 런 판정은 Mirror 리슨 호스트가 소유하며 계정 API는 인벤토리나 결계 시뮬레이션을 실행하지 않는다.

## 결계

`RunManager`의 런 단계와 시계를 기준으로 `BarrierController`가 결계 상태와 외부 피해를 관리한다. 클라이언트는 동기화된 전환 메타데이터로 경계와 경고를 표현한다. 매 프레임 렌더링 결과를 판정 원본으로 보내지 않는다.

결계 중심은 보스 지점 하나로 반드시 수렴하는 옛 설계가 아니다. 현재 선택은 이전 원·필수 랜드마크와 `BarrierMapBinding` 제약을 사용한다. 반경·축소 시간·피해·회복 배율은 `BarrierPhaseSO`와 해당 서버 로직을 함께 확인한다. `BarrierTargetAdapter`는 대상 등록과 수명 주기를 담당하며 `BarrierVisual`이 존재한다.

회복 요청자는 원본 회복량을 전달하고 서버 회복 경로가 결계 배율을 적용한다. UI 또는 물약 호출부에서 같은 배율을 다시 곱하지 않는다.

## 12칸 통합 인벤토리

`InventorySnapshot.SLOT_COUNT` 기준 **4×3, 12슬롯**이다. 장비·물약·권을 함께 담는다. 구 4칸, 별도 물약 수량, 구매 즉시 권 소비 설계를 현재 계약으로 사용하지 않는다.

| 계약 | 책임 |
|---|---|
| `InventorySlotSnapshot` | 슬롯 종류, 아이템 ID, 개수, `WeaponCombatSnapshot` |
| `WeaponCombatSnapshot` | 서버 확정 장비 식별·무기·등급·공격·옵션 값 |
| `InventoryController` | 서버 인벤토리·장착·revision·요청 중복 방지·소유자 동기화 |
| `PlayerWeapon` | 실제 장착 무기 반영 |
| `ICombatLoadoutProvider` / `CombatStatCalculator` | 장비와 특성의 전투 스탯 계산 |
| `EquipmentTransferService` | 양도 제안·수락·거절·취소와 서버 검증 |

클라이언트는 요청과 표시만 한다. 슬롯 수, 아이템 가격, 장비 옵션, 거리를 클라이언트 입력만으로 신뢰하지 않는다. 동시 구매·양도·장착은 revision과 트랜잭션 잠금을 통과해야 하며 실패 시 아이템이나 재화가 일부만 변경되면 안 된다.

무기 종류 변경은 특성을 초기화하고 같은 종류의 장비 교체는 선택한 특성을 보존한다. 노드 규칙은 [특성 UI 인계](TALENT_UI_HANDOVER.md)를 따른다.

## 드롭 · 상호작용 · 상점

드롭 생성은 서버가 결정한다. `DroppedItem`의 표시와 `InteractionPromptView`의 안내가 상호작용 후보를 보여주며 G 입력은 `InteractionClient`/`InteractionController` 요청 경로를 거친다. 판정은 서버에서 거리·대상 상태·소유권·잔여 공간을 재검사한다.

팀 노잣돈은 `CurrencyManager`, 구매·재조정·등급 상승은 `ShopTransactionService`가 처리한다. 상품·가격·스택·사용 조건의 원본은 `ShopSO`와 서버 서비스다. 권과 물약의 사용, 양도, 장착 요청은 [클라이언트 API](CLIENT_API_REFERENCE.md)와 [서버 API](SERVER_API_REFERENCE.md)를 따른다.

## 저장과 종료

인벤토리와 전투 상태는 런 스냅샷으로 관리한다. 계정 PostgreSQL에 영구 장비 창고를 추가한 구조가 아니다. 런 종료·플레이어 이탈·씬 전환에서 진행 중 제안과 요청을 정리한다.

## 검증 기준

- 빈 슬롯·가득 찬 슬롯·동일 아이템 스택과 아이템 사용.
- 잔액 부족·동시 구매·중복 요청·오래된 revision 거부.
- 장착/해제·같은 종류 교체·다른 종류 교체와 스탯/특성 갱신.
- 양도 중 거리 이탈·사망·접속 종료·상대 인벤토리 가득 참.
- 결계 안팎 피해·회복·단계 전환과 3인 표시 일치.

이 목록은 재현 절차이며 이번 문서 수정에서 실행한 테스트 결과가 아니다.
