# 바텐딩 병 아트와 데이터 연결 현황

기준일: 2026-08-19

## 적용 원칙

- 원본 PNG의 픽셀 크기, 캔버스, 종횡비를 변경하지 않는다.
- 상점은 `*_blank.png`, 술장은 뚜껑이 있는 `*_lid.png`, 바테이블은 뚜껑이 없는 기본 `*.png`를 사용한다.
- `hotwater.png`는 별도 변형이 없으므로 세 화면에서 같은 이미지를 사용한다.
- 이름과 데이터가 정확히 대응할 때만 연결한다. 유사 이름으로 추정하거나 새 데이터를 만들지 않는다.
- 데이터만 있는 항목은 데이터와 기존 레거시 참조를 보존하되 새 컨텍스트 이미지 필드는 비워 둔다.
- 아트만 있는 항목은 Unity 에셋으로 반입하되 데이터에는 연결하지 않는다.
- 기존 술장 배경과 프레임은 이번 작업에서 변경하지 않는다.

## 연결한 아트 세트

| 아트 이름 | LiquorBottleDef ID | 추가로 같은 아트를 쓰는 LiquorBottleDef ID | ItemDef ID | 비고 |
| --- | --- | --- | --- | --- |
| `tropicaljuice` | `tropical_juice` | `item_1001` | `item_1001` | 3종 연결 |
| `siltrop` | `siltrop` | `item_1002` | `item_1002` | 3종 연결 |
| `syntheticlemon` | `synthetic_lemon` | `item_1003` | `item_1003` | 합성 레몬 전용. 레몬주스에 재사용하지 않음 |
| `slop` | `item_1005` | - | `item_1005` | 3종 연결 |
| `nanangna` | `nanangna` | `item_1007` | `item_1007` | 3종 연결 |
| `cotton` | `cotton` | `item_1008` | `item_1008` | 3종 연결 |
| `hectar` | `hectar` | `item_1009` | `item_1009` | 3종 연결 |
| `bless` | `bless` | `item_1010` | `item_1010` | 3종 연결 |
| `breezevodka` | `breeze_vodka` | `item_1011` | `breeze_vodka`, `item_1011` | 3종 연결 |
| `johnnydogs` | `johnny_dogs` | `item_1012` | `item_1012` | 3종 연결 |
| `burnhambourbon` | `burnham_bourbon` | `item_1013` | `item_1013` | 3종 연결 |
| `beatha` | `beatha` | `item_1014` | `item_1014` | 3종 연결 |
| `coffeepowder` | `item_1025` | - | `item_1025` | 3종 연결 |
| `hotwater` | `item_1024` | - | `item_1024` | 단일 PNG를 세 컨텍스트에 연결 |

각 3종 연결은 다음 필드 대응을 뜻한다.

| 화면/용도 | 파일 | 데이터 필드 |
| --- | --- | --- |
| 상점 | `<name>_blank.png` | shop/blank sprite |
| 술장 | `<name>_lid.png` | shelf/lid sprite |
| 바테이블 | `<name>.png` | bar/open sprite 및 대응 ItemDef icon |

## 데이터는 있지만 새 아트가 없는 항목

| 데이터 | 상태 | 처리 |
| --- | --- | --- |
| `LiquorBottleDef: lemon_juice` | `lemonjuice` 아트 세트 없음 | 레몬주스 데이터와 기존 레거시 sprite를 삭제하지 않음. 새 shop/shelf/bar 필드는 비움 |
| `ItemDef: lemon_juice` | `lemonjuice` 아트 세트 없음 | 기존 레거시 icon을 보존. 합성 레몬 이미지를 대신 연결하지 않음 |
| `LiquorBottleDef/ItemDef: item_1023` (`꿀`) | 대응 병 아트 없음 | 새 shop/shelf/bar 및 icon 연결을 비움 |
| `ItemDef: lans_whiskey` | 대응 LiquorBottleDef와 새 병 아트 없음 | 기존 데이터를 보존하고 이번 연결 대상에서 제외 |

## 아트는 있지만 데이터가 없는 항목

| 아트 | 상태 | 처리 |
| --- | --- | --- |
| `minutefizz.png`, `minutefizz_blank.png`, `minutefizz_lid.png` | 정확히 대응하는 LiquorBottleDef/ItemDef 없음 | PNG와 Unity 에셋만 보존하고 미연결 |

## 런타임 데이터 불일치

다음 레거시 LiquorBottleDef는 이번 아트 세트와 연결되지만 동일 ID의 ItemDef가 없어서 현재 술장에서 바테이블로 생성할 수 없다. 액체 속성 데이터를 추정해 ItemDef를 새로 만들지 않았다.

- `beatha`
- `bless`
- `burnham_bourbon`
- `cotton`
- `hectar`
- `johnny_dogs`
- `nanangna`
- `siltrop`
- `synthetic_lemon`
- `tropical_juice`

`breeze_vodka`는 동일 ID의 LiquorBottleDef와 ItemDef가 모두 있어 세 화면 흐름을 검증할 수 있다. Planning 데이터의 `item_1001`~`item_1025` 중 현재 생성된 항목은 동일 ID의 두 정의가 있으므로, 아트가 있는 항목에 한해 세 화면 흐름을 검증할 수 있다.

## 후속 자료가 필요한 항목

- 레몬주스용 `lemonjuice.png`, `lemonjuice_blank.png`, `lemonjuice_lid.png`
- 꿀(`item_1023`)용 기본/blank/lid 병 이미지
- 미닛피즈의 안정 ID, 가격, 용량, 액체 색상과 분류를 포함한 LiquorBottleDef/ItemDef
- 위 레거시 10종의 바테이블 사용을 위한 동일 ID ItemDef 액체 데이터

도구장 배경, 도구 아트, 얼음 아트 및 도구 소유/배치 기능은 다음 작업 범위다.
