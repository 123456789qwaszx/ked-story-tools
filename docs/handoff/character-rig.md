# 모션 툴 세션에 넘기는 일 — R7

2026-11-22 · 저장소: ked-motion-tool-characterRig(`dev` @ `538a99e`에서 셌다) · 읽기만 했다
읽을 것: 그 저장소의 `CLAUDE.md` · `docs/MAINTENANCE.md` · `src/Ked.Presentation.Core/vendoring.md` → 런타임 `docs/PLAN_rig-layers.md`(§2 · §3 · §6 · §7 R7).

## 0. 무엇이 바뀌었나

런타임이 캐릭터 리그를 세 덩어리(포커스 · 슬롯 · 초상)로 다시 짰다 — 25노드 → 20노드(런타임 `feat/rig-layers` `45e25d09`). 구조는 유니티가 정하고 이 툴은 따른다.

| | 노드 |
|---|---|
| 걷혔다(7) | `CharSlot_Track_Idle` · `CharSlot_Track_X` · `CharSlot_Track_Y` · `CharSlot_Scale` · `CharacterPortrait_Track_Move_X` · `CharacterPortrait_Track_Move_Y` · `CharacterPortrait_ActingScale_Y` |
| 새로 생겼다(2) | `CharFocus_Rotation` · `CharFocus_Scale` |
| 이름 · 자리가 바뀌었다 | `CharSlot_Track_Focus` → `CharFocus_Track` · `CharSlot_DepthY` → `CharacterPortrait_DepthOffset` · `CharSlot_DepthScale` → `CharacterPortrait_DepthScale` · `CharacterPortrait_ActingScale_X` → `CharacterPortrait_Mirror` |

그 밖에: 이모지가 서는 자리가 `CharSlot_Scale` → `CharacterPortrait_DepthScale`이다. 좌우 · 상하를 다른 박자로 움직이는 일은 노드 둘이 아니라 **클립 하나(채널 안의 x · y 키)**로 짠다(리그의 계층 L12 · L13). `nod`는 `CharacterPortrait_Track_Move`로 옮겨졌다(키 값은 그대로).

## 1. 앞선 조건

런타임의 `feat/line-anchor` · `feat/rig-layers`가 `dev`에 합쳐지고, 소유자가 Unity에서 `ExportedTuning/rig-schemas.json`을 **다시 낸 뒤**에 시작한다(리그의 계층 §6 — 덤프는 Unity에서만 낼 수 있다).

## 2. 할 일

| | 무엇 |
|---|---|
| 코어 사본 | 런타임 → 사본, 한 방향(`vendoring.md`의 절차). 지금 사본은 전체 동기화가 아니라 부분 반입이 여러 번 쌓인 상태다 — 이번에 전체를 맞출지 닿는 파일만 들일지는 그 세션에서 정한다. 줄의 앵커가 고친 `Reduce/LineBeatSchedule.cs` · `YarnCommandTextExtractor.cs`도 사본에 있다 |
| 리그 스키마 | `src/Mt.Authoring/Defaults/ExportedTuning/rig-schemas.json` · `tests/Mt.Authoring.Tests/Fixtures/rig-schemas.json`을 새 덤프로 |
| 걷힌 노드를 가리키는 곳 | 아래 표 — 코드 · 시험 · 예시 · 문서 |
| 툴이 낸 견본 | 런타임 `Tests/EditMode/UnityParity/ToolExport~/motions.json`에 `CharSlot_Track_X` · `CharSlot_Scale`의 키가 있다 — 남는 노드의 견본으로 다시 내어 런타임 세션에 넘긴다 |
| 설명서 · 그림 | 노드 이름이 화면과 그림에 나오면 다시 뽑는다(`MT_RENDER_DIR` · `tools/guide-figures.py`) |

걷히거나 이름이 바뀐 노드를 가리키는 파일 수(2026-11-22, `bin` · `obj` 제외):

| 노드 | 코어 사본 | `src/Mt.*` | 시험 | 예시 | 문서 |
|---|---|---|---|---|---|
| `CharSlot_Track_Idle` | 1 | 1 | 6 | 0 | 0 |
| `CharSlot_Track_X` | 2 | 1 | 16 | 0 | 1 |
| `CharSlot_Track_Y` | 2 | 1 | 7 | 0 | 1 |
| `CharSlot_Scale` | 2 | 6 | 13 | 1 | 6 |
| `CharacterPortrait_Track_Move_X` | 1 | 2 | 5 | 0 | 2 |
| `CharacterPortrait_Track_Move_Y` | 2 | 2 | 28 | 0 | 2 |
| `CharacterPortrait_ActingScale_Y` | 1 | 2 | 5 | 0 | 2 |
| `CharacterPortrait_ActingScale_X` | 1 | 2 | 6 | 0 | 2 |
| `CharSlot_Track_Focus` | 3 | 3 | 11 | 0 | 3 |
| `CharSlot_DepthY` | 1 | 3 | 8 | 0 | 3 |
| `CharSlot_DepthScale` | 1 | 2 | 6 | 0 | 3 |

시험이 가장 많다 — `CharacterPortrait_Track_Move_Y`(28개 파일)와 `CharSlot_Track_X`(16)에 키를 잡는 시험은 남는 노드(`CharacterPortrait_Track_Move` · `CharSlot_Track`)의 x · y 채널로 옮긴다.

## 3. 소유자에게 물을 것

- 저작본(이 툴로 이미 만든 모션 · 이모지 파일)에 걷힌 노드의 키가 있으면 — 옮기는 변환을 둘지, 다시 잡을지.
- 포커스 계층의 노드에 모션 클립의 키를 허용할지는 런타임이 "금지 그대로"로 두었다(리그의 계층 §9 ③) — 툴의 키잉 금지 목록도 같게 한다.

## 4. 하지 않는 것

명세 표 · 줄 읽기 · 연출 고치기는 이 툴의 일이 아니다(ked-story-tools). 이 툴은 모션과 이모지의 내용을 만든다.
