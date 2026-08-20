# 다른 기기에서 작업 재개 체크리스트

## 이동 전 현재 기기에서 할 일

현재 변경에는 미추적 파일이 많다. 일반적인 `git diff` 파일만 복사하면 신규 셰이더, 재질, 주문 에셋 및 `.meta`가 빠진다.

권장 방법:

1. Unity Play Mode를 종료한다.
2. Unity가 import와 scene save를 끝냈는지 확인한다.
3. `git status --short`로 의도한 파일을 확인한다.
4. 작업을 보존할 임시 브랜치에 모든 필요한 파일과 `.meta`, 이 문서를 커밋한다.
5. 원격 저장소로 push한 뒤 다른 기기에서 해당 브랜치를 checkout한다.
6. 저장소 밖 CSV 3개를 별도로 복사한다.

주의:

- `git stash`는 원격으로 자동 전달되지 않는다.
- 미추적 파일을 포함하지 않은 stash에는 신규 셰이더와 `.meta`가 들어가지 않는다.
- 전체 프로젝트 폴더를 복사한다면 `Library`, `Temp`, `Logs`는 제외해도 되지만 `Assets`, `Packages`, `ProjectSettings`, `.git`과 외부 CSV는 보존해야 한다.
- 이 문서를 만들면서 소스 변경을 커밋하지 않았다.

## 다음 기기의 첫 실행

1. Unity Hub에서 기존 프로젝트 Unity 버전 `6000.3.5f2`로 연다.
2. 전체 import가 끝날 때까지 기다린다.
3. Console의 shader/C# 오류를 먼저 확인한다.
4. `Sample_Scene`을 닫았다가 다시 열어 외부 YAML 변경이 로드되게 한다.
5. `dotnet build Assembly-CSharp.csproj --no-restore`를 실행한다.
6. Inspector에서 WaterCam/MetaballQuad 구성을 확인한다.

## 우선순위 1 — 메타볼 렌더링

1. WaterCam에 `LiquidMetaballRenderer`가 있는지 확인.
2. MetaballQuad 재질 shader가 `Slainte/LiquidMetaballComposite`인지 확인.
3. 기존 WaterScreen MeshRenderer가 비활성인지 확인.
4. Main Camera가 Water 레이어를 직접 그리지 않는지 확인.
5. Alpha 1의 고립된 입자 하나를 띄워 중심/가장자리 색을 비교.
6. Alpha 0.2 고립 입자로 반복.
7. 같은 RGBA 두 입자를 붙여 연결부가 더 진해지는지 확인.
8. 문제가 남으면 Density/Color/ratio debug view를 추가한 뒤 원인을 확정.
9. 원인을 확인하기 전 threshold나 기본 sprite를 바꾸지 않는다.

## 우선순위 2 — 액체 성능

Profiler에서 같은 조건으로 확인:

- 첫 액체 상호작용 순간
- Pour 중
- 서로 다른 액체 혼합 순간
- 다수 입자 동시 활성화
- GC.Alloc
- `LiquidReaction.FixedUpdate`, 충돌 callback, `LiquidPayload.MixPair`
- draw call과 `Liquid Metaball Accumulation` command buffer

Pool 고갈 여부가 확인될 때만 prewarm/pool size를 조정한다.

## 우선순위 3 — UI와 제출 색

1. 계량 UI에 재료명과 ml가 노란색 NotoSansKR로 표시되는지 확인.
2. UI 갱신 시 매 프레임 문자열 GC가 증가하지 않는지 확인.
3. 제출 결과가 `#RRGGBBAA`를 먼저 표시하는지 확인.
4. Alpha가 ItemDef 원본과 composition volume 평균을 따르는지 확인.

## 우선순위 4 — 병렬 작업 회귀

1. 8개 슬롯 생성과 배치.
2. Glass/Beaker 즉시 슬롯 이동 시 액체와 얼음 동반 이동.
3. Bottle Pour ml/s와 프레임 catch-up.
4. Serving Area 표시/fade.
5. 최근 손님 2명 제한과 후보 부족 시 spawn 중단.
6. invalid order 방문 제외 및 기술 실패 메시지.
7. 배송 성공/실패/연속 구매 캐릭터 연출.

## 데이터 import 재개

- [05_DATA_IMPORT_NOTES.md](05_DATA_IMPORT_NOTES.md)를 먼저 읽는다.
- 외부 CSV를 확보하기 전 importer나 Planning 에셋을 추측으로 갱신하지 않는다.
- generated Planning 에셋 삭제는 새 import 결과 확인 후에만 한다.

## 완료 전 체크

- Unity Console 오류 0
- C# build 오류 0
- 셰이더 분홍색 fallback 없음
- 단일/동일/상이 입자 렌더 테스트 통과
- Pour/혼합 Profiler 비교 기록
- 계량 UI와 제출 HEX 확인
- 병렬 작업 시스템 회귀 확인
- 모든 신규 Unity 에셋의 `.meta` 포함

