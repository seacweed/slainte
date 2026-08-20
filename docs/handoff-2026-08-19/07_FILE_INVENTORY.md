# 기준 커밋 이후 파일 목록

기준: `789b875`. 아래 목록은 이 인수인계 문서를 만들기 직전 상태다.

## 수정된 추적 파일 — 79개

### 손님/주문 데이터 — 43개

- `Assets/Data/CharacterData/CharacterDatabase.asset`
- `Assets/Data/CustomerImport/DraftVisits/CustomerVisit_d1001.asset` ~ `CustomerVisit_d1039.asset` — 39개
- `Assets/Data/CustomerOrder/CustomerOrderDatabase.asset`
- `Assets/Resources/CustomerVisit/CustomerVisitDatabase.asset`
- `Assets/Resources/CustomerVisit/Data/CustomerVisit_yukari_sample.asset`

### Editor/Validator — 6개

- `Assets/Editor/BartendingInteractionValidator.cs`
- `Assets/Editor/BusinessCustomerPoolStressTestTools.cs`
- `Assets/Editor/BusinessShiftValidator.cs`
- `Assets/Editor/CustomerPlanningCsvImporter.cs`
- `Assets/Editor/DeliverySystemValidator.cs`
- `Assets/Editor/TVSystemValidator.cs`

### 액체/메타볼 — 4개

- `Assets/MetaballFluid/Graphics/MetaballMat.mat`
- `Assets/MetaballFluid/Prefabs/water_particle.prefab`
- `Assets/MetaballFluid/Scripts/LiquidReaction.cs`
- `Assets/MetaballFluid/Scripts/ObjPooling.cs`

### Prefab/설정/씬 — 7개

- `Assets/Prefabs/Beaker.prefab`
- `Assets/Prefabs/Bottle.prefab`
- `Assets/Prefabs/Glass.prefab`
- `Assets/Prefabs/OrangeJuiceBottle.prefab`
- `Assets/Resources/Bartending/BusinessBartendingSettings.asset`
- `Assets/Scenes/Sample_Scene.unity`
- `Assets/TextMesh Pro/Fonts/NotoSansKR-Regular SDF.asset`

### Bartending 스크립트 — 12개

- `Assets/Scripts/Bartending/BartendingSandboxBootstrap.cs`
- `Assets/Scripts/Bartending/BartendingSessionBuilder.cs`
- `Assets/Scripts/Bartending/BeakerController.cs`
- `Assets/Scripts/Bartending/BottleController.cs`
- `Assets/Scripts/Bartending/BusinessBartendingSettings.cs`
- `Assets/Scripts/Bartending/CocktailEvaluator.cs`
- `Assets/Scripts/Bartending/CocktailOrderGenerator.cs`
- `Assets/Scripts/Bartending/GlassController.cs`
- `Assets/Scripts/Bartending/IceCubeController.cs`
- `Assets/Scripts/Bartending/LiquidParticleData.cs`
- `Assets/Scripts/Bartending/VesselLiquidTracker.cs`
- `Assets/Scripts/LiquorShelf/LiquorShelfUI.cs`

### Business/Conversation 스크립트 — 7개

- `Assets/Scripts/Business/BusinessCustomerPoolStressBootstrap.cs`
- `Assets/Scripts/Business/BusinessIntegrationPlaytestBootstrap.cs`
- `Assets/Scripts/Business/BusinessOrderSessionController.cs`
- `Assets/Scripts/Business/BusinessSequencePlanner.cs`
- `Assets/Scripts/Business/BusinessShiftController.cs`
- `Assets/Scripts/Conversation/Sell/CustomerSpawner.cs`
- `Assets/Scripts/Conversation/Sell/CustomerVisitData.cs`

합계: 43 + 6 + 4 + 7 + 12 + 7 = 79개.

## 미추적 파일 — 31개

### Serving Area — 2개

- `Assets/Art/Bartending/serving_area.png`
- `Assets/Art/Bartending/serving_area.png.meta`

### 신규 주문 에셋 — 16개

- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1001_rec_1014.asset`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1001_rec_1014.asset.meta`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1008_rec_1012.asset`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1008_rec_1012.asset.meta`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1009_rec_1012.asset`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1009_rec_1012.asset.meta`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1011_rec_1012.asset`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1011_rec_1012.asset.meta`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1014_rec_1012.asset`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1014_rec_1012.asset.meta`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1039_rec_1006.asset`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1039_rec_1006.asset.meta`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1040_rec_1007.asset`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1040_rec_1007.asset.meta`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1041_rec_1007.asset`
- `Assets/Data/CustomerImport/DraftOrders/CustomerOrder_d1041_rec_1007.asset.meta`

### 신규 방문 에셋 — 4개

- `Assets/Data/CustomerImport/DraftVisits/CustomerVisit_d1040.asset`
- `Assets/Data/CustomerImport/DraftVisits/CustomerVisit_d1040.asset.meta`
- `Assets/Data/CustomerImport/DraftVisits/CustomerVisit_d1041.asset`
- `Assets/Data/CustomerImport/DraftVisits/CustomerVisit_d1041.asset.meta`

### 신규 메타볼 렌더링 — 8개

- `Assets/MetaballFluid/Graphics/LiquidMetaballAccumulation.mat`
- `Assets/MetaballFluid/Graphics/LiquidMetaballAccumulation.mat.meta`
- `Assets/MetaballFluid/Graphics/LiquidMetaballAccumulation.shader`
- `Assets/MetaballFluid/Graphics/LiquidMetaballAccumulation.shader.meta`
- `Assets/MetaballFluid/Graphics/LiquidMetaballComposite.shader`
- `Assets/MetaballFluid/Graphics/LiquidMetaballComposite.shader.meta`
- `Assets/MetaballFluid/Scripts/LiquidMetaballRenderer.cs`
- `Assets/MetaballFluid/Scripts/LiquidMetaballRenderer.cs.meta`

### 기존 추가 문서 — 1개

- `docs/customer-availability-missing-data.md`

합계: 2 + 16 + 4 + 8 + 1 = 31개.

## 이번 요청으로 추가된 인수인계 문서

- `docs/handoff-2026-08-19/README.md`
- `docs/handoff-2026-08-19/01_WORKTREE_OVERVIEW.md`
- `docs/handoff-2026-08-19/02_LIQUID_RENDERING_AND_PERFORMANCE.md`
- `docs/handoff-2026-08-19/03_BARTENDING_UI_AND_INTERACTIONS.md`
- `docs/handoff-2026-08-19/04_CUSTOMER_BUSINESS_DELIVERY.md`
- `docs/handoff-2026-08-19/05_DATA_IMPORT_NOTES.md`
- `docs/handoff-2026-08-19/06_NEXT_DEVICE_CHECKLIST.md`
- `docs/handoff-2026-08-19/07_FILE_INVENTORY.md`

## 줄바꿈 경고

Git이 다수 파일에서 `LF will be replaced by CRLF`를 경고한다. 다른 기기에서 `.gitattributes` 또는 Git autocrlf 설정 차이로 전체 파일이 수정된 것처럼 보일 수 있으므로, 의미 없는 줄바꿈 정규화를 피할 것.

