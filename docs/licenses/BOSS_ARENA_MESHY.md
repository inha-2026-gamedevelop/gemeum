# 보스 아레나 생성 에셋

제작일: 2026-09-22. 용도: `Boss_Arena.unity`의 사당·석등·돌무더기. 텍스트 설명에서 Meshy API로 새로 생성했으며 타사 모델이나 이미지를 입력하지 않았다.

| 에셋 | Preview 작업 ID | Refine 작업 ID |
|---|---|---|
| 사당 | `01a0c7bb-e13b-7346-8dd2-d1cdfefd9f20` | `01a0c7bd-167d-7135-8f46-63da8e52e1fe` |
| 돌무더기 | `01a0c7bb-ef12-7667-b577-8f804e119ac9` | `01a0c7bd-22d3-744d-a621-9973ab9afcf9` |
| 석등 | `01a0c7bb-f914-72a5-aa58-8c7efd2f175c` | `01a0c7bd-27fa-72b0-ac43-e8ff026a7ff7` |

- 도구: Meshy CLI 0.3.0, `meshy-t2`, smart topology, PBR 2K refine. [공식 Text-to-3D API](https://docs.meshy.ai/en/api/text-to-3d).
- 위치: `Assets/98.FreeAssets/BossArena/Meshy/{shrine,cairn,lantern}/`. FBX와 base color·normal·roughness·metallic 텍스처 포함.
- Unity 가공: URP 재질 변환, roughness → smoothness 패킹, 크기·회전 보정. 자동 Collider 생성 없음. 생성 지형과 붉은 달 셰이더는 프로젝트 자체 제작이다.
- 권리 증빙: 생성 작업 ID를 기록했다. 서비스 계정의 플랜·약관에 따른 이용 권리는 기존 [외부 에셋 라이선스 기록](THIRD_PARTY_ASSETS.md)의 Meshy 항목과 함께 확인한다. API 잔액만으로 소유권이나 플랜을 확정하지 않는다.
- 이번 작업 중 API 잔액은 2600 → 2555로 45 감소했다. 실제 사용량 대사는 계정 사용 내역을 기준으로 한다.

씬 재구성: 보스 씬을 편집 모드로 열고 `그믐 > 아트 > 보스 아레나 > 사당과 붉은 달 구성` 실행. 기존 공유 판정 구조는 유지하며 `ArenaArt_20260922` 장식 루트를 재구성한다. 수동 수정한 장식은 재구성 전에 별도 보관한다.
