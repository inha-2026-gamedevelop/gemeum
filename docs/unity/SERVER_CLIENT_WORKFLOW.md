# 서버·클라이언트 협업

2026-09-15 최종 결정: 같은 Unity 프로젝트와 저장소, Mirror 호스트 서버 구조를 유지하며 **실행 위치와 작성 담당을 분리한다.** 판정·AI는 서버에서만 실행되지만(권위 원칙 그대로) 작성은 원래 파트 담당이 하고, @AidenKim923은 네트워크 코드를 맡는다.
처음에는 서버 구현 전체를 팀장이 맡는 안을 적용했다가 같은 날 판정·AI 작성을 원래 담당으로 되돌렸다.

## 1. 담당 경계

| 담당 | 구현하는 것 | 사용하는 것 |
|---|---|---|
| @AidenKim923 | 네트워크 코드: 도메인 폴더의 `Net/{클래스}.network.cs`(SyncVar·SyncList 선언, `[Command]`), 중앙 `98.Servers/`의 `.server.cs`(서버 수명 콜백·`[Server]` 메서드·요청 검증·동기화 연결). `01.Network`·`02.Combat`·`00.Common`·`05.Art`·`99.Dev`, 통합 | 파트 담당이 작성한 판정·AI 코드 |
| @oname36-creator | 몬스터·보스(`03.AI/`): 보스 페이즈 상태 머신·패턴 선택·실행, 잡귀 AI, 대상·무리 규칙, 원본 컴포넌트(클라 실행 코드 포함)와 표현 | 서버 경로의 호출, 복제된 상태·표현 이벤트 |
| @kjw1237 | 게임플레이 시스템·결계·UI(`04.Systems/`, `06.UI/`): 경제·거래·드랍·장비 생성·성장·상호작용 규칙, 결계 대상 선택, 원본 컴포넌트, 입력·UI | 서버 경로의 호출, 공개 Request API·조회·결과 이벤트 |

**보스가 언제 공격할지, 누구를 공격할지, 어디에 스폰할지는 서버에서만 실행되는 기능이며, 로직 작성은 몬스터·보스 담당이 한다.** 표현은 서버가 확정한 예고와 상태를 표시한다. 애니메이션 완료를 피해·공격 종료의 판정 근거로 삼지 않는다.
기존 씬·프리팹·데이터 작업 소유는 유지하되 서버 컴포넌트, NetworkIdentity, 판정 콜라이더, NavMesh·스폰 배선 변경은 팀장과 함께 검토한다. 밸런스 값은 기존 데이터 작업 규칙을 따른다.

## 2. 코드 배치와 소유

서버 partial의 기준 위치는 `Assets/01.Scripts/98.Servers/`이며 이 폴더에는 `.server.cs`만 둔다. 판정·AI·규칙 클래스와 도메인별 요청·조회 창구는 기존 도메인 폴더에 둔다. `Client/`·`Presentation/`은 새 소비자를 만들 때 사용할 위치이며 일부는 아직 없다.

```text
Assets/01.Scripts/
  98.Servers/                              팀장: 서버 partial(.server.cs)만
    03.AI/Boss/                         보스 서버 수명 콜백·[Server] 메서드·동기화 연결
    03.AI/Enemy/                        몬스터·투사체 서버 연결
    03.AI/Spawn/                        스폰 서버 연결
    04.Systems/                         경제·장비·성장·결계·상호작용·인벤토리·결과 서버 연결
  00.Common/Interfaces/                  팀장: 공용 요청·조회 계약
  03.AI/Boss/
    BossController.cs                   몬스터·보스 담당: 원본 컴포넌트·공개 상태·클라 실행 코드(hook·Rpc)
    Phase1State.cs                      몬스터·보스 담당: 페이즈 판정·패턴(서버 경로에서만 호출)
    Net/BossController.network.cs       팀장: 서버 통신 계약(SyncVar 선언·Command)
    Presentation/                       몬스터·보스 담당: 별도 표현 컴포넌트
  04.Systems/Economy/
    ShopTransactionService.cs           시스템 담당: 원본 컴포넌트·공개 요청 창구
    CurrencyRules.cs                    시스템 담당: 판정 규칙(서버 경로에서만 호출)
    Net/                                팀장: 요청 수신·동기화 선언
    Client/                             시스템 담당: 별도 로컬 소비자
  06.UI/                                시스템 담당: UI
```

- `03.AI/`는 몬스터·보스 담당, `04.Systems/`는 게임플레이 시스템 담당이 소유한다. 어트리뷰트 없는 판정·AI 클래스와 해당 테스트, 원본 컴포넌트 파일(클라 실행 코드 포함)도 포함한다. 각 도메인의 `Net/`와 중앙 `98.Servers/`는 제외한다.
- 기존 `EnemyAnimationController`·`EnemyParryWindowFlash`는 몬스터·보스 담당 소유다. 새 AI 표현은 `Presentation/`에 작성한다.
- 시스템의 새 로컬 소비자는 `Client/` 또는 `Presentation/`, UI는 `06.UI/`에 작성한다.
- `98.Servers/` 전체와 각 도메인의 `Net/`는 팀장 소유다. 새 `.server.cs`는 `98.Servers/{03.AI|04.Systems}/{기능}/`에 작성한다. 하위 경로는 기존 `Assets/01.Scripts/{03.AI|04.Systems}/{기능}/`을 그대로 비춘다. 기능 폴더 아래에 `Server/`를 다시 만들지 않는다. 새 판정·AI·규칙 클래스는 `98.Servers/`가 아니라 원래 도메인 폴더에 파트 담당이 작성한다.
- `Net/{클래스}.network.cs`에는 서버가 소유하는 통신 계약(SyncVar·동기화 컬렉션 선언, Command와 Command 전용 헬퍼, `OnSerialize`/`OnDeserialize`)만 둔다. hook·`ClientRpc`·`TargetRpc`·클라 수명 콜백은 원본 `{클래스}.cs`에 둔다. 남을 멤버가 없으면 `Net/` 파일을 만들지 않는다. 세부는 [코딩 컨벤션 9장](../coding-convention.md).
- 기존 partial은 네임스페이스(`Geumeum.AI`·`Geumeum.Systems`)와 어셈블리를 유지한다. `98.Servers`는 서버 partial의 위치를 나타내며 별도 실행 프로세스나 `Geumeum.Servers` 네임스페이스를 뜻하지 않는다.
- **partial은 코드 접근을 제한하지 않는다.** 원본과 `.server.cs`는 private 필드를 공유한다. 원본에서 서버 상태 필드를 바꾸거나 서버 partial이 쓰는 멤버 이름을 바꿀 때는 팀장 리뷰를 받는다. 새 네트워크 멤버(`[SyncVar]`·`[Command]`·Rpc)나 `.server.cs` 변경이 필요하면 팀장에게 요청하고, 같은 클래스의 partial을 따로 추가하지 않는다.
- 판정·AI 코드는 서버 경로(`[Server]` 메서드·`98.Servers/` partial)에서만 호출된다. 클라이언트·UI 코드는 판정 코드를 직접 호출하지 않고 공개 Request API·복제 상태를 쓴다.
- 현재는 협업·리뷰 경계다. 컴파일 단계의 접근 제한이나 클라이언트 빌드에서 서버 코드 제거를 제공하지 않는다. 그것이 필요해지면 partial 의존성을 먼저 제거한 뒤 어셈블리 분리를 별도로 설계한다.

## 3. 기능 하나를 진행하는 순서

1. 기능 이슈에 사용자 행동, 요청 인자, 읽을 상태, 성공·거절 표시를 적는다. 예: 상품 버튼 → 상품 종류·현재 revision 전송 → 서버 결과 후 잔액 갱신.
2. 판정 담당 파트가 서버 경로에서 호출될 판정·AI 로직을 도메인 폴더에 작성하고, 팀장이 `Net/`·`.server.cs` 연결(최소 API 계약과 동작하는 요청 창구)을 제공한다. 판정 PR·네트워크 PR·소비자 PR을 서로 연결한다.
3. 표현·UI 담당은 표시용 샘플 상태로 레이아웃·애니메이션을 병행한다. 샘플은 테스트·개발 표현에만 사용하고 생산 서버 상태에 쓰지 않는다. 실제 요청 경로가 없으면 UI에서 사용 불가로 표시한다.
4. 팀장은 `.server.cs`에서 호출자·소유권·상태·거리·revision·중복 요청 검증을 연결하고 결과를 전달한다. 판정 규칙 자체는 판정 담당 파트가 작성한다. 서버 자체 행동인 AI·스폰은 네트워크 요청 API를 만들 필요가 없다.
5. 클라이언트·UI 코드는 [클라이언트 API 안내](CLIENT_API_REFERENCE.md)대로 요청하고, 결과·현재 상태로 화면을 갱신한다. API나 네트워크 멤버가 부족하면 팀장에게 계약 변경을 요청한다.
6. 기능별로 판정·네트워크·소비자 PR을 짧게 병합한다. 서버 계약 변경을 먼저 통합하고, 오래된 소비자를 깨는 변경은 해당 소비자 수정도 함께 준비한다. 브랜치는 기존 담당자 브랜치를 유지한다.

요청 계약에는 최소한 **호출 가능한 객체·시점, 인자 의미, 반환값 의미, 결과 이벤트, 거절 사유, 늦은 구독·재접속 처리**를 적는다. 모든 메서드의 반환 형식이 같다고 가정하지 않는다.

## 4. 검증과 리뷰

- 클라이언트는 호스트여도 같은 공개 요청 경로를 사용한다. `isServer`를 검사해 판정을 직접 호출하는 소비자를 만들지 않는다.
- 판정 담당 파트는 판정 결과를, 팀장은 중복·오래된 revision·다른 사람의 객체 요청 거절을 검증한다.
- 표현·UI 담당은 요청 불가·대기·성공·거절 화면과 구독 해제·재바인딩을 검증한다.
- 네트워크 기능은 3인 접속에서 승인·거절·각자 상태 표시를 확인한다. 기존 L1/L2/L3 기준을 따른다. 파일 이동만으로 이전 테스트 결과를 새 코드 검증으로 간주하지 않는다.
- `CODEOWNERS`는 리뷰 담당 지정이다. 필수 승인에는 대상 브랜치의 Code Owner 리뷰 요구 설정이 필요하다. 여러 소유자를 같은 서버 규칙에 적으면 그중 한 명의 승인이 가능하므로 네트워크 경로(`Net/`·`98.Servers/`)는 팀장 단독으로 둔다. [GitHub 공식 설명](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners)
- 팀장 본인의 PR은 다른 팀원이 요구사항·검증 증거를 리뷰한다. 팀장이 자기 PR에 승인할 수 있다고 가정하지 않는다. 실제 보호 규칙에 따른 병합 가능 여부를 확인한다.

## 5. 현재 적용 범위

서버 partial 20개(`.server.cs`)를 `.meta`와 함께 중앙 `98.Servers/`로 이동했다. 함께 옮겼던 판정·AI·규칙 클래스 22개는 2026-09-15 같은 날 `.meta`와 함께 원래 도메인 폴더로 되돌렸다. `98.Servers/`와 `Net/`는 팀장 소유이고, 원본 컴포넌트와 판정·AI 클래스는 파트 담당 소유다.

| 중앙 위치 | 현재 파일 |
|---|---|
| `98.Servers/03.AI/Boss/` | `BossController.server.cs` · `BossPartController.server.cs` · `BossSoulController.server.cs` · `BossSpawner.server.cs` · `BossStatus.server.cs` |
| `98.Servers/03.AI/Enemy/` | `CurseProjectile.server.cs` · `EnemyController.server.cs` · `EnemyStatus.server.cs` |
| `98.Servers/03.AI/Spawn/` | `SpawnManager.server.cs` |
| `98.Servers/04.Systems/Barrier/` | `BarrierController.server.cs` |
| `98.Servers/04.Systems/Economy/` | `CurrencyManager.server.cs` · `ShopTransactionService.server.cs` |
| `98.Servers/04.Systems/Equipment/` | `DropDirector.server.cs` |
| `98.Servers/04.Systems/Growth/` | `GrowthManager.server.cs` · `TalentController.server.cs` |
| `98.Servers/04.Systems/Interaction/` | `DroppedItem.server.cs` · `ShopInteractionPoint.server.cs` |
| `98.Servers/04.Systems/Inventory/` | `EquipmentTransferService.server.cs` · `InventoryController.server.cs` |
| `98.Servers/04.Systems/Results/` | `RunStatistics.server.cs` |

도메인 폴더로 되돌린 판정·AI 클래스는 아래와 같다. 서버 경로에서만 호출되며 작성은 파트 담당이 한다.

| 도메인 위치 | 파일 | 작성 담당 |
|---|---|---|
| `03.AI/Boss/` | `BossAttackExecution.cs` · `BossPatternRunner.cs` · `BossPatternSelector.cs` · `BossPhaseStateBase.cs` · `Phase1State.cs` · `Phase2State.cs` | @oname36-creator |
| `03.AI/Enemy/` | `EnemyBrainBase.cs` · `ChanggwiBrain.cs` · `AekgwiBrain.cs` · `HeokkaebiBrain.cs` · `ChanggwiAttackExecution.cs` · `EnemyTargetRules.cs` · `EnemyHerdingEnhancementRules.cs` | @oname36-creator |
| `03.AI/Spawn/` | `SpawnHerdingRules.cs` | @oname36-creator |
| `04.Systems/Barrier/` | `BarrierTargetSelector.cs` | @kjw1237 |
| `04.Systems/Economy/` | `CurrencyRules.cs` · `TransactionRequestRules.cs` · `TransactionServiceUtility.cs` | @kjw1237 |
| `04.Systems/Equipment/` | `EquipmentGenerator.cs` · `DropRules.cs` | @kjw1237 |
| `04.Systems/Growth/` | `GrowthRules.cs` | @kjw1237 |
| `04.Systems/Interaction/` | `InteractionRules.cs` | @kjw1237 |

`BossPatternExecutionSpec`은 보스 예고 표시도 사용하고 `TalentRules`는 UI도 사용하므로 도메인 공용 코드로 유지했다. `01.Network`·`02.Combat`의 기존 서버 코드와 공유 수명 콜백은 기존 위치에 있으며 팀장 담당이다. **중앙 `98.Servers/`의 범위는 위 `.server.cs` 20개다.** 이들 도메인에서도 새로 분리하는 서버 partial은 `98.Servers/01.Network/`·`98.Servers/02.Combat/`에 둔다. 클라이언트가 부르는 API나 공유 계산까지 일괄 이동하지 않는다.

상호작용의 입력·후보 탐색은 독립 `InteractionClient`로 추출했다. 소비자는 `IInteractionRequestSource`로 요청하고, 원본 컴포넌트는 직렬화 설정·GUID와 소유자 검사·Command 전송을 유지한다. 몬스터 표현은 `IEnemyPresentationSource`를 구독하고 현재 상태를 읽는다.
추가 분리 시에도 `.cs.meta` GUID와 씬·프리팹 참조를 보존한다. 어셈블리 접근 제한·서버 전용 빌드 분리는 도입하지 않았다. GitHub 보호 설정 적용 여부는 별도 확인이 필요하다. 런타임 검증 결과는 [실행 기록](SERVER_CLIENT_REFACTOR_VALIDATION.md)에 남긴다.
