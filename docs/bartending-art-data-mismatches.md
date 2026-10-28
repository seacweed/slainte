# 바텐딩 병 아트와 데이터 연결 현황

기준일: 2026-10-27

## 현재 기준

활성 재료는 `items.csv`의 `item_1001`~`item_1015` 중 뜨거운 물(`item_1014`)을 뺀 14종이다. 임포터는 각 행에서 다음 두 에셋을 만들거나 갱신한다.

- 제조·액체 데이터: `Assets/Resources/Bartending/Items/<ID>.asset`
- 술장·상점·바테이블 표시: `Assets/_Project/Features/Bartending/Content/Generated/LiquorBottles/Planning/<ID>.asset`

`Planning/` 술장 정의는 14개이며, 모든 `LiquorBottleDef`의 상점·술장(재고 있음/없음)·바테이블 Sprite 참조가 연결되어 있다.

## 이미지 사용 규칙

- 상점: `*_blank` Sprite → `LiquorBottleDef.shopBlankSprite`
- 술장(재고 있음): `*_in` Sprite → `LiquorBottleDef.shelfInSprite`(구 `shelfLidSprite`, `FormerlySerializedAs`로 값 유지)
- 술장(재고 없음): `*_dis` Sprite → `LiquorBottleDef.shelfDisabledSprite`. 비어 있으면 술 선택 슬롯이 기존처럼 반투명으로 대신 표시한다(가니쉬 등)
- 바테이블: 기본 Sprite → `LiquorBottleDef.barSprite`와 `ItemDef.icon`
- 모든 병 아트는 `Art/Bottles/`에 두고 310×590 Single Sprite(FullRect, 여백 포함)로 임포트된다 — 폴더 임포트 규칙이 자동 적용하며 `Tools/Slainte/Validate Bartending Art Contract`가 14종×4장(기본·`_blank`·`_in`·`_dis`)+잔 16장=72장을 검증한다.
- 이름이 비슷하다는 이유만으로 다른 재료의 이미지를 대신 연결하지 않는다.

## 활성 데이터 연결표

| Item ID | 표시 이름 | CSV `IconName` | 아트 기본 이름 |
|---|---|---|---|
| `item_1001` | 열대 주스 | `icon_tropicalJuice` | `tropicaljuice` |
| `item_1002` | 실청 | `icon_siltrop` | `siltrop` |
| `item_1003` | 합성 레몬 | `icon_syntheticLemon` | `syntheticlemon` |
| `item_1004` | 슬롭 | `icon_slop` | `slop` |
| `item_1005` | 나낭나 | `icon_nanangna` | `nanangna` |
| `item_1006` | 코튼 | `icon_cotton` | `cotton` |
| `item_1007` | 헥타르 | `icon_hectar` | `hectar` |
| `item_1008` | 블레스 | `icon_bless` | `bless` |
| `item_1009` | 브리즈 보드카 | `icon_breezeVodka` | `breezevodka` |
| `item_1010` | 조니 독스 | `icon_johnnyDogs` | `johnnydogs` |
| `item_1011` | 번햄 버번 | `icon_burnhamBourbon` | `burnhambourbon` |
| `item_1012` | 바하 | `icon_beatha` | `beatha` |
| `item_1013` | 탄산 미닛 | `icon_minuteFizz` | `minutefizz` |
| `item_1015` | 커피 분말 | `icon_coffeePowder` | `coffeepowder` |

뜨거운 물(`item_1014`)은 기획 변경으로 2026-10-27 술장·상점 카탈로그와 술장 정의·아트에서 제거했다. 제조용 `ItemDef`와 레시피·CSV·세이브 마이그레이션은 이후 다른 기획에서 다시 쓰기 위해 남겨 두었다(아이콘은 비어 있음). `items.csv` 행은 그대로 두고, `PlanningCsvAssetImporter.ShelfExcludedItemIds`(술장 제외 목록)가 이 ID의 술장 정의·카탈로그 항목 생성을 건너뛴다. 자동 재임포트 판정과 임포트 후 검증도 이 목록을 뺀 개수로 센다. 다시 진열하려면 목록에서 빼고 아트를 추가한 뒤 재임포트한다.

`IconName`은 기획 키이고 실제 Sprite 이름 탐색은 임포터의 이름 정규화와 접미사 규칙을 거친다. 연결 여부는 파일명 추측이 아니라 생성 에셋의 직렬화 참조와 검증 메뉴로 확인한다.

## 병존하는 이전 에셋

`Content/Generated/LiquorBottles` 바로 아래에는 `beatha.asset`, `breezeVodka.asset` 같은 이름 기반 `LiquorBottleDef`가 남아 있다. 이 에셋들은 현재 동일 재료의 canonical `ItemDef`를 직접 참조하도록 연결되어 있으므로, 과거 문서에 적힌 “동일 ID의 ItemDef가 없어 소환할 수 없음” 상태는 해소됐다.

다만 이름 기반 에셋과 `Planning/item_####.asset`이 함께 존재하는 구조는 중복이다. 술장 카탈로그, Scene, Prefab의 참조를 조사하지 않은 채 이전 에셋을 삭제하면 안 된다. 최종 목표는 참조를 ID 기반 에셋으로 통일한 뒤 이름 기반 호환 에셋을 제거하는 것이다.

## 남은 확인

- 상점 → 술장 → 바테이블 세 화면에서 15종 Sprite가 의도한 상태로 보이는지 수동 확인
- 병 Sprite Collider 자동 생성 결과와 실제 클릭·드래그 범위 확인
- 입력된 `RGBA`가 병 아트와 완성 음료에서 의도한 색으로 보이는지 확인
- 이름 기반 이전 `LiquorBottleDef`의 Scene·Prefab·카탈로그 참조 감사 후 제거 여부 결정
