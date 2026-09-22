# 외부 에셋 라이선스 기록

> [에셋·데이터 취급 정책](../ai/12_ASSET_AND_DATA_POLICY.md) 3장 양식에 따른 기록.
> 2027년 인디게임 출품전 제출 대비, 상업 이용 가능 여부와 출처를 남긴다.

> **2026-09-13 전수 조사.** 설치된 에셋을 아래 방법으로 확인했다. 라이선스 유형을 확인한 것과 증빙 요건(아래 7항목)을 채운 것은 다르다. **증빙 열이 `미확인`이면 출품·배포 빌드에 넣지 않는다.**
>
> - 식별: `.meta`의 `AssetOrigin`(상품 ID · 패키지명 · 버전), git 패키지는 `packages-lock.json` 해시
> - 제작자 · 라이선스 유형: Asset Store 상품 페이지, 저장소에 있는 LICENSE 원문, GitHub API, Sketchfab API
> - 실제 사용: 커밋된 텍스트 에셋(씬 · 프리팹 · 머티리얼 · 애니메이터 등)이 패키지 GUID를 참조하는지 대조. 바이너리 직렬화 에셋 21개(`Boss_Arena.unity`, 지형 · NavMesh 등)는 대조하지 못했다

### 1. Unity Asset Store

스토어 페이지 기준 **여기 있는 에셋은 전부 Standard Unity Asset Store EULA이고, 라이선스 유형은 `Extension Asset`으로 표시**된다.

| 에셋 (버전) | 제작자 | 상품 ID | 설치 위치 | 실제 사용 | 상업 이용 | 취득일 | 증빙 |
|---|---|---|---|---|---|---|---|
| CombatGirls_SwordCharacterPack (1.3.1) | BIPER | 345454 | `99.ThirdParty/Animation/CombatGirlsCharacterPack` | 로코모션 애니메이터 4개 | EULA 기준 가능 | 미확인 | 미확인 |
| Omni Animation - Core Locomotion Pack (1.0.1) | Opsive | 286945 | `99.ThirdParty/Animation/OmniAnimation` | `PlayerLocomotion.controller` | EULA 기준 가능 | 미확인 | 미확인 |
| Succubus Anim Set (1.21) | wemakethegame | 179271 | `99.ThirdParty/Animation/Succubus_Anim_Set` | `BossLocomotion.controller` | EULA 기준 가능 | 미확인 | 미확인 |
| Kitsune Yuna (1.0) | Ida Faber | 265718 | `99.ThirdParty/Model/Ida Faber` | `Boss_Proxy.prefab` | EULA 기준 가능 | 미확인 | 미확인 |
| Gaia for Unity 6 (4.2.5) | Procedural Worlds Inc | 264629 | `99.ThirdParty/Background/Gaia` | 텍스트 에셋 참조 0 (지형은 바이너리라 미대조) | EULA 기준 가능 | 미확인 | 미확인 |
| Book Of The Dead Environment (1.1) | Unity Technologies | 121175 | `99.ThirdParty/Background/Procedural Worlds` | `Main.unity` 등 13개 | EULA 기준 가능. **단 3D 텍스처 · 후처리 · Shader Graph · Render Pipeline Core는 Unity Companion License** (`ThirdPartyNotices.txt`) | 미확인 | 미확인 |
| Weather Maker (8.1.0) | Digital Ruby (Jeff Johnson) | 60955 | `99.ThirdParty/Background/WeatherMaker` | 씬 17개 | EULA 기준 가능 | 미확인 | 미확인 |
| RPG & Fantasy Sounds Bundle (1.0) | PlaceHolder Inc (폴더명 NaPH) | 379678 | `99.ThirdParty/Sounds/NaPH - RPG & Fantasy Sounds Bundle` | `SoundCatalogSO.asset` | EULA 기준 가능 | 미확인 | 미확인 |
| Realistic Spirit Blossom Spells VFX (1.0) | Piloto Studio | 399376 | `99.ThirdParty/VFX/Piloto Studio` | 무녀 VFX 프리팹 5개 | EULA 기준 가능 | 미확인 | 미확인 |
| Stylized Elemental Shield VFX (1.0) | TvitchiVFX | 346144 | `99.ThirdParty/VFX/VFX SHields` | `M_BarrierWall.mat` | EULA 기준 가능. 일부 텍스처는 CC0 (`license.txt`) | 미확인 | 미확인 |
| Human Archer Animations FREE (1.0) | Kevin Iglesias | 335880 | `98.FreeAssets/Human Archer Animations` (커밋) | 로코모션 애니메이터 4개 | EULA 기준 가능 | 미확인 | 미확인 |
| Editor Console Pro (3.983) | FlyingWorm | 11889 | `99.ThirdParty/Tools/ConsolePro` | 에디터 전용 | EULA 기준 가능 | 미확인 | 미확인 |
| Easy Effect/UI Viewer (1.1.4) | Yu-Rin-Chi GameStudio | 348954 | `99.ThirdParty/Tools/EasyProjectViewer` | 에디터 전용 | EULA 기준 가능. 동봉 고지: WuHuan (2018) | 미확인 | 미확인 |
| Hierarchy Designer (3.2.2) | Pedro Verpha | 273928 | `99.ThirdParty/Tools/Hierarchy Designer` | 에디터 전용 | EULA 기준 가능 | 미확인 | 미확인 |
| PhysioBrush (1.6.7) | Rip Vertices Studio | 375898 | `99.ThirdParty/Tools/PhysioBrush` | 갓 · 너울 물리 프리팹 2개 · 설정 1개 (2026-09-13 갓·너울 프리팹 2개와 PhysioSettings의 런타임 자산 3개 저장소 추적) | EULA 기준 가능 | 미확인 | 미확인 |
| Wingman (1.1.0) | Kyle Rhoads | 303181 | `99.ThirdParty/Tools/Wingman` | 에디터 전용 | EULA 기준 가능 | 미확인 | 미확인 |
| Mesh Baker (3.39.0, 유료판) | Ian Deane | 5017 (`.meta`에 출처가 없어 판본은 사용자 확인) | `99.ThirdParty/Tools/MeshBaker` | 참조 0 | EULA 기준 가능 | 미확인 | 미확인 |

### 2. 오픈소스 · 무료 라이선스

| 에셋 | 제작자 | 출처 | 라이선스 | 설치 위치 | 근거 |
|---|---|---|---|---|---|
| Mirror (96.0.1) | Mirror Networking | [GitHub](https://github.com/MirrorNetworking/Mirror) · Asset Store 129321 | MIT | `Assets/Mirror` | `Assets/Mirror/LICENSE`, GitHub API. 예제 `BenchmarkStinkySteak`(MIT, StinkySteak) · `Hosting/Edgegap`(MIT, Edgegap) 동봉 |
| FizzySteamworks | Fizz Cube Ltd | [GitHub](https://github.com/Chykary/FizzySteamworks) (lock `2339a4a474`) | MIT | `Packages` | 패키지 캐시 `LICENSE` |
| Steamworks.NET | Riley Labrecque | [GitHub](https://github.com/rlabrecque/Steamworks.NET) (lock `ba71581f1e`) | MIT | `Packages` | `LICENSE.md`, GitHub API |
| Steamworks SDK 재배포 파일 (`steam_api`) | Valve | Steamworks.NET 동봉 | **Valve Steamworks 약관 (MIT 아님)** | `Packages/.../Plugins` | 저장소 루트 `steam_appid.txt`가 `480`(Valve 테스트 앱)이다 |
| MCP for Unity | CoplayDev | [GitHub](https://github.com/CoplayDev/unity-mcp) (lock `30d2207509`) | MIT | `Packages` (에디터 전용) | GitHub API |
| UnityChanSpringBone | Unity Technologies ApS | [GitHub](https://github.com/unity3d-jp/UnityChanSpringBone) | MIT | `98.FreeAssets/UnityChanSpringBone` (커밋) | `LICENSE`, GitHub API. `Boss_Proxy.prefab`에서 사용 |
| Remotion (4.0.524, 티저 조립) | Remotion | [공식 라이선스 안내](https://www.remotion.dev/docs/license/pricing) | 적용 조건은 배포 전 공식 문서 재확인 | `tools/teaser` | package.json·lockfile에 설치 정의 있음. 조직·협업 인원에 따른 권리를 별도 확인 |
| Unity 공식 패키지 (URP · Timeline · TextMesh Pro · Newtonsoft Json 등) | Unity Technologies | Unity Registry | 패키지별 (Unity Companion License 등) | `Packages`, `Assets/TextMesh Pro` | 각 패키지 `LICENSE.md` |
| 명원체 / 한글재민체4.0 (UI 폰트) | 국민대학교 | [국민대학교 전용서체](https://www.kookmin.ac.kr/comm/menu/user/0a4f7cf187cc3164678352bc3f4575b3/content/index.do) | OFL — 상업 이용 · 게임 임베딩 · 수정 허용, 폰트 파일 유료 판매 금지 | `13.UI/00.UI_Resource/Fonts` | 배포 페이지 |

### 3. 외부 사이트 모델 · 애니메이션

| 에셋 | 제작자 | 출처 | 라이선스 | 설치 위치 | 비고 |
|---|---|---|---|---|---|
| 검 (Four Tiger Sword) | KOREA HERITAGE SERVICE [KHS] | [Sketchfab](https://skfb.ly/p9yx6) | **CC Attribution. 저작자 표기 필수, 상업 이용 가능** | `06.Model/Weapon/Sword/SM_GB_FourTigerSword.fbx` | Sketchfab API로 확인 |
| 활 | KOREA HERITAGE SERVICE [KHS] | [Sketchfab](https://skfb.ly/p9zBC) | **CC Attribution. 저작자 표기 필수, 상업 이용 가능** | `06.Model/Weapon/Bow/SM_Bow.fbx` | 사용자 확인 (2026-09-13, 검과 같은 제작자 · 라이선스) |
| 화살 | KOREA HERITAGE SERVICE [KHS] | [Sketchfab](https://skfb.ly/p9zBJ) | **CC Attribution. 저작자 표기 필수, 상업 이용 가능** | `06.Model/Weapon/Arrow/SM_Arrow01a.fbx` | 사용자 확인 (2026-09-13, 검과 같은 제작자 · 라이선스) |
| 방울 | KOREA HERITAGE SERVICE [KHS] | [Sketchfab](https://skfb.ly/p9zWJ) | **CC Attribution. 저작자 표기 필수, 상업 이용 가능** | `06.Model/Weapon/Bell/SM_24RE0039_Low.fbx` | 사용자 확인 (2026-09-13, 검과 같은 제작자 · 라이선스) |
| Stand To Roll | Mixamo (Adobe) | Mixamo | 미확인 | `04.Animations/Cilp/Stand To Roll.fbx` | |
| Sprinting Forward Roll | Mixamo (Adobe) | Mixamo | 미확인 | `04.Animations/Cilp/Sprinting Forward Roll.fbx` | 구르기에 실제 사용 중(2026-08-31 기록). 출처는 사용자 확인 |

### 4. AI 생성물

생성 시점의 플랜에 따라 권리가 달라지므로 **생성일과 플랜을 함께 남긴다**([구현 로드맵](../05-implementation-roadmap.md) 조달 절).

| 자산 | 도구 | 위치 | 권리 조건 | 상태 |
|---|---|---|---|---|
| 플레이어 3종(차사 · 신궁 · 무녀) · 잡귀 3종(창귀 · 액귀 · 허깨비) 모델과 텍스처 | Meshy AI (일부 Blender 재작업) | `06.Model/Player`, `06.Model/Monster`, `03.Image` | **유료 플랜이라 생성자 소유** (무료 플랜 생성물이었다면 CC BY 4.0) | 플랜은 사용자 확인. **생성일 미기록** |
| 보스 1페이즈 · 객주 갓과 너울 | Meshy AI 초안 → **미채택**, Blender에서 새 메시로 재작성 | `06.Model/Monster/GatNeoul` | 최종 메시는 팀 제작물. 초안 생성은 위 Meshy 조건 | 착용 확인용 파일에만 구매 보스 모델을 넣었고 최종 FBX · GLB에는 없다. 구매 에셋을 Meshy에 올리지 않았다 (제작 경계와 런타임 경로는 [갓·너울](../unity/GAT_NEOUL_PHYSIO.md) 참조) |
| HUD 이미지 | ChatGPT (OpenAI) | `13.UI/00.UI_Resource/HUD` | [OpenAI 이용약관](https://openai.com/policies/terms-of-use)에 따름. 원문 캡처 미보관 | 도구는 사용자 확인. 반입 2026-09-07 (`666d63e`), 프롬프트는 `docs/unity/HUD_DESIGN.md` |
| 인게임 메뉴 이미지 15장 | ChatGPT (OpenAI) | `13.UI/00.UI_Resource/InGame` | [OpenAI 이용약관](https://openai.com/policies/terms-of-use)에 따름. 원문 캡처 미보관 | 도구는 사용자 확인. 반입 2026-09-18. 한 장짜리 시트로 받아 요소별로 잘라 나눴고 시트 원본은 남기지 않았다. 프롬프트 미기록 |
| BGM | Suno (예정) | 없음 | 아래 "AI 생성 음원" 절 | 아직 사용 안 함 (`SoundCatalogSO` BGM 비어 있음) |

**출처만 기록된 항목은 라이선스·상업 이용 가능 여부가 확정되기 전까지 출품·배포용 빌드에 포함하지 않는다.**
각 출처 페이지에서 제작자명과 라이선스(CC-BY, Asset Store EULA 등 정확한 종류)를 확인해 채워 넣는다.

## 배포 전 조치 (2026-09-13 조사 기준)

| 우선 | 할 일 | 이유 |
|---|---|---|
| 1 | **`steam_appid.txt`의 `480`을 자체 App ID로 교체** | `480`은 Valve 테스트 앱이다. 배포 빌드에 쓸 수 없다 |
| 2 | **Extension Asset 좌석 조건 확인** | 스토어가 1장의 에셋 전부를 `Extension Asset`으로 표시한다. 좌석(사용자)별 라이선스 조항이 있다면, 구매본 하나를 드라이브로 3인이 공유하는 지금 방식이 EULA에 맞는지 확인해야 한다 |
| 3 | 사용자 확인으로 채운 항목의 증빙 확보 | Sketchfab 활 · 화살 · 방울, `Sprinting Forward Roll`, HUD 이미지 도구, Meshy 플랜, Mesh Baker 판본은 2026-09-13 사용자 답변으로 채웠다. 페이지 · 구매 내역 캡처는 아직 없다 |
| 4 | 크레딧 화면 항목 확정 | 무기 4종 검 · 활 · 화살 · 방울 (KHS, CC BY 저작자 표기) · 명원체 (OFL 고지) |
| 5 | 전 항목 증빙 7항목 채우기 | 취득일 · 취득 계정 · EULA/라이선스 원문 캡처 보관 위치가 아직 전부 비어 있다 |
| 6 | 쓰지 않는 에셋 정리 판단 | 참조 0: Mesh Baker (에디터 전용 도구 제외). 제거하면 대장에서도 뺀다 |

## 폰트

UI 폰트는 국민대학교가 배포하는 **명원체**(명원박물관 전용서체)이며, 공식 명칭은 `한글재민체4.0`이다.
같은 배포 페이지의 **국민체는 CC BY-NC-ND로 상업 이용이 불가하므로 이 프로젝트에서 쓰지 않는다.**
KMU80(성곡체 · 해옹체)은 CC BY-ND라 변형 금지 조항이 걸려 있어 역시 도입하지 않는다.

OFL은 폰트를 게임에 포함해 배포하는 것을 허용하지만, 재배포 시 라이선스 원문과 저작권 고지를 함께 전달할 것을 요구한다.
빌드 크레딧이나 동봉 문서에 표기한다.

---

## 증빙 요건 — 제출·배포 전 필수 (D-P1-5)

> 2026-09-04 진행 가능성 감사가 지적한 항목이다. 감사 문서는 2026-09-07에 폐기했고, 증빙 요건은 이 문서가 이어받는다. 추적은 이 문서에서 직접 한다.

**현재 대장의 다수가 `미확인`이며, 자체 정책상 미확인 자산은 제출·배포 빌드에 포함할 수 없다.** 위 표는 출처만 있고 아래 항목이 비어 있다. 항목별로 다음을 모두 채워야 "확인"으로 올린다.

| 채울 것 | 비고 |
|---|---|
| 제작자와 원본 URL | 표에 URL만 있고 제작자명이 없는 항목이 다수다 |
| 에셋/음원 이름과 **버전** | 같은 에셋의 버전별 라이선스가 다를 수 있다 |
| **취득일 및 취득 계정** | 구매 계정이 팀 공용인지 개인인지 포함 |
| 적용 EULA/라이선스 **원문 또는 캡처 보관 위치** | 링크만으로는 증빙이 되지 않는다. 페이지가 바뀐다 |
| 상업적 사용 · 수정 · 재배포 · **크레딧 요구** 여부 | 크레딧 요구는 제출물 크레딧 화면에 직접 영향 |
| 프로젝트 내 **실제 사용 경로** | 쓰이지 않는 에셋은 제거 대상으로 분류 |
| **제거/대체 담당자와 기한** | 확인 불가 판정 시의 대체 계획 |

### AI 생성 음원(Suno)의 권리 확인

유료 플랜에서 생성된 콘텐츠의 상업적 이용 권리는 **생성 시점의 구독 상태에 좌우되며, 과거 무료 플랜 생성물에 소급되지 않는다.** 따라서 곡별로 **생성일과 그 시점의 플랜**을 함께 보관해야 한다. stem 분리 기능의 플랜 제한과 다운로드 한도, 권리 문구는 변경될 수 있다.

- [Suno Terms of Service](https://suno.com/terms/)
- [Suno Pricing](https://suno.com/pricing)
- [Suno Terms 업데이트 안내](https://suno.com/blog/suno-updates-tos)
- [상업적 이용 권리의 비소급 안내](https://help.suno.com/en/articles/2425729)

### 문서 간 불일치 주의

사운드 설계 문서가 "라이선스 디렉터리가 비어 있다"고 설명하던 시점이 있었으나 **실제로는 이 대장 파일이 존재한다.** 라이선스 상태를 인용할 때는 이 파일을 정본으로 본다.
