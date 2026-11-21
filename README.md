# ked-story-tools

연출가가 **yarn을 보지 않고** 연출을 고치는 툴. 대사 한 줄(LineId)을 잡고, 고치려는 종류(카메라 · 자리와 구도 · 연기 · 화면)만 걸러 보며, 그 종류의 카탈로그에서 골라 넣고 수치를 맞춘다. 고친 것은 yarn에 그대로 적히고, ked-presentation-runtime에서 되감으면 화면에 반영된다.

지금은 **명령줄(`st`)과 읽는 화면**이 있다 — `st`는 줄을 종류로 걸러 읽고 조작 다섯(값 바꾸기 · 더하기 · 빼기 · 지연 · 간격)으로 고친다. 화면(`dotnet run --project src/St.App`)은 대본 폴더를 열어 줄을 찾고 종류로 걸러 본다 — 화면에서 고치는 일은 다음 단계다.

```
st read  대본폴더 --line ln_abc --kind acting
st set   대본폴더 ln_abc/start/acting/0 face 05
```

- 쓰는 법: [`docs/CLI.md`](docs/CLI.md)
- 설계: [`docs/PLAN.md`](docs/PLAN.md)
- 세션마다 넘겨받을 일: [`docs/handoff/`](docs/handoff/)
