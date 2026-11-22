# Ked.Presentation.Core — 복사 반입본 (정본은 ked-presentation-runtime)

`ked-presentation-runtime` 저장소 `Assets/Ked.Presentation/Core/`의 복사본이다.
**정본은 언제나 저쪽이고, 흐름은 한 방향이다: 런타임 → 이 사본.** 이 사본의 파일은 고치지 않는다 —
필요한 것은 런타임에서 먼저 정한다. 패키지화는 하지 않는다(형제 툴 `ked-motion-tool-characterRig`와 같다).

| 항목 | 값 |
|---|---|
| 원본 | `ked-presentation-runtime` `dev` @ `417bf41e` (N5 옛 표기 걷기 `ebbde5ad` · 마무리 `417bf41e` — 그 앞의 K1 명세 표 `e5f81084` · K2 줄 읽기 `33c89805` · 종류 배정 확정 `c39986c2`를 담는다) |
| 반입일 | 2026-11-22 |
| 범위 | `Core/` 전부 — `Ease/` · `Emoji/` · `Motion/` · `Primitives/` · `Read/` · `Reduce/` · `Spec/` · `State/` · `Tokens/` · `Transforms/` · `Tuning/` 소스와 `Documentation~/` |
| 제외 | `Tests/` · `*.meta` · `*.asmdef` |
| 테스트 | 순수 테스트(코어의 타입만 쓰는 것) 31개를 `tests/Ked.Presentation.Core.Tests/EditMode/`로. 저쪽에 남는 것 — 파서 표의 원문을 읽는 `StageCommandSpecTableParserTests`, 코어 밖의 타입을 쓰는 `CastRegistrySlotOnlyTests` · `CharacterRigRegistryOutsideFoldTests`(`using`에는 드러나지 않는다 — 이름공간 없이 다른 어셈블리의 타입을 쓴다) |
| 견본 | `tests/St.Authoring.Tests/Fixtures/presentation_demo_Demo_Presentation.yarn`은 같은 커밋의 `Assets/Dialogue/ChapterProgression/` 그대로다(줄 끝 문자는 LF — 작업 트리가 아니라 `git show <커밋>:<경로>`로 꺼낸다. Windows 체크아웃은 CRLF다). 새 표기이고 대사 줄 116개 모두 LineId가 있다 |
| 이쪽 살림 | `Ked.Presentation.Core.csproj` · `vendoring.md` (동기화 때 유지) |

선반영(이쪽에서 먼저 고친 것)은 없다. 이 툴이 쓰는 것은 `Spec/`(명세 표) · `Read/`(줄 읽기 · 시간표) · `Reduce/`(`LineBeatSchedule` · `YarnCommandTextExtractor`) · `Tokens/`(시간 토큰) · `Tuning/CommandSpecsFileDto.cs`다.

## 동기화 절차

1. **감사** — `diff -r`로 세 갈래를 가른다: ⓐ 저쪽에만 있는 파일(가져온다) ⓑ 내용이 다른
   파일(저쪽 본으로 교체) ⓒ 이쪽에만 있는 코드 멤버 — 그건 로컬 수정이다. 있어서는 안 된다.
2. **이식** — 저쪽 트리에서 `Tests/` · `*.meta` · `*.asmdef` 제외 전부 복사. 테스트는
   `Tests/EditMode/*.cs` 중 순수한 것만 `tests/Ked.Presentation.Core.Tests/EditMode/`로.
   기준: `UnityEngine` · `UnityEditor` · `DG.` · `Ked.Presentation.Runtime/Editor/Stage/Playback` ·
   `Ked.CommandExecutor` · `Newtonsoft` · `LogAssert`를 참조하거나 저쪽 소스 파일을 읽으면 제외.
   `using`만으로는 가려지지 않는다 — 들인 뒤 빌드해서 코어에 없는 타입을 쓰는 것을 마저 뺀다.
3. **보수** — 소비자(`St.Authoring` · `St.Cli` · `St.App`)의 컴파일 · 의미 적응은 **소비자 쪽에서** 고친다.
   줄 고치기는 코어가 적는 문제 글 하나(커맨드가 남지 않은 표지 — `LineEditor.IsUnusedMarker`)에 기댄다.
   읽기만인 줄은 코어의 칸(`LineRead.Unanchored` — 대사 줄 앞의 at 없는 명령)으로 가른다.
   그 글이 바뀌면 `표지의_마지막_커맨드를_빼면_알리고_받아들인다` 시험이 실패해 알린다.
4. **고정** — 전체 빌드 + 전체 테스트. 이 표의 원본 커밋 · 반입일을 갱신한다.

## 검증

```
diff -r --exclude=Tests --exclude='*.meta' --exclude='*.asmdef' \
        --exclude=Ked.Presentation.Core.csproj --exclude=vendoring.md --exclude=bin --exclude=obj \
        <ked-presentation-runtime>/Assets/Ked.Presentation/Core src/Ked.Presentation.Core
```

차이 0줄이 동기화 완료다(줄 끝 문자는 `.gitattributes`가 LF로 맞춘다 — Windows 체크아웃끼리 견줄 때는 `--strip-trailing-cr`).

## 유니티 호환 안전핀

csproj가 원본 asmdef의 `noEngineReferences: true`와 유니티 C# 수준(LangVersion 9,
netstandard2.1)을 컴파일러 제약으로 유지한다.
