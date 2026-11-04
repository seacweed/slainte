# GPU 액체 샌드박스 개발 기준

2026-10-29 정리 이후의 유지보수·검증 기준이다. 현재 동작과 조작은 [README](README.md)에 정리한다. 이전 비교 실험의 설계·완료 주장·후속 제안은 [정리 전 개발 계획](../../../../../FluidGpuExperimentEvidence/Cleanup20261029/History/DEVELOPMENT_PLAN.before-cleanup.md)에 원문으로 보관하며, 현재 구현 상태로 간주하지 않는다.

## 유지할 실행 경로

개발 기준은 F의 응집 액체 한 경로다. 입자 크기·입자당 ml·초기 채움·병 방출·고정 물리 시간·완료 상태 회계와 기존 수치 설정을 보존한다. 압축 보정·독립 응집·재료별 전단 점성·보존형 혼합을 수행하고, 같은 입자로 틱 보간·이웃 분포 타원·벽 마스크 표면을 만든다. `Natural`과 `Explosive`는 같은 계산 위에서 선택하는 파손 스타일이다. 얼음의 `NonColliding`과 `Colliding`은 부력·흐름 저항을 공유한다.

작업 범위는 이 기능 폴더와 `FluidGpuExperimentEvidence`의 근거 자료다. 기존 게임 씬, PhysicsLab, 공용 칵테일 평가기, 아트 원본과 프로젝트 Build Settings를 이번 기능 정리의 대상으로 삼지 않는다. 공유 의존성을 변경해야 하는 새 작업은 해당 변경의 목적·호출자·검증 범위를 먼저 구체화한다.

## 제작 자산과 식별자

기존 샌드박스 씬의 이름·GUID와 배치·프리팹 오버라이드를 유지한다. `FluidExperimentComparison`은 저장된 컴포넌트 정체성을 유지하는 샌드박스 제어기다. Material·얼음 충돌·파손 스타일·Reset만 제공하며, 개발 재현은 별도 검증 하네스에서 수행한다.

재사용 경로는 `FluidExperimentRuntimeRig`, Runtime/Environment 프리팹, `FluidRuntimeSample.unity`, Designer다. 프리팹 33개와 카탈로그 28개 항목의 GUID·카탈로그 ID를 유지한다. World가 소유하는 디스펜서는 일반 생성 카탈로그에 등록하지 않는다. 새 런타임은 직접 참조한 설정·셰이더·재질과 빈 씬 템플릿으로 조립한다. 제작 빌더는 기존 샌드박스를 열어 값을 복사하거나 배치를 다시 만들지 않는다.

Designer 변경은 미리보기 초안의 Undo/Redo, 명시적 저장, 미저장 상태 복구, 격리된 Play Test의 복원 경계를 함께 확인한다. 생성·재빌드가 기존 프리팹 조정값과 씬 오버라이드를 덮어쓰지 않는지 점검한다. 메타데이터 재생성이나 enum 값 변경이 필요한 경우 직렬화 참조와 저장된 값을 함께 이전한다.

## 기능별 보존 조건

| 경로 | 보존할 조건 |
| --- | --- |
| 생성·회계·전달 | 생성 성공 때만 재고 차감, 입자별·성분별 ml 보존, 완료 스냅샷 일관성, 오래된 리비전·미완료 생성의 전달 거절 |
| 소유권·재수용 | 열린 입구를 통한 수용, 옆면·바닥 겹침에 의한 흡수 방지, 소속 해제 때 위치·속도·조성 유지 |
| 입력·투척 | 상대 마우스 이동, 작은 회전 연속성, 커서 복원, 자동 복귀 속도 제외, 놓기·스왑·포커스 상실 정리 |
| 얼음·가니쉬 | 실제 Rigidbody 접촉·자유 회전, 유효한 부력·흐름 샘플, 빠른 벽 이동의 관통 복구, 놓인 개체의 재집기 방지 |
| 스푼 | 겹친 잔 벽에서 안전 여유를 기다리는 제약, 잔에 힘을 주지 않는 이동 제한, 부드러운 흐름·성분 혼합·멈춤 뒤 감쇠 |
| 파손 | Natural의 기존 운동·충격 전달, 초기 중첩 파편 쌍의 분리 대기, 실제 얇은 벽·파편 접촉, Explosive의 별도 선택 |
| 디스펜서 | World별 재고·카메라, 노즐 차단 시 보존, 수직 배출, 실제 줄 감기·매달림, Reset과 범위 밖 생성물 정리 |
| 칵테일 판정 | 기존 평가기 재사용, 온전한 잔·잔 ID·성분·얼음·기법 이력, 패널과 입력의 클릭 차단, 전달·초기화 후 이력 정합성 |
| 표면·수명 | 소유 용기별 표시 순서, 실제 벽 마스크, 틱 보간, Reset·해상도 변경·비활성화·재초기화의 리소스 정리 |

스푼의 부드러운 흐름과 혼합은 현재 동작에 포함한다. 스푼 고체로 액체를 밀거나 표시를 파내는 방식으로 바꾸지 않는다. `spoonFlowSpeed`, `spoonFlowAcceleration`, `spoonMixRate`, `spoonSettleDuration`과 기법 관찰 조건을 별도 목적으로 다루며, 흐름 속도를 조절한다는 이유로 혼합 판정 시간을 함께 바꾸지 않는다.

## 변경 전후 검증

현재 정리의 실행 결과는 [Cleanup20261029 보고서](../../../../../FluidGpuExperimentEvidence/Cleanup20261029/report.md)와 그 폴더의 산출물이 기준이다. 이 문서는 실행 통과를 선언하지 않는다. 원본 소스·에셋은 `before.zip`, 파일 목록·해시는 `before-manifest.json`, 보호 대상은 `protected-before-sha256.json`에 남긴다. 원래 작업 중이던 Git 변경과 미추적 파일도 별도 상태·패치 기록으로 구분한다.

[prepare_validation.py](../../../../../FluidGpuExperimentEvidence/Cleanup20261029/prepare_validation.py)는 현재 소스, 공유 의존성의 고정 해시, 현재 검증 소스를 사용해 `Library` 아래에 폐기 가능한 프로젝트를 준비한다. 구 비교 구현을 현재 프로젝트에 다시 넣지 않는다. 검증 프로젝트를 갱신할 때는 실제 소스 해시와 포함 파일을 확인하고, Compute Shader 및 include의 재임포트를 강제해 이전 컴파일 산출물의 재사용을 방지한다.

검증은 다음 순서로 수행한다.

1. 삭제 대상·공유 호출자·메타데이터·GUID·카탈로그 ID·직렬화 참조를 점검하고 보호 대상의 해시를 비교한다. 저장된 프리팹 참조는 **Validate Prefab References**로 확인한다.
2. 별도 프로젝트에서 C#과 실제 그래픽 장치의 Compute/표면 셰이더를 컴파일한다. 초기화·커널 지원·GPU 바인딩 오류와 dispatch 건너뛰기를 실패로 처리한다.
3. 보존 원본과 현재 소스에 같은 고정 재현을 적용해 입자 위치·이전 위치·속도·ml·성분·기법·소유권을 비교한다. 재료와 얼음 충돌 선택, 병 방출, 혼합·스푼, 파손, 디스펜서, 재사용 조립과 Designer를 기능별로 검증한다.
4. Reset·연속 실행·런타임 생성·비활성화/재활성화 뒤 상태와 리소스를 확인한다. 실패 실행과 수정 이후 재실행을 모두 보존한다.
5. 보존 대상의 최종 해시와 실행에 사용한 소스 해시를 확인하고 보고서에 자동 검증 범위·실패·수정·남은 관찰 범위를 기록한다.

정지 물리 문제는 바닥이 지지하는 잔과 중력이 켜진 고체로 재현한다. 한 개·여러 개의 얼음 또는 가니쉬를 둔 최소 장면에서 최대 속도·에너지·수면 변위와 시간 경과를 관찰한다. 평균이나 마지막 프레임만으로 순간 튐·누출·흔들림을 통과 처리하지 않는다. GPU·배치·오프스크린 통과와 사람이 직접 보는 움직임·조작감·자연스러움의 확인을 구분한다.

## 작업 진입점

| 변경 목적 | 진입점 |
| --- | --- |
| 계산·설정 | [FluidExperimentGpuLiquid.cs](Runtime/FluidExperimentGpuLiquid.cs), [Cohesive.cs](Runtime/FluidExperimentGpuLiquid.Cohesive.cs), [FluidExperimentLiquidSettings.cs](Runtime/FluidExperimentLiquidSettings.cs) |
| 경계·소유권·혼합 | [FluidExperimentLiquid.compute](Shaders/FluidExperimentLiquid.compute), [FluidExperimentCohesiveContacts.hlsl](Shaders/FluidExperimentCohesiveContacts.hlsl), [Accounting.cs](Runtime/FluidExperimentGpuLiquid.Accounting.cs) |
| 표면·보간·정렬 | [ImprovedSurface.cs](Runtime/FluidExperimentGpuLiquid.ImprovedSurface.cs), [Rendering.cs](Runtime/FluidExperimentGpuLiquid.Rendering.cs), [RenderingLayers.cs](Runtime/FluidExperimentGpuLiquid.RenderingLayers.cs) |
| 입력·붓기·스푼 | [FluidExperimentInteractor.cs](Runtime/FluidExperimentInteractor.cs), [FluidExperimentBody.Pouring.cs](Runtime/FluidExperimentBody.Pouring.cs), [Stirring.cs](Runtime/FluidExperimentGpuLiquid.Stirring.cs), [SpoonContacts.cs](Runtime/FluidExperimentBody.SpoonContacts.cs) |
| 칵테일·기법 | [FluidExperimentCocktailSession.cs](Runtime/FluidExperimentCocktailSession.cs), [Techniques.cs](Runtime/FluidExperimentCocktailSession.Techniques.cs), [Cocktails.cs](Runtime/FluidExperimentGpuLiquid.Cocktails.cs) |
| 프리팹 제작·조립 | [FluidExperimentRuntimeRig.cs](Runtime/FluidExperimentRuntimeRig.cs), [FluidExperimentAssemblyBuilder.cs](Editor/FluidExperimentAssemblyBuilder.cs), [FluidExperimentDesignerWindow.cs](Editor/FluidExperimentDesignerWindow.cs), [FluidExperimentPrefabAudit.cs](Editor/FluidExperimentPrefabAudit.cs) |

새 물리 기능·표시 개선·성능 변경은 이 정리와 별도 변경으로 다룬다. 현재 조건의 재현 자료를 먼저 확보하고, 바꾸려는 현상과 보존할 양을 정한 뒤 수치·GPU·직접 조작의 근거를 남긴다.
