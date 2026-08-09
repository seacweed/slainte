# 메인메뉴 인트로 연출

## 개요

`MainMenuScene` 진입 시 팀 로고 → 타이틀 로고 → 배경 → 메뉴 순으로 이어지는 연출. 클릭/키 입력으로 언제든 스킵 가능.

## 스크립트 구성

| 스크립트 | 위치 | 역할 |
|---|---|---|
| `MainMenuIntroController` | `Assets/Scripts/MainMenu/` | 인트로 시퀀스 전체 진행(코루틴), 스킵 입력 처리 |
| `InfiniteHorizontalScroller` | `Assets/Scripts/MainMenu/` | 타일 2개를 이어붙여 무한 루프시키는 범용 스크롤러 — 구름 앞/뒷면에 재사용 |
| `MainMenuManager` | `Assets/Scripts/MainMenu/` | 시작 버튼 클릭 로직 (기존, 변경 없음). 인트로 완료 전엔 `MenuGroup`의 CanvasGroup이 non-interactable이라 클릭 불가 |
| `MainMenuCharacterSpawner` | `Assets/Scripts/MainMenu/` | 화면 양 끝에서 캐릭터를 반복 스폰(스폰 간격/최대 인원수/스프라이트 선택 담당), 씬 로드 즉시 시작해 메인메뉴 진입 후에도 계속 동작 |
| `MainMenuCharacterWalker` | `Assets/Scripts/MainMenu/` | 스폰된 캐릭터 1명의 개별 이동(속도, 위아래 bobbing) 및 반대쪽 끝 도달 시 콜백 담당 |

## 시퀀스 순서

1. 팀 로고(`teamLogoGroup`) fade in → hold → fade out
2. 타이틀 로고(`titleGroup`)와 타이틀 발광(`titleGlowGroup`)이 함께 화면 중앙에서 fade in → `titleBlinkSteps` 순서대로 둘이 동일한 알파값으로 동기화되어 N회 깜박임
3. 배경 딤머(`backgroundDimmerGroup`) fade out 시작, 동시에 타이틀 발광(`titleGlowGroup`)만 별도로 fade out 시작(타이틀 로고는 alpha 1 유지) — **이후 단계들과 완전히 독립적으로 진행**, 언제 끝나든 뒤 시퀀스를 막지 않음
4. `moveStartDelay` 대기 후, 타이틀+배경(`backgroundGroup`)이 동시에 위로 이동
5. 이동이 끝나면 곧바로 펍 조명(`pubLightingGroup`)이 깜박이며 켜짐
6. 조명 연출이 끝나면 곧바로 메뉴(`menuGroup`) fade in + interactable 활성화

임의의 키/클릭 입력 시 `SkipIntro()`가 `StopAllCoroutines()`로 전부 중단하고 각 CanvasGroup/위치를 최종 상태로 즉시 스냅.

캐릭터 등장 연출(`MainMenuCharacterSpawner`)은 위 시퀀스와 완전히 별개로 씬 로드 즉시 시작되며, 인트로가 스킵되거나 끝나도 멈추지 않고 메인메뉴 화면에서도 계속 반복된다.

## 타이틀 깜박임 커스터마이즈 (`titleBlinkSteps`)

`TitleBlinkStep` 리스트로 관리되며, 리스트 길이만큼 깜박임 횟수가 결정된다(기본 3개). 각 스텝은 다음을 개별 지정한다:

| 필드 | 의미 |
|---|---|
| `fadeOutDuration` | 밝은 상태(alpha 1)에서 `targetAlpha`까지 어두워지는 속도 |
| `targetAlpha` | 이번 깜박임에서 내려갈 알파값 |
| `holdDuration` | `targetAlpha`에서 머무르는 시간 |
| `fadeInDuration` | `targetAlpha`에서 다시 alpha 1로 밝아지는 속도 |
| `intervalBefore` | 이번 스텝의 페이드아웃을 시작하기 전 대기 시간(첫 스텝이면 타이틀 fade in 직후부터) |

인스펙터에서 리스트 항목을 추가/삭제하면 깜박임 횟수 자체도 바뀐다.

## 타이틀 발광 레이어 (`titleGlowGroup`)

타이틀 로고 뒤에 배치할 별도 CanvasGroup. 아직 씬에 미배치 상태이며(발광 이미지 에셋 추후 수급 예정), `titleGlowGroup`이 비어있으면 관련 로직은 자동으로 스킵되어 기존 인트로 동작에 영향을 주지 않는다.

- fade in과 `titleBlinkSteps` 깜박임 전 구간에서 `titleGroup`과 완전히 동일한 알파값으로 동기화되어 함께 움직임(`FadeCanvasGroups`/`BlinkCanvasGroups`가 여러 CanvasGroup에 동시 적용)
- 깜박임이 끝나 alpha 1로 복귀한 뒤, 배경 딤머(`backgroundDimmerGroup`) fade out과 **같은 시점**에 `titleGlowGroup`만 독립적으로 fade out 시작(`titleGroup`은 계속 alpha 1 유지) — 사라지는 타이밍/속도만 분리되고 그 전까지는 로고와 완전히 같이 깜박임
- fade out 지속시간은 `titleGlowFadeOutDuration`으로 딤머와 독립적으로 커스텀 가능(시작 타이밍만 동기화)
- 씬 배치 시 계층상 `TitleLogo`보다 아래(뒤)에 위치해야 함

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
│   ├─ CharacterSpawnArea  (RectTransform, MainMenuCharacterSpawner의 spawnArea — 배경과 함께 move up)
│   └─ BackgroundDimmer    (불투명 검정 Image + CanvasGroup, 맨 위)
├─ TitleGlow               (CanvasGroup, TitleLogo 바로 아래에 배치 — 미배치 시 자동 스킵)
├─ TitleLogo               (CanvasGroup, RectTransform 이동 대상)
├─ TeamLogoPanel           (CanvasGroup)
└─ MenuGroup               (CanvasGroup, 기존 StartButton 포함)
```

## 캐릭터 등장 연출 (`MainMenuCharacterSpawner` / `MainMenuCharacterWalker`)

`Assets/Sprites/maintitle/`의 캐릭터 실루엣 8종(`메인화면_캐릭터(...)​.png`)을 `MainMenuCharacterSpawner`의 `characterSprites` 배열에 연결해 사용한다. 스폰/이동 로직은 프리팹 없이 런타임에 `Image` 컴포넌트를 코드로 생성하는 방식이라 씬에는 다음만 준비하면 된다:

1. 빈 `RectTransform`(`CharacterSpawnArea`)을 `BackgroundRoot`(`backgroundGroup`) 하위에 배치 — `BackgroundDimmer` 앞(위) 어디든 상관없으나 순서상 그 직전을 권장. `BackgroundRoot`의 자식이므로 배경 move up 애니메이션에 캐릭터도 자동으로 함께 따라간다. 이 RectTransform의 폭이 캐릭터가 좌우로 오가는 이동 구간(양 끝)이 되므로, 화면 폭에 맞춰 앵커/사이즈를 설정
2. `MainMenuCharacterSpawner` 컴포넌트를 아무 GameObject에 부착하고 `spawnArea`에 위 RectTransform 연결, `characterSprites`에 8종 스프라이트 연결
3. `baselineY`로 캐릭터가 걷는 세로 위치(기준선)를 지정 — 스폰마다 세로 위치는 랜덤화하지 않고 이 값 고정 + `bobbingAmplitudeRange`/`bobbingFrequencyRange` 범위 내 위아래 흔들림만 랜덤 적용
4. 각 캐릭터는 `moveSpeedRange` 범위 내 랜덤 속도로 반대쪽 끝까지 이동한 뒤 자동 소멸(`Destroy`), `maxConcurrentCharacters`로 동시 존재 가능한 최대 인원수 제한
5. 스폰 간격은 `spawnIntervalRange` 범위 내 랜덤이며, 씬 로드 즉시(`Start`) 시작해 인트로 진행 상태와 무관하게 계속 반복 스폰됨(메인메뉴 진입 후에도 유지)
6. `spriteFacesRight`는 원본 스프라이트가 기본적으로 오른쪽을 보고 있는지 여부 — 이동 방향과 다르면 자동으로 좌우 반전(localScale.x 부호 반전)

## 에셋 (`Assets/Sprites/maintitle/`)

- `splash_01.png` — 팀 로고 ("TEAM ISLAND" + 모래시계 아이콘)
- `main_title_text.png` — 타이틀 워드마크 "Sláinte"
- 배경 레이어 8종: `메인화면_(전체배경/구름_뒷면/구름_앞면/감시탑/철제_구조물/펍_실루엣/펍_실루엣_라이팅/바닥부분).png`
- 캐릭터 실루엣 8종: `메인화면_캐릭터(뚱뚱한_남성/뚱뚱한_할머니/마른_남성/마른_할머니/어린_소녀/어린_소년/일반여성/할아버지).png` — `MainMenuCharacterSpawner.characterSprites`에 연결
- 타이틀 발광 에셋: 추후 수급 예정, 받으면 `TitleGlow` CanvasGroup의 Image에 연결
- **미사용**: `main_title.png`, `메인화면_(전체화면).png`(구버전 합성 목업)

## 알려진 함정

| 항목 | 설명 |
|---|---|
| 신규 다운로드 스프라이트가 Multiple 모드로 임포트됨 | 위 배경/로고 파일들은 받은 그대로면 Sprite Mode가 Multiple이고 알파 기준으로 자동 트리밍된 서브 스프라이트만 들어있다(예: `splash_01`이 기본값으로는 모래시계 아이콘만 잘려 나옴). 사용 전 반드시 **Single 모드로 변경** 필요 |
| Screen Space - Overlay 배경색 | UI가 덮지 않는 영역은 Main Camera의 Background Color(Clear Flags: Solid Color)가 그대로 비친다 — 에디터/빌드 동일하게 적용되므로, 완전한 검정 바탕을 원하면 카메라 배경색을 검정으로 바꾸거나 화면 전체를 덮는 불투명 레이어를 깔아야 한다 |
| 구름 레이어 폭 | `구름_뒷면`/`구름_앞면`은 5117px(캔버스 폭의 약 2배)로 제작되어 있어 `InfiniteHorizontalScroller`의 2타일 무한 루프에 맞춰져 있다 |
