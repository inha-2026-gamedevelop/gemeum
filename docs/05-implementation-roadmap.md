# 그믐 (晦) — 구현 로드맵

> 기준: 2026-09-22 현재 작업 트리. 코드 존재와 실제 플레이 검증을 구분한다.
> 상위 문서: [주간 계획](06-weekly-plan.md)이 일정·담당·우선순위의 정본이다. 여기는 항목별 "지금 어디까지 됐고 무엇을 어떻게 만드는가"만 다룬다.

### 변경 이력

| 날짜 | 변경점 |
|---|---|
| **2026-09-19** | **전면 개정.** 09-08 판을 폐기하고 현재 코드를 파트별로 전수 대조해 다시 썼다. U01~U41 중 24개를 완료로 정리하고 남은 항목만 상세히 남겼다. 신규 항목: **U42 보스 Unity Behavior 전환**, U43 릴리스 씬 채우기, U44 봇 파티 검증·튜닝, U45 창귀 예고 뷰·일시정지 보정. 인벤토리 2칸 → 12칸 실물 반영. ML-Agents는 제출 전 착수 보류로 기록. 본문에서 날짜 표기를 제거했다 |
| 2026-09-08 | U36 YAML 해소 · U41 컷씬 신설 · 에셋 도입 조건 |
| 2026-09-07 | 초판 U01~U40 |

---

## 0. 이 문서를 읽는 법

- **U번호는 유지한다.** 인수인계·커밋 메시지·다른 문서가 이 번호를 참조한다. 완료된 항목도 번호를 비우지 않고 1장 표에 남긴다.
- `상태`는 셋 중 하나다. **완료** = 코드·에셋 배선까지 있음. **부분** = 남은 일이 구체적으로 있음. **미착수** = 코드 없음.
- 완료라도 대부분 **L3(3PC) 미검증**이다. 검증 등급은 [주간 계획 4장](06-weekly-plan.md)을 따르고, 여기서는 "코드가 있는가"만 판단한다.
- 수치는 전부 SO에 있다. 이 문서에 적힌 값은 확인용이며 정본은 `Assets/09.Data/`다.
- 씬·프리팹·meta가 필요한 항목은 그렇게 표시했다. [씬·에셋 안전](unity/SCENE_AND_ASSET_SAFETY.md) 절차가 먼저다.

**검증 수단이 바뀌었다.** 로비 "빈자리 봇 충원"으로 사람 1명 + 봇 2명 런이 가능해져 단일 PC에서 전체 루프를 반복할 수 있다. 다만 **봇 2명은 사람 2명의 대체가 아니다.** 원격 클라이언트의 RTT·예측·표현 경로는 실제 사람 접속에서만 검증된다.

---

## 1. 완료 항목

아래 구현은 코드·배선 기준이다. 과거 테스트 수를 현재 검증으로 승계하지 않으며 변경 범위의 기능 검증과 외부 PC 3인 L3를 별도로 수행한다.

| U | 항목 | 핵심 위치 | L3 확인 포인트 |
|---|---|---|---|
| U02 | 필드 → 전실 → 보스 게이트 · 30초 강제 이동 · 결과 → 로비 | `RunManager.ServerTryStartBossEntranceCountdown` · `BossEntranceRules` · `Field.unity`의 `BossEntranceTrigger` | 동시 진입 시 전환 1회, 느린 1인 잔류 없음 |
| U03 | 보스 사망 → Victory | `BossController.server.HandleDied` → `EnemyDiedEvent.ForBoss` → `RunManager.HandleEnemyDied` | 실제 처치로 Result 도달 |
| U04 | 이탈 대기 취소 UI | `06.UI/Systems/RunStatusPanel` · `RunManager.ServerTryAbandonParticipantWait` | 호스트만 포기 가능 |
| U05 | 표식 런·씬 수명 | `MarkRegistry.ClearAll/ClearOwner/ClearEntity/ClearTarget` 소비자 10곳 | 씬 전환 후 옛 표식 공명 없음 |
| U06 | 장비 전투 snapshot | `InventoryController : ICombatLoadoutProvider` · `CombatStatCalculator.TryBuild` | 원격 장비 피해 반영 |
| U07 | 파티 공유 경험치 · 레벨업 | `GrowthManager.server.ServerAwardEnemyExperience` | 늦은 참가자 레벨 동기 |
| U08 | 장비 생성 · 드랍 · **12칸** 인벤토리 · 줍기 · 양도 | `DropDirector.server` · `DroppedItem.server.ServerTryPickup` · `EquipmentTransferService` | 동시 줍기 단일 소유 |
| U09 | 상호작용 서버 검증 | `InteractionController.network.ServerRequestInteract` · `InteractionRules.Evaluate` | 전투 후 5초 규칙 |
| U10 | 팀 재화 · 상점 · 물약 | `CurrencyManager.server` 2단계 원자 구매 · `ShopTransactionService.server` | 동시 구매 잔액 |
| U11 | 교환 · 재조정권 · 상승권 · 특성 초기화 | `EquipmentTransferService` · `ShopTransactionService.Request*Ticket` · `TalentController.server.ServerTryReset` | 수락 지연 중 재접속 |
| U12 | 특성 39개(무기별 13) · 연사/관통/치유/버프 | `TalentTreeSO.asset` · `CombatStatCalculator` | 3인 각자 다른 특성 |
| U13 | 부활 재화 차감 | `ReviveController` → `PartyWalletLocator.TryApplyReviveLoss` (사망 세대 키로 1회) | 관전 만료 vs 구조 경합 |
| U14 | 결과 집계 · 정산 · 개인 지표 · 평가 등급 · 로컬 리더보드 | `RunStatistics` · `ScoreRuleSO._evaluationGrades` · `RunLeaderboardStore` | 3인 동일 점수 |
| U16 | 정밀 사격 · 자유 조준 · 시전 취소 카운터 | `PrecisionShotController` · `PlayerAttack.ServerTryBuildPrecisionShot` · `BossController.TryInterruptPrecisionShot` | 호스트 외 상하 조준 |
| U17 | 보스 패턴 종료 · 페이즈/그로기 취소 | `BossPhaseStateBase.CancelActiveAttack/Exit` 호출부 7곳 | 전환 직후 옛 패링 창 없음 |
| U19 | 꼬리 3덩이 · 1그로기 1파괴 · 25% 게이트 · 봉인·이관 | `BossPartController.server` · `BossPartHitbox` | 3인 동시 타격 1파괴 |
| U20 | 그로기 3직업 진입로 · 본체×1.5 · 꼬리×3 | `ServerRecordParryCounter` · `TryInterruptPrecisionShot` · `ServerNotifyPatternEnded` | 무녀 2명 배율 |
| U21 | 패턴 SO 이관 · 선택기 · 연타 · 엇박 | `BossPatternSO.asset` 10패턴 · `BossPatternSelector` · `BossPatternExecutionSpec` | — (잔여 상수는 U42에서) |
| U22 | 광폭화 · 경고 · 14분 | `TickEnrageState` · `BossHudController.ApplyEnrage` | pause 중 정지 |
| U23 | 원혼 셋 · 8초 창 · 백귀야행 · 삼켜짐 | `BossSoulController.server` · `PlayerStatus : IBossSwallowTarget` | 마지막 원혼 vs 만료 경합 |
| U24 | 스폰의 결계 소비 · 몰이 · 재강화 | `SpawnManager.server.HandleBarrierSnapshot` · `EnemyController.server.ServerBeginHerding` | 교전 중 HP 급증 없음 |
| U36 | SO 누락 YAML · 부트 검증 | 23필드 명시 완료 · `GeumeumBootValidator` | — |
| U37 | 릴리스 개발도구 분리 | `GeumeumBootValidator.SceneProfile` · `FrameRecorder`는 개발 빌드만 | 릴리스 빌드 실측 1회 |
| U38 | 락온 서버 검증 · 고정 PartId | `LockOnController.ServerSetLockOn` · `ITargetPartIdSource` | 런 phase 검사는 없음(허용) |

**완료로 보지만 문서와 다른 것.** 인벤토리는 12칸(4×3)이고 `SessionManagers.prefab`에 슬롯 12개가 실물로 있다. 이전 "2칸" 결정은 폐기됐다. 상점 창구 이름은 `ShopKeeper`가 아니라 `ShopTransactionService` + `ShopInteractionPoint`다.

---

## 2. 남은 항목 — 기반 · 검증

### U01 자체 계정 릴레이 실제 3PC 기준선

- 상태: **부분.** 단일 PC 다중 프로세스(KCP, M2)로 사람 3 · 사람 1 + 봇 2 · 사람 2 + 봇 1 전체 루프 통과 기록이 있다. AWS 릴레이 경유 동일 PC 검증도 있으나 서로 다른 회선 PC 3대 M3는 별도다.
- 권위: 판정 서버 / 입력·표현 로컬
- 권장 접근: 같은 빌드로 자체 계정 AWS 릴레이 3PC를 우선 확인하고 직접 KCP·Steam 호환 경로는 별도 회귀한다. 이동·락온·창귀 패링 20회·화살·치유·구조·전환·2연속 런을 한 세션에 본다. 실패는 새 회귀와 기존 결함으로 나눠 기록한다
- 선행: 팀원 PC의 `99.ThirdParty` 동기화(URP_KitsuneYuna 패키지 각자 임포트) · 3인 시간
- 위험: 호스트만 성공, 원격 예측 어긋남, Steam 로비 무기 선호 누락(15일 수정분 미검증)
- 공수: 3인·일. 담당 전원

### U39 화살 · 저주 투사체 원격 표현

- 상태: **부분.** `ArrowProjectile.prefab`은 `NetworkTransformUnreliable`(서버→클라, 보간)이 있고, `CurseProjectile.prefab`에는 NetworkTransform이 없다. 두 투사체의 원격 표현 방식이 다르다
- 권장 접근: U01 세션에서 발사·유도·일시정지·파괴를 같은 서버 시각으로 비교한다. 저주 투사체가 원격에서 어긋나면 화살과 같은 방식으로 맞춘다. 전송 방식 자체는 바꾸지 않는다
- 공수: 1인·일. 담당 @AidenKim923

### U44 봇 파티 검증 · 스킬 프로필 튜닝 — 신규

- 상태: **구현 완료, 튜닝 전.** Unity Behavior 그래프(`Resources/BT/PlayerBehavior`) · `PlayerBrain` · `BotParty` · `PlayerBotSpawner` · `03.AI/PlayerBot/` · `BotConfig`/`BotSkillProfile` SO. 로비 봇 충원 버튼까지 있다. M2에서 세 편성 모두 루프 통과
- 권위: 봇 판단은 서버 전용. 사람 입력은 기존 Command 경로 유지
- 권장 접근: ① Steam 3PC에서 사람 1 + 봇 2로 원격 클라가 봇을 올바르게 보는지 확인. ② 사람 3명 기록 몇 판을 `BotSkillProfile`의 반응 지연·누락·오차 노브와 비교해 "사람보다 조금 못한" 수준으로 맞춘다. ③ 자연 완주(개발 명령 없이 원혼 창 통과) 1회 관찰
- 하지 않는 것: **ML-Agents 학습형 봇은 제출 전 착수하지 않는다.** [설계 초안](unity/PLAYER_BT_ML_AGENTS_DESIGN.md)은 후속 연구로 보관한다. 사람 10런 기록으로 프로필을 맞추는 쪽이 같은 목표를 훨씬 싸게 달성한다
- 위험: 봇 검증 통과를 사람 3인 L3 통과로 착각하는 것. 봇이 게이트를 먼저 밟거나 결계 밖에서 죽는 행동은 설계 4.10의 규칙이 실제로 지켜지는지 관찰해야 안다
- 공수: 2인·일. 담당 @AidenKim923

---

## 3. 남은 항목 — 보스 · 잡귀

### U42 보스 Unity Behavior 전환 — 신규

플레이어를 `com.unity.behavior` 그래프로 옮긴 것과 같은 방식으로 보스 판단부를 옮긴다. **행동을 바꾸는 작업이 아니라 구조를 바꾸는 작업이다.** 전환 전후 패턴 선택·타이밍·판정 결과가 같아야 한다.

- 현재 구조: 2단 FSM. 상단은 `BossController`가 페이즈 1/2 × `GroggyPhase` × `BossEncounterStage` × `BossFinalState`를 직접 들고, 하단은 `Phase1State`(757줄)·`Phase2State`(681줄)가 `AttackState{Idle, Windup, Recovery}`를 돈다. 틱 진입은 `AuthorityTickScheduler` → `BossController.ServerTick`
- **BT로 옮길 판단부**: `TickIdle`의 후보 구성(`BuildCandidates` · `IsDistanceEligible` · 오프닝 시퀀스 1→2→4 · 순간 접근 뒤 패턴 1 강제) · `BossPatternSelector.TrySelect` · `SelectTargetExcludingLast` · 상단 전이 조건(`TickGroggyState` · `CommitPendingPhaseTransitions` · `TickEnrageState`의 임계값) · `BossSoulController`의 창 만료·성공 판정
- **BT 밖에 남길 실행·네트워크부**: `ServerBeginTelegraph/ServerCancelTelegraph`(SyncList) · `CommitPhaseSnapshot` · `Rpc*` · `BossPatternRunner.ExecuteMeleeArc` · `ServerApplyPatternDamage`(삼켜짐 전환) · 패링 창 수명 · 공격 ID 발급 · `StopServerExecution/ResumeServerExecution`. **돌진 이동(`Self.position` 직접 갱신, `Phase1State.TickWindup`)도 실행부로 뺀다.** 지금은 State 안에 섞여 있다
- 권장 접근:
  1. 두 Phase State의 공통 흐름(선택 → `BeginAttack` → 예고 발행 → `TickWindup` → `ResolveImpact` → `TickRecovery`)을 "실행기" 하나로 추출한다. 페이즈 차이는 후보 패턴 목록과 공격 간격뿐이다
  2. 플레이어와 같은 규칙으로 그래프를 만든다. 서버에서만 `BehaviorGraphAgent` 추가, 자동 Update 끄고 권위 틱에서 `Tick`, 동기식(한 틱에 판단 완료, `Wait`/`Repeat` 없음), 게임 전용 리프는 `Action` 상속 하나(`BossBehaviorAction`)로 통일
  3. 루트 `Try In Order`: 정지·전환 게이트 → 그로기/기상 예고 → 원혼 창·실패 공격 → 광폭화 판정 → 대상 재평가 → 패턴 선택·개시 → 대기. 우선순위는 지금 `ServerTick`의 호출 순서 그대로다
  4. 잔여 상수를 이때 SO로 옮긴다: `MELEE_PARRY_WINDOW_DURATION 0.32f`(두 State 중복) · `DASH_TRACK_RATIO 0.5` · `TARGET_EXCLUSION_SCAN_RADIUS 30`. `SelectionWeight`는 선언만 있고 소비처가 없으니 선택기에 연결하거나 필드를 지운다. `ResolveLegacyShape`는 도달 불가라 삭제
  5. 기존 EditMode(`BossPatternSelectorTests` · `BossExecutionTests` · `BossAdvancedRulesTests` · `BossEnrageTests`)가 그대로 통과해야 한다. 선택기는 순수 클래스라 리프 안에서 그대로 호출한다
- 하지 않는 것: 패턴 추가, 난수 호출 순서 변경, 잡귀 3종 Brain 전환(별도 판단. 잡귀는 `EnemyController.server`가 이미 판단/실행을 나눠 놓아 급하지 않다)
- 선행: 없음. U17~U23이 완료라 지금이 가장 싼 시점이다. **밸런싱(W7) 전에 끝내야** 10월에 수치를 만질 때 코드 재빌드가 없다
- 위험: 전환 중 두 경로가 동시에 켜져 예고가 두 번 나가는 것 → 플레이어 전환 때처럼 리프가 기존 실행기를 호출하고 State 코드를 지운다. 동기식 그래프 제약을 어기면 한 틱에 판단이 끝나지 않는다
- 공수: 5인·일. 담당 @oname36-creator, 그래프 수명·네트워크 연결은 @AidenKim923. **P0(구조), W3~W4**
- 씬·프리팹: `Boss_Proxy.prefab`에 Agent 컴포넌트가 붙는다. 프리팹 변경, meta 동반

### U18 텔레그래프 표현 · U45 창귀 예고 뷰 · 일시정지 보정

- 상태: **부분.** 보스 `BossTelegraphView`가 4템플릿·다중 cue·패링 가능 색 구분까지 그린다. 남은 것은 둘이다
  - **창귀 전용 예고 뷰가 없다.** `EnemyParryWindowFlash`(렌더러 색)가 전부다. 사람이 패링을 배우는 교보재가 창귀인데 화면에 돌진 방향·범위가 안 보인다
  - **일시정지 시각 보정이 없다.** `UpdateAlpha`가 `RunTimeLocator.IsPaused`를 보지 않아 이탈 대기 중 예고가 계속 진행한다
- 권장 접근: 창귀는 `EnemyVisualState.Telegraph` 전이와 `EnemySO.WindupDuration`·`ChargeHitRadius`로 서버 절대 시각 기준 레인 하나를 그린다. 보스 뷰의 메시 빌더를 재사용한다. 일시정지는 두 뷰가 `RunTimeLocator`의 정지 누적을 빼고 진행률을 계산한다
- 공수: 2인·일. 담당 @oname36-creator + @AidenKim923. P1

### U20 기상 예고 표현

- 상태: **부분.** 서버는 `WakeWarning` 단계를 커밋하지만 `BossAnimationView`가 Groggy와 같은 자세로 묶고, HUD는 Stage를 읽지 않는다
- 권장 접근: `BossPhaseSnapshot.Stage == WakeWarning`에서 기상 모션 트리거와 보스 바 색 변화. 직후 패턴 2(예고 0.8초)가 고정이라 이 2초가 보이지 않으면 예고가 장식이 된다
- 공수: 1인·일. 담당 @oname36-creator. P1

### U25 거점 조합 프리셋 · 접두사 행동 · 밤 modifier

- 상태: **미착수.** `SpawnConfigSO`는 비율 추첨만, `EnemySO.EnhanceEntry`는 배수와 `PrefixName`만. 속도 +30% · 사망 장판 · 1회 부활 코드 없음
- 권장 접근: 프리셋 5종(무리·돌격대·저주진·혼성·창귀 무리)을 `SpawnConfigSO`에 배열로 추가하고 구간별 풀에서 고른다. 접두사 행동은 `EnemySO`에 필드 셋과 Brain의 작은 분기로 끝낸다
- 공수: 2.5인·일. 담당 @oname36-creator. **P2 선택** — W4 미통과 시 잘린다

### 창귀 기상 공격

- 상태: **미착수.** `Changgwi_StandUp.fbx`는 있으나 `ChanggwiLocomotion.controller`에 상태가 없고 `TickKnockback`은 곧장 `PatrolIdle`로 돌아간다
- 공수: 1인·일. 담당 @oname36-creator. P2

---

## 4. 남은 항목 — 표현 · UI · 씬

### U43 릴리스 씬 채우기 — 신규

코드는 거의 다 있는데 **릴리스 씬이 비어 있는** 항목을 하나로 묶는다. 이대로 릴리스 빌드를 만들면 타이틀이 빈 화면이다.

| 씬 | 현재 | 필요한 것 |
|---|---|---|
| `Title.unity` | 3.5 KB, 사실상 빈 씬. `TitleMenuController`는 `Dev_Title`에만 있다 | 시작·종료·Steam 로비 진입 버튼 배선 |
| `BossEntrance.unity` | 바닥·조명·스폰 3개뿐 | 전실 대기 공간, 트라이 편성 UI 자리(U35) |
| `Boss_Arena.unity` | 바이너리 직렬화. `BossSpawner`·`BossArenaPhaseView` 포함 확인 | 소품·재질 최종본(U40) |
| `Field.unity` | `Ground` + 문 3조각 + 클러스터 3개 프로토타입 | 실제 지형(U31 실측과 함께) |
| 빌드 씬 목록 | 릴리스 7 + `Main`·`Dev_Combat`·`Dev_Title`·`Dev_Lobby`·`Dev_BossTrial` 혼입 | 릴리스 빌드 프로필에서 Dev 씬 제외 |

- 권위: UI 로컬 / 입장·역할·준비·런 시작 서버(이미 있음)
- 위험: 씬은 머지가 안 된다. **씬 하나에 한 사람만** 들어간다. 타이틀·전실은 @kjw1237, 아레나·필드는 @AidenKim923
- 공수: 4인·일. **P0, W3~W4**

### U27 HUD

- 상태: **부분.** `RunHUD.prefab`에 자원 바 3종 · 파티 플레이트 · 보스 바(HP·그로기 게이지·광폭화 경고) · 시간 · 미니맵(도트·위험 오버레이·결계 원) · 결계 경고 배너 · 관리 창 중 HUD 숨김이 있다
- 남은 것:
  - 락온 표식(`LockOnReticle`)과 미니맵 펼치기(`MinimapExpandToggle`)는 **`Dev_HUD`에만 있고 `RunHUD.prefab`에 없다.** 옮기기만 하면 된다
  - `WeaponAbilityHUD`가 현재 반격 표식·무기 능력 상태를 표시한다. 후속은 실제 배선·가독성·원격 변화 검증이며 구 패링 게이지 위젯을 새로 만들지 않는다.
  - 봇 참가자의 "AI" 배지(설계 8장)
- 공수: 2인·일. 담당 @kjw1237. P0

### U28 씬 UI 배선 · EventSystem

- 상태: **부분.** `Boot.unity` → `SessionManagers.prefab`이 프로젝트 유일 EventSystem(`RunEventSystem`)과 HUD·관리 창·결과·상태 패널·로비 무기 패널을 전역으로 들고 간다. 로비 준비·무기 선호·봇 충원은 `LobbyWeaponPanel`이 코드로 생성한다. 결과 패널도 전역
- 남은 것: 타이틀 씬 버튼(U43) · 릴리스 빌드 clean build에서 Boot부터 전 경로 1회
- 공수: 1인·일. 담당 @kjw1237. P0

### U35 보스 트라이 편성 UI · 전실 배치

- 상태: **부분.** 서버(`BossTrialDirector` · `BossTrialRules` · `BossTrialLoadoutSO`)와 개발 패널·`Dev_BossTrial` 씬은 있다. **정식 편성 UI와 전실 공간이 없다.** 편성 포인트는 임시값(예산 100 · 등급 10/25/45/불가 · 물약 10 · 최대 3)
- 선행: U43 전실. 필드 30분 시점 기대 획득량 실측
- 공수: 2인·일. 담당 @kjw1237. **P2 선택**

### U29 · U40 보스 아레나 연출 · 재질 · 소품

- 상태: **부분.** `BossArenaPhaseView`는 권위 연동·늦은 접속 복원·6초 보간까지 코드 완료. 안개·하늘 프로파일 실물 있음. 소품·반사·최종 재질은 미완
- 선행: U43 아레나 씬 소유자 결정
- 공수: 1.5인·일. 담당 @AidenKim923. P1(티저에 보스가 들어간다)

### U31 맵 반경 실측 · 랜드마크 제약

- 상태: **부분.** 제약 로직(`BarrierPhaseSO.RequiredLandmarks` · `BarrierMapBinding` · 폴백 중심)과 Field 씬 랜드마크 3종 배선은 완료. **`_maxRadius 400`은 실측 근거가 없는 초기값**이고 필드가 프로토타입이라 실측할 지형 자체가 없다
- 권장 접근: 실제 지형이 들어온 뒤 3차 결계 최종 반경 안에 보스 입구가 들어오는지 100판 시뮬레이션(에디터 메뉴)으로 확인한다. 미해결 결정(3차 편향 · 입구 이동 · 반경 확대)은 이때 정한다
- 공수: 2인·일. 담당 @kjw1237 + @AidenKim923. P0(지형 도착 즉시)

### U26 애니메이션 계약

- 상태: **부분.** 플레이어(`PlayerActionCue` · 히트스톱)와 잡귀는 완료. **보스는 `BossAnimationView`가 `Attack`·`Dead` 트리거 2종뿐.** 그로기·기상·페이즈 전환·부위 파괴 모션 미연결
- 공수: 2인·일. 담당 @AidenKim923. P1

### U30 사운드 · U34 타격 사운드

- 상태: **부분.** SFX 37엔트리 클립 연결 완료(`ArtProductionWiringHelper`가 자동 배선). 방울·치유·표식·패링 키도 있다. **누락 4키: `StaminaEmpty` · `FootstepWet` · `BossHurt` · `BossGroggyEnd`. BGM 4트랙 전부 빈 클립**
- 권장 접근: 4키는 보유 번들(RPG & Fantasy Sounds)에서 고른다. BGM은 조달 문제라 P1이지만 26분 고정 길이를 런 제한으로 쓰지 않는다
- 공수: 1인·일. 담당 @AidenKim923. P1

### U34 타격감 5항목

- 상태: **4/5 완료.** 셰이크 · 히트스톱 · 피격 플래시(프리팹 5개 배선) · 검기 VFX 완료. 타격 사운드만 위 4키 누락
- 남은 결정: Bloom intensity(현재 0이라 검기 칼끝이 발광하지 않음) · 치유·표식 VFX 아군 색 교체
- 공수: 0.5인·일. P1

### U32 DayCycle 프리셋

- 상태: **부분.** `DayCycleController`가 Field에 배치돼 런 시계로 WeatherMaker 시각을 쓴다. **`DayCycleSO._presets`가 빈 배열**이라 조명 프리셋 보간은 동작하지 않는다
- 공수: 0.5인·일. 담당 @AidenKim923. P1

### U41 컷씬 · 시네머신

- 상태: **미착수.** Cinemachine 패키지 없음, Timeline만 있음, playable 0건
- 권장 접근: 컷씬 1개 × 15초, 객주 폭로 하나. 각 클라가 서버 확정 시각을 읽어 재생하고 **컷씬 중 서버 판정을 멈추지 않는다**. 실패 시 정지 화면 + 텍스트
- 선행: 갓·너울 에셋, 갓이 머리 메시와 분리될 것
- 공수: 5인·일. 담당 @AidenKim923. P1 — **절단선 5번**

### U33 카메라 개인 옵션 · 근접 가림

- 상태: **미착수.** 옵션 UI·PlayerPrefs 저장·디더 페이드 없음. `CameraRig`는 SO 기본값만 읽는다
- 공수: 1인·일. **P2 선택**

### U15 Weapon 실행 계약 · ArrowLauncher

- 상태: **의도적 미착수.** `PlayerAttack`이 직접 스폰한다. 중복이 실제로 생길 때만 추출한다. **P2 선택**

---

## 5. 문서에 없었는데 생긴 것

로드맵 항목이 아니지만 검증·유지보수 범위에 들어간다.

| 기능 | 위치 | 비고 |
|---|---|---|
| 참가자 종류 · 파티 종류 분리 | `ParticipantKind` · `RunPartyKind` · `ParticipantDirectory` | 리더보드가 봇 파티 기록을 분리해 읽는다. 결과 네트워크 필드가 바뀌어 **호스트·클라 같은 빌드 필수** |
| 빌드 신원 검증 | `01.Network/Identity/BuildIdentity` + `GeumeumAuthenticator` | 버전 불일치 접속 차단 |
| 런처 · 패치 배포 | 저장소 루트 `launcher/`(.NET) · [패치 시스템 설계](unity/PATCH_SYSTEM_DESIGN.md) | 팀원 빌드 배포용. 게임 코드와 무관 |
| 자동 검증 하네스 | `01.Network/Dev/VerificationCommandDriver`(1,432줄) · `tools/verify-*.ps1` | HTTP로 M2 시나리오 자동 실행. L3 대체 아님 |
| `RendererTintRegistry` | `00.Common/Utility/` | 피격 플래시와 패링 창 강조의 틴트 충돌 중재 |
| `Dev_BossTrial` · `Dev_HUD` | 개발 씬 | 빌드 목록 혼입 주의(U43) |

---

## 6. 외부 에셋 도입 조건

| 에셋 | 용도 | 조건 |
|---|---|---|
| **PhysioBrush** (본 물리) | 너울·옷자락·꼬리 2차 모션 | **표현 본에만 건다.** 꼬리는 부위 판정 대상이라 판정 콜라이더는 흔들리지 않는 본에 붙인다. 리깅 단계에서만 가능 |
| **HTrace AO** | 티저·인게임 화질 | **렌더 프로파일 분리.** 3인 테스트는 AO를 끈 프로파일로. 한 대만 프레임이 떨어지면 네트워크 문제와 구분이 안 된다 |
| RPG & Fantasy Sounds | `SoundCatalogSO` 누락 4키 · BGM | 조건 없음 |
| RPG Character Mecanim Pack | 보스 모션 보충(U26) | 리타게팅 확인 |
| Sketchfab 의상 | 객주 = 보스 1페이즈 동일 외형 | 한 벌이어야 폭로가 읽힌다 |
| Meshy AI 갓 | 컷씬 소품(U41) | 머리 메시와 분리된 독립 오브젝트. 생성일·플랜 기록 |
| UnityChanSpringBone (도입됨) | 보스 프록시 의상 | 적용 완료 |

**임포트 시점.** 사운드·Mecanim 팩은 아무 때나. **HTrace·PhysioBrush는 3PC 관통 확인 이후.** 관통 스프린트 중에 렌더러나 물리를 바꾸지 않는다. 구매·생성 즉시 [라이선스 기록](licenses/THIRD_PARTY_ASSETS.md) 7항목을 채운다.

---

## 7. 이 문서의 검증 한계

- 정적 조사 기준이다. 바이너리 씬(`Boss_Arena.unity`)은 문자열 검색으로만 확인했다.
- "완료"는 코드·배선 존재를 뜻하며 3PC 통과를 뜻하지 않는다. 검증 등급은 [주간 계획 4장](06-weekly-plan.md).
- 공수는 담당자가 해당 파트를 이미 아는 상태를 가정한다.
- 우선순위와 일정은 이 문서가 정하지 않는다. [주간 계획](06-weekly-plan.md)이 정한다.
