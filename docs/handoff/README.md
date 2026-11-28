# 인수인계 — 세션마다 넘겨받을 일

2026-11-22 · 설계는 [`../PLAN.md`](../PLAN.md). 이 폴더는 그 §8의 일을 저장소(세션)마다 가른 것이다.
각 문서는 이 설계를 세운 대화 없이도 시작할 수 있게 썼다 — 그 저장소의 `CLAUDE.md`와 함께 읽는다.

| 문서 | 저장소 | 일 | 시작할 수 있는 때 |
|---|---|---|---|
| [runtime.md](runtime.md) | ked-presentation-runtime | K1 명세 표 · K2 줄 읽기 · (뒤에) K5 도구 연결 · N5 옛 표기 걷기 | **K1 · K2 · N5 끝(2026-11-22)** — `dev`에 있다(N5는 `417bf41e`). K5는 뒤에 |
| [workspace-n4.md](workspace-n4.md) | ked-vn-workspace | N4 + R6 — 포크 · 파이썬 도구 · 카드 · 다시 내기 | **끝났다(2026-11-22)** |
| [character-rig.md](character-rig.md) | ked-motion-tool-characterRig | R7 — 코어 사본 · 리그 스키마 · 걷힌 노드 | **지금 시작할 수 있다** — 런타임 `dev` `b56a2ebc`(튜닝 덤프도 다시 냈다 — `6968f30c`) |
| [story-tools.md](story-tools.md) | ked-story-tools(여기) | K3 줄 고치기 · CLI · K4 화면 | **K3 · K4-1 ~ K4-4 끝(2026-11-22)** — `dev`. 다음은 K4-5(계획서 §2.3 — 써 본 뒤의 불편으로 차례를 정한다) |
| [toolchain-blueprint.md](toolchain-blueprint.md) | 여러 저장소 | 툴 전체의 역할 · 일의 흐름(2026-11-28), 몸짓 모션을 "이름 붙은 커맨드 묶음"으로 — 합칠 때 풀어 게임에는 커맨드만 | **정할 것(§6)부터** — 구현은 아직 없다 |

## 차례

```
런타임: 리그의 계층 R1~R5 ──┬─→ K1 → K2 ─────────────┬─→ (K5)
                           ├─→ 작업공간: N4 + R6 ───┤
                           ├─→ 모션 툴: R7 ─────────┼─→ 런타임: N5(옛 표기 걷기)
                           └─→ 추출기 · compositor ─┘
                                        K1 · K2 · N4 ─→ 이 저장소: K3 → K4
```

- **N5는 맨 뒤다.** 옛 표기(`<<step>>` · `at` 없는 명령 · 걷힌 커맨드 이름)를 먼저 걷으면 아직 옮기지 않은 소비처의 대본이 깨진다. — **끝(2026-11-22, 런타임 `417bf41e`)**. 이 저장소는 코어 사본을 다시 들였다(`story-tools.md` §16).
- **같은 저장소를 두 세션이 함께 고치지 않는다.** 런타임의 K1 · K2는 리그의 계층을 하는 세션이 끝난 뒤에 시작한다.
- 추출기(ked-staging-extractor) · ked-story-compositor(오라클 `Vn.StageOracle`과 코어 사본)는 문서를 따로 쓰지 않았다 — 할 일은 런타임 `docs/stage-changes-handoff.md`에 N5 때 적힌다(줄의 앵커 §9 N5).
