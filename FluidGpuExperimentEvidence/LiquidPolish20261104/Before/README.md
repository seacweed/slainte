# GPU 액체 샌드박스

현재 개발 기준인 F 응집 액체를 조작하고, 재사용 런타임과 프리팹을 만드는 독립 기능이다. 기존 게임 씬·PhysicsLab·Build Settings·Resources 로딩·게임의 액체 백엔드는 교체하지 않는다. 이전 비교 설계와 사용 설명은 [정리 전 README](../../../../../FluidGpuExperimentEvidence/Cleanup20261029/History/README.before-cleanup.md)와 [정리 전 개발 계획](../../../../../FluidGpuExperimentEvidence/Cleanup20261029/History/DEVELOPMENT_PLAN.before-cleanup.md)에 보관한다.

## 실행과 설정

Unity 메뉴 **Slainte > Fluid GPU Experiment > Open Sandbox**에서 [FluidGpuComparison.unity](Scenes/FluidGpuComparison.unity)를 열고 Play를 누른다. 씬 이름과 GUID는 기존 배치·참조를 보존하기 위해 유지한다. Compute Shader를 지원하는 GPU가 필요하다.

샌드박스는 하나의 응집 액체 계산 경로를 사용한다. 화면의 Material은 Auto/Water/Spirit/Syrup/Milk를 선택하며 입자의 점성과 응집 계수를 바꾼다. Ice / liquid는 기본 `NonColliding`과 `Colliding`을 선택한다. Glass breakage의 기본 `Natural`은 기존 운동과 측정한 충격을 사용하고, `Explosive`는 같은 액체 계산에 파편 비산 연출을 더한다. `R` 또는 Reset sandbox는 원래 배치·재고·입력 상태를 복원한다.

물리 설정은 [FluidExperimentLiquidSettings.asset](Data/FluidExperimentLiquidSettings.asset)의 Coherent liquid 그룹에 있다. 기본 압축 보정은 4회이며 `cohesiveDensityIterations=0`으로 끌 수 있다. `cohesiveSurfaceTension=0`, `cohesiveShearViscosity=0`은 각각 응집과 전단 점성을 끈다. 반복 압축 보정은 중력이나 물체 이동을 다시 적분하지 않는다. 입자별 ml와 성분별 ml를 유지하는 혼합을 사용하며, 응집은 유한 거리 근사다. 표면은 틱 보간·이웃 분포 타원·실제 벽 마스크로 구성한다. `useSurfaceRendering`은 진단용 입자 표시와 전환할 때 사용한다.

## 조작

- 일반 물체는 좌클릭으로 집거나 놓고, 우클릭과 마우스 상대 이동으로 회전한다. 회전 중 시스템 커서를 잠그고 회전 중심에 화면 커서를 표시한다. 우클릭을 놓으면 잠금을 풀고 수직 복귀 중에도 마우스 이동을 따른다. 자동 복귀 회전과 커서 좌표 전환은 투척 속도에 포함하지 않는다. Esc·포커스 이탈·초기화·비활성화는 잠금을 해제한다.
- 잡은 물체를 놓는 클릭 위치에 다른 대상이 있으면 기존 물체를 현재 사용자 이동·회전 속도로 놓고 새 대상을 집는다. 새 대상에는 이전 물체의 투척 속도를 넘기지 않는다. 놓는 순간 바닥·다른 물체와의 침투를 보정한다. 잡은 일반 물체는 외부 고체를 통과하지만 자기 용기 안의 얼음과 액체 접촉은 유지한다.
- 쓰러진 잔을 집어 수직으로 세우는 순간에는 내부 액체를 함께 옮기며 액체량과 속도 크기를 유지한다. 이후 사용자의 이동·회전에는 기존 액체 물리가 동작한다. 잔은 열린 입구를 바깥에서 안으로 통과한 액체를 다시 받는다. 옆면·바닥 겹침이나 내려놓기만으로 액체를 흡수하지 않는다.
- 얼음은 실제 다각형의 Physics2D 접촉으로 쌓인다. 빠른 용기 이동·회전의 벽 관통은 형상 기반 연속 검사로 복구하며, 정지한 얼음을 강제로 재우지 않는다. 두 액체 충돌 선택 모두 부력과 흐름 저항을 유지한다. `NonColliding`에서는 얼음이 GPU 액체를 밀지 않고, `Colliding`에서는 접촉과 공통 속도 한도를 적용한다. 열린 입구로 나온 얼음은 아래로 떨어지며 테이블 아래에서 위로 끌어올리지 않는다.
- 얼음 버켓은 내부 자유 액체를 담지 않는 공급원이다. 기울이면 재고에서 얼음을 배출하고 전면 잔량 표시가 바뀐다. 가니쉬 통을 클릭하면 새 조각을 집고 다시 클릭해 놓는다. 놓은 가니쉬는 중력·관성·충돌·자유 회전을 따르며 액체 흐름과 부력을 힘으로 받는다. 잡은 가니쉬는 커서를 그대로 따르고 놓을 때 충돌을 복원한다. 한 번 놓인 얼음·가니쉬는 다시 집을 수 없다.
- 놓인 쉐이커의 뚜껑·스트레이너를 각각 클릭해 분리하고 가까이 놓아 재부착한다. 스트레이너에 붙은 뚜껑은 함께 이동한다. 뚜껑만 열면 액체가, 스트레이너도 열면 얼음까지 출구를 통과한다. 잡은 쉐이커의 `C`는 밀폐 → 뚜껑 제거 → 스트레이너 제거 → 밀폐 순서다.

## 스푼과 칵테일 판정

잡은 스푼은 다른 고체를 통과하며 우클릭으로 회전하지 않는다. 우클릭 중 스푼 끝이 용기 내부에 안전하게 들어가면 그 용기의 벽이 스푼 이동만 제한한다. 잔을 밀거나 충격을 주지 않는다. 벽에 겹친 상태에서 시작하면 튕겨내지 않고 여유 공간으로 옮길 때까지 대기한다. 열린 입구로 꺼내기·우클릭 해제·Esc·포커스 이탈·놓기·Reset은 제약을 해제한다.

스푼을 용기 액체와 겹친 채 움직이면 재료가 서서히 섞이고 젖은 부분 주변에 약한 흐름을 만든다. 스푼의 고체 GPU 액체 충돌과 액체 표시를 파내는 마스크는 사용하지 않는다. `spoonFlowSpeed`는 기본 0.16u/s, `spoonFlowAcceleration`은 0.6u/s²이며 자극 흐름의 목표와 가속도 한도다. `spoonMixRate`는 1.6/s다. 멈추면 혼합 자극을 해제하고 기본 0.3초의 감쇠 흐름을 거쳐 자연 액체 계산만 남긴다. 이 값들은 전체 유체 속도 상한이 아니다.

`P`로 칵테일 패널을 열고 잔·요청 레시피를 선택한다. 패널의 판정 버튼 또는 `Enter`로 판정한다. 완료된 GPU 스냅샷의 재료별 ml·잔 ID·얼음·기법 이력을 기존 `CocktailComposition`, `CocktailEvaluator`, `CocktailOrderEvaluator`에 전달한다. 스냅샷이 오래됐거나 생성 예약이 남은 전달은 거절하며, 성분과 기법 이력은 액체 전달 경로를 따른다. Stir와 Shake는 실제 접촉·움직임·시간·균일도 조건으로 관찰한다.

## 디스펜서와 파손

오른쪽 상단 디스펜서의 파란 버튼은 Ice, 빨간 버튼은 Hot이며 같은 버튼을 다시 누르면 None이다. 본체를 좌클릭해 집고 우클릭을 유지해 수직 배출한다. 잡은 동안 커서를 따라 움직이며 회전·충돌·이동 범위 제한을 적용하지 않는다. 노즐이 다른 고체 안에 있으면 생성을 멈춘다. 다시 좌클릭하면 실제 `DistanceJoint2D`의 줄을 감아 돌아가고, 수납 길이에 도달한 뒤에도 호스에 매달려 출렁인다. 복귀 중에는 재집기와 모드 변경을 막는다. 호스 폭·여유 길이·감는 속도·가감속·본체 중력과 감쇠는 [Dispenser.prefab](Prefabs/Dispenser.prefab)에서 조절한다.

Ice/Hot은 재고 5000 ml를 공유한다. 물은 40 ml/s, 얼음은 0.3초마다 하나이고 한 개에 10 ml를 차감한다. 양수인 10 ml 미만 재고도 마지막 얼음 하나를 허용한다. 생성 거절·노즐 차단·동시 입자/경계 한도에서는 재고를 보존한다. 생성 얼음은 범위를 벗어나면 정리되며, 총 재고가 동시 얼음 개수를 보장하지 않는다. Hot은 [DispenserHotWater.asset](Data/DispenserHotWater.asset)의 80°C 온도 데이터로 생성한다. 열교환·증발·녹음은 구현하지 않는다. Reset은 None·가득 찬 재고로 복원한다.

허리케인 잔은 놓거나 던진 상태에서 충분히 강하게 충돌하면 16개 파편으로 깨진다. 샌드박스의 Natural 기준은 강제 액체 스플래시 없이 위치·이전 위치·속도·부피를 보존해 잔 소속만 해제한다. 실제 얇은 파편 벽과 바닥·다른 물체 접촉으로 액체가 흐른다. 추가 방사형 분리와 충격 비례 퍼짐은 기본 0이다. 생성 시 겹친 파편 쌍은 서로 분리될 때까지 기다린 뒤 네이티브 접촉을 유지한다. `ShardContactPairCount`, `PendingShardContactCount`로 접촉 상태를 확인할 수 있다. Explosive는 파편에 바깥·위쪽 속도와 회전을 더하므로 자연 파손 재현과 구분한다. Reset은 파편을 치우고 원래 잔을 복원한다.

## 재사용과 제작

[FluidRuntimeSample.unity](Scenes/FluidRuntimeSample.unity)는 비교 UI 없이 같은 액체·입력·디스펜서·칵테일 모듈을 조립한 예시다. **Build Reusable Runtime Assets**, **Open Runtime Sample**, **Assemble New Runtime Scene** 메뉴를 사용한다. 런타임과 환경의 생성은 설정·셰이더·테이블 재질을 직접 참조하며 샌드박스 씬을 열 필요가 없다. 기존 프리팹과 디자이너 값은 생성 경로에서 보존한다.

[FluidExperimentRuntime.prefab](Prefabs/Runtime/FluidExperimentRuntime.prefab)에 카메라와 환경을 명시적으로 연결한다. `FluidExperimentRuntimeRig`의 `Spawn`, `SpawnObject`, `ResetRig`, `PrepareDrop`으로 조립·생성·초기화·재현을 수행한다. **Designer**는 프리팹 미리보기, 설정과 윤곽 편집, Undo/Redo, 저장, 격리된 Play Test를 제공한다. 프리팹 33개와 카탈로그 28개 항목의 기존 GUID·ID를 유지하며 디스펜서는 World 소유로 카탈로그에서 제외한다. **Validate Prefab References**는 저장된 프리팹·카탈로그·런타임·씬 참조를 읽기 전용으로 점검한다.

| 소스 | 역할 |
| --- | --- |
| [FluidExperimentGpuLiquid.cs](Runtime/FluidExperimentGpuLiquid.cs), [Cohesive.cs](Runtime/FluidExperimentGpuLiquid.Cohesive.cs) | 생성·초기 채움·고정 단계·압축·응집·전단 점성 |
| [FluidExperimentLiquid.compute](Shaders/FluidExperimentLiquid.compute), [FluidExperimentCohesive.hlsl](Shaders/FluidExperimentCohesive.hlsl) | GPU 입자·경계·소유권·보존형 혼합·응집 계산 |
| [ImprovedSurface.cs](Runtime/FluidExperimentGpuLiquid.ImprovedSurface.cs), [Rendering.cs](Runtime/FluidExperimentGpuLiquid.Rendering.cs) | 틱 보간·타원 표면·용기별 표시·리소스 수명 |
| [Accounting.cs](Runtime/FluidExperimentGpuLiquid.Accounting.cs), [Cocktails.cs](Runtime/FluidExperimentGpuLiquid.Cocktails.cs) | 완료 상태 회계·성분 전달·기존 판정 데이터 어댑터 |
| [Stirring.cs](Runtime/FluidExperimentGpuLiquid.Stirring.cs), [SpoonContacts.cs](Runtime/FluidExperimentBody.SpoonContacts.cs) | 스푼의 부드러운 흐름·혼합·용기 제약 |
| [FluidExperimentBody.Pouring.cs](Runtime/FluidExperimentBody.Pouring.cs) | 잔량·기울기·방출 시각·사용자 운동 속도 |
| [FluidExperimentComparison.cs](Runtime/FluidExperimentComparison.cs) | 기존 씬 정체성을 유지하는 F 샌드박스 UI |
| [FluidExperimentRuntimeRig.cs](Runtime/FluidExperimentRuntimeRig.cs), [FluidExperimentAssemblyBuilder.cs](Editor/FluidExperimentAssemblyBuilder.cs) | 재사용 런타임과 씬 조립 |

## 검증 자료

현재 정리의 범위·보존 비교·실행 결과와 한계는 [Cleanup20261029 보고서](../../../../../FluidGpuExperimentEvidence/Cleanup20261029/report.md)에 기록한다. 원본은 같은 폴더의 `before.zip`, `before-manifest.json`과 Git 상태·패치에 보관한다. 이전 실행 자료는 당시 코드의 근거이며 현재 정리본의 실행 결과와 구분한다.

[prepare_validation.py](../../../../../FluidGpuExperimentEvidence/Cleanup20261029/prepare_validation.py)는 현재 소스와 고정된 공유 의존성을 `Library` 하위의 폐기 가능한 검증 프로젝트로 복사한다. 프로젝트 루트에서 `python FluidGpuExperimentEvidence/Cleanup20261029/prepare_validation.py`로 준비한다. 원본 프로젝트의 실행 중인 Editor·AssetImportWorker를 확인한 뒤 별도 프로젝트에서 컴파일·GPU 회귀·참조 검증을 수행한다. 통과 여부는 보고서와 해당 실행 산출물로 확인한다. 자동·오프스크린 검증은 사람이 보는 튐·회전·누출·조작감과 시각적 자연스러움을 대신하지 않는다.

## 병 프리팹 관리

`Prefabs/Bottle_item_1001.prefab`부터 `Bottle_item_1015.prefab`까지 15종을 병별 원본으로 관리한다. 각 병의 재료·외형·충돌 프로필·붓기 설정은 해당 프리팹에서 수정한다. 형태가 서로 다르므로 현재는 개별 프리팹을 유지한다. 샌드박스의 초기 병 3개는 원본 프리팹을 참조하고 배치 Transform만 별도로 저장한다.

`Slainte > Fluid GPU Experiment > Validate Prefab References`는 15종 전체의 카탈로그 등록, 재료 ID, 외형, 충돌·선택 Collider, 주입구·배출 방향, 용량·붓기 값, 씬 연결을 함께 검사한다. 기존 `Slainte > Fluid GPU Experiment > Build Reusable Runtime Assets` 실행 시 병 카탈로그의 재료·표시 이름·분류는 프리팹 원본 값으로 동기화된다.
