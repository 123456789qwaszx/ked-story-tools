# 작업공간 — N4 + R6 (사전 조사와 할 일)

> **끝났다(2026-11-22).** 작업공간 `dev` — `2679d3c`(포크에 런타임 `b56a2ebc`를 들임) · `c85d66b`(대본을 줄의 앵커 표기 · 새 어휘로) · `6d9f08e`(카드 · 초안 · director 초안 걷기) · 참고집 엑셀 다시 계산. 결과는 맨 아래 「6. 한 것」. 아래 0 ~ 5절은 시작하기 전의 조사와 계획이다.

2026-11-22 · 저장소: ked-vn-workspace(`dev` @ `6774bbe`에서 셌다) · 읽기만 했다 — 고친 것은 없다
읽을 것: 런타임 `docs/PLAN_line-anchor.md` 맨 아래 「N4에서 할 일」 · `docs/PLAN_rig-layers.md` §4 · §6 · §7(R6) → 작업공간 `runtime/FORK.md` · `docs/plan.md` 3절.

## 0. 한 일인 까닭

줄의 앵커 N4(새 표기로 다시 내기)와 리그의 계층 R6(걷힌 커맨드를 새 어휘로)은 같은 도구(`tools/stage/script.py`)를 고치고 같은 대본을 다시 낸다. 포크에 들이는 것도 한 번에 한다.

## 1. 센 것

재현 대본 폴더(`runtime/Assets/Workspace/Dialogue`)의 yarn 39개 — 초안의 yarn도 여기에 있다(`ch09_prologue.yarn` 등).

| | 수 | 옮겨 가는 곳 |
|---|---|---|
| `at` 없는 명령 | 20,447 | `<<at start …>>` 또는 앞 앵커의 `<<at 이름 …>>` |
| `<<at …>>` | 2,014 | 그대로 |
| `<<step>>` | 1,125 | 그 줄의 첫 글자 앞 표지 `[beat=이름/]` + `<<at 이름 …>>` |
| `pause` | 3,318 | 스텝의 맨 끝 → `<<gap 이름 길이>>` · 처음 · 중간 → `<<at 이름 pause …>>` |
| `<<gap>>` | 0 | — |
| `scale_by` | 1,445 | `size 슬롯 x배율`(상대값 → 절대값 — 리그의 계층 §5 (나)) |
| `shot_to` | 6 | `shot_zoom` · `shot_pan`을 나란히 |
| `cutin_panel` | 7 | 포크에만 있는 이름 — 그대로(명세 표에는 포크가 덧붙인다) |
| `messenger_open` · `messenger_close` | 쓴다(`ch05_00` · `ch05_03b` · `ch05_04` …) | 포크의 브리지에만 있는 구간 커맨드 — 그대로. 파서 표 밖이라 명세 표에 어떻게 적을지는 K1에서 정한다 |
| `place*` · `size` · `left` · `right` · `up` · `down` · `scale_reset` · `focus_on` · `shot_focus_to` · `show` | 0 | 쓰지 않는다 — 대본에서 고칠 것이 없다 |

걷히는 어휘 가운데 작업공간 대본이 쓰는 것은 `scale_by`와 `shot_to` 둘뿐이다.

## 2. 고칠 곳

| 자리 | 무엇 | 크기 |
|---|---|---|
| `runtime/`(포크) | 런타임 `feat/line-anchor`(`9c28c522` · `c69be4f9` · `7a471df3`)와 `feat/rig-layers`를 들인다. `runtime/FORK.md` 1절에 적는다 | 포크의 파서 표는 91개 이름 — 공개 쪽에 없는 것은 걷히는 옛 이름 14개와 `cutin_panel` |
| 포크의 조립 | `YarnCommandBridge`는 `IStageStepRunner`를 받지 않는다 · `StageStepFlow`를 `LinePresentationFlow`에 넘긴다 · `InlineCommandTrack`이 `ICommandRunScopeProvider` · `PlaybackRuntimeState`를 받는다 | `SampleBootstrap.Presentation.cs`를 따른다 |
| 포크의 즉시 명령 | 쌓지 않고 바로 실행하는 명령이 포크에 더 있으면 이제 그 줄의 앞 앵커보다 먼저 실행된다 | `Playback/YarnBridge/CommandBridge*.cs`를 본다 |
| 녹화기 | `Workspace/Recording/PlaybackRecorder.cs`(149 ~ 152줄)가 `RollbackHistory.Points`에 새 점이 붙는 때를 줄의 시각으로 읽는다 — 이제 앞 연출이 시작되기 전이다 | 시계 0 · `#auto` 줄의 규칙을 다시 본다 |
| `tools/stage/script.py`(1,098줄) | 새 표기로 낸다(위 표) · `scale_by` → `size x배율` · 편 머리의 장면 세우기 → 첫 줄의 앞 앵커 하나(줄의 앵커 Q2) | 옛 표기가 닿는 줄: `step` 7 · `pause` 9 · `scale_by` 3 · `at` 5 |
| `tools/stage/yarn_check.py`(356줄) | 경계에서 `<<step>>`을 뺀다 · 짝(AR7) · 이름(AR6) · `<<gap>>`의 꼴 · 다음이 대사 줄인가(AR10) · `at` 없는 명령 · 걷힌 이름. 대사 줄 머리의 기다림 검사는 걷는다 | 코어 추출기의 경고와 같게 |
| 초안 도구 | `drafts/ch09-prologue/compose.py`(620줄) · `drafts/ch09-prologue-b/compose.py`(467줄) · `drafts/ev-chaton-director/compose.py`(199줄) | 앞의 둘이 `<<step>>` · `scale_by`를 낸다 |
| `catalog/cards.json` | 카드의 커맨드 글이 옛 표기다 — `<<step` 17 · `<<pause` 21 · `<<scale_by` 4 · `<<cutin_panel` 1(그 밖에 `<<at` 24) | 고친 뒤 `python3 tools/catalog/build.py catalog`로 참고집을 다시 내고 페이지를 다시 싣는다 |
| 작업공간 문서 | `CLAUDE.md`의 「대본의 꼴(11-17)」 · `docs/handoff-episodes.md` 4절 | 새 표기로 |

`tools/stage/panels.py` · `band_scene.py` · `tools/catalog/tally.py` · `tools/messenger/make_ui.py` · `catalog/tools-draft/t2.py`는 낱말 검색에는 걸렸으나 옛 표기를 내지 않는다(`step` · `scale_by` · `pause` · `shot_to` 모두 0줄) — 시작할 때 한 번 더 본다.

## 3. 차례와 대조

다시 내기를 **두 번으로 나눈다** — 한 번에 하면 대조할 기준이 없다.

| | 무엇 | 대조 |
|---|---|---|
| ① | 포크에 들이기 · 조립 · 녹화기 | Unity 컴파일 · 옛 표기 그대로 재생(소유자) |
| ② | 앵커 표기만 바꿔 다시 낸다(어휘는 옛 것) | 코어 추출기로 뽑은 **명령 열이 다시 내기 전과 같다** — 차례가 같으면 끝 상태가 같다. `yarn_check` 오류 0 · 줄 ID가 그대로 |
| ③ | 어휘를 바꿔 다시 낸다(`scale_by` → `size x배율` · `shot_to`) | 옛 대본(옛 코어) ↔ 새 대본(새 코어)의 무대 대조 — 리그의 계층 R4의 방법 |
| ④ | 카드 · 초안 도구 · 문서 · 참고집 다시 내기 | `build.py` · 페이지 |
| ⑤ | 녹화 한두 편 | 소유자 컴퓨터 |

②와 ③의 순서는 포크가 두 브랜치를 한 번에 들이는지에 달렸다 — 새 코어에는 `scale_by`가 없으므로, 한 번에 들이면 ②의 대조는 "옛 코어로 뽑은 옛 대본 ↔ 옛 코어로 뽑은 새 표기 대본"으로 한다(코어는 .NET으로 Unity 밖에서 돌린다 — 줄의 앵커 N1의 방법).

## 4. 초안 대본 (소유자, 2026-11-22)

검토에 넘긴 초안은 yarn이 정본이고 다시 내지 않는다(계획서 D1). 지금의 초안은 아직 넘기기 전이다 — 생성 도구가 정본이다.

| 초안 | 할 일 |
|---|---|
| `ch09_prologue` · `ch09_prologue_b` | `compose.py`를 새 표기 · 새 어휘로 고쳐 **다시 낸다** |
| `ev_chaton_d01` · `ev_chaton_d02` | **버린다** — 옮기지 않는다. 대본과 딸린 파일(`.motions.json` · `.progression.json`) · 씬 · 생성 도구(`drafts/ev-chaton-director`)에서 무엇까지 걷을지는 N4를 시작할 때 목록을 보여 확인받는다 |

## 5. 뒤따르는 것(이 일 밖)

- 카드의 `class` 여덟을 종류 넷에 잇는 표(`catalog/`) — K1이 끝난 뒤(계획서 §4.4).
- 초안 도구가 카드에서 온 커맨드에 출처를 남기는 일(계획서 P5) — 자리가 정해진 뒤.

## 6. 한 것 (2026-11-22)

| | 결과 |
|---|---|
| 포크 | 공개 런타임 `5715f852..b56a2ebc` 22커밋을 전부 얹었다(207개 파일). 손으로 합친 넷 — `LinePresentationFlow` · `CommandBridge` · `StageCommandParser.Table` · `CutIn`. 포크의 어휘는 공개 쪽 83개 + `cutin_panel` · `messenger_open` · `messenger_close`. Unity 컴파일 오류 없음(소유자) |
| 메신저 구간 커맨드 | 그대로 둔다 — 바로 실행하지만 기록의 상태만 바꾸고 화면은 다음 줄이 고른다. **`<<at>>`을 붙이지 않는 명령이다**(명세 표 · N5의 "at 없는 명령은 오류"에서 빼야 한다 — 공개 쪽의 `surface_layout` · `surface_reset`도 같다) |
| 녹화기 | 시각표에 없는 줄의 넘김을 글자가 다 나온 뒤에 잡는다. 시각표에 있는 줄은 그대로다 — 원본의 넘김 시각이 앞 앵커 도중에 오면 앞 연출이 끊긴다(전에는 늦게 넘어갔다). 녹화 때 본다 |
| 변환 | `tools/stage/anchors.py`(새로) — `script.py` · `compose.py`는 지금까지처럼 무대 스텝과 줄 머리로 짜고, 쓰기 직전에 앵커 표기로 옮긴다. **`<<gap>>`의 길이는 끝 `pause`만이 아니라 그 구간의 `pause` · `screen_cover` 길이의 합이다** — 간격은 앵커가 열린 때부터 잰다(AR4) |
| 장면 세우기 | 노드의 첫 줄에 무대 스텝이 없으면 그 앞의 명령 전부가 앞 앵커 하나다(줄의 앵커 Q2). 무대 스텝이 있으면 세우기는 이미 앞 앵커에 있고 머리는 `start`로 남는다 |
| 어휘 | `scale_by`(비) → `size 슬롯 x배율`(절대값). 작업공간 대본은 `size`의 레벨을 쓰지 않아 깊이가 기본값(1)이다 — `slot`의 셋째 칸 `mid`는 깊이가 아니라 무대의 층이다. `shot_to` → `shot_zoom` + `shot_pan` |
| 대조 | 코어 추출기를 .NET 8로 돌려 다시 내기 전 ↔ 뒤 — 37편 3,722줄에서 명령 열이 같고(끝 `pause` ↔ `gap` · `scale_by` ↔ `size` · `shot_to`만 다르다) 줄마다 기다림의 합이 같다. 추출기 경고 0 · 줄 ID 그대로 · 몸짓 파일은 바이트까지 같다. 배율은 옛 비의 누적과 새 절대값이 가장 크게 0.2% 다르다(옛 쪽의 반올림). **무대를 접어 견주는 대조(옛 코어 ↔ 새 코어)는 하지 않았다** — Unity 재생은 소유자가 보았다(큰 이상 없음) |
| 카드 | 커맨드 칸 54장을 새 표기로. 크기 카드 P03 ~ P06은 `size x1.5` · `x1.17` · `x1`(제 크기) · `x0.88`. 「대사 줄에는 기다림을 둘 수 없다」처럼 이제 맞지 않는 말을 고쳤다 |
| 초안 | `ch09_prologue` · `ch09_prologue_b`를 `compose.py`로 다시 냈다. `ev_chaton_d01` · `d02`와 `drafts/ev-chaton-director/`는 걷었다(소유자) |
| 남은 옛 표기 | `ch05_01` 노드 끝의 `<<step>>` 하나와 꼬리 명령 넷 — 붙을 줄이 없다(암묵 스텝). N5가 `<<step>>`을 걷을 때 이 꼬리를 본다 |

**도구를 돌린 자리** — 소유자 컴퓨터 쪽 셸에는 .NET이 없고 푸시도 막혀 있다. 코어 추출기는 클라우드 쪽에서 `apt`의 `dotnet-sdk-8.0`으로 돌렸다(NuGet이 막혀 `csc`를 직접 불렀다). 커밋은 컴퓨터에서 하고 푸시는 소유자가 한다.

**뒤따르는 일**(이 문서 5절 그대로) — 카드의 `class` → 종류 표(K1 뒤) · 카드 출처 기록(P5).
