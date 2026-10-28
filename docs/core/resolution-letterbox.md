# 해상도 대응 (16:9 고정 + 레터박스)

게임은 어떤 해상도·화면비에서도 **16:9 게임 영역 하나**로 그려지고, 남는 부분은 검은 띠(레터박스/필러박스)로 덮인다. UI·아트는 계속 QHD(2560×1440) 기준으로 배치하면 된다.

## 구성 요소 (`Assets/_Project/Shared/Runtime/Display`, asmdef `Slainte.Shared.Display`)

| 타입 | 역할 |
|---|---|
| `GameAspect` (static) | 기준 해상도·목표 비율 상수, 현재 16:9 게임 영역 픽셀 사각형(`GameScreenRect`), 정규화 좌표 → 게임 영역 변환, 팝업 좌우 배치(`PlaceBesideTarget`), 해당 UI가 속한 게임 영역 루트 찾기(`FindGameAreaRect`), 해상도 변경 이벤트(`ScreenSizeChanged`) |
| `GameAreaRoot` | 각 루트 Canvas 바로 아래의 중앙 고정 2560×1440 RectTransform 표식. 이름은 `GameArea16x9` |
| `LetterboxOverlay` | 첫 씬 로드 직후 `RuntimeInitializeOnLoadMethod`로 스스로 생성되는 최상단(sortingOrder 32767) Overlay 캔버스. 게임 영역 밖을 검은 띠로 덮고 클릭을 막으며, 해상도 변경을 감지해 `GameAspect.ScreenSizeChanged`를 발행 |

## 동작 원리

- 모든 루트 Canvas의 CanvasScaler는 **Scale With Screen Size / 2560×1440 / Expand**.
  Expand는 캔버스가 항상 기준 해상도 이상이 되게 하므로, 그 안의 중앙 고정 `GameArea16x9`는 어느 화면비에서든 정확히 2560×1440 캔버스 단위를 유지한다. 1 canvas unit = QHD 1px 관계가 그대로 유지된다.
- Canvas의 실제 콘텐츠는 전부 `GameArea16x9` 아래에 둔다. 루트 캔버스에 직접 붙이면 21:9·16:10에서 띠 영역까지 늘어나 잘린다.
- 띠는 씬에 배치하지 않는다. 개발용 씬을 단독 실행해도 자동으로 붙는다.

## 코드 작성 규칙

- "화면 끝", "보이는 영역"을 판정할 때 `Screen.width/height`나 루트 캔버스 크기를 쓰지 말고 `GameAspect.GameScreenRect` / `GameAspect.FindGameAreaRect(transform)`를 쓴다.
- 화면 기준 정규화 좌표(예: 바텐딩 서빙 타깃 `serveTargetNormalized`)는 `GameAspect.NormalizedToScreenRect`로 변환한다.
- 툴팁·정보카드처럼 대상 옆에 붙는 팝업은 `GameAspect.PlaceBesideTarget`을 사용한다. 간격은 캔버스 단위로 받아 해상도에 비례해 스케일된다.
- 실제 픽셀 크기에 맞춘 RenderTexture 등은 `GameAspect.ScreenSizeChanged`를 구독해 다시 만든다(`BartendingViewport` 참고). Expand 레이아웃에서는 해상도가 바뀌어도 RectTransform 크기가 그대로라 `OnRectTransformDimensionsChange`가 오지 않을 수 있다.

## 씬 변환 (에디터)

`Slainte > 해상도 > 열린 씬 Canvas를 16:9 레이아웃으로 변환`

- 열린 씬의 최상위 Screen Space Canvas마다 CanvasScaler를 위 설정으로 바꾸고, 기존 자식들을 새 `GameArea16x9` 아래로 순서 그대로 옮긴다(앵커·오프셋 유지, Undo 가능). 이미 변환된 Canvas는 건너뛴다.
- 프리팹 인스턴스인 Canvas는 자식 구조를 바꿀 수 없어 건너뛰고 로그로 알린다. 원본 프리팹에서 직접 감싼다.
- 변환 후 씬을 저장해야 반영된다.

## 미결 사항

- `BartendingSandbox`는 코드(`BartendingSandboxBootstrap.CreateCanvas`)로 캔버스를 만드는 개발용 씬이라 아직 16:9 루트가 없다.
- 해상도·창 모드 설정 메뉴는 없다(Player Settings 기본 1920×1080 전체화면 창).
