# `st` — 명령줄로 읽고 고치기

2026-11-22 · K3 (카드 층 · 바꾸기는 2026-11-23 · K7-6) · 설계는 [`PLAN.md`](PLAN.md) §4.3 · §4.5 · §2.4. AI 세션과 작업공간의 파이썬 도구가 부르는 입구다 — 화면(K4)과 같은 함수(`St.Authoring`의 `LineEditor`)를 지난다.

```
st read   <경로> [--line <LineId>] [--kind <종류>[,<종류>]]
st vocab  [--kind <종류>]
st cards  <경로> [--line <LineId>] [--catalog <폴더>]
st set    <경로> <주소> <칸> <값>          (값 대신 --clear)
st add    <경로> <LineId> <앵커> <커맨드> [인자…] [--index <순번>] [--card <카드 ID>]
st remove <경로> <주소>
st remove <경로> <LineId> --card <출처>
st replace <경로> <주소> <커맨드> [인자…]
st delay  <경로> <주소> <길이>
st gap    <경로> <LineId> <앵커> <길이>    (길이 대신 --clear)
st compose <연출 층 폴더> [--dialogue <대사 층>] [--out <게임 폴더>] [--force]
공통: --json · --dry-run · --specs <command-specs.json>
```

Windows에서는 `st.cmd`, 그 밖에서는 `./st`. 처음 부르면 빌드한다(소스를 받아 온 뒤에는 `st-build`).

| | |
|---|---|
| `<경로>` | yarn 파일 하나, 또는 대본 폴더(그 아래의 `*.yarn` 전부). 폴더면 LineId로 파일을 찾는다 — 두 파일에 있으면 거절한다 |
| `<주소>` | `LineId/앵커/종류/순번` — `ln_abc/start/expression/0`. LineId는 대본의 `#line:` 뒤의 글이다. 순번은 그 앵커의 같은 종류 안에서 몇 번째인가(0부터) |
| `<종류>` | `camera` · `placement` · `expression`(표정) · `acting`(연기) · `emoji`(이모지) · `screen` · `setup` · `sound` · `time` |
| `<길이>` | 시간 토큰 — `15fr`(30fps) · `0.5s` |
| 종료 코드 | 0 됨 · 1 거절(파일은 그대로다) · 2 쓰는 법이 틀렸다 |

## 읽기

```
$ st read 대본폴더 --line ln_k7inl2cm --kind expression
ln_k7inl2cm · 하린 · Demo_Presentation · 124행
  종류: 표정
  start   "…"
  a       "…"
      a/expression/0         +0s      face_swap  slot=s1 face=3 duration=13fr
  b       "…"
      b/expression/0         +0s      face_swap  slot=s1 face=05 duration=13fr
  end
```

- 커맨드 줄의 첫 칸이 주소의 뒤 세 칸이다 — 앞에 LineId를 붙이면 주소다.
- `+0.5s`는 지연 — 앵커가 열린 뒤 그 커맨드가 시작하기까지. 종류로 걸러 `pause`가 보이지 않아도 지연은 남는다.
- `칸=값`은 대본에 적은 것, `칸=(값)`은 적지 않아 생략값으로 가는 것이다. `<칸>`의 이름은 `st vocab`이 보여 준다.
- 앵커는 커맨드가 없어도 모두 보인다 — 거기에 더할 수 있다. `간격 1s`는 그 앵커의 `<<gap>>`.
- `--line` 없이 부르면 줄의 목록이다(`--kind`를 주면 그 종류의 커맨드가 있는 줄만). `[읽기만]`은 고칠 수 없는 줄이다.

## 카드 층

초안 도구와 카드 더하기는 카드에서 온 커맨드의 줄 끝에 출처를 남긴다 — `<<at start fade_in s1 0.57s>> // card:E04`, 한 줄에 같은 카드를 또 펼치면 둘째부터 `E04#2`(PLAN P5). `st cards`는 이 출처로 한 줄 안의 커맨드를 카드 한 장씩 묶는다.

```
$ st cards 대본폴더 --line ln_abc --catalog ../ked-vn-workspace/catalog
# …/ch09.yarn
  ln_abc            42행  하린: …  (자리와 구도 3 · 표정 2 · 연기 1)
  카드 3장
  E04      옆에서 들어옴  — 커맨드 2: start/placement/0 start/placement/1
  G01      뛰기  — 커맨드 2: start/acting/0 start/expression/0  [다음 줄에 이 카드의 다른 묶음]
  E04#2    옆에서 들어옴 · 일부  — 커맨드 1: a/placement/0
```

- 커맨드의 주소는 `st read --line`과 같은 꼴(뒤 세 칸 — 앞에 LineId를 붙이면 주소다).
- `--catalog`(작업공간의 `catalog/` — `cards.json` · `kinds.json`)를 주면 카드의 이름을 붙이고, 카드의 묶음과 커맨드 이름을 견주어 빠진 것이 있으면 **일부**라고 한다. 값을 고친 것은 일부가 아니다. 없으면 묶기만 한다.
- `[다음 줄에 이 카드의 다른 묶음]` — 같은 노드의 이웃 줄에 그 카드의 다른 묶음이 있다. 줄을 넘는 짝(B01 · M 계열)일 수 있다 — 짝인지는 카드가 말하지 않는다. 카탈로그가 있어야 안다. 묶기 · 빼기는 한 줄 안에서만 한다.
- 카드의 글(의도 · 해석 · 쓰인 자리)은 내지 않는다 — 팀의 위키에 있다.
- `--line` 없이 부르면 카드가 있는 줄의 목록(줄마다 출처, 일부면 `E04#2(일부)`). 출처가 없는 대본(재현 대본 · 스토리 공간)에는 카드가 없다.
- `--json`이면 `{ path, lineId, cards[ { tag, cardId, name, whole, elsewhere, commands[주소] } ] }`(줄 없이는 `{ files[ { path, lines[ { lineId, cards[…] } ] } ] }`). `whole`은 온전 true · 일부 false · 모름 null.

## 고치기

| 조작 | 하는 일 |
|---|---|
| `set` | 그 `<<at>>` 줄의 인자 하나. 뒤 칸을 적는데 앞 칸이 비어 있으면 앞 칸을 생략값으로 채운다(생략값이 없으면 거절). `--clear`는 그 칸을 비운다 |
| `add` | 그 앵커의 맨 뒤에 `<<at 앵커 커맨드 인자…>>`. `--index N`이면 그 종류의 N번째가 되게 끼운다. 커맨드가 아직 없는 앵커면 뒤 앵커의 줄 앞에 선다. `--card E04`면 줄 끝에 카드의 출처(`// card:E04`)를 남긴다 — 같은 카드를 한 줄에 또 펼칠 때의 번호(`E04#2`)는 부르는 쪽이 적는다 |
| `remove` | 그 `<<at>>` 줄. `--card <출처>`면(주소 대신 LineId) 그 줄의 그 카드(`E04` · `E04#2`)의 커맨드를 모두 — 한 번에 쓴다. 간격과 `pause`(지연)에는 출처가 없어 건드리지 않고, 카드의 커맨드가 선 앵커에 남은 것을 `알림:`으로 보인다 |
| `replace` | 그 `<<at>>` 줄을 제자리에서 다른 커맨드로 다시 쓴다 — 앵커 · 앞의 `pause`(지연) · 같은 앵커 안의 차례가 그대로다. 줄 끝의 카드 출처는 걷힌다(더는 그 카드의 것이 아니다). 다른 줄 끝 주석은 남는다. **인자를 적지 않으면 슬롯 · 길이 · 이징을 옛 커맨드에서 옮겨 적는다**(같은 갈래끼리 차례대로, 대본에 적힌 값만 — 거리 · 이름은 커맨드마다 뜻이 달라 옮기지 않는다). 꼭 적는 칸이 비면 거절한다. 인자를 적으면 그대로 쓴다 |
| `delay` | 지연을 그 값으로 — 그 커맨드 바로 앞의 `pause`를 넣거나 · 고치거나 · 뺀다. **`pause`는 같은 앵커의 뒤 커맨드를 모두 붙잡으므로 그 뒤의 커맨드도 같은 만큼 움직인다.** 더 앞선 기다림보다 당길 수는 없다 |
| `gap` | 그 앵커의 `<<gap>>`을 넣거나 · 고치거나 · (`--clear` · `0s`) 뺀다 |

지키는 것 — 시험이 지킨다:

1. **그 줄의 커맨드 묶음만 다시 쓴다.** 조작 하나는 묶음의 한 줄을 고치거나 넣거나 뺀다. 파일의 나머지는 바이트 하나 바뀌지 않는다(줄 끝 문자 · BOM · 주석 · 빈 줄 · 줄 끝 주석).
2. **깨진 yarn은 쓰지 않는다.** 고친 글을 줄 읽기로 다시 읽어, 새 문제가 생기거나 다른 줄의 읽기가 달라지거나 읽은 값이 고친 값과 다르면 거절한다.
3. 같은 값으로 고치면 쓰지 않는다("바뀐 것이 없다").

거절하는 것:

- **at 없는 명령이 든 줄**(걷힌 `<<step>>` 포함) — 읽기만 한다. 그 명령은 실행되지 않는다(런타임 N5) — 앵커를 붙이거나 지우는 것은 원문에서. 바로 실행하는 명령(`surface_reset` 같은 것)과 노드 끝의 명령은 걸리지 않는다.
- LineId가 없는 줄 · 한 대본에 LineId가 둘인 줄.
- 그 줄에 없는 앵커 — **표지(`[beat=이름/]`)를 새로 만들거나 옮기지 않는다**(대사 글의 일이다).
- 명세 표에 없는 커맨드를 더하기 · 바로 실행하는 명령을 더하기 · 꼭 적는 칸을 비우기 · 칸보다 많은 인자 · 읽지 못할 시간 토큰 · 정해진 낱말이 아닌 값.
- 값에 든 공백 · 따옴표 · `<<` · `>>` · `//`.

표지의 마지막 커맨드를 빼면 받아들이되 알린다 — 표지가 대사 글에 남는다(원문 검사의 경고).

값의 뜻(그 표정 · 모션 · 슬롯이 있는가, 거리 · 배율의 꼴)은 검사하지 않는다 — 런타임의 파서가 읽을 때 드러난다. 이름 인자의 후보는 K4에서 붙는다.

## 합치기 — 대사 층과 연출 층 (K8 · 2026-11-28)

설계는 [`PLAN.md`](PLAN.md) §2.5. 대사는 ked-story-director가 내는 대사 층(`exported/yarn`)에, 연출은 이 툴이 고치는 연출 층(`staging/`)에 있다. `st compose`가 둘을 **줄 ID로** 맞물려 게임 폴더에 완성본을 쓴다 — 런타임은 그 파일 하나만 읽는다.

```
st compose stories/eunseol/staging --out runtime/Assets/Dialogue/Story/eunseol    # 처음 — 짝을 준다
st compose stories/eunseol/staging                                               # 다음부터
st compose stories/eunseol/staging --dry-run                                     # 무엇이 바뀔지만
```

| | |
|---|---|
| 짝 | 연출 층 폴더의 `compose.json` — `{ "dialogue": "../exported/yarn", "out": "../../../runtime/Assets/Dialogue/Story/eunseol" }`. 연출 층 폴더에서 본 상대 경로다. `--dialogue` · `--out`으로 주면 이번에 쓰고 설정에 적는다(`--dry-run`이면 적지 않는다). `dialogue`를 적지 않으면 `../exported/yarn` |
| 쓰는 것 | 보관소(`staging/<장면>.retired.yarn`) → 연출 층(`staging/<장면>.yarn`) → 게임 yarn의 차례로, **바이트가 다른 파일만**. 대사가 연출 층의 대사와 같으면 아무것도 쓰지 않는다 |
| 같은 줄 | 연출 층의 줄 그대로 |
| 글 · 화자가 바뀐 줄 | 게임 yarn에는 새 글 + 연출 층의 박자 표시와 태그(말 도중의 박자는 줄 끝으로 간다). 연출 층에는 옛 글이 남는다 — `다시 볼 것`으로 알리고 옛 글과 새 글을 보인다 |
| 새 줄 | 연출 없이 그 자리에 — `연출 없음` |
| 지운 줄 | 묶음과 옛 줄이 보관소로 — `보관`. 같은 줄 ID가 대사 층에 다시 나타나면 저절로 돌아온다 — `복원` |
| 옮긴 줄 | 다른 노드 · 다른 장면으로 간 줄은 연출이 따라간다 — `옮김` |
| 손으로 고친 게임 yarn | 마지막으로 쓴 게임 yarn의 지문을 `staging/.composed.json`에 적어 둔다. 게임 폴더의 파일이 그 지문과 다르면(기록에 없는 파일이어도) **그 파일만 멈추고** 표준 오류에 `멈춤:`으로 알린다 — 다른 장면은 쓴다. 종료 코드 1. `--force`면 덮는다. 게임 yarn은 손으로 고치지 않는다 — 대사는 디렉터에서, 연출은 연출 층에서 |
| 거절 | 줄 ID가 한 층의 두 곳에 있다 · 합친 대본에 받은 대본에 없던 문제가 생긴다 · 대사 층에 yarn이 없다 — 아무것도 쓰지 않는다. 종료 코드 1 |
| `--json` | `scenes` · `lines` · `notes[]`(`kind` = `review` · `noStaging` · `retired` · `restored` · `moved`, `line` · `file` · `node` · `old` · `new` · `beatsMoved`) · `files[]`(`layer` · `name` · `change` = `written` · `removed` · `held` · `reason`) · `errors[]` |

보관소(`*.retired.yarn`)와 `compose.json` · `.composed.json`은 손으로 고치지 않는다.

## AI가 쓸 때

- 한 줄의 한 종류만 고칠 때 줄 전체를 읽고 다시 쓰지 않는다 — `st read … --line … --kind …`로 주소를 얻고 조작을 부른다.
- 먼저 `--dry-run`으로 바뀔 줄(`-` · `+`)을 본다.
- 초안을 카드 단위로 고칠 때 — `st cards … --line …`로 그 줄의 카드를 보고, `st remove … --card`로 통째로 빼고, 다른 카드는 `st add … --card`로 커맨드마다 넣는다(카드를 바꾸는 한 번의 조작은 화면에만 있다).
- `--json`이면 읽기는 `{ path, line: { lineId, editable, anchors, steps[ { anchor, gap, commands[ { address, name, kind, delay, args[…] } ] } ] } }`, 고치기는 `{ ok, changed, written, target, removed, added, warnings, notes, line }`다. 한 번에 여럿을 고친 것(`remove --card`)은 `removed` · `added`가 줄바꿈으로 이어진다. `notes`는 함께 알리는 것(남은 간격 · 지연, 옮겨 적은 인자).
- 포크의 대본(포크에만 있는 커맨드)은 `--specs <런타임이 낸 command-specs.json>`으로 읽는다. 없이 읽으면 그 커맨드는 "명세 표에 없다"로 보이고 주소가 없어 고칠 수 없다.
