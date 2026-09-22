# 플레이어 행동 트리 · 동료 봇

2026-09-22 코드 기준. Unity Behavior 기반 규칙형 봇이 구현되어 있다. 직접 작성한 옛 BT 엔진은 현재 실행 경로가 아니다. ML-Agents 훈련·ONNX 정책은 [후속 제안](PLAYER_BT_ML_AGENTS_DESIGN.md)으로 분리한다.

## 참가자와 권위

사람 1+봇 2, 사람 2+봇 1, 사람 3을 같은 런 규칙으로 지원한다. 일반 런은 사람이 없는 편성으로 시작하지 않는다. 운영 계정 경로의 봇 추가·무기·준비·출발은 런처에서 수행하며 호스트 게임이 서버 봇을 생성한다.

`ParticipantId`, `RunParticipantRegistry`, `PlayerBotSpawner`, `PlayerRunAgent`는 연결 없는 봇을 사람 연결 ID와 구분한다. 봇도 피해·치유·구조·인벤토리·성장·정산의 참가자다. 네트워크 connection 수를 전체 참가자 수로 대체하지 않는다.

사람 입력과 봇 판단은 공통 서버 행동 API를 사용한다. 봇이 HP·피해·자원·물리 이동·판정 결과를 직접 덮어쓰지 않는다. 관찰·의사결정은 서버, 원격 애니메이션과 HUD는 기존 상태 동기화 경로다.

## 현재 구조

| 구성 | 책임 |
|---|---|
| `PlayerBrain` | 서버 행동 실행과 사람/봇 분기 |
| `Assets/01.Scripts/03.AI/PlayerBot/` | 봇 관측·우선순위·역할 행동 |
| `BotConfigSO`, `BotSkillProfileSO` | 이동·반응·행동 설정 |
| Unity Behavior 작성/런타임 그래프 | 우선순위와 행동 연결 |
| `InventoryController` 봇 요청 경로 | 동료 배낭 조회와 허용된 서버 처리 |

### 에디터에서 열기

- 편집 그래프: `Assets/09.Data/BT/PlayerBehavior.asset`를 더블클릭한다.
- 메뉴: `Geumeum > AI > Open Player Behavior Graph`.
- 작성 에셋은 `BT`에 보관하고, 빌드 전 `Resources/BT/PlayerBehavior.asset`에 런타임 그래프와 런타임 블랙보드만 내보낸다. 작성 전용 타입이 플레이어에 포함되어 발생하던 직렬화 레이아웃 오류를 방지한다. 기존 작성 그래프는 유지하며, Regroup 분기가 없을 때만 추가한다.
- 그래프의 기본 `Try In Order` 노드는 왼쪽부터 평가한다. 순서/연결은 그래프 에셋이 정본이다.

```text
On Start
└─ Try In Order (Unity)
   ├─ Blocked
   ├─ Human
   └─ PrepareBot → Try In Order (Unity의 암시적 Sequence)
                  ├─ Safety
                  ├─ Defend
                  ├─ Regroup
                  ├─ Support
                  ├─ Combat
                  ├─ Follow
                  └─ Idle
```

`PlayerBehaviorAction`은 Unity Behavior의 Action을 상속하는 게임 전용 리프다. 선택한 Step의 기존 서버 실행기를 호출할 뿐 자식 순서/폴백/스케줄러를 구현하지 않는다. 적 센서·NavMesh 방향 계산·공격/방어 예약과 API는 기존 구현을 재사용한다.

### 실행 수명과 네트워크

`PlayerController.OnStartServer` → `PlayerBrain`에서 `BehaviorGraphAgent`를 추가하고 그래프를 개별 인스턴스화한다. 순수 클라이언트에는 Agent를 추가하지 않는다. 자동 Update를 끄고 기존 서버 권위 틱에서 패키지의 `BehaviorGraph.Restart/Tick`을 호출한다. 사람 입력은 Human 리프를 거쳐 기존 Command/이동 버퍼 경로를 유지한다.

이 에셋은 **한 번의 결정을 완료하는 동기식 그래프**다. 리프의 Success는 게임 행동 전체 완료가 아니라 이번 판단에서 의도 선택이 끝났다는 뜻이다. 이동·구조·정밀 홀드 등 지속 행동은 기존 서버 실행기의 상태로 유지하며 분기 변경/일시정지/세대 변경/종료 시 취소한다. 관측 갱신 간격은 BotConfig를 따른다.

On Start의 Repeat는 OFF, AllowMultipleRepeatsPerTick은 ON이다. 패키지 1.0.16의 Start는 Repeat가 꺼져 있어도 기본 프레임 가드를 적용하므로 같은 권위 틱에 완료하려면 후자가 필요하다. Wait/Repeat/순환 노드는 이 동기식 그래프에 추가하지 않는다. 128단계 안에 끝나지 않으면 오류를 남기고 입력을 정지한다. 이 제한은 무한 실행 방지용이며 봇 밸런스 수치가 아니다.


## 생명 주기와 동료 UI

봇은 씬 전환에 참가하며 같은 런의 참가자 ID를 유지한다. 새 런과 종료에는 이전 상태·요청·목표를 정리한다. 사람 이탈의 일시정지·복구와 봇 정지/재개를 함께 확인한다. 런 중 이탈자를 자동으로 봇으로 교체하거나 호스트를 자동 이전하는 기능은 현재 기본 계약이 아니다.

동료 봇 추적·공격과 배낭 조회가 구현되어 있다. UI는 `RequestBotInventory`/`TryGetBotInventory` 및 `BotInventoryView` 경로를 사용한다. 보스 부위·원혼·결계·구조·아이템 상호작용은 각 서버 서비스의 허용 조건을 통과해야 한다.

## 검증과 남은 범위

- 사람 1+봇 2 / 사람 2+봇 1 / 사람 3 편성에서 시작·씬 전환·결과·로비 복귀·두 번째 런.
- 사람 접속 종료·재접속 시 동일 런의 신원과 봇 상태, 결과 분류.
- 각 무기의 공격·자원·신규 R·특성 효과와 봇 의사결정 연동.
- 동료 추적·장애물·장시간 자연 플레이와 부하.
- 자체 계정/AWS 릴레이의 서로 다른 PC 3인 검증. Steam 호환 경로는 별도 검증.

이전 개발 빌드의 테스트 수와 경고 수는 현재 빌드의 검증 결과가 아니다. 이번 문서는 코드 구조 대조이며 자연 완주율·사람 대비 성능·학습 성능을 새로 측정하지 않았다.
