# 그믐 (晦)

> 그믐밤이 걷히기 전, 세 차사가 원귀의 근원을 베고 저승으로 돌아와야 한다.

한국 저승 설화를 배경으로 한 **코옵 멀티플레이 로그라이크 액션 RPG**.
엘든 링: 밤의 통치자의 구조를 가져오되, 세계와 규칙은 새로 만든다.

> **팀에 처음 합류했다면 → [어디부터 읽을까](docs/00-reading-guide.md)**
> 문서가 29개지만 첫날 읽을 것은 3개뿐이다.

| | |
|---|---|
| 장르 | 코옵 멀티플레이 로그라이크 액션 RPG |
| 플랫폼 | PC |
| 인원 | 3인 온라인 협동 |
| 엔진 | Unity 6.4.7f1 / URP |
| 네트워크 | Mirror |
| 한 판 | 약 40분 |
| 개발 | 2026.08.30 – 2026.11.16 |

---

## 코어 루프

아침 8시에 시작해 그믐 자정에 결착한다. 시간이 흐를수록 좋은 장비가 나오지만, 동시에 결계가 조여 온다.

| 구간 | 실시간 | 게임 내 | 범위 |
|---|---|---|---|
| 개활 파밍 | 0:00 – 7:00 | 08:00 – 12:40 | 100% |
| 1차 축소 | 7:00 – 12:00 | 12:40 – 16:00 | 50% |
| 2차 축소 (객주) | 12:00 – 15:00 | 16:00 – 18:00 | 30% |
| 3차 축소 · 보스 | 15:00 – | 해질녘 – 자정 | 보스맵 |

무기가 직업을 정한다. 검이면 차사, 활이면 신궁, 방울이면 무녀다.

---

## 시작하기

```bash
git clone https://github.com/inha-2026-gamedevelop/gemeum.git
cd gemeum

# LFS 초기화 — 반드시 실행
git lfs install
git lfs pull
```

Unity Hub에서 **6.4.7f1** 버전으로 연다. 다른 버전으로 열면 프로젝트 파일이 갱신되어 되돌리기 어렵다.

자기 브랜치로 이동하고, 자기 담당 씬을 연다.

```bash
git checkout feature/{자기이름}
```

| 파트 | 브랜치 | 씬 |
|---|---|---|
| 파트 | 담당자 | 브랜치 | 씬 |
|---|---|---|---|
| 네트워크 · 전투 | @AidenKim923 | `feature/Minsung` | `Assets/00.Scenes/Dev_Combat.unity` |
| 몬스터 · 보스 | @oname36-creator | `feature/{이름}` | `Assets/00.Scenes/Dev_AI.unity` |
| 게임플레이 시스템 | @kjw1237 | `feature/{이름}` | `Assets/00.Scenes/Dev_System.unity` **(미생성)** |
| 테크니컬 아트 | @AidenKim923 (겸임) | `feature/Minsung` | `Assets/00.Scenes/Dev_Art.unity` **(미생성)** |

---

## 문서

**[어디부터 읽을까](docs/00-reading-guide.md)** — 읽는 순서와 파트별 필독 목록. 아래 전체 목록보다 이걸 먼저 본다.

### 기획

| 문서 | 내용 |
|---|---|
| [시스템 기획서](docs/01-system-design.md) | 개요, 세계관, 코어 루프, 일정, 스코프 원칙 |
| [게임 디자인](docs/02-game-design.md) | 밸런스 수치, 직업, 장비, 경제, 성장, 점수 |
| [기술 설계](docs/03-technical-design.md) | 아키텍처, 서버·클라 경계, 파트별 스펙 |

### 개발

| 문서 | 내용 |
|---|---|
| [처음 프로젝트를 열 때](docs/unity/UNITY_START_HERE.md) | 클론부터 첫 실행까지 |
| [협업 규칙](CONTRIBUTING.md) | 브랜치, 씬 규칙, PR, 주간 통합 |
| [코딩 컨벤션](docs/coding-convention.md) | 네이밍, 스타일, GameDB/Constants 데이터 관리 |
| [네트워크 권위](docs/unity/NETWORK_AUTHORITY.md) | 서버냐 클라냐 판단 기준 |
| [씬 · 에셋 안전](docs/unity/SCENE_AND_ASSET_SAFETY.md) | 씬 충돌, `.meta`, LFS |
| [밸런스 데이터 작업](docs/unity/BALANCE_DATA_WORKFLOW.md) | ScriptableObject 운용 |
| [엔지니어링 패턴](docs/unity/UNITY_ENGINEERING_PATTERNS.md) | Tick 수명, 캐싱, 코루틴 |
| [LOD · 컬링 현황](docs/unity/LOD_AND_CULLING.md) | LOD, 오클루전 컬링, 배칭 적용 상태 |

### AI 협업

| 문서 | 내용 |
|---|---|
| [CLAUDE.md](CLAUDE.md) | AI가 자동으로 읽는 최상위 지침 |
| [AI 작업 문서](docs/ai/README.md) | 브리프, 검증, 인수인계, 멀티플레이 테스트 |

**밸런스 수치는 전부 프로토타입용 시작점이다.** 실제 플레이 테스트로 절반은 바뀐다.

---

## 팀

3인 팀. 테크니컬 아트는 팀장이 겸임한다.

| 파트 | 담당자 | 핵심 기술 |
|---|---|---|
| 네트워크 · 전투 (팀장) | 김민성 @AidenKim923 | 네트워크 환경에서의 패링 판정 동기화 |
| 몬스터 · 보스 | @oname36-creator | 보스 패턴 상태 머신 |
| 게임플레이 시스템 · 결계 | @kjw1237 | 가중치 기반 절차적 아이템 생성 |
| 테크니컬 아트 (겸임) | 김민성 @AidenKim923 | URP 기반 렌더링 및 아트 파이프라인 |

---

## 개발 도구

| 도구 | 용도 |
|---|---|
| Mirror | 네트워크 (KCP Transport) |
| Gaia for Unity 6 | 지형 생성 |
| Editor Console Pro 3 | 로그 필터링, 네트워크 디버깅 |
| Hierarchy Designer | 하이어라키 정리 |
| Maintainer 2 | 참조 검사, 미사용 리소스 정리 |

---

## 스코프 원칙

완성을 최우선한다. 아래는 고정이며, 바꾸려면 팀 전체 합의가 필요하다.

- 보스는 **1마리**. 여유가 생기면 그때 추가 검토
- 맵은 **고정 맵 + 랜덤 스폰**. 절차적 생성 없음
- 제작 · 합성 **없음**. 주우면 바로 사용
- 인벤토리 슬롯 3~4칸, 확장 없음

---

## 에셋 라이선스

구매 및 무료 에셋은 상업 이용 가능 범위로 관리한다.
2027년 인디게임 출품전 제출을 대비해 근거 문서를 `docs/licenses/`에 보관한다.
