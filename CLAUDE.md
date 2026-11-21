# ked-story-tools

연출가가 yarn을 보지 않고 연출을 고치는 툴(.NET 10 + Avalonia 12) — 줄(LineId) · 스텝(앵커) · 종류로 읽고 고친다. **지금은 K4-5까지다 — 줄 고치기와 명령줄(`st`), 화면에서 읽고 고치고(값 · 빼기 · 지연 · 간격) 더하고(어휘 · 카드) 타임라인에서 끌어 고치기, 여럿 고르기 · 복사 · 단축키 · 맞대기.**

- **먼저 읽을 것**: `docs/PLAN.md`(무엇을 왜 · 정한 것 · 부품 · 단계) → `docs/handoff/`(세션마다 넘겨받을 일) → `docs/CLI.md`(`st`의 쓰는 법 — 다른 세션의 AI가 본다) · `docs/USER-GUIDE.md`(화면의 쓰는 법). 단계가 끝날 때마다 계획서에 결정과 상태를 적는다.
- 관계: 정본은 yarn이고 재생은 `ked-presentation-runtime`이 한다. 이 툴은 무대를 그리지 않는다. 명세 표와 줄 읽기는 런타임 코어의 것이고 여기서는 사본을 쓴다 — **코어 사본은 고치지 않는다**(필요한 것은 런타임에서 먼저 정한다. 동기화는 `src/Ked.Presentation.Core/vendoring.md`).
- 대사 글 · 분기는 `ked-story-director`의 일, 모션의 내용은 `ked-motion-tool-characterRig`의 일이다 — 여기서 하지 않는다.
- `main`은 설계 문서까지다. 일은 `dev`에서 한다.

## 구조

`Ked.Presentation.Core`(런타임 코어의 사본 — 순수 C#, netstandard2.1 · C# 9) → `St.Authoring`(도메인 — `Editing` 줄 고치기 · `Reading` 줄 찾기와 펴 보일 꼴 · `Catalog` 연출 카드 · `Scripts` 대본 파일 · `Settings`) → `St.App`(Avalonia 화면) + `St.Cli`(`st`).
형제 툴 `ked-motion-tool-characterRig`와 같은 꼴이다 — 화면의 셸은 `src/St.App/MainWindow.*.cs`에 맡은 일별 partial로 둔다(`Files` · `Library` · `Line` · `Edit` · `Add` · `Timeline` · `Keys`). 칸의 모양은 `Views/ArgField` · `Views/FormGrid` 하나다 — 고치는 칸과 채우는 칸이 같은 것을 쓴다.
테스트: `tests/St.Authoring.Tests`(xunit — CLI도 여기서) · `tests/St.App.Tests`(xunit — 화면은 `HeadlessUi.Run`) · `tests/Ked.Presentation.Core.Tests`(NUnit — 코어의 순수 테스트).

## 명령

```bash
dotnet build KedStoryTools.sln -c Release
```

```bash
dotnet test KedStoryTools.sln -c Release --no-build
```

```bash
./st read tests/St.Authoring.Tests/Fixtures --line ln_k7inl2cm
```

화면을 그림으로 뽑는다(헤드리스가 Skia로 실제 렌더 — 고친 화면은 눈으로 확인한다). `ST_RENDER_DIR`이 있을 때만 켜지고, 테스트가 `RenderShots.Save`(`tests/St.App.Tests/HeadlessUi.cs`)로 낸다:

```bash
ST_RENDER_DIR=<폴더> dotnet test tests/St.App.Tests -c Release
```

## 관습

- 코드 주석 · 문서 · 커밋 메시지는 **한국어**(문서 · 커밋은 한다체), 대화는 합니다체. 커밋은 "무엇을 왜" 한 줄 + 본문.
- 소유자가 정하고 Claude가 분석 · 구현한다 — 제안과 결정을 구분해 적는다(`docs/PLAN.md` §2).
- **한 번에 하나.** 단계마다 전체 테스트가 통과한 채로 커밋한다.
- `TreatWarningsAsErrors` — 안 쓰는 필드 · using · 인자는 빌드가 막는다.
- **yarn을 쓰는 길은 `LineEditor` 하나다** — 화면도 CLI도 조작(`LineEdit`)을 지난다. 조작 하나 = 되돌리기 한 칸, 같은 값은 기록하지 않는다(`StagingEditor.Revision`). 화면은 `EditSession`을 지나 조작마다 바로 파일에 쓴다 — 바깥에서 바뀐 파일은 덮어쓰지 않는다.
- **고친 줄의 커맨드 묶음만 다시 쓴다** — 파일의 나머지는 바이트 하나 바뀌지 않는다. 깨진 yarn은 쓰지 않는다. 시험이 지킨다.
- **대사 원문이 든 대본은 이 저장소에 넣지 않는다** — 시험에는 지어낸 대본과 런타임의 데모 대본(`tests/St.Authoring.Tests/Fixtures`)만 쓴다. 작업공간의 카드(`catalog/`)도 넣지 않는다 — 시험의 카탈로그는 꼴만 본뜬 것이다.
- `st`의 출력 · 조작의 규칙을 바꾸면 `docs/CLI.md`를 같이 고친다. 화면의 기능에는 테스트 손잡이와 헤드리스 테스트를 붙인다. 좌표 셈 · 잡기는 순수 계산으로 떼어 화면 없이 시험한다(`Views/TimelineLayout`). 단축키는 `MainWindow.Keys.cs`의 표에 더하고(F1의 창이 그 표를 보인다) `docs/USER-GUIDE.md`에 적는다.
- 안 쓰는 기능은 흔적 없이 지운다 — 코드 · 주석 · 문서 · 테스트까지.
- PR은 요청받았을 때만 만든다.
