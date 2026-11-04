# 스프라이트 겹침·레이어 점검 — 2026-10-28

실험 프리팹 33개, 저장된 렌더러 52개와 F 모드의 실제 GPU 렌더를 점검했다. 원본 실험 코드·프리팹·씬·ProjectSettings는 수정하지 않았다. 변경 전 해시로 확인한 426개 파일 모두 그대로다.

현재 화면 순서는 개체 전체의 앞뒤보다 **부품 종류별 고정 Sorting Order**가 우선한다. 저장된 렌더러는 모두 Sorting Layer `Default`, Z=0이며 SortingGroup은 없다. 카메라·충돌에 사용하는 GameObject Layer 29와 화면의 Sorting Layer/Order는 별개다.

## 현재 순서

Order가 작은 쪽에서 큰 쪽으로 정리했다. 실제 로드한 스프라이트 이름·재질·Queue는 `01-overlap/loaded-renderers.tsv`, 직렬화된 전체 목록은 `serialized-inventory.csv`와 JSON에 기록했다.

| 대상 | Order | 비고 |
| --- | ---: | --- |
| 테이블 선 | -10 | 공통 환경 프리팹 |
| 디스펜서 호스 | -3 | 런타임 LineRenderer |
| 잔 뒤쪽 2개 레이어 | -2 | 잔 4종 공통 |
| 스푼 전체, 지거 양쪽, 얼음통 양쪽, 쉐이커 뒤 | -1 | 개체를 잡아도 그대로 |
| GPU 액체 | 0 | 모든 용기의 액체를 하나의 화면 합성 MeshRenderer로 표시 |
| 잔 영역에 겹친 가니쉬 | 2 | 실제 소속이나 잡은 상태 대신 위치 포함 여부로 변경 |
| 병 15종 | 3 | 병마다 단일 SpriteRenderer |
| 잔 앞쪽 2개 레이어, 쉐이커 앞·뚜껑·스트레이너, 파편 | 4 | 파편은 생성 코드에서 4로 덮어씀 |
| 얼음 | 5 | 잔 안에서도 유지 |
| 가니쉬 공급통 / 내용 아이콘 | 20 / 21 | 고정 공급 개체 |
| 디스펜서 본체 | 30 | 항상 높은 우선순위 |
| 잔 밖의 가니쉬 | 45 | 원래 프리팹 Order를 기억해서 복원 |

렌더된 SpriteRenderer 재질은 `Sprite-Lit-Default`, Queue 3000이다. GPU 액체도 Queue 3000, Order 0이며 표시 셰이더는 `ZWrite Off`, `ZTest Always`를 사용한다. 카메라는 Orthographic, transparency sort mode는 Default, 카메라에 기록된 축은 (0,0,1)이다.

## 확인한 문제와 정책 공백

### 1. 잡은 개체를 앞에 표시하는 일반 규칙이 없다

병을 잡아도 Order 3이 유지되어 다른 잔의 앞면 Order 4가 병 위에 그려진다. 같은 위치·각도에서 잡기 전후 캡처의 PNG 해시가 일치했다. 손에 든 병이 잔 뒤에 있는 것처럼 보이는 상태를 재현했다. 지거·스푼과 분리된 쉐이커 부품도 잡기에 따른 공통 전면 승격이 없다.

![잡은 병 위에 겹쳐 그려지는 잔 앞면](/A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerAudit20261028/01-overlap/02-bottle-glass-held.png)

근거: [SetHeld](/A:/UnityHub/proj/slainte/Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentBody.cs:113), [병 Order 3](/A:/UnityHub/proj/slainte/Assets/_Project/Features/Bartending/FluidGpuExperiment/Prefabs/Bottle_item_1001.prefab:451), [잔 앞면 Order 4](/A:/UnityHub/proj/slainte/Assets/_Project/Features/Bartending/FluidGpuExperiment/Prefabs/Glass_highball.prefab:173).

### 2. 잡은 가니쉬가 잔에 겹치면 곧바로 잔 내부 순서로 내려간다

바깥에서 Order 45인 가니쉬를 잡은 채 잔 영역으로 옮기면 Order 2로 바뀐다. 재현 당시 `IsHeld=True`, `ContainingVesselId=0`이었으므로 실제로 잔에 소속되거나 놓인 상태가 아니다. 임의의 용기가 해당 위치를 포함하는지만 검사한다. 따라서 마우스로 들고 있는 중에도 잔 속에 들어간 것처럼 그려진다.

![소속이 없는 잡은 가니쉬가 잔 앞면 뒤로 내려간 상태](/A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerAudit20261028/01-overlap/05-held-garnish-over-glass.png)

근거: [잡은 상태에서도 sorting 갱신](/A:/UnityHub/proj/slainte/Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentBody.GarnishMotion.cs:85), [위치 기준으로 2로 변경](/A:/UnityHub/proj/slainte/Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentBody.GarnishMotion.cs:234).

### 3. 도구·내용물의 앞뒤 규칙이 일관된 개체 관계로 표현되지 않는다

스푼 전체와 지거의 앞·뒤 그림이 모두 -1이라 액체 0 아래에 표시된다. 다른 잔의 액체와 겹친 정지 렌더에서 도구가 액체 뒤에 잠긴 것처럼 보였다. 반대로 얼음은 잔 안에서도 5로 남아 잔 앞면 4 위에 표시되고, 가니쉬는 2라 앞면 뒤에 표시된다. 얼음·가니쉬를 어떻게 표현할지는 의도 확인이 필요한 정책 항목이지만, 현재는 같은 내용물 사이에도 기준이 다르다.

![액체 아래에 표시되는 지거와 스푼](/A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerAudit20261028/01-overlap/07-tools-over-liquid.png)

근거: [지거 앞·뒤 -1](/A:/UnityHub/proj/slainte/Assets/_Project/Features/Bartending/FluidGpuExperiment/Prefabs/Jigger.prefab:357), [스푼 -1](/A:/UnityHub/proj/slainte/Assets/_Project/Features/Bartending/FluidGpuExperiment/Prefabs/Spoon.prefab:533), [얼음 5](/A:/UnityHub/proj/slainte/Assets/_Project/Features/Bartending/FluidGpuExperiment/Prefabs/Ice.prefab:305), [액체 0](/A:/UnityHub/proj/slainte/Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentGpuLiquid.Rendering.cs:189).

### 4. 클릭으로 고르는 개체와 화면 앞뒤가 일치하지 않는다

일반 개체 선택은 Sorting Order 대신 포인터와 개체 원점의 거리를 사용한다. 실제 병/지거가 겹치는 영역에서 Order 3인 병 뒤의 Order -1 지거가 선택됐다. 클릭 좌표는 (0.10, 0.31), 선택 결과는 Jigger였다. 별도로 디스펜서, 쉐이커 부품, 새 가니쉬/공급통에 대한 우선 처리도 존재한다.

근거: [원점 거리 기반 후보 선택](/A:/UnityHub/proj/slainte/Assets/_Project/Features/Bartending/FluidGpuExperiment/Runtime/FluidExperimentInteractor.cs:115).

## 이번에 재현하지 못한 항목

두 병은 모두 Order 3·Z=0이지만, 병 생성 순서를 A→B와 B→A로 반전한 이번 대조에서는 변경 픽셀이 0개였다. 동일 Order만으로 화면이 깜빡인다고 결론 내리지 않았다. 움직임·배치 순서·여러 그래픽 환경에서의 모든 동률 정렬을 검증한 것은 아니다.

## 정리 방향

수정 우선순위는 잡은 개체의 전면 표시, 실제 소속에 따른 내용물 표시, 화면 순서와 클릭 우선순위의 일치다. 이어 개체 전체의 앞뒤와 잔의 뒤→내용물→앞이라는 내부 순서를 함께 정의할 필요가 있다.

현재 액체는 모든 용기를 하나의 Order 0 합성 면에 그리므로 SortingGroup만 일괄 추가하거나 개체의 모든 Order를 같은 값만큼 올리는 방법은 자기 잔의 액체를 뒤로 가릴 수 있다. 액체의 소유자별 합성·가림 정책과 함께 설계해야 한다. 이번 점검에서는 원본의 레이어나 코드를 바꾸지 않았다.

## 증거 범위

Unity 6000.3.5f2 / D3D11 / RTX 3070의 격리 프로젝트에서 원본 489개 소스·에셋을 그대로 사용해 8장 렌더와 동작 관찰을 기록했다. 겹침 위치를 고정한 렌더링 진단이며 자연스러운 배치나 충돌 해결을 평가한 물리 시험은 아니다. 가니쉬 순서 갱신과 선택은 실제 런타임 함수를 호출했다. 캡처 5장을 직접 확인했다. 원본 실험 및 ProjectSettings 426개 파일의 변경은 0개다.

원자료: `serialized-inventory.json`, `serialized-inventory.csv`, `01-overlap/loaded-renderers.tsv`, `01-overlap/sprite-layer-validation.txt`, `01-overlap/Editor.log`, `preservation.json`.
