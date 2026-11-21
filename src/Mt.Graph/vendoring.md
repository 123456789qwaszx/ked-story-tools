# Mt.Graph · Mt.Graph.Avalonia — 복사 반입본 (정본은 ked-motion-tool-characterRig)

`ked-motion-tool-characterRig` 저장소 `src/Mt.Graph/`와 `src/Mt.Graph.Avalonia/`의 복사본이다 — 커브의 모델 · 그래프의 산수와
그래프 에디터의 화면(`GraphEditorView`). 줄의 모션을 그래프로 고치는 데 쓴다(docs/PLAN.md §4.8 · D12).
**정본은 언제나 저쪽이고, 흐름은 한 방향이다: characterRig → 이 사본.** 이 사본의 파일은 고치지 않는다 —
필요한 것은 characterRig에서 먼저 정한다.

| 항목 | 값 |
|---|---|
| 원본 | `ked-motion-tool-characterRig` `dev` @ `cc86a00` (그래프를 라이브러리로 뗀 것 `aae00f9` · 키를 고치는 명령의 속 `3a4bda7` · 부드럽게 끌기 `cc86a00`) |
| 반입일 | 2026-11-22 |
| 범위 | `src/Mt.Graph/` 전부(`Graph/` · `Model/` · `Mt.Graph.csproj`) · `src/Mt.Graph.Avalonia/` 전부(`GraphEditorView.cs` · csproj) |
| 테스트 | 가져오지 않는다 — 그래프의 테스트는 저쪽에 있다(`Mt.Authoring.Tests` · `Mt.App.Tests/GraphEditorViewTests`) |
| 이쪽 살림 | 이 `vendoring.md`. 솔루션(`KedStoryTools.sln`)에 두 프로젝트를 더했다 |

네임스페이스는 저쪽의 것 그대로다(`Mt.Authoring.Model` · `Mt.Authoring.Graph` · `Mt.App.Views`). csproj의 `InternalsVisibleTo`(`Mt.App.Tests`)도 그대로 둔다.
이 툴이 쓰는 것: `IGraphEditor`(그래프가 편집기에 바라는 것 — 이 툴의 것은 `St.Authoring/Motions/MotionGraphEditor`) · `MotionKeyEdits`(키를 고치는 명령의 속) ·
`MotionAsset` · `KeyAsset` · `GraphEditorView`.

## 동기화 절차

1. **감사** — `diff -r`로 저쪽과 견준다. 이쪽에만 있는 코드는 로컬 수정이다 — 있어서는 안 된다.
2. **이식** — 두 폴더를 통째로 복사한다(`bin` · `obj` 제외).
3. **보수** — 소비자(`St.Authoring` · `St.App`)의 컴파일 · 의미 적응은 소비자 쪽에서 고친다.
   `Mt.Graph`는 코어(`Ked.Presentation.Core`)에 기댄다 — 두 저장소의 코어 사본이 같은 판이어야 한다.
4. **고정** — 전체 빌드 + 전체 테스트. 이 표의 원본 커밋 · 반입일을 갱신한다.
