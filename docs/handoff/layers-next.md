# 대사 층과 연출 층 — 이어서 할 일 (2026-11-28 세션의 인수인계)

| | |
|---|---|
| 상태 | **K8의 I1 ~ I6을 지었고, 소유자가 레피아 1편으로 한 바퀴를 확인했다.** 레피아 이벤트 9편을 이야기 단위로 층에 올렸다(I7의 첫 이야기). 남은 것은 아래 §4 |
| 먼저 읽을 것 | 이 문서 → `../PLAN.md` §2.5(결정 C1 ~ C8 · I1 ~ I6에서 지은 것) → `../CLI.md`의 "합치기" → `../USER-GUIDE.md`의 "대사 층과 연출 층" → 작업공간 `stories/README.md`의 "층으로 일하는 이야기" |
| 설계의 뿌리 | `layers-compose.md`(방향 L1 ~ L6 · 꼴의 본보기 · 옛 레인에서 배운 것) — 그 §5 · §8 ~ §11은 그때의 제안이고, 정한 것은 PLAN §2.5다 |

## 1. 한 장으로

- **대사는 ked-story-director, 연출은 이 툴이 쓴다. 둘은 줄 ID로만 만나고, 런타임에는 합친 yarn 하나만 들어간다.**
- 이야기 폴더(작업공간 `stories/<이야기>/`)에 원고 · 대사 층(`exported/yarn`) · 연출 층(`staging/`)이 모이고, 합친 것은 게임 폴더(`runtime/Assets/Dialogue/Story/<이야기>/`)에 쓴다.
- 합치는 때: 툴이 연출 층 폴더를 열 때 · 고칠 때마다 · 창으로 돌아오거나 F5일 때 대사 층이 바뀌었으면. 명령줄은 `st compose <연출 층 폴더>`.
- 작가가 글을 고친 줄은 게임에는 새 글이 들어가고 연출 층에는 옛 글이 기준으로 남는다 → 화면의 "다시 볼 것" → 연출가가 「확인」하거나 박자를 새 글에 놓으면 닫힌다.

## 2. 지은 것 (이 저장소 `dev`)

| 단계 | 커밋 | 무엇 | 코드 |
|---|---|---|---|
| 규칙 | `6bfd8ec` | C1 ~ C8 · 차례 | `docs/PLAN.md` §2.5 |
| I1 | `dcb51ca` | 합치기(순수 함수) | `St.Authoring/Composing/LayerScript` · `LineText` · `LayerComposer` · `ComposeResult` |
| I2 | `1c3dc21` | 폴더에서 돌리기 · `st compose` · 짝의 설정 · 지문 | `Composing/ComposeFolder` · `St.Cli/StCli.Compose.cs` |
| I3 | `d13c9ba` | 연출 층 폴더를 열면 고칠 때마다 합친다 | `Editing/EditSession`(`Layers` · `Composed` · `ComposeProblem`) |
| I4 | `b155218` | 딱지 · "할 일" 필터 · 확인 · 덮어쓰기 | `St.App/MainWindow.Layers.cs` · 조작 `AcceptText` |
| I5 | `18fa9cb` | 박자 놓기(글자를 눌러) | `Composing/BeatPlacement` · `MainWindow.Beats.cs` · 조작 `PlaceBeat` |
| I6 | `bc81845` | 보관소 보기 · 이 줄로 옮기기 | `Composing/Shelf` · 조작 `AdoptStaging` · `EditSession.Adopt` |

시험은 `tests/St.Authoring.Tests`(`ComposeTests` · `ComposeFolderTests` · `LayeredSessionTests`)와 `tests/St.App.Tests/LayersTests`. 마지막으로 통과한 수: 도메인 213 · 화면 78 · 코어 602.

그 뒤의 고침(같은 날): "할 일" 필터를 켠 채 확인 · 박자 놓기를 해도 **고른 줄은 목록에 남는다**(`FilterTodo`에 고른 줄을 넘긴다 — 빠지면 가운데가 비어 연출이 사라진 것처럼 보인다). 다른 줄을 고르면 그때 빠진다. 시험 1 — 화면 79.

## 3. 작업공간의 지금 (`ked-vn-workspace` `dev`)

| 자리 | 무엇 |
|---|---|
| `stories/ev_leafia/` | 층으로 일하는 첫 이야기 — 디렉터 원고(형식 5 · 3) · `exported/`(대사 층) · `runtime.target.json` · `staging/`(연출 층 26장면 · `compose.json`) |
| `runtime/Assets/Dialogue/Story/ev_leafia/` | 그 게임 폴더 — 손으로 고치지 않는다 |
| `stories/ch05` · `ev_chaton` | 디렉터 원고만 있다(형식 5 · 3으로 올렸다). `exported/`가 없다 — 옛 꼴(게임 폴더의 yarn이 정본) |
| `runtime/Assets/Dialogue/Story/` | 재현 편만 남았다 — 공개 런타임의 견본(`ChapterProgression` · `motions.json` · `emojis.json`)과 시험 씬 다섯을 걷었다 |
| `_to_delete/eunseol/` | 은설 원고(git에 올린 적 없음) — 무시 폴더로 옮겨 두었다. 소유자가 지운다 |
| `git stash` | "leafia01 시험 흔적" — 소유자가 1편으로 시험하며 고친 것. 필요 없으면 버린다 |

- 본보기 `leafia01`(1편만 뗀 것)은 확인이 끝나 `ev_leafia`로 되돌려 합쳤다 — 없다.
- 레피아의 대사 층은 게임 yarn과 파일 26 · 줄 ID 880이 모두 맞는다. **디렉터 원고에는 주인공의 줄에 화자 「주인공」이 있고 연출 층에는 없다**(1편 109줄 가운데 21줄 — 전체 880줄 가운데 175줄). 합치면 게임에 디렉터의 것이 들어가고 그 줄들이 "다시 볼 것"(화자만 — 박자는 제자리)으로 선다.
- `Story.unity`의 Story Folder는 `Assets/Dialogue/Story`(전체)다. 한 이야기만 보려면 인스펙터에서 그 폴더로 바꾼다(커밋하지 않는다).

## 4. 남은 일 — 차례대로

| # | 일 | 정해진 것 · 정할 것 |
|---|---|---|
| N1 **끝** | 레피아 9편의 첫 합치기(11-28, 소유자의 컴퓨터) — **장면 26 · 줄 880 · 다시 볼 것 175(모두 화자 「주인공」만 — 박자를 옮긴 줄 0) · 게임 yarn 23개와 연출 층 23개를 썼다**(연출 층은 파일 머리의 출처 줄이 빠진 것). 한 번 더 돌리면 쓴 것 0 · 그대로 52 | **속도는 걱정할 것이 없다** — `st compose` 한 번이 프로세스를 띄우는 것까지 0.2 ~ 0.4초다(26장면). 고칠 때마다 이야기 전체를 합쳐도 된다 — 바뀐 장면만 합치는 일은 하지 않는다 |
| N2 | **화자 「주인공」** — 게임에 이름을 보일지 | **소유자가 정한다.** 보이지 않게 하려면 디렉터에서 화자를 걷는다(다시 볼 것도 함께 사라진다). 보이게 두면 툴에서 그 줄들을 확인한다 — 여러 줄을 한 번에 확인하는 길이 없다(아래 N6) |
| N3 | **모션 파일(C4)** — 소유자가 정했다(11-28): **스토리 씬이 스토리 폴더 아래의 `*.motions.json`을 모두 읽고, 합치기는 연출 층 곁의 모션 파일을 게임 폴더로 복사한다** | 할 일: ① 예제 부트스트랩(`Assets/Samples/Scripts/Bootstrap/SampleBootstrap.Presentation.cs`의 `LoadMotions` — 지금은 `motionsJson` TextAsset 하나)이 진행 JSON처럼 폴더의 모션 파일을 모으게 한다. 공개 런타임에서 먼저 정하고 작업공간의 포크에 들인다(L6 — "런타임은 바꾸지 않는다"를 예제 코드에 한해 푼 것이다. 코어는 그대로) ② 같은 이름의 모션이 두 파일에 있을 때의 규칙 ③ `ComposeFolder`가 `staging/*.motions.json`을 게임 폴더로 복사하고 지문에 넣는다 ④ 프리셋과 편마다의 클립이 한데 든 `Story/story.motions.json`(지금은 `tools/story/build.py`가 낸다)을 어떻게 가를지 ⑤ 이 툴의 `MotionFile.SharedPath`(대본 곁의 `motions.json`)가 연출 층에서 무엇을 읽을지 |
| N4 | **`ch05` · `ev_chaton`을 층에 올린다** | 디렉터에서 원고를 열어 저장하면 `exported/`가 생기는지 먼저 본다. 생기면 레피아와 같은 방법 — `staging/`에 게임 yarn을 복사 · `compose.json` · `runtime.target.json` · `st compose --dry-run`으로 줄 ID와 다른 글의 수를 본 뒤 옮긴다 |
| N5 | 다시 볼 줄의 머리 · 타임라인이 연출 층의 옛 글인 것 | 소유자가 "지금대로"로 정했다(11-28) — 옛 글의 연출과 새 글을 함께 보고 판단한다. 써 보고 헷갈리면 다시 본다 |
| N6 | 화면에만 있는 조작의 명령줄 — 확인 · 박자 놓기 · 보관소에서 옮기기. 여러 줄을 한 번에 확인하기. 타임라인에서 박자 끌기. 장면을 넘나드는 "할 일"의 수. 보관소의 줄 버리기 | 미뤘다 — 필요가 생기면 |

## 5. 일하는 법 — 이 일에서 걸렸던 것

- **클라우드 세션에는 .NET SDK가 없고 받을 길도 막혀 있다**(dot.net · nuget.org). 빌드 · 시험은 소유자의 컴퓨터에서 돈다 — `artifacts/k8-check.cmd`(git이 무시하는 폴더 — 세션마다 다시 쓴다)를 써 두고 소유자가 돌린 뒤 로그를 붙인다. 통과하기 전에는 커밋하지 않는다. 화면은 `ST_RENDER_DIR`로 그림을 뽑아 읽는다.
- **`git push`는 클라우드에서 막혀 있다** — 소유자가 한다.
- 작업공간의 커밋 규칙: 작성자 `Claude <noreply@anthropic.com>` · 꼬리 줄 없음 · 한국어 제목 + `-` 목록 + `녹화 점수:` 줄 · 커밋마다 푸시(소유자). 이 저장소는 꼬리 줄(Co-Authored-By · Claude-Session)을 단다.
- **작업공간에서 `git -c core.autocrlf=true`로 파일을 꺼내면(stash · checkout) 줄 끝이 CRLF로 바뀐다** — 대본과 원고는 LF다. 커밋에만 쓰고, 꺼내는 일에는 쓰지 않는다.
- 디렉터는 `exported/`를 **저장 전에도 고칠 때마다** 다시 쓴다 — 쓰던 중의 대사가 툴로 들어온다. 줄 ID로 맞추고 보관소가 되살리므로 다음 합치기에서 제자리를 찾는다.
- 디렉터는 0.4.0부터 형식 5(판 파일 3)만 연다 — 옮기는 길이 없다. 조건 공급 노드(`"kind": "set"`)가 없는 원고는 번호만 올리면 된다. 작업공간의 `tools/story/build.py`는 형식 5 · 3으로 낸다.
- 같은 노드 이름이 Yarn 프로젝트(`Assets/Dialogue/**/*.yarn`)에 둘이면 컴파일이 깨진다 — 연출 층과 대사 층은 반드시 그 밖(`stories/`)에 둔다.
- 대사 원문이 든 대본은 이 저장소에 넣지 않는다 — 시험은 지어낸 대본으로, 실제 원고의 확인은 작업공간에서 `st compose --dry-run`으로.

## 6. 다음 세션의 첫 지시문 (본보기)

> ked-story-tools의 `docs/handoff/layers-next.md`를 먼저 읽어 주세요. K8(대사 층과 연출 층)을 이어서 합니다.
> ① `git status`에 커밋하지 않은 고침이 남아 있으면 그것부터 정리하고, ② §4의 N1(레피아 9편의 첫 합치기 — 수와 시간)을 확인한 뒤,
> ③ N3(모션 파일)의 할 일 다섯을 조사해서 정할 것을 저에게 하나씩 물어 주세요. 정한 것은 `docs/PLAN.md` §2.5에 적습니다.
> 구현은 한 단계씩, 제 컴퓨터에서 빌드 · 시험이 통과한 뒤 커밋합니다.
