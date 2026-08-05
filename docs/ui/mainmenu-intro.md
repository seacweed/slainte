# 메인메뉴 인트로 연출

## 개요

`MainMenuScene` 진입 시 팀 로고 → 타이틀 로고 → 배경 → 메뉴 순으로 이어지는 연출. 클릭/키 입력으로 언제든 스킵 가능.

## 스크립트 구성

| 스크립트 | 위치 | 역할 |
|---|---|---|
| `MainMenuIntroController` | `Assets/Scripts/MainMenu/` | 인트로 시퀀스 전체 진행(코루틴), 스킵 입력 처리 |
| `InfiniteHorizontalScroller` | `Assets/Scripts/MainMenu/` | 타일 2개를 이어붙여 무한 루프시키는 범용 스크롤러 — 구름 앞/뒷면에 재사용 |
| `MainMenuManager` | `Assets/Scripts/MainMenu/` | 시작 버튼 클릭 로직 (기존, 변경 없음). 인트로 완료 전엔 `MenuGroup`의 CanvasGroup이 non-interactable이라 클릭 불가 |

## 시퀀스 순서

1. 팀 로고(`teamLogoGroup`) fade in → hold → fade out
2. 타이틀 로고(`titleGroup`) 화면 중앙에서 fade in → N회 깜박임
3. 배경 딤머(`backgroundDimmerGroup`) fade out 시작 — **이후 단계들과 완전히 독립적으로 진행**, 언제 끝나든 뒤 시퀀스를 막지 않음
4. `moveStartDelay` 대기 후, 타이틀+배경(`backgroundGroup`)이 동시에 위로 이동
5. 이동이 끝나면 곧바로 펍 조명(`pubLightingGroup`)이 깜박이며 켜짐
6. 조명 연출이 끝나면 곧바로 메뉴(`menuGroup`) fade in + interactable 활성화

임의의 키/클릭 입력 시 `SkipIntro()`가 `StopAllCoroutines()`로 전부 중단하고 각 CanvasGroup/위치를 최종 상태로 즉시 스냅.

## 배경 레이어와 딤머 방식

배경 레이어(전체배경/구름/감시탑/펍 실루엣/바닥/철제구조물 등)는 씬 시작부터 **항상 alpha 1**로 전부 그려져 있고, `BackgroundRoot`의 마지막 자식인 불투명 검정 `BackgroundDimmer`가 그 위를 덮고 있다가 alpha 1→0으로 걷히면서 밝아지는 방식이다.

레이어 각각을 개별 CanvasGroup으로 페이드시키는 방식은 레이어마다 겹치는 면적/알파 패턴이 달라 밝아지는 체감 속도가 서로 다르게 보이는 문제가 있었다 (알파 합성 특성상 `그룹알파 × 최종합성`과 같지 않음). 이미 다 그려진 최종 합성 위에 단일 딤머만 걷어내는 방식으로 바꿔 해결.

`BackgroundDimmer`는 `BackgroundRoot`의 자식이라 상승 이동(move up) 애니메이션도 배경과 함께 자동으로 따라간다.

펍 조명(`펍_실루엣_라이팅`)은 배경과 별개의 CanvasGroup(`pubLightingGroup`)으로 분리되어 있다 — 배경은 항상 켜져 있지만 조명만 알파 0으로 시작했다가, move up 완료 시점에 독립적으로 깜박이며 켜진다.

## 씬 계층 구조

```
Canvas
├─ BackgroundRoot          (CanvasGroup, RectTransform 이동 대상)
│   ├─ FullBackground
│   ├─ CloudBackContainer  [InfiniteHorizontalScroller] → Tile0, Tile1
│   ├─ WatchTower
│   ├─ PubSilhouette
│   ├─ PubSilhouetteLighting (CanvasGroup 별도)
│   ├─ Floor
│   ├─ CloudFrontContainer [InfiniteHorizontalScroller] → Tile0, Tile1
│   ├─ SteelStructure
│   └─ BackgroundDimmer    (불투명 검정 Image + CanvasGroup, 맨 위)
├─ TitleLogo               (CanvasGroup, RectTransform 이동 대상)
├─ TeamLogoPanel           (CanvasGroup)
└─ MenuGroup               (CanvasGroup, 기존 StartButton 포함)
```

## 에셋 (`Assets/Sprites/maintitle/`)

- `splash_01.png` — 팀 로고 ("TEAM ISLAND" + 모래시계 아이콘)
- `main_title_text.png` — 타이틀 워드마크 "Sláinte"
- 배경 레이어 8종: `메인화면_(전체배경/구름_뒷면/구름_앞면/감시탑/철제_구조물/펍_실루엣/펍_실루엣_라이팅/바닥부분).png`
- **미사용**: `main_title.png`, `메인화면_(전체화면).png`(구버전 합성 목업), 캐릭터 실루엣 8종(이번 범위 밖)

## 알려진 함정

| 항목 | 설명 |
|---|---|
| 신규 다운로드 스프라이트가 Multiple 모드로 임포트됨 | 위 배경/로고 파일들은 받은 그대로면 Sprite Mode가 Multiple이고 알파 기준으로 자동 트리밍된 서브 스프라이트만 들어있다(예: `splash_01`이 기본값으로는 모래시계 아이콘만 잘려 나옴). 사용 전 반드시 **Single 모드로 변경** 필요 |
| Screen Space - Overlay 배경색 | UI가 덮지 않는 영역은 Main Camera의 Background Color(Clear Flags: Solid Color)가 그대로 비친다 — 에디터/빌드 동일하게 적용되므로, 완전한 검정 바탕을 원하면 카메라 배경색을 검정으로 바꾸거나 화면 전체를 덮는 불투명 레이어를 깔아야 한다 |
| 구름 레이어 폭 | `구름_뒷면`/`구름_앞면`은 5117px(캔버스 폭의 약 2배)로 제작되어 있어 `InfiniteHorizontalScroller`의 2타일 무한 루프에 맞춰져 있다 |
