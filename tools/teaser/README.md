# 그믐 티저 준비 도구

현재 단계는 **게임 영상을 녹화하지 않는 준비·조립 검토 도구**다. `TeaserPlan`은 25개 샷의 타이밍과 소리 지시를 보여주며 “게임 화면 아님”을 표시한다. `TeaserReview`는 등록한 원본 클립·음원을 재생하고 “원본 조립 검토 · 미완성”을 표시한다. 아직 원본은 없다.

## 실행

Node 22 이상. 원본 등록·실측에는 PATH의 ffprobe, 미디어 통합 테스트에는 ffmpeg도 필요하다. 저장소 루트에서 PowerShell로:

```powershell
Set-Location tools/teaser
npm.cmd ci
npm.cmd run validate
npm.cmd test
npm.cmd run studio
```

`studio`가 출력하는 로컬 주소에서 `TeaserPlan` 또는 `TeaserReview`를 선택한다. 재생·정지·프레임 이동으로 73초 전체를 확인한다. 실행 시 등록된 원본을 실측 검사하며, 원본이 없는 현재 초안은 ffprobe 없이도 열린다. PowerShell 실행 정책을 변경하지 않도록 `.cmd` 명령을 사용한다.

`npm.cmd run still`은 계획 화면 한 장을 `out/plan.png`에 만든다. `npm.cmd run bundle`은 정적 Studio 번들을 `build/`에 만든다. 게임 녹화 기능과 완성 영상 출력 명령은 포함하지 않는다.

## 게임 원본 없이 완성한 편집 요소

```powershell
npm.cmd run export:graphics
npm.cmd run test:graphics
```

`out/graphics/`에 아래 파일을 만든다. 다시 실행하면 이 출력 파일들을 재생성한다.

| 파일 | 내용 |
|---|---|
| `preview.mp4` | 자막 4초 → 먹 전환 2초 → 엔딩 5초. 체커보드·검토 라벨이 있는 무음 11초 미리보기 |
| `ending.mp4` | 1920×1080/60fps, 먹 덮기 1초 + 제목 정지 4초. 검토 라벨 없음 |
| `subtitle-alpha.mov` | 240프레임, ProRes 4444 알파. 실제 시간표의 20초에 얹는다 |
| `ink-transition-alpha.mov` | 120프레임, ProRes 4444 알파. 시간표의 450f에 얹고 539/540f를 완전히 덮는다 |
| `subtitles.srt` / `subtitles.vtt` | 같은 JSON에서 만든 절대 시각 자막 |
| `manifest.json` / `verification.json` | 출력 출처·규격 및 파일 디코딩 검증 결과 |

Studio의 `TeaserGraphics`에서 전체를 보고 `TeaserEnding`, `TeaserSubtitle`, `TeaserInkTransition`에서 개별 편집한다. `TeaserReview`도 같은 컴포넌트를 쓴다. MOV를 일반 플레이어에서 검정 배경으로 보여도 알파가 없다는 뜻은 아니다.

타이틀/자막은 기존 한글재민체4.0을 직접 참조한다. `src/editorial.mjs`에서 색·글자 크기·위치와 전환 진행률을 조절한다. 먹은 고정 seed의 SVG 노이즈로 만든 **화면 밖 효과**이며 엔진 셰이더나 게임의 1:02 먹 연출이 아니다. 갓을 화면에 남기는 실제 매치컷은 원본의 실루엣/위치 마스크가 있어야 하므로 이 일반 먹 소스와 별도로 맞춘다.

## 오디오 마감과 마스터 검사

```powershell
npm.cmd run master:audio -- --input out/mix.wav --output out/master.wav
npm.cmd run master:audio -- --input out/assembled.mp4 --output out/master.mp4
npm.cmd run inspect:master -- --input out/master.mp4
```

- 2패스 loudnorm으로 −14 LUFS를 목표로 보정한다. 인코딩 여유를 위해 보정 피크는 −1.5 dBTP이며 최종 파일을 다시 읽어 −14±0.5 LUFS / ≤−1 dBTP를 검사한다. 결과는 `.loudness.json`에 남는다. WAV는 PCM24/48kHz, MP4는 기존 비디오 스트림을 보존하고 AAC/320k/48kHz로 만든다.
- 기존 출력 파일을 덮어쓰지 않는다. 무음은 정상화 성공으로 표시하지 않는다. 최종 음량 불합격은 실패 코드와 진단 파일을 남긴다. 이 명령은 음악 생성이나 개별 음원의 자동 믹스가 아니다.
- `inspect:master`는 H.264·1920×1080·60fps·4320~4380프레임·음량·피크와 48초 급감 대비를 검사한다. 대비는 **45~48초와 48~51초를 각각 자른 3초 구간의 integrated loudness 차이 ≥15 LU**다. `.inspection.json`에 결과를 남긴다. 그림·라이선스·최종 미술 검수는 별도다.
- `npm.cmd run test:master`는 73초 합성 신호로 보정·AAC 재측정·급감 대비·덮어쓰기 거절·무음 거절을 실제 실행한다. 검증용 합성음은 BGM/게임 소리가 아니다.
- 현재 실제 BGM·바람·C3-03 소스는 아직 미등록이다. 원본이 들어온 뒤 레벨·리버브·덕킹을 결정한다.

## Unity 준비 창

메뉴 **Geumeum → Art → Teaser Preparation**에서 연다.

1. 샷을 선택하고 프레임 슬라이더로 해당 시점의 자막·소리 지시를 확인한다.
2. 시작/종료 포즈를 남기려면 샷에 지정된 씬의 Perspective 카메라를 참조 필드에 넣는다. 씬을 자동으로 열지 않는다.
3. 편집 모드에서 `현재 포즈 → 시작` 또는 `현재 포즈 → 종료`를 누른다. 월드 position/euler/FOV만 JSON에 저장된다. 카메라나 씬은 바뀌지 않는다.
4. 파일이 다른 곳에서 수정되면 저장을 거절한다. 새로 읽고 저장한다.

이 창은 Editor 폴더에만 있다. 제품 빌드에 들어가지 않으며 서버 판정·배속·HUD·Recorder를 호출하지 않는다. 브라우저 DevPanel의 카메라 제어와 HUD 숨김/복원은 아직 추가하지 않았다.

## 단일 데이터 원천

`Assets/09.Data/Teaser/teaser-shots.json`을 Unity와 Remotion이 **직접 함께 읽는다**. tools 폴더로 복사하거나 별도 시간표를 만들지 않는다. 변경 후 CLI 검증을 실행한다. Studio는 원본 JSON 변경을 다시 번들링한다.

- 버전 1, 1920×1080, 60fps, 총 4380프레임 = 73초.
- 모든 구간은 `[startFrame, startFrame + durationFrames)`다. 끝 프레임은 포함하지 않는다.
- `shots`는 순서대로 빈틈과 겹침 없이 화면을 채운다. 30~36초 교차 컷은 6개의 60프레임, 패링 컷은 30프레임이다.
- `overlays`는 절대 출력 프레임 위에 얹는다. 전환 가이드는 샷을 짧게 만들지 않는다. 추후 TransitionSeries의 overlap 전환으로 바꿀 때는 구간/여유 프레임 계산을 함께 바꾼다.
- 타이틀: 4080~4139f 화면 밖 먹 덮기, 4140~4379f 4초 정지. `TeaserPlan`에는 지시, `TeaserReview`에는 실제 편집 그래픽이 나온다.
- `sourceInFrame`은 원본의 사용 시작 프레임, `handleFrames`는 향후 녹화 앞뒤 여유 목표치다. `warmupFrames`는 편집에 넣지 않는 사전 물리 안정화 길이다. 실제 준비·녹화 실행기는 아직 없다.
- `triggers`의 `offsetFrame`은 **샷 출력 프레임 기준**, `clock`은 `output`만 허용한다. 트리거는 현재 데이터만 보관하며 실행하지 않는다.
- `timeScale`은 전투 샷에서 1만 허용한다. Mirror 서버 시간과 VFX 수명이 `Time.timeScale`을 따르지 않으므로, 전투 슬로우는 별도 검증 없이 허용하지 않는다.
- `cameraFrom/cameraTo`의 null은 미설정이다. 가짜 위치를 준비된 값처럼 채워 두지 않는다. `setupStatus`가 ready여야 배우·환경 초기 상태를 준비한 것으로 센다.
- `requirements`는 pending/blocked/ready다. 갓 탈착은 티저의 낙하 클로즈업과 게임의 OFF+재 파티클을 먼저 통일해야 해서 blocked다.
- `audioCues`의 `assetPath`가 null이면 소리 지시만 있다. 등록 후 `TeaserReview`에서 재생하며 `TeaserPlan`은 계속 무음이다.

## 원본 등록과 재생

`takes`에는 `{id, path, fps, frameCount, origin}`을 남기고 `selectedTakeId`로 선택한다. 로컬 원본을 `public/media/`에 둔 뒤 아래처럼 등록한다. 파일은 이동·복사·녹화하지 않는다. `origin: unity-recorder`는 제작자의 출처 선언이며 파일 내용에서 출처를 증명하는 검사가 아니다. 실제 Unity Recorder 원본만 게임 샷으로 등록한다.

```powershell
npm.cmd run register-media -- --shot A1-01-lantern --take take01 --file media/lantern-take01.mp4 --source-in 60
npm.cmd run register-media -- --audio voice-C3-03 --file media/C3-03.wav --source-in 0
npm.cmd run validate:media
npm.cmd run studio
```

- 영문·숫자·밑줄·대시·점·하위 폴더로 구성한 `media/...` 경로를 쓴다. `..`, 외부 링크 대상, 폴더 자체는 등록할 수 없다.
- 등록 시 ffprobe로 실제 해상도·평균/기준 fps·디코딩 프레임 수와 오디오 스트림/길이를 확인한다. 중복 테이크 ID는 덮어쓰지 않는다. 배우·카메라의 준비 상태는 자동으로 ready로 바꾸지 않는다.
- `TeaserReview`에서 각 클립은 `sourceInFrame`부터 샷 길이만큼 재생한다. 클립 재생 배율은 1이며 JSON의 시뮬레이션 `timeScale`을 다시 적용하지 않는다. 원본 오디오는 음소거하고 `audioCues`로 따로 믹스한다.
- 음원은 `sourceInFrame`(기본 0), `volume`(기본 1, 범위 0~1), `fadeInFrames/fadeOutFrames`(기본 0)를 지원한다. 모두 출력 fps 기준이다. 페이드는 큐 내부 시각을 사용하며, 끝난 큐는 즉시 멈춘다. LUFS 마감은 별도 명령이며 리버브·자동 덕킹은 아직 없다.
- 원본이 미등록인 샷은 누락 안내를 보여준다. 등록된 파일이 없어지거나 손상되면 검증/렌더가 실패한다.
- `validate:media`와 `--ready`는 등록된 파일을 다시 실측한다. 평균/기준 fps 비교는 가변 프레임률의 모든 타임스탬프를 검사하는 것은 아니다. 촬영 원본은 60fps 고정으로 준비한다.

## 준비 완료 검사

```powershell
node scripts/validate.mjs --ready
```

이 명령은 아직 준비가 남으면 실패 코드 1을 돌려준다. 현재 초안은 **정상적으로 실패해야 한다**. 일반 validate 통과는 시간표가 유효하다는 뜻이고, 촬영·납품 준비 완료라는 뜻이 아니다. ready 검사는 데이터의 준비 표시와 원본 실측을 확인하며 실제 아트·오디오 품질 승인을 대신하지 않는다.

## 검증

- `npm.cmd test`: 구간 빈틈/겹침, 잘못된 배속, 프레임 경계, 전환 상대 샷, 카메라 값, 원본 경로·길이, 준비 상태 검사.
- `npm.cmd run test:media`: 합성 색상/음향을 별도 생성하여 등록 성공·실패 시 파일 보존·프레임 0/239/240/269/270의 실제 색·잘라낸 음원 재생을 검사한다. 생산 JSON은 수정하지 않는다. `out/verify-*/result.json`과 스틸/1초 테스트 영상이 증거다. 테스트 소스는 `public/media/verify-*`에 남으며 모두 Git 제외다. 게임 촬영·게임 아트 검증으로 세지 않는다.
- Unity EditMode `Geumeum.Art.Editor.Tests.TeaserPlanDocumentTests`: 포즈 저장/재로드, 다른 샷 보존, 외부 수정 덮어쓰기 거절, 다른 씬 카메라 거절, 잘못된 시간 데이터 거절.
- 원본·캡처·node_modules/build/out는 로컬 전용. package-lock.json은 팀의 동일 버전 설치를 위해 추적한다.

도구 버전은 Remotion/CLI 4.0.524로 고정했다. [공식 구성 방식](https://www.remotion.dev/docs/the-fundamentals), [라이선스 조건](https://www.remotion.dev/docs/license/pricing)을 기준으로 구성했다. 실제 촬영·공개 전 외부 협업 형태와 최종 에셋 권리를 다시 확인한다.

클립 연결은 설치된 4.0.524의 API와 [OffthreadVideo 공식 문서](https://www.remotion.dev/docs/offthreadvideo)를 기준으로 한다. Sequence는 출력 위치, trimBefore는 원본 시작 프레임을 담당한다.

알파 출력은 [Remotion 투명 영상](https://www.remotion.dev/docs/transparent-videos), 마감은 [FFmpeg loudnorm](https://ffmpeg.org/ffmpeg-filters.html#loudnorm)의 2패스 방식을 사용한다. 폰트 출처·배포 고지는 [프로젝트 라이선스 대장](../../docs/licenses/THIRD_PARTY_ASSETS.md#폰트)을 따른다.
