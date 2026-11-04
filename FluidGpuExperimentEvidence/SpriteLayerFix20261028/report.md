# 스프라이트 겹침·선택 수정 검증

2026-10-28, Unity 6000.3.5f2, Windows / D3D11 / NVIDIA RTX 3070.

**최종 결과: PASS — 9개 검증, 2,363개 항목.** 원본 실험의 F/G 및 독립 Runtime에 개체별 정렬을 적용했다. A–E는 원래 정렬 정책을 유지한다. 원본 프로젝트를 Unity로 열어 저장하지 않고, 동일 소스의 격리 프로젝트에서 실행했다.

## 변경한 동작

| 상황 | 수정 후 동작 |
|---|---|
| 병·잔·도구를 잡음 | 전체 개체가 앞에 오며 놓은 뒤에도 마지막으로 잡은 순서를 유지 |
| 용기의 내부 | 뒤면 → 자기 액체 → 실제 내용물 → 앞면 → 스트레이너·뚜껑 |
| 손에 든 가니시가 잔을 통과 | 실제 소유권이 없으므로 앞에 유지 |
| 놓은 얼음·가니시가 잔에 들어감 | 실제 ContainingVesselId의 액체와 앞면 사이로 함께 정렬 |
| 스푼을 용기에 삽입 | 끝부분이 내부에 있는 동안 내용물 순서, 꺼내면 잡은 개체 순서 |
| 분리한 셰이커 부품 | 독립 정렬, 스트레이너가 들고 있는 캡의 내부 순서 유지 |
| 겹친 개체 클릭·스왑 | 콜라이더 후보 중 SpriteRenderer 표시 순서가 앞인 개체를 우선 |
| 정렬 옵션 해제·A–E 전환 | 저장된 원래 Sorting Layer와 Order로 복원 |

용기별 GPU 소유 ID로 액체 합성 결과를 분리했다. 잔을 앞으로 올려도 다른 잔의 액체까지 앞에 오지 않는다. 소유권 0인 공중 액체는 기본 레이어에 남긴다. 물리 입자·성분 버퍼, Rigidbody 위치·속도는 표시 처리 전후 동일함을 검사했다.

코드는 `FluidExperimentWorld.Rendering.cs`, `FluidExperimentInteractor.VisualPicking.cs`, `FluidExperimentGpuLiquid.RenderingLayers.cs`에 분리했다. 기존 코드에는 등록·Reset·렌더·선택 진입점과 F/G 가니시 정렬 분기만 연결했다. improved accumulation shader의 소유 ID 필터는 렌더링에만 적용한다.

## 실제 렌더 확인

900×700 오프스크린 카메라 출력과 용기별 GPU 합성 텍스처를 읽어 확인했다. 이미지 생성 도구로 만든 그림이 아니다.

[수정 전 정렬 제어군](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerFix20261028/04-rendering/01-bottle-authored-control.png), [잡은 병 수정 후](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerFix20261028/04-rendering/02-bottle-held-fixed.png), [가니시를 들고 잔과 겹침](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerFix20261028/04-rendering/03-held-garnish-fixed.png), [실제 잔 내부의 얼음·가니시](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerFix20261028/04-rendering/04-contained-ice-garnish-fixed.png), [액체가 든 잔을 잡음](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerFix20261028/04-rendering/05-filled-glass-held-fixed.png), [삽입한 스푼과 지거](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerFix20261028/04-rendering/06-tools-own-liquid-fixed.png), [앞으로 가져온 지거 선택](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerFix20261028/04-rendering/07-pick-visible-jigger-fixed.png), [분리한 스트레이너](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerFix20261028/04-rendering/08-detached-strainer-fixed.png).

잡은 병이 잔 앞면에 덮이던 픽셀 변화, 빈 잔의 합성 텍스처가 다른 잔의 액체를 포함하지 않는 것, 공중 액체 레이어가 용기 소유 입자를 포함하지 않는 것을 검사했다. 용기 두 개의 액체 마스크, 출력 해상도 변경, 용기 제거, Reset, 실제 G/D 전환 후 자원 재생성도 확인했다.

지거 테스트는 기존 Fill의 실제 방출량 3.5ml와 해당 액체 픽셀 1,754개를 확인했다. 요청 20ml 전체가 채워졌다는 검증은 아니다. 기존 Fill·충돌 형상·용량 설정은 수정하지 않았다.

## 회귀 검증

| 실행 폴더 | 검증 | 통과 항목 |
|---|---|---:|
| 04-rendering | 겹침·소유 액체·선택·부품·복원·자원 수명 | 39 |
| 05-input | 기존 입력·회전·셰이커 조작 | 1,925 |
| 06-runtime | 독립 Runtime·디스펜서·Reset | 29 |
| 07-auto-f | F 자동 실행 | 29 |
| 08-auto-g | G 자동 실행 | 29 |
| 09-shards | 파편 운동·접촉·액체 보존 | 76 |
| 10-garnish | 가니시 물리·소유권·공급·모드 전환 | 170 |
| 11-cocktails-final | 실제 레시피·등급·조성·제조법·패널 | 61 |
| 12-authoring | 프리팹 참조·반복 생성 보존 | 5 |
| 합계 | | **2,363** |

모든 최종 실행이 exit 0, timeout false다. 기존 가니시 테스트는 수동 물리 스텝 후 표시 갱신을 호출하고, 손에 든 가니시의 표시 기대값 한 곳만 새 요구에 맞게 앞쪽으로 변경했다. 물리 판정식은 그대로 사용했다. 원본 검증 파일을 남기고 이 폴더에 수정 사본을 보관했다.

`01-rendering`은 검증 코드에서 internal ContainingVessel을 직접 참조한 컴파일 실패다. 공개 ContainingVesselId 검사로 수정했다. `02-rendering`, `03-cocktails`의 중간 성공 기록도 남겼으며 위 합계에는 최종 실행만 포함했다. 기존 셰이더 경고와 Unity 종료 시 allocator 진단은 남아 있으므로 로그가 경고 없이 깨끗하다는 주장은 하지 않는다.

## 보존과 증거 범위

변경 전 838개 파일을 SHA-256으로 기록했다. 831개가 동일하고, 허용한 기존 파일 7개만 바뀌었다. 추가 런타임 소스 3개와 각 meta가 생겼다. 기존 프리팹·씬·충돌/액체 설정·compute 물리 소스·칵테일 판정 원본·ProjectSettings는 동일하다. 최종 검증 소스 495개와 각 실행의 harness 스냅샷을 대조했고 불일치와 meta GUID 오류가 없다. 반복 BuildAll도 기존 에셋 바이트를 유지했다.

[기계 판독 검증 결과](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerFix20261028/validation-summary.json), [보존 결과](A:/UnityHub/proj/slainte/FluidGpuExperimentEvidence/SpriteLayerFix20261028/preservation.json). 각 실행 폴더에 명령·소스 해시·harness 해시·Editor.log·검증 내역·종료 코드를 보관했다.

실제 GPU 렌더와 자동 동작 검증까지 완료했다. 사람의 직접 마우스 조작 감각·실제 게임 FPS는 측정하지 않았다. 용기마다 화면 크기의 합성 텍스처와 draw 제출이 추가되므로 용기 수·해상도가 증가하면 렌더 비용도 증가한다. `enableObjectSorting`으로 이전 표시 정책으로 복원할 수 있다.
