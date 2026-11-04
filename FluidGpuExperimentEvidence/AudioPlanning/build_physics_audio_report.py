"""Build the source-backed audio plan; does not edit or run the Unity project."""
from pathlib import Path
import csv
import hashlib
import json
import re
import subprocess
from collections import Counter

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent
FEATURE = ROOT / 'Assets/_Project/Features/Bartending/FluidGpuExperiment'
DATE = '20261021'

# These are evidence locations, not implemented audio hooks.
SOURCES = {
    'hold': ('Runtime/FluidExperimentBody.cs', 'public void SetHeld(bool value)'),
    'input': ('Runtime/FluidExperimentInteractor.cs', 'public bool Drop(Vector2 pointer)'),
    'contacts': ('Runtime/FluidExperimentWorld.cs', 'public void RefreshCollisionPairs()'),
    'pour': ('Runtime/FluidExperimentBody.Pouring.cs', 'private void EmitBottle(float dt)'),
    'reservoir': ('Runtime/FluidExperimentBody.Reservoir.cs', 'private bool EmitReservoirParticle('),
    'snapshot': ('Runtime/FluidExperimentGpuLiquid.cs', 'private void RequestReadback()'),
    'modes': ('Runtime/FluidExperimentGpuLiquid.cs', 'private bool ParticipatesInLiquidCollision('),
    'float': ('Runtime/FluidExperimentBody.GarnishMotion.cs', 'internal void StepGarnishMotion('),
    'flow': ('Runtime/FluidExperimentGpuLiquid.IceFlow.cs', 'private void BuildIceFlowComponents()'),
    'stock': ('Runtime/FluidExperimentBody.cs', 'internal void Emit(float dt)'),
    'garnish': ('Runtime/FluidExperimentGarnishSource.cs', 'public bool TryDispense('),
    'shaker': ('Runtime/FluidExperimentBody.Shaker.cs', 'internal void RefreshShakerClosure()'),
    'part': ('Runtime/FluidExperimentShakerPart.cs', 'public bool TryAttach()'),
    'dispenser': ('Runtime/FluidExperimentDispenser.cs', 'public bool TryClickMode('),
    'emit': ('Runtime/FluidExperimentDispenser.Emission.cs', 'public void Tick(float dt)'),
    'retract': ('Runtime/FluidExperimentDispenser.Motion.cs', 'public void StepMotion(float dt)'),
    'break': ('Runtime/FluidExperimentBreakableGlass.cs', 'internal void CompleteBreak()'),
    'shard': ('Runtime/FluidExperimentWorld.Breakage.cs', 'internal void RefreshGlassShardContacts()'),
    'shardwater': ('Runtime/FluidExperimentGpuLiquid.Shards.cs', 'private void UploadShardGeometry('),
    'splash': ('Runtime/FluidExperimentGpuLiquid.Splash.cs', 'public void ReleaseWithSplash('),
    'environment': ('Runtime/FluidExperimentWorld.Environment.cs', 'private void ResolveEnvironment()'),
    'account': ('Runtime/FluidExperimentGpuLiquid.Accounting.cs', 'public bool TryTransferContents('),
    'effects': ('Runtime/FluidExperimentEffects.cs', 'private void UpdateAudio(float dt)'),
    'audiogate': ('Runtime/FluidExperimentEffects.cs', 'private bool IsActive =>'),
    'configuration': ('Runtime/FluidExperimentRuntimeRig.cs', 'public void ApplyConfiguration()'),
    'runtimeprefab': ('Prefabs/Runtime/FluidExperimentRuntime.prefab', '  enableAudio: 0'),
    'material': ('Runtime/FluidExperimentMaterials.cs', 'public static FluidExperimentMaterialPreset Resolve('),
}
source_refs = {}
for key, (relative, needle) in SOURCES.items():
    p = FEATURE / relative
    lines = p.read_text(encoding='utf-8-sig').splitlines()
    matches = [i for i, line in enumerate(lines, 1) if needle in line]
    assert matches, (relative, needle)
    source_refs[key] = (p, matches[0])

# ID | situation | sound | start | update/end | drivers | asset family | status
# M: physical event exists, audio hookup missing; A: optional authored foley;
# C: actual contact/feature gate required; N: deliberately silent; F: future feature.
DATA = r'''
H01|단단한 물건 집기|손과 표면의 짧고 작은 잡는 소리|Pick/SetHeld가 실제 false→true 전이|1회; 이미 잡힌 물건 재호출은 무음|물건 재질·크기·접근 속도|hand_rigid|A
H02|가니쉬 집기|얇은 껍질이 손에 닿는 바스락|공급원 생성 및 Pick 성공 또는 최초 직접 Pick 성공|1회; 생성 실패 시 취소|가니쉬 종류·건조/젖음|peel_pick|A
H03|손에서 놓기|작은 손 마찰만 선택 사용|실제 Release/FinishDrop 성공|1회; 바닥에 닿는 소리는 아직 내지 않음|재질·잡기 상태|hand_release|A
H04|빠르게 휘두르거나 던짐|아주 짧은 공기 가름|실제 고체의 속도가 고속 문턱을 넘음|속도가 내려가면 종료; 일반 이동은 무음|물건 면적·속도·각속도|object_whoosh|A
H05|겹친 대상과 집기 교대|기존 놓기/집기 폴리의 조합|Drop 성공 후 다음 Pick도 성공|각 전이에만 1회; 별도 순간이동 효과음 없음|두 대상 재질|hand_swap|A
H06|안전한 놓기 실패|선택적인 작은 조작 거절음|TryResolveRelease=false; 사용자가 놓기를 시도함|실패 전이/클릭 1회; 프레임 반복 금지|실패 종류|interaction_denied|A
C01|고체를 가볍게 내려놓음|접촉 재질에 맞는 톡/짤랑/둔탁음|유효한 최초 접촉과 충분한 유입 충격|짧은 잔향; 손 놓기와 시간 분리|법선 속도·충격량·재질·질량·충전율|impact_light|M
C02|고체 낙하/강한 부딪힘|같은 재질의 강한 충격과 공명|실제 강한 접촉; 해당 잔이 파손되면 파손음에 양보|1회 후 잔향|입사 충격·접촉점·크기·충전율|impact_heavy|M
C03|잔/병끼리 충돌|유리의 맑거나 두꺼운 충돌음|유리-유리 실제 접촉|가벼움/강함을 교차 혼합; C01/02와 대체 관계|잔형·병형·젖음·충전율|glass_glass|M
C04|유리와 금속 충돌|유리 공명과 금속 어택|실제 유리-금속 접촉|한 접촉당 한 합성 이벤트|두 재질·충격·속 빈 부피|glass_metal|M
C05|금속끼리 충돌|딸깍/챙/짧은 금속 울림|지거·쉐이커·스푼·부품의 유효 접촉|큰 통 울림/작은 부품 어택 분리|금속 형상·충격·충전율|metal_metal|M
C06|얼음과 잔/병 충돌|차갑고 짧은 얼음-유리 딸깍|실제 얼음-유리 접촉|동일 쌍 합치기; 두 얼음 모드 공통|충격·잠김·잔 공명·얼음 수|ice_glass|M
C07|얼음과 금속 도구/통 충돌|짧은 얼음 충격과 금속 통 울림|실제 얼음-금속 접촉|쉐이크 군집음과 동일 접촉 중복 금지|잠김·충격·통 충전율|ice_metal|M
C08|얼음끼리 충돌|각진 작고 단단한 딸깍|실제 얼음-얼음 접촉|쌓인 상태의 중력 접촉은 반복하지 않음|접근 속도·충격·잠김|ice_ice|M
C09|가니쉬와 단단한 벽/도구 접촉|매우 작은 마른 톡 또는 젖은 툭|실제 가니쉬-고체 접촉|지각 문턱 아래 무음; 유리 링 금지|두께·젖음·접촉 속도|peel_hard|C
C10|가니쉬와 얼음 접촉|작은 젖은 툭|실제 가니쉬-얼음 접촉|얼음-유리 같은 날카로운 울림 금지|젖음·법선 속도|peel_ice|C
C11|가니쉬끼리 접촉|거의 들리지 않는 부드러운 마찰|실제 빠른 접촉만|평상시 무음 허용; 대량 배치 증폭 금지|속도·두께·젖음|peel_peel|A
C12|디스펜서 외함 충돌|외함과 상대 재질의 짧은 둔탁음|수납/복귀 중 충돌이 켜진 상태의 실제 접촉|잡힌 동안 trigger 겹침은 무음|상대 재질·충격·CollisionSuppressed|casing_impact|C
C13|고체가 표면을 끌림|재질별 긁힘/미끄러짐 루프|접촉 유지+접선 이동이 문턱 이상|접촉/속도 소실 시 짧게 페이드|접선 속도·법선 하중·젖음|solid_scrape|M
C14|고체가 구르거나 흔들리며 닿음|드문 틱과 짧은 구름 질감|실제 접촉점 이동/접촉 펄스|잠들거나 정지하면 종료; 각속도만으로 재생 금지|반지름·각속도·접촉 하중|solid_roll|C
C15|첫 충돌 뒤 재튐|점점 약해지는 동일 재질 충격|실제 분리 후 새 충돌|원래 충돌 cooldown을 통과한 재접촉만|각 재충돌의 실제 충격|impact_bounce|M
C16|입구/림에 걸려 부딪힘|림 위치의 가벼운 재질 어택|림/뚜껑/스트레이너에 실제 접촉|C03~C10 중 해당 재질군으로 라우팅; 추가 레이어 금지|접촉 위치·림 공명·충격|rim_contact|C
L01|병에서 흐르기 시작|작은 액체 출발 어택|실제로 수락된 액체 배출량이 0→양수|출발 1회 후 L02로 연결|수락 ml/s·입구 폭·점성|liquid_source_start|M
L02|병의 연속 붓기|가늘거나 두꺼운 흐름 루프|실제 연속 배출과 이동하는 줄기 존재|실제 유량 추종; 중단 시 L05로 연결|유량·점성·출구 속도·입구 폭|liquid_source_loop|M
L03|병목의 간헐적 꿀렁임|간헐적인 공기/액체 글러그|병 유량의 실제 맥동에 맞춘 연출|독립 공기역학이 있다는 의미는 아님; 빠른 연타 제한|유량 맥동·잔량·병목 크기|bottle_glug|A
L04|적은 유량/점적 상태|가느다란 졸졸 또는 떨어지는 방울 출발|실제 유량이 지속 흐름 문턱 아래|연속음과 교차 전환; 무배출 상태 무음|실제 수락량·점성|liquid_trickle|M
L05|붓기 중단|짧아지는 흐름 꼬리|유량이 실제로 끝남|즉시 절단하지 않고 짧은 페이드; 이미 떠난 액체의 도착음은 계속|직전 유량·남은 줄기 길이|liquid_source_stop|M
L06|병입구 잔여 방울|작은 방울 분리음과 추후 도착음|실제 마지막 birth/neck 잔여가 배출됨|출발/도착 별도 위치·시간; 없는 방울을 합성하지 않음|방울 ml·목 잔량·점성|liquid_tail_drip|C
L07|병 내용물이 완전히 소진|흐름 종료만 또는 마지막 꿀렁임|remainingMl이 실제 배출로 0 도달|빈 병을 기울인 채 유지하면 무음|직전 유량·잔량|bottle_empty_tail|C
L08|빈 유리잔 안으로 액체 도착|유리 바닥의 밝은 또르륵/촤르륵|액체가 비어 있던 잔 바닥/내벽에 실제 도착|도착 지점 루프; 채워지면 L10으로 혼합|도착 ml/s·낙차·법선 속도·잔형|liquid_receive_glass|M
L09|빈 금속 지거/쉐이커/통에 도착|가벼운 금속 통 울림을 동반한 물소리|빈 금속 용기 내부에 실제 도착|채워지면 풀 도착음으로 전환|유입량·통 형상·충전율|liquid_receive_metal|M
L10|이미 있는 액체 위에 붓기|부드러운 촤르륵/꼴꼴/작은 튐|기존 풀의 표면에 실제 액체 유입|유입이 멈추면 짧은 잔향; 정지 수면은 무음|유입량·표면 상대 속도·수심·혼합 점성|liquid_receive_pool|M
L11|충돌 모드 얼음 위로 액체 도착|작은 갈라진 물줄기와 젖은 얼음 질감|Colliding에서 실제 얼음 경계와 액체 접촉|L10과 지점별 합산; 과도한 폭발성 물보라 금지|유입량·상대 운동·노출된 얼음 면적|liquid_receive_ice|C
L12|비충돌 얼음 위를 액체가 지남|기본 풀 도착음; 선택적으로 아주 약한 젖음|NonColliding이며 동일 위치에 물과 얼음이 공존|얼음이 액체를 막는 충돌/분출 소리는 내지 않음|실제 풀 유입·잠김 변화|liquid_receive_pool|C
L13|가니쉬 주변으로 액체 도착|기본 풀 도착음; 얇은 젖음만 선택|가니쉬는 액체를 밀지 않는 현재 모델|큰 독립 물보라/차단음 금지|실제 액체 유입·가니쉬 잠김|peel_wet|C
L14|병/도구 외벽을 적심|얇은 외벽 물막/흘러내림|액체가 외측 고체에 실제 충돌·흐름|물막이 없거나 흐름이 멈추면 종료|유입량·벽 접선 속도·재질|liquid_wet_surface|M
L15|테이블/바닥에 액체를 쏟음|넓게 퍼지는 찰박/젖은 착지|실제 환경 표면에 액체 도착|흐름 지속 시 작은 퍼짐 루프|도착량·속도·표면 재질·기존 젖음|liquid_spill_surface|M
L16|액체가 옆 벽/천장에 부딪힘|짧고 납작한 물 튐|GPU 액체의 실제 환경 경계 충돌|충돌 패치별 합산; 경계 복구만으로 발음 금지|법선 유입량·표면 재질|liquid_wall_hit|M
L17|용기 입구 넘침/쏟아짐|림을 타는 물소리와 도착면의 튐|실제 입구를 가로질러 밖으로 나오는 액체|출구음과 이후 바닥/다른 잔 도착음을 분리|실제 유출량·림 폭·점성|liquid_overflow|M
L18|독립 방울이 풀에 도착|크기에 맞는 작은 퐁/톡|분리된 액체 방울의 실제 수면 도착|입자 1개당 재생 금지; 한 방울/패치로 집계|방울 부피·상대 속도·수심|drop_pool|M
L19|독립 방울이 마른 고체에 도착|작은 탁/찍/찰박|실제 건조 표면 도착|젖은 면이면 더 둔하고 작은 변형|방울 ml·표면 재질·젖음|drop_solid|M
L20|움직인 잔 안의 액체가 한쪽 벽에 부딪힘|방향감 있는 출렁임 어택|풀의 벌크 이동이 벽에 실제 도달|벽 도착 펄스별 1회; 잔 이동 입력만으로 발음 금지|상대 유동·액체량·벽 충격·충전율|liquid_slosh_hit|M
L21|연속 흔들기/기울이기 중 출렁임|낮고 연속적인 액체 이동 질감|실제 유체 운동 에너지가 문턱 이상|잔을 놓은 뒤에도 기존 운동량에 따라 지속|실제 이동 기반 에너지·충전율·용기 재질|liquid_slosh_loop|M
L22|잔을 멈추거나 놓은 뒤의 감쇠|약해지는 출렁임과 마지막 벽 닿음|기존 액체 운동이 계속되는 동안|Release 순간 리셋/재시작 금지; 에너지 소실 후 페이드|기존 루프 위상·유동·감쇠|liquid_slosh_tail|M
L23|바 스푼으로 젓기|작은 휘젓는 물소리|잠긴 스푼과 액체의 실제 상대 이동|스푼이 멈추거나 물 밖이면 정지; 잔 벽 딸깍은 C04|잠김·상대 속도·점성·수심|liquid_stir|M
L24|시럽 붓기/젓기/방울|두껍고 둔한 점성 질감|실제 시럽 프리셋/혼합 점성이 높은 액체의 위 이벤트|기본 이벤트의 변형; 추가 중복 루프 아님|점성·유량·끊김·혼합 비율|liquid_viscous|M
L25|Milk 프리셋의 액체|부드럽고 덜 날카로운 액체 질감|명시적으로 Milk가 선택된 실제 액체 동작|우유 전용 거품 물리는 가정하지 않음|점성·유량·혼합 비율|liquid_milk|C
L26|혼합액 소리 전환|기존 액체 레이어 사이 완만한 혼합|완료된 성분/프리셋 비율이 변하고 실제 운동 중|재료 색/성분 변화만으로 새 효과음을 내지 않음|ml 가중 점성·성분 비율|liquid_material_blend|M
L27|액체가 차며 용기 공명이 변함|빈 통 울림이 줄고 도착음이 부드러워짐|실제 유입/충돌 중 충전율 변화|기존 도착음/EQ의 연속 파라미터; 별도 딩 금지|완료된 ml·용량·잔 형상|vessel_fill_resonance|M
L28|테이블 위 퍼짐과 가장자리 흘러내림|얇은 젖은 흐름; 가장자리 점적|Table Holds Liquid=true의 실제 흐름|유한 테이블 끝에서 떨어지면 실제 다음 접촉만 발음|표면 흐름·경사·낙차·표면 종류|liquid_runoff|C
L29|스푼/용기/도구가 액체 표면에 진입|크기에 맞는 작은 물 가름/찰박|GPU에 실제 접촉 형상이 업로드된 고체가 풀에 진입|얼음은 I03/04 정책 우선; 가니쉬와 비업로드 자유 부품은 반작용 음 제외|실제 상대 진입 속도·젖는 면적·풀의 물량|solid_water_entry|C
L30|잠긴 스푼/도구를 액체 밖으로 뺌|작은 물 분리/젖음 꼬리|실제 고체-액체 접촉이 표면을 지나 끝남|표면 근처 샘플 떨림으로 연타 금지; 실제 없는 부착 물방울음은 만들지 않음|실제 이동·접촉 면적·점성|solid_water_exit|C
L31|물에 닿은 도구에서 실제 방울이 분리/낙하|관찰되는 작은 방울과 도착음|도구 주변에 실제 존재하던 액체 덩어리가 분리돼 이동/착지|현재 묻음 필름을 따로 보존하는 모델은 가정하지 않음; L18/19와 통합|실제 방울 ml·분리 지점·도착 표면|liquid_tail_drip|C
I01|얼음통을 흔듦/기울임|통 안의 짤그락 질감|아직 재고만 있는 통의 실제 가속/회전|재고 수와 동작에 따른 연출; 내부 개별 충돌을 측정한 것 아님|iceStock·통 가속·각속도|ice_bucket_stock|A
I02|얼음통에서 얼음이 나옴|입구의 짧은 얼음/통 덜컥|Instantiate 성공 및 iceStock 실제 차감|얼음 1개당 1회; 이후 착지는 C06~08|재고·배출 속도·통 재질|ice_chute|M
I03|충돌 얼음의 물 진입|작고 무게감 있는 퐁/찰박|Colliding; 건조→잠김 전이와 실제 상대 진입|표면 진입 1회; 잠김 떨림으로 재트리거 금지|진입 속도·잠긴 면적 변화·주변 물량|ice_water_entry|C
I04|비충돌 얼음의 물 진입|아주 작은 젖음/둔한 퐁 선택|NonColliding; 실제 잠김 전이|큰 분출음/액체 반작용은 암시하지 않음|잠김 변화·진입 속도|ice_water_soft|A
I05|젖은 얼음이 수면을 가르며 이동|약한 물 마찰|Colliding에서 표면과 유효 상대 이동|잠김·속도가 낮아지면 종료; NoCollision은 기본 무음|상대 속도·노출면·물량|ice_water_rub|C
I06|잠긴 얼음끼리/벽과 충돌|고역이 약해진 실제 얼음 딸깍|유효 고체 충돌과 높은 잠김|C06~08의 물속 변형; 추가 물속 루프 금지|잠김·충격·수심|ice_underwater_contact|M
I07|다수 얼음이 움직이며 한꺼번에 충돌|짧은 군집 짤그락|동일 용기에서 실제 접촉 이벤트가 밀집|군집 1개 또는 대표 접촉 선택; 개수만큼 음량 합산 금지|유효 충격 합·활성 얼음 수·잠김|ice_cluster|M
I08|얼음이 멈추며 마지막으로 자리잡음|마지막 작은 딸깍만|실제 마지막 새 접촉/미끄러짐|정지 후 잔류 노이즈 금지; 정지 타이머 자체 무음|접촉 펄스·잔여 속도|ice_settle|C
I09|스트레이너가 얼음을 걸러냄|얼음-금속의 짧은 짤그락|실제 얼음이 스트레이너/닫힌 입구에 닿음|통과하지 못한 매 틱마다 재생 금지|충격·통 기울기·잠김|ice_strainer|M
I10|얼음이 열린 입구로 빠져나감|필요 시 짧은 림 접촉만|실제 입구 통과 및 림 접촉이 있음|공중 낙하 자체 무음; 도착 접촉은 별도|림 접촉·배출 속도·젖음|ice_exit|C
P01|무한 가니쉬 공급원에서 꺼냄|가벼운 껍질 바스락|TryDispense와 Pick이 모두 성공|H02와 같은 이벤트로 합치기; 항아리 덜컥은 실제 접촉 때만|두 가니쉬 종류·선택 위치|peel_pick|A
P02|마른 가니쉬가 잔/도구에 놓임|작은 종이 같은 톡|실제 고체 착지|C09와 대체 관계; 손 놓기 시점에 선재생 금지|두께·법선 속도·상대 재질|peel_dry_touch|C
P03|가니쉬가 액체에 진입|작고 부드러운 젖음/톡|신뢰할 수 있는 잠김 상승과 실제 진입 운동|반작용 없는 모델이므로 큰 물보라 금지|잠김·상대 속도·면적|peel_wet|A
P04|젖은 가니쉬와 잔벽/얼음 접촉|매우 작은 젖은 툭|실제 고체 접촉|C09/10의 젖은 변형; 중복 금지|젖음·충격·상대 재질|peel_wet_touch|C
P05|가니쉬가 수면을 따라 미끄러짐|대체로 무음; 가까운 미세 물 마찰만 선택|실제 빠른 상대 이동이 있을 때|천천히 떠 있거나 흐름과 함께 이동하면 무음|상대 속도·잠김|peel_water_rub|A
P06|가니쉬가 액체 밖으로 나옴|선택적인 약한 젖음 꼬리|실제 잠김 감소·표면 통과|현재 가니쉬에 묻은 물/방울이 따로 생성되지 않으면 방울 착지음 금지|잠김·표면 상대 속도|peel_water_exit|A
P07|가니쉬가 열린 잔 밖으로 빠짐|실제 림 접촉음만|물리적 입구 통과·접촉 확인|한 프레임 표시/보정 위치 변화는 무음|림 접촉·젖음|peel_rim|C
S01|스트레이너를 본체에서 분리|짧은 금속 마찰과 빠지는 소리|IsAttached true→false의 사용자 분리|1회; 초기화/리셋 Detach는 무음|부품 종류·부착 상태|shaker_strainer_off|M
S02|작은 캡을 스트레이너에서 분리|작고 높은 금속 팝/마찰|캡의 실제 사용자 분리|1회; 스트레이너에 붙어 같이 움직이는 캡은 추가 분리음 없음|캡 질량·상태|shaker_cap_off|M
S03|스트레이너를 본체에 장착|낮은 금속 안착과 짧은 끼움|TryAttach 성공; 분리→장착 전이|1회; 근처에 놓았으나 부착 실패하면 재생하지 않음|부착 상대 속도·형상|shaker_strainer_on|M
S04|작은 캡 장착/밀봉|작고 단단한 금속 닫힘|실제 캡 Attach 성공|1회; Closed 상태 매 갱신 무음|상대 속도·부착 상태|shaker_cap_on|M
S05|분리한 부품이 바닥/도구에 낙하|작은 금속판/캡의 얇은 튕김|자유 부품의 실제 충돌|C05/15로 재질 라우팅; 장착음과 분리|법선 속도·충격·Role|metal_part_impact|M
S06|닫힌 쉐이커의 젖은 쉐이크|금속통 안 액체 출렁임+얼음 군집|Closed이며 실제 상대 운동과 내용물 존재|동작 종료 뒤 내부 관성만큼 잔향; 빈 통은 액체음 없음|액체량·얼음 수·잠김·상대 운동|shaker_wet|M
S07|액체 없는 얼음 쉐이크|건조한 얼음-금속 짤그락|Closed/Open 관계와 무관한 실제 얼음-통 충돌|액체 레이어 없음; 얼음도 없으면 큰 내부 소리 없음|얼음 충격·재고/실물 구분|shaker_dry|M
S08|스트레이너를 통한 붓기|좁은 출구의 가는 흐름|Straining에서 실제 액체가 출구 통과|붓기 출발/수신음과 연결; 얼음은 별도 걸림 소리|출구 실제 유량·점성·기울기|shaker_strain|M
S09|스트레이너 붓기 종료/마지막 방울|가늘어지는 흐름과 실제 방울|유출이 줄고 실제 줄기/방울 남음|관찰된 방울만; 일정 시간 예약으로 가짜 방울 생성 금지|유출량·줄기 길이|shaker_strain_tail|C
S10|Open 쉐이커 붓기/넘침|넓은 입구의 일반 붓기|열린 실제 입구로 액체 이동|L17/L08~10으로 라우팅; 밀봉된 상태는 재생 금지|입구 폭·실제 유출량|shaker_open_pour|M
S11|단축키로 Open/Straining/Closed 변경|실제로 변한 부품의 탈착 소리|CycleShakerClosure의 전/후 상태 차이|초기 Attach와 중간 Refresh 이벤트 중복 제거; S01~04만 선택|이전/새 Closure·변한 부품 집합|shaker_state_change|M
D01|Ice 버튼 선택|짧은 실제 버튼 딸깍|Docked에서 None/Hot→Ice|클릭 성공 1회; 동작 중 무시된 버튼 입력 무음|버튼 종류·상태|dispenser_button|M
D02|Hot 버튼 선택|같은 버튼군의 약간 다른 딸깍|Docked에서 None/Ice→Hot|클릭 성공 1회; 물 흐름은 아직 시작하지 않음|버튼 종류·상태|dispenser_button|M
D03|같은 버튼으로 모드 해제|짧은 해제 딸깍|Docked에서 Ice/Hot→None|클릭 성공 1회; 별도 물리 모드 변경음 아님|이전 모드|dispenser_button_off|M
D04|수납된 디스펜서 집기|손잡이 폴리와 선택적인 수납 해제|Docked→Held 성공|1회; held 중 반복 Pick 시도 무음|상태·손잡이 재질|dispenser_pick|A
D05|Hot 배출 시작|짧은 밸브 개방과 실제 흐름 어택|첫 실제 hot birth 수락|RMB 입력만 있고 차단/빈 재고이면 흐름 어택 없음|실제 유량·노즐 위치|dispenser_hot_start|M
D06|Hot 연속 배출|작은 일정 수압의 물줄기|실제 수락된 Hot 배출 지속|노즐 차단/재고 소진/취소/수납 시 페이드; 수신음은 별도|수락 ml/s·수신 거리|dispenser_hot_loop|M
D07|Hot 배출 정지|작은 밸브 닫힘과 물줄기 꼬리|배출 상태가 실제 on→off|정지 1회; 남은 공중 액체는 계속 소리 가능|직전 유량·정지 원인|dispenser_hot_stop|M
D08|Ice 배출 기구 시작|짧은 게이트 작동 폴리|사용자 배출 활성+모드 Ice; 유효 작동 상태|1회; 실제 모터는 미구현이므로 큰 모터 지속음은 선택|배출 활성·재고·차단|dispenser_ice_gate|A
D09|Ice 한 개 실제 배출|노즐의 짧은 덜컥/얼음 찰칵|Instantiate 성공 및 10ml 재고 차감|배출 성공당 1회; 차단/한도 거절 틱은 생성음 없음|SpawnedIceCount 증가·출구 속도|ice_chute|M
D10|Ice 배출 종료|작은 게이트 닫힘 폴리|배출 활성 on→off|1회; D08이 없다면 생략 가능|직전 모드·정지 원인|dispenser_ice_gate_off|A
D11|공유 재고 소진/빈 상태 시도|선택적인 작은 빈 장치 딸깍/상태음|RemainingMl=0으로 전이 또는 빈 장치 새 배출 시도|전이/의도적인 새 시도만; 매 틱 경고 금지|공유 재고·이전 재고·입력 전이|dispenser_empty|A
D12|노즐 차단|선택적인 짧은 배출 거절 표시음|BlockedReason이 Nozzle blocked로 새 전이|물리 충돌음이 아님; 막힌 채 유지하면 무음|차단 원인·사용자 배출 시도|dispenser_denied|A
D13|Water/Ice unavailable|선택적인 작은 사용 불가음|유효 사용자 시도 후 unavailable 상태 진입|에디터 설정 오류를 기계 고장음으로 연출하지 않음|원인·입력 전이|dispenser_unavailable|A
D14|액체/얼음 동시 생성 한도 도달|선택적인 같은 사용 불가 피드백|실제 emission 거절과 새 사용자 시도|소프트웨어 한도에 막힘/충돌/얼음 배출음 금지|Liquid capacity/Ice capacity|dispenser_denied|A
D15|복귀 시작|약한 호스 당김/릴 시작 폴리|Held→Retracting|1회; 실제 감김 이동이 없으면 지속음 없음|복귀 상태·이동 속도|hose_retract_start|A
D16|호스 감김/본체 복귀 이동|가벼운 릴/고무 마찰|Retracting이고 실제 거리 감소/이동 있음|정지/대기/수납/disable 시 페이드|실제 이동 속도·호스 길이 변화|hose_retract_loop|A
D17|복귀 도중 장애물 접촉|외함 충돌/짧은 접촉 마찰|충돌 복원 후 실제 접촉|충돌 복원 상태 전이 자체는 무음; C12와 통합|유입 속도·상대 재질|casing_impact|C
D18|복귀 경로가 막혀 대기|기본 무음; 선택적인 1회 막힘 피드백|Return path blocked로 새 전이|대기 중 모터/릴 루프 정지; 반복 경고 금지|이동 속도·차단 상태|retract_blocked|A
D19|수납 완료|작은 수납 안착 딸깍/둔탁음|Retracting→Docked 실제 완료|1회; 마지막 외함 충돌과 중복 제거|접근 속도·수납점|dispenser_dock|M
D20|잡고 움직이는 호스|매우 약한 고무 늘어남/스침 폴리|Held이며 실제 호스 길이/곡률 변화가 큼|호스는 비충돌이므로 주변 물건을 때리는 소리 금지|길이 변화율·곡률 변화|hose_handle|A
B01|깨지지 않은 잔의 강한 충격|정상 유리 충격|유효 충돌이나 파손 확정이 없음|C02/03로 처리; 파손 Queue만으로 깨지는 소리 금지|속도·충격·파손 판정|glass_intact_hit|M
B02|Natural 유리 파손|한 번의 날카로운 균열/파열과 짧은 유리 낙하 질감|CompleteBreak에서 검증된 파편 생성과 IsBroken=true 확정|원래 C02 어택을 대체; 잔 1개당 1회|LastImpactSpeed/Impulse/Point·잔형·내용물|glass_break_natural|M
B03|Explosive 유리 파손|더 넓고 빠른 유리 파열/비산|G 또는 Explosive 스타일의 실제 파손 확정|B02와 대체; 화약 폭발/불꽃 소리는 넣지 않음|실제 비산 설정·충격·파편 수|glass_break_burst|M
B04|큰 파편의 첫 환경 착지|큰 유리 조각의 짧은 짤랑/탁|파편-환경 실제 충돌|원래 파열음과 구별되는 시각/시간의 대표 접촉|파편 면적·충격·환경 재질|shard_impact|M
B05|작은 파편의 연속 튕김|짧고 높은 작은 틱|실제 환경 재충돌|폭주 시 그룹 대표 선택; 감쇠와 문턱 적용|실제 충격·파편 크기|shard_bounce|M
B06|파편이 환경에서 미끄러짐/구름|짧은 유리 긁힘/드문 틱|파편-환경의 유효 접촉과 이동|휴면/저속이면 종료|접선 속도·파편 면적·하중|shard_scrape|M
B07|채워진 잔 파손 시 액체 방출|액체량에 맞는 큰 출렁임/퍼짐|실제로 액체가 있는 잔 파손과 ReleaseWithSplash/방출|파손 트랜잭션당 한 레이어; 이후 도착음 별도|실제 ml·충격·splash 설정·Natural/Explosive|break_liquid|M
B08|빈 잔 파손|유리 파손음만|파손 직전 액체량 0|B07 재생 금지; 고체 얼음/가니쉬 충돌은 실제 발생 시|액체량·파손 확정|glass_break_dry|M
B09|움직이는 파편과 액체 접촉|작은 젖은 충격/국소 튐|shardLiquidCollision=true이며 실제 업로드된 proxy와 액체 접촉|false/예산으로 누락된 proxy는 충돌음 금지|실제 proxy 접촉·액체 유입·파편 속도|shard_liquid|C
B10|파편이 마지막으로 멈춤|실제 마지막 작은 틱만|실제 새 접촉/유효 미끄러짐|파편 삭제/복구 시 잔향을 새로 만들지 않음|잔여 충격·접촉점|shard_settle|C
N01|부력으로 천천히 떠오름|무음|고체가 떠오른다는 이유만으로 이벤트 생성 금지|표면 통과/벽 충돌이 있을 때만 해당 별도 이벤트|부력은 발음 원인이 아님|none|N
N02|유속 저항/점성 감쇠 계산|무음|힘·토크가 적용되는 매 고정 틱|실제 관찰된 표면/충돌 이벤트만 허용|AddForce/AddTorque 호출량은 음량 아님|none|N
N03|액체/얼음/가니쉬 정지·휴면|무음|기준 이하 운동/지속 중력 접촉|기존 루프 종료; 자동 떠오름/떨림을 소리로 은폐하지 않음|신뢰 가능한 실제 이동 에너지|none|N
N04|벽 겹침/관통/깊은 접촉 수치 복구|무음|ResolveCompleted/Recovery/보정 이동|복구 이동을 충격·새 출렁임으로 계산 금지|IceRecoveryTranslation·보정 원인|none|N
N05|집기 시 바로세움/기준 좌표 변환|무음|RestorePickupPose/CarryPickupContents/자동 복원|실제 이후 충돌/유동은 허용; 자동 자세 스냅음 금지|자동/사용자 운동 구분|none|N
N06|놓기 시 액체 기존 관성 유지|별도 무음|클릭 Drop/TranslateContentsPreservingMotion|기존 액체 루프를 이어감; 잔 속도 동기화 효과음 금지|기존 이벤트 위상·세대|none|N
N07|얼음 Colliding/NonColliding 전환|물리 전환 자체 무음|모드 값 변경|UI 클릭을 원하면 UI 채널 1회; 실제 없는 물보라 금지|이전/새 모드|none|N
N08|초기 채움/seed/Teleport/시험 스폰|무음|SeedLiquids/Fill/PrepareDrop/초기 생성|사용자가 실제 붓는 생성과 구분|생성 원인·세대|none|N
N09|범위 밖 입자/얼음/가니쉬/파편 정리|무음|회계 retire·SetActive(false)·Destroy|마지막 실제 접촉의 잔향은 허용; 삭제 소리 금지|cleanup 이유|none|N
N10|용기 소유권/성분 회계 갱신|무음|VesselId/ledger/revision 변경|실제 입구 통과/물리 도착 없으면 무음|snapshot revision·generation|none|N
N11|세션 reset/모드 재시작/깨진 잔 복구|무음|ResetSession/ResetSimulation/RestoreIntact|이전 세대 루프와 예약 이벤트 폐기; 복구 파손음 금지|worldId·generation|none|N
N12|보이지 않는 시뮬레이션 경계에서 회수|무음|월드 바깥에서 retired|실제 보이는 벽/테이블 충돌과 구분|실제 collider 여부·retire 원인|none|N
N13|잡힌 물체의 무효 외부 겹침/비충돌 바닥|무음|IgnoreCollision/trigger/CollisionSuppressed로 접촉이 없는 상태|겹침/근접만으로 충돌음 재생 금지|실제 접촉 정책|none|N
N14|파편-파편/파편-도구/잔 고체 접촉|현재 무음|현재 파편은 환경 이외 native contact를 무시함|향후 실제 충돌 정책을 켰을 때만 확장|RefreshGlassShardContacts|none|N
N15|GPU 압력 보정 속도/읽기 지연의 순간값|무음|실제 이동 없는 raw velocity spike/오래된 snapshot|이벤트 버리기/루프 감쇠; 물보라로 증폭 금지|실제 transport·snapshot age·revision|none|N
N16|장식 거품/시각 입자/물색 변화|기본 무음|E cosmetics 또는 렌더 표면만 변화|실제 기포 방출 물리와 혼동 금지|cosmetic/physical 구분|none|N
N17|온도 데이터만 80°C로 설정|무음|Hot 물 생성/온도 필드 변화|가열기·증기·끓음·얼음 녹는 소리를 자동 추가하지 않음|현재 열교환/증발 모델 부재|none|N
N18|TryTransferContents로 논리적 제공|물리음 없음|검증된 액체 회계 이관 성공|잔을 실제 건네거나 마시는 소리로 간주하지 않음; 별도 UI 피드백 선택|불변 transfer receipt|none|N
N19|얼음 재고 그림/잔량 숫자 갱신|무음|IceStockVisualIndex/GUI 수치 변화|배출 성공음과 별도 효과음 중복 금지|실제 배출 여부|none|N
N20|단순 회전 조작/오른쪽 버튼 자동 바로세움|기구 소리 없음|BeginRotation/EndRotation/AdvanceUprightReturn|실제 액체·접촉 소리만; 일반 잔에 서보음 금지|실제 유동·접촉|none|N
N21|활성 해제/포커스 상실/일시 정지|새로운 물리 효과음 없음|입력 취소/OnDisable/포커스 상실|보유 loop 정리; 의도적 복귀 이동은 상태에 맞춰 처리|취소 원인·실제 복귀 상태|none|N
F01|탄산의 지속 기포/개봉 치익|현재 미할당|향후 실제 탄산 상태/가스 방출 구현 시|탄산 미닛이라는 이름만으로 켜지 않음|용존 가스·압력·기포 방출량|future_fizz|F
F02|가열/끓음/증기|현재 미할당|향후 가열기·상변화·증기 이벤트 구현 시|80°C 데이터만으로 끓는 소리 없음|실제 열원·상변화량|future_heat|F
F03|얼음 녹음/열 충격 균열|현재 미할당|향후 녹음·고체 균열 상태 구현 시|수면에서 정지한 얼음의 랜덤 자동 크랙 금지|온도차·실제 균열/질량 변화|future_ice_thermal|F
F04|커피 분말의 사락/알갱이 낙하|현재 미할당|실제 분말/입자 모델 도입 시|현재 Bottle 액체 배출 경로에 건조 가루음 금지|실제 알갱이 수·질량·표면|future_powder|F
F05|병 마개 돌림/코르크/진공 개봉|현재 미할당|실제 사용자 마개 탈착 상호작용 도입 시|SetSealed 초기화/그림 on/off만으로 재생 금지|마개 재질·탈착 상태·압력|future_bottle_cap|F
F06|가니쉬 자르기/짜기/비틀어 향 내기|현재 미할당|실제 절단/압착/변형 상호작용 도입 시|현재 공급원에서 꺼내는 동작은 절단이 아님|파단·과즙량·변형량|future_peel_prepare|F
F07|세척/배수구 빨림/닦기|현재 미할당|실제 세척 도구·배수구·닦기 상호작용 도입 시|범위 밖 회수와 Table Holds Liquid=false는 배수구가 아님|실제 배수 유량·접촉면|future_cleanup|F
F08|마시기/빨대/건네기|현재 물리 범위 외|실제 서비스/음용 동작과 연결할 때|TryTransferContents 호출 자체로 음용음 금지|실제 사용자 서비스 상태|future_serving|F
'''

GROUPS = {
    'H': ('손 조작', 'hold,input', '손/물건 위치', '전이당 1회; 물리 충돌과 구별', 'P3', '3~5'),
    'C': ('고체 접촉', 'contacts,hold', '실제 접촉점', '정렬한 body 쌍+접촉 패치; 45~100ms 재무장', 'P1/P2', '약/중/강 각 4~6'),
    'L': ('액체', 'pour,reservoir,snapshot,material,environment', '출발은 입구; 도착은 실제 도착점', 'source/수신 패치당 집계; 입자별 재생 금지', 'P1/P2', '원샷 4~8; 루프 2~3'),
    'I': ('얼음', 'stock,modes,float,flow,contacts', '통 입구/실제 접촉점/표면 통과점', '실제 접촉만; 밀집 시 용기별 군집 선택', 'P1/P2', '짧은 접촉 6~10; 군집 3~4'),
    'P': ('가니쉬', 'garnish,float,modes', '가니쉬/실제 접촉점', '두 프리팹 변형 공유 가능; 부력 매 틱 재생 금지', 'P3', '종류별 3~5; 젖음 3~4'),
    'S': ('쉐이커', 'shaker,part,contacts,modes', '변한 부품/통/출구', '최종 상태 차이로 집계; 내부 접촉과 군집음 중복 금지', 'P1/P2', '탈착 각 4~6; 흔들기 3~4'),
    'D': ('디스펜서', 'dispenser,emit,retract', '버튼/노즐/호스 릴/수납점', '성공한 상태 전이/실제 배출만; 경고는 새 시도당 1회', 'P1/P2/P3', '버튼/기구 3~5; 루프 2~3'),
    'B': ('유리 파손', 'break,shard,shardwater,splash', '파손점/개별 환경 접촉/실제 액체 도착점', '파손 transaction 1회; 잔향과 대표 파편만', 'P0/P2', '파손 스타일별 5~8; 파편 크기별 6~10'),
    'N': ('무음/억제', 'contacts,input,snapshot,account,float', '해당 없음', '이벤트 생성 억제 또는 기존 루프 정리', '무음', '0'),
    'F': ('향후 기능', 'material,dispenser,modes,account', '향후 실제 원인 위치', '기능 구현 전 재생하지 않음', '미정', '현재 0'),
}
STATUS = {
    'M': ('필수', '물리/상태 경로 존재; 전용 사운드 연결 미구현'),
    'A': ('선택 연출', '실제 동작 기반 폴리 제안; 녹음/재생 연결 미구현'),
    'C': ('조건부', '실제 접촉/기능/문턱을 충족할 때만 연결'),
    'N': ('무음 필수', '현재 모델에서 별도 발음 금지'),
    'F': ('향후 검토', '현재 물리 또는 사용자 상호작용 없음'),
}
fields = ['ID','대분류','동작_상황','필요도','현재상태','들려야할소리','시작조건','갱신_종료조건','조절변수','재생위치','중복방지','우선순위','에셋군','권장바리에이션','근거소스','확인시나리오']
rows = []
for line in DATA.strip().splitlines():
    cells = line.split('|')
    assert len(cells) == 8, line
    id_, title, sound, start, end, drivers, bank, status = cells
    group, keys, position, dedup, priority, variants = GROUPS[id_[0]]
    req, state = STATUS[status]
    if id_ in {'L02','L04'}:
        state += '; E에만 전역 합산 절차음 초안 존재, F/G 전용 경로 없음'
    references = '; '.join(f'{source_refs[k][0].relative_to(ROOT).as_posix()}:{source_refs[k][1]}' for k in keys.split(','))
    rows.append(dict(zip(fields, [id_,group,title,req,state,sound,start,end,drivers,position,dedup,priority,bank,variants,references,f'{title}: 조건 충족→유지→종료; 반대 조건에서는 미재생 확인'])))
assert len(rows) == len({r['ID'] for r in rows})
CSV = OUT / f'physics-audio-cue-sheet-{DATE}.csv'
with CSV.open('w', encoding='utf-8-sig', newline='') as f:
    writer = csv.DictWriter(f, fieldnames=fields)
    writer.writeheader(); writer.writerows(rows)

# Inspect all catalog entries rather than treating named liquids as implemented gases/powders.
catalog = FEATURE / 'Data/FluidExperimentCatalog.asset'
entries = []
for block in catalog.read_text(encoding='utf-8-sig').split('  - id: ')[1:]:
    name = re.search(r'displayName: (.*)', block).group(1)
    name = json.loads(name) if name.startswith('"') else name
    category = re.search(r'category: (.*)', block).group(1)
    entries.append((name, category))
assert len(entries) == 28

def digest(p):
    return hashlib.sha256(p.read_bytes()).hexdigest()

tracked_sources = sorted(p for p in FEATURE.rglob('*') if p.suffix in {'.cs','.compute','.hlsl','.shader'} and '/Editor/' not in p.as_posix())
assert len([p for p in tracked_sources if p.suffix == '.cs']) == 55
snapshots = {p.relative_to(ROOT).as_posix(): digest(p) for p in tracked_sources}
snapshots[catalog.relative_to(ROOT).as_posix()] = digest(catalog)

# Every native acoustic material pair is listed, including currently disabled pairs.
MATERIALS = {'G':'잔 유리','B':'병 유리','M':'금속 통/지거','T':'스푼/금속 부품','I':'얼음','P':'가니쉬 껍질','D':'디스펜서 외함','F':'유리 파편','S':'테이블/환경'}
matrix = []
keys = list(MATERIALS)
for i,a in enumerate(keys):
    for b in keys[i:]:
        pair = {a,b}
        state = '실제 native contact가 허용되고 발생할 때'
        if a == b == 'S': cue, state = '무음', '정적 환경끼리는 물리 발음 대상 아님'
        elif a == b == 'D': cue, state = '현재 없음', 'World당 디스펜서 1개; 다중 장치 도입 때 외함 충격군 공유'
        elif 'F' in pair and 'S' not in pair: cue, state = 'N14 무음', '현재 파편은 환경 이외 고체 접촉을 무시함'
        elif 'F' in pair: cue = 'B04~B06 파편 충격/긁힘'
        elif 'P' in pair:
            cue = 'C11 미세 마찰/대체로 무음' if a == b else 'C10/P04 젖은 툭' if 'I' in pair else 'C09/P02/P04 작은 마른 톡/젖은 툭'
        elif a == b == 'I': cue = 'C08 얼음 딸깍'
        elif 'I' in pair:
            cue = 'C06 얼음-유리' if pair & {'G','B'} else 'C07 얼음-금속' if pair & {'M','T'} else '재질별 작은 얼음 충격'
        elif pair <= {'G','B'}: cue = 'C03 유리-유리; 병/잔 공명 구분'
        elif pair & {'G','B'} and pair & {'M','T'}: cue = 'C04 유리-금속'
        elif pair <= {'M','T'}: cue = 'C05 금속-금속; 통/부품 구분'
        elif 'D' in pair: cue = 'C12 외함-상대 재질'
        else: cue = 'C01/02/13/14 상대 표면 충격/미끄러짐/구름'
        if 'S' in pair and pair & {'I','P'}:
            state += '; floorColliders에 등록된 바닥은 loose solid와 IgnoreCollision이므로 그 쌍은 무음; 실제 접촉 가능한 환경만 허용'
        if 'D' in pair and a != b:
            state += '; Held+CollisionSuppressed 중에는 무음'
        matrix.append({'재질A':MATERIALS[a],'재질B':MATERIALS[b],'소리_라우팅':cue,'현재조건':state})
assert len(matrix) == 45
with (OUT/f'physics-audio-contact-matrix-{DATE}.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=list(matrix[0]));writer.writeheader();writer.writerows(matrix)

def link(key):
    p,line=source_refs[key]
    return f'[{p.name}:{line}]({p.as_posix()}:{line})'

def table(headers, values):
    return '| '+' | '.join(headers)+' |\n| '+' | '.join('---' for _ in headers)+' |\n'+'\n'.join('| '+' | '.join(str(v).replace('|','／').replace('\n',' ') for v in row)+' |' for row in values)+'\n'

INTRO = '''# 물리 파트 사운드 요구사항 전체 보고서

작성일: 2026-10-21 · 대상: 현재 Slainte `FluidGpuExperiment` F/G 및 재사용 런타임, 관련 E 비교 기능 · 성격: **현행 소스 기반 요구사항/제작 명세**. 이번 작업은 소리 조사·설계·문서 작성이며 오디오 재생 코드를 구현하거나 새 음원을 제작한 작업은 아니다.

이 문서의 “필수”는 해당 물리 현상을 들리게 만들 사운드 요구사항이다. “조건부”는 실제 접촉이나 설정이 있을 때만 필요하고, “선택 연출”은 손·호스·재고 등 사실적인 느낌을 더할 폴리다. “무음 필수”도 누락 방지 목록에 포함한다. **움직임이 있다는 이유만으로 모두 소리를 내지는 않는다.** 부력·저항·수치 보정 자체는 무음이며, 실제 표면 통과·접촉·유출이 발음 원인이다.

범위의 완전성은 아래 객체 목록, 접촉 45쌍, 액체의 출발/공중/수신/잔류 상태, 도구 상태 전이, 예외·무음 정책을 대조해 관리한다. 아직 존재하지 않는 기능이나 향후 추가할 객체까지 완성됐다는 뜻은 아니다. 게임 전체 BGM·대화·손님·주문 연출은 별도 영역이다.

## 1. 현재 구현 상태와 핵심 결론

- 현재 이 실험 안에서 확인한 사운드 재생 코드는 `FluidExperimentEffects`의 **선택적인 E 전용 절차적 붓기 루프/방울음**이다. `IsActive`는 `useImprovedPhysics`에 묶여 있다. F/G와 재사용 런타임은 `useCohesivePhysics=true`, `useImprovedPhysics=false`여서 이 경로가 활성화되지 않는다. Runtime prefab의 `enableAudio`도 0이다.
- 기존 E 소리는 모든 병의 `ReservoirCurrentFlowMlPerSecond`를 합산한 루프 1개와 전역 `EmittedMl` 증가를 이용한 방울 채널 1개다. 위치·수신 재질·실제 방울 착지·개별 병·얼음·가니쉬·파손을 구분하는 최종 사운드 시스템은 아니다. F/G에서 이 플래그만 켜는 것으로 해결되지 않는다.
- 공통 `AudioManager.PlaySfx`는 이름으로 Resources 음원을 읽어 단일 SFX Source에서 `PlayOneShot`하는 API다. 물리별 지속음의 소유권, 접촉 집계, 위치별 루프, 모드 억제는 여기서 확인되지 않았다. 이번 조사에서 Core SFX 파일은 `sfx_trrr.mp3`, `sfx_come_in.mp3`, Business에는 `Untitled.wav`가 확인됐다. 음원을 청취해 물리용 적합성을 확인한 것은 아니며, 이름만으로 재사용 가능 판정을 내리지 않는다.
- 가장 중요한 제작 순서는 **잔/도구 충돌 → 붓기의 출발과 도착 → 얼음 접촉/쉐이커 → 출렁임과 놓기 관성 → 디스펜서 → 유리 파손/파편 → 가니쉬 미세음**이다. 우선순위는 제작 순서일 뿐, 뒤 항목을 범위에서 빼는 의미는 아니다.
- 이전 수정의 미해결 항목인 비충돌 얼음 8개 흔들기 후 잔량 60→55ml는 그대로 남아 있다. 실제 넘침이면 해당 소리를 낼 수 있지만, 벽 누출·보정·회계 차이를 넘침 소리로 포장하면 안 된다. 오디오 추가는 이 물리 문제의 해결 근거가 아니다.

## 2. 대상 객체와 재질

카탈로그 28개 항목을 전부 포함하며, 여기에 카탈로그 밖 디스펜서, 런타임 생성 쉐이커 부품, 파편, 환경을 더한다. 같은 음원을 공유해도 객체별 공명·크기·젖음·충전율 파라미터는 유지한다.
'''

body = [INTRO]
body.append('현재 오디오 활성 조건 근거: '+', '.join(link(k) for k in ('effects','audiogate','configuration','runtimeprefab'))+'. 공통 API: [AudioManager.cs:34]('+ (ROOT/'Assets/_Project/Core/Runtime/Audio/AudioManager.cs').as_posix()+':34).\n\n')
body.append(table(['카탈로그 객체','분류','사운드 적용'],[(name,category,
    '병 잡기/충격/표면 마찰; 실제 액체 출발·목 유량·소진; 수신면 별도' if category=='Bottle' else
    '잔형 공명·충전율·림 접촉·액체·내용물; 파손은 opt-in인 Hurricane만 현행 대상' if category=='Glass' else
    '얼음 고체 접촉·젖음·수면 통과; 두 액체 모드 구분' if category=='Ice' else
    '공급 클릭 폴리만; 생성된 실제 가니쉬의 물리음과 분리' if category=='Garnish Source' else
    '마른/젖은 미세 접촉·공급원·표면 통과; 큰 분출 없음' if category=='Garnish' else
    '재고 연출·실제 배출·얼음-통 접촉' if category=='IceBucket' else
    '금속 부품 탈착·밀봉 상태·액체·얼음·여과 붓기' if category=='Shaker' else
    '금속 접촉·젓기·림 접촉' if category=='Spoon' else
    '금속 계량 용기 충돌·공명·수신·넘침') for name,category in entries]))
body.append('''
추가 대상: **Dispenser 본체/두 버튼/노즐/호스/수납점**, **Cap/Strainer 분리 부품**, **Hurricane 생성 파편과 액체 proxy**, **테이블·좌우 벽·천장·유한 테이블 가장자리**. 가니쉬의 고정 공급원 그림은 움직이는 실제 가니쉬와 다른 대상이다.

### 2.1 재질 지정에서 확정과 제안 구분

잔과 병은 유리군, 얼음과 두 가니쉬는 각각 고체/껍질군으로 다룬다. 지거·코블러 쉐이커·바 스푼·부품·얼음통의 금속 음색은 외형/용도에 따른 **사운드 제작 제안**이며, 현재 물리 Material이 오디오 재질을 제공하는 것은 아니다. 디스펜서 외함의 금속/플라스틱, 테이블의 나무/금속/고무는 실제 아트에 맞춰 별도 AudioMaterial을 지정해야 한다. 파일명만 보고 나무 소리로 고정하지 않는다.

잔 네 종류는 Rock의 짧고 두꺼운 울림, Highball의 길쭉한 울림, Martini의 얇고 높은 울림, Hurricane의 볼록한 통 공명을 녹음/가공의 출발점으로 제안한다. 실제 음색/피치는 녹음과 청취로 결정한다. 물이 차면 울림이 달라지도록 파라미터화하며, 모든 잔에 같은 피치를 무작위 적용하는 것으로 대체하지 않는다.

### 2.2 액체 프리셋과 재료 이름의 차이

`FluidExperimentMaterials.Resolve(Auto)`의 현재 분류는 Syrup 카테고리/액체형이면 Syrup, 도수>0 또는 Spirit/Liqueur이면 Spirit, 나머지는 Water다. Milk는 명시적 선택이 필요하다. 병 15종 각각 독립 음원을 강제하기보다 실제 프리셋과 혼합 비율을 사용한다.

- **Water:** 열대 주스, 탄산 미닛, 뜨거운 물, 커피 분말로 명명된 Bottle의 현재 Auto 경로. 이름이 탄산/분말이어도 현재는 가스나 알갱이 물리가 아니다.
- **Syrup:** 실청, 합성 레몬, 슬롭. 두껍고 둔한 흐름·방울 변형.
- **Spirit:** 나낭나, 코튼, 헥타르, 블레스, 브리즈 보드카, 조니 독스, 번햄 버번, 바하. 기본 액체군을 공유하고 유량/점성 차이를 반영한다.
- **Milk:** 현재 카탈로그에 우유 병을 새로 가정하지 않고, 명시적 프리셋 선택 상황을 포함한다.
- 혼합액의 음색은 ml 가중치로 부드럽게 변한다. 알코올 도수 숫자만으로 과장된 “술 전용” 충돌음을 만들 필요는 없다.

## 3. 전체 이벤트 시트

아래 각 행은 “발생 상황”이다. 한 행이 반드시 새 WAV 하나를 의미하지 않는다. 기존 물리/상태가 있는 항목도 전용 오디오 연결은 아직 미구현이다. 같은 실제 사건에 여러 행이 해당하면 **하나를 라우팅하거나 의도된 레이어만 합성**한다. 예: C01+C06+I06은 “약한 물속 얼음-유리 충격 1회”이며 세 번 재생하지 않는다.

Excel용 CSV에는 재생 위치, 소유권/중복 정책, 우선순위, 권장 변형 수, 소스 근거와 확인 시나리오도 함께 들어 있다. 아래 표의 수치가 없는 모든 강도는 청취 튜닝 대상이다.
''')
for key, (name,source_keys,*_) in GROUPS.items():
    selected = [r for r in rows if r['ID'].startswith(key)]
    body.append(f'\n### 3.{list(GROUPS).index(key)+1} {name} — {len(selected)}개 상황\n\n')
    body.append('근거: '+', '.join(link(k) for k in source_keys.split(','))+'.\n\n')
    body.append(table(['ID / 상황','구분','소리','시작 조건','유지·종료·예외','강도/음색 변수'],[(r['ID']+' '+r['동작_상황'],r['필요도'],r['들려야할소리'],r['시작조건'],r['갱신_종료조건'],r['조절변수']) for r in selected]))

body.append('''
## 4. 고체 접촉 재질 전체 매핑 — 45쌍

다음 표는 두 고체의 재질 쌍을 빠짐없이 열거한다. 실제 콜라이더 접촉이 없으면 재질 음원도 재생하지 않는다. Held 외부 겹침, loose solid와 `floorColliders`의 무시된 쌍, trigger, 복귀 전 충돌 억제를 반드시 반영한다. 환경이 여러 콜라이더여도 테이블과 좌우/천장 AudioMaterial을 따로 지정한다.

금속 통과 스푼/부품, 잔과 병을 나눈 이유는 충격 어택과 통 울림이 다르기 때문이다. 물속 여부는 별도 재질을 늘리기보다 같은 접촉음의 필터/어택 변형으로 처리할 수 있다. 액체 접촉은 이 표에 섞지 않고 3절의 수신면별 이벤트로 전부 다룬다.
''')
body.append(table(list(matrix[0]),[list(r.values()) for r in matrix]))

DESIGN = '''
## 5. 중복되지 않는 실제 재생 조합

### 5.1 얼음 한 개를 액체가 든 잔에 넣기

1. 손에서 놓을 때 H03의 작은 폴리는 선택 사항이다. 아직 잔 딸깍/물 진입음을 내지 않는다.
2. 림에 실제 닿으면 C06 한 번. 가운데로 떨어져 닿지 않으면 림 소리 없음.
3. **Colliding:** 실제 수면 진입에 I03 한 번. 액체가 실제로 튄 후 도착하면 L18/19. 진입과 입자마다 각각 큰 물보라를 내지 않는다.
4. **NonColliding:** I04의 작은 젖음 연출은 선택이다. 얼음이 물을 밀어내는 대형 찰박·압력 분출음은 없다. 부력/저항은 계속 적용돼도 힘 계산 자체는 무음이다.
5. 잔 바닥/다른 얼음에 실제 닿으면 C06/08. 잠김에 따라 I06 변형을 사용한다.
6. 부력으로 올라오는 동안 무음. 실제 표면 교차가 유효할 때만 작은 젖음 꼬리를 허용하고, 그 뒤 정지하면 완전한 무음으로 끝난다.

### 5.2 얼음이 든 잔을 흔들다가 놓기

잡기 H01 → 실제 액체 벽 도착 L20 + 움직이는 풀 L21 + 실제 얼음 접촉 C06/08 또는 I07 군집 → 손 놓기 H03 → **L21/22와 고체 접촉이 원래 관성으로 이어짐** → 잔이 테이블에 실제 도착한 시점 C01/02 → 에너지가 줄면 각 루프 종료. 놓은 프레임에 액체음을 멈추거나 기본 붓기음을 새로 켜지 않는다. 8/12개라고 각 개체의 소리를 무제한 더하지 않는다.

### 5.3 가니쉬 넣기와 잔 이동

공급 클릭/Pick 성공 P01=H02 → 진짜 림/얼음 접촉 C09/10 → 실제 물 진입에 아주 작은 P03 선택 → 떠서 흐름을 따라가는 동안 대체로 무음. 가니쉬는 F/G에서 액체를 밀지 않는다. 한 프레임 외벽처럼 보였던 표시 지연이나 사후 위치 복구를 근거로 새로운 림 충돌음을 만들지 않는다. Synth Lemon/Nananga는 길이·두께가 달라 폴리 변형이 가능하나 단단한 얼음 딸깍을 공유하지 않는다.

### 5.4 병 → 지거 → 쉐이커 → 잔

병 L01/02(출구) + 빈 지거 L09(수신) → 충전율에 따른 L10/27 → 병 복원 후 L05, 실제 마지막 방울 L06. 지거를 기울이면 이미 시뮬레이션 중인 내용물이 실제 입구로 나가는 L17과 쉐이커 수신 L09/10이 발생한다. **지거/쉐이커/잔의 실제 유출을 Bottle.EmittedMl로만 판단하면 빠진다.** 출발 이후 날아가는 동안 새 입자가 생성되지 않아도 도착음은 필요하다. 열린 잔에서 다른 잔으로 옮기거나 입구를 놓쳐 테이블에 쏟는 경우도 같은 라우팅이다.

### 5.5 쉐이커 밀봉·흔들기·여과

스트레이너 S03 → 캡 S04 → 닫힌 실제 흔들기에 S06(액체)/S07(얼음) → 캡만 분리 S02 → Straining에서 S08 + 얼음이 부딪힌 경우 I09 → 실제 꼬리 S09. 스트레이너까지 제거하면 S01 뒤 Open 유출 S10. `C` 단축키는 내부에서 여러 Attach/Refresh를 호출하므로 함수 호출 횟수가 아니라 최종 상태 차이로 선택한다. 캡이 붙은 스트레이너를 통째로 들었다고 캡 탈착음을 추가하지 않는다. 빈 쉐이커를 움직일 때 가상의 물·얼음 소리는 없다.

### 5.6 디스펜서

수납 상태 버튼 D01/02/03 → 집기 D04 → 실제 Hot 배출 D05/06 또는 Ice 성공 배출마다 D09 → 실제 수신면 소리 별도. 노즐이 막히거나 생성 한도/빈 재고이면 배출음은 없다. Ice는 기본 0.3초 간격, 한 개당 10ml 차감이며 잔량 10ml 미만의 마지막 한 개도 성공할 수 있다. **실제 생성 성공을 기준으로 하므로 성공한 마지막 얼음음은 내고, 거절된 틱은 내지 않는다.** 공유 재고 기본 5000ml의 변화는 음량을 키우는 기준이 아니다.

놓기/취소로 실제 복귀 D15/16 → 장애물에 실제 접촉하면 D17 → 경로 대기면 루프 감쇠와 D18 선택 → 실제 수납 D19. 복귀 중 우회점 전환, 충돌 복원, 0.5초 목표 시간 자체에는 딸깍을 배정하지 않는다. 호스는 비충돌 시각 체인이므로 다른 물체를 때리는 효과음은 없다. 별도 전기 모터/펌프/히터는 구현된 모델이 아니며 필요하면 낮은 선택 폴리로만 도입한다.

### 5.7 유리잔 파손

유효 충돌을 수집 → 파손 확정 여부 결정 → **정상 충격 C02 또는 파손 B02/B03** → 실제 액체가 있으면 B07 → 파편의 실제 환경 충돌 B04/05와 미끄러짐 B06 → 마지막 실제 틱 후 무음. 현재 파손은 F/G의 opt-in Hurricane 경로다. 다른 세 잔/병/얼음을 같은 임계값으로 깨지는 대상으로 추가하지 않는다.

Natural과 Explosive는 각각 자연 낙하 파열/빠른 비산 질감을 사용한다. Explosive는 실제 유리 비산 스타일이며 화약 폭발이나 압력 용기 파열이라는 뜻이 아니다. 비산 계수가 0이면 과장된 폭발 레이어를 강제하지 않는다. 파편끼리와 파편-잔/도구의 고체 접촉은 현재 무시되므로 임의의 부딪힘 소리는 없다. `shardLiquidCollision`이 꺼졌거나 해당 proxy가 예산에서 빠졌으면 파편이 액체를 때리는 소리도 만들지 않는다. 파편이 젖은 표면에 실제 닿으면 환경 충돌의 젖은 변형은 가능하다.

## 6. 이벤트 연결 위치와 추가로 필요한 데이터

### 6.1 Native 고체 접촉

현행 BreakableGlass의 `OnCollisionEnter2D/Stay2D`만 모든 고체 소리를 처리하는 경로는 아니다. Body, 자유 ShakerPart, GlassShard용 접촉 수집기를 추가하는 것이 제안이다. 신규 구현에서는 다음 순서를 지킨다.

- 고정 틱의 **충돌 전** 속도와 각속도를 보관하고, 접촉점 속도로 법선 접근 속도/접선 이동을 계산한다. 솔버 뒤 0이 된 속도만 사용하거나, 단순 `Abs(relativeVelocity)`로 멀어지는 접촉을 공격음으로 만들지 않는다.
- 각 collider 쌍을 body 쌍으로 정규화하고 접촉 패치를 합친다. 양쪽 콜백, compound hull의 여러 벽, Enter+Stay에서 같은 사건이 반복돼도 한 번만 발음한다.
- Physics2D 뒤 `ProcessGlassBreakage`의 최종 판정을 반영해 같은 잔의 충격과 파손 어택을 조정한다. `QueueImpact`만으로 파손음을 재생하지 않는다. 파편 생성 검증이 실패하면 정상 충격 쪽으로 남긴다.
- native가 실제 허용한 접촉을 우선한다. 겹침 검사·근접 반경·containment·벽 복구만으로 충돌음을 새로 만들지 않는다. 초기 부착/초기 스폰/무충돌 복원은 원인 태그로 억제한다.

### 6.2 GPU 액체 접촉

액체에는 고체의 `OnCollisionEnter2D`가 그대로 오지 않는다. 현재 완료 스냅샷은 보통 0.2초 간격으로 요청되므로, 이를 그대로 짧은 물방울 착지 트리거로 쓰면 지연·누락이 생길 수 있다. GPU 원소별 `PlayOneShot`이나 매 프레임 동기 `ReadbackNow`를 추가하는 것도 피한다.

제안: GPU가 고정 틱마다 **용기/출구/수신 표면 패치별 요약**(실제 통과 ml, 법선 유입량, 대표 위치, 실제 이동 에너지)을 만들고 별도 작은 이벤트/집계 버퍼를 비동기로 회수한다. 최초 source start/stop와 생성 성공은 CPU에서 즉시 알 수 있는 경로를 사용하고, 수신은 실제 GPU 접촉 요약으로 확인한다. 루프는 낮은 빈도의 집계를 보간한다. 더 촘촘한 오디오 전용 비동기 갱신률은 성능/지연을 측정해 결정하며, 현재 5Hz 경로를 이미 저지연 구현으로 부르지 않는다.

- 새 입자 수가 아니라 ml를 집계한다. 입자 해상도/서브스텝/프레임률이 바뀌어도 같은 실제 흐름의 음량이 비슷해야 한다.
- 출구(sourceId/streamId)와 수신(receiverId/패치)을 구분한다. 같은 줄기가 둘 이상의 잔/테이블로 갈라지면 각 실제 도착 부분만 배분한다.
- 한 방울을 여러 입자가 표현해도 하나의 사건이다. 연속 줄기가 닿으면 도착 루프, 떨어진 작은 덩어리면 방울 원샷으로 분류한다.
- 벽의 압축/관통 복구 변위와 실제 이동을 구분한다. 얼음 수정에서 확인한 raw solver velocity의 유령 운동을 액체 출렁임/물보라 강도로 재사용하지 않는다. `IceFlow`는 연결된 풀의 실제 이동을 보는 참고 경로이며, 그대로 완성된 오디오 텔레메트리 API인 것은 아니다.
- 수신 풀의 실제 ml, 빈 통/이미 젖은 통, 벽/림/내부/외부를 구분한다. VesselId 변화 하나만으로 넘침·새 착지를 확정하지 않는다.
- 세대/World/revision/시간을 함께 확인한다. 오래되거나 부분적으로 완료된 읽기는 버리고, 신뢰 불가 시 루프를 감쇠한다. 지나간 방울음을 나중에 몰아서 재생하지 않는다.

### 6.3 상태 기반 연결

`HeldChanged`는 집기/놓기의 유효 전이에 활용 가능하다. `ShakerClosureChanged`는 현재 중간 Attach/Refresh에서도 반복 호출될 수 있어 최종 diff를 확인한다. 디스펜서 State/Mode/BlockedReason/재고와 실제 배출 성공을 별도 이벤트로 노출하고, 파손은 최종 커밋 1회에 연결한다. 이것들은 **연결 지점 제안**이며 이번 문서 작성으로 새 API가 추가된 것은 아니다.

제안 이벤트 정보: `worldId, generation, tick, sequence, bodyIdA/B, kind, cause, mode, position, normalSpeed, tangentSpeed, normalImpulse, effectiveMass, volumeMl, flowMlPerSecond, fillFraction, submergence, materialA/B, physicalTime` 및 수치 보정/초기화/취소 플래그. 모든 이벤트에 GPU와 CPU 시간을 혼용하지 말고 단일 시뮬레이션 기준을 둔다.

## 7. 강도·음색·중복 재생·믹싱 정책

아래 숫자는 **검증된 현재 설정이 아닌 최초 청취 튜닝 제안**이다. 현 프로젝트 월드 단위와 2D 질량을 실제 SI 측정값으로 해석하지 않는다.

| 항목 | 제안 | 이유/종료 조건 |
| --- | --- | --- |
| 충격 강도 | 충돌 전 접근 속도와 유효 질량의 에너지 또는 normalImpulse를 압축 곡선으로 정규화 | 고정 중력 하중/침투 복구를 새 충격으로 세지 않음 |
| 재생 문턱 | 작은 딸깍/큰 착지의 유입 속도와 impulse 문턱을 재질별 분리 | 모든 물건에 같은 질량/속도 기준을 강제하지 않음 |
| 동일 body 쌍 재무장 | 얼음/작은 부품 약 45~70ms, 큰 고체 약 70~100ms부터 튜닝 | 실제로 분리됐다 재충돌한 강한 새 사건은 보존 |
| 물 진입 재무장 | 잠김 진입/이탈 문턱을 다르게 두고 80~150ms 디바운스부터 평가 | 수면 샘플의 작은 요동이 연타되지 않게 함 |
| 군집 | 약 30~60ms 창에서 한 용기의 미세 접촉을 집계 | 8/12개 쌓임에서 소리 수/음량 폭증 방지 |
| 점음/루프 전환 | 실제 수신량·사건 간격의 히스테리시스 | 줄기와 방울의 전환에서 채터/이중 재생 방지 |
| 루프 시작/끝 | 약 20~60ms 시작, 60~180ms 종료 페이드부터 청취 | 갑자기 자르지 않되 종료 뒤 물소리가 남지 않게 함 |
| 음색 변형 | 녹음 변형 우선, 반복 회피; 피치 ±3~5% 수준부터 | 얼음/유리 종류 구별을 과도한 랜덤 피치로 지우지 않음 |
| 잠김 | 개별 접촉 고역/어택 감소, 부분 잠김은 완만한 혼합 | 게임 전체나 물 밖 잔 공명까지 일괄 저역 통과시키지 않음 |
| 채워진 용기 | 충전율로 통 공명·감쇠/EQ 교차 변화 | 정지 중 ml 업데이트 자체로 소리를 내지 않음 |
| 2D 위치 | 활성 World의 카메라/화면 x에 따른 패닝, 공간 규모에 맞춘 완만한 감쇠 | 소스와 수신 위치 분리; 3D 기본 거리 감쇠를 무조건 적용하지 않음 |
| 기본 보이스 예산 | 전체 원샷 24 + 루프 8부터; World별 원샷 12/루프 4부터 측정 | 고정 보장 수치가 아님. 멀고 작은 반복음부터 제거 |
| 보이스 우선권 | P0 파손, P1 주된 조작/도착/착지, P2 지속 물리 질감, P3 미세 폴리 | 파손 때 모든 유체음까지 끄지 말고 짧게 필요한 범위만 덕킹 |
| 레이어 합산 | 강도는 선형 개수 합산을 피하고 압축/상한 | 12개가 1개의 12배 음량으로 커지지 않음 |

Mixer 제안: Physics Master 아래 `Impacts/Glass`, `Impacts/Metal`, `Ice`, `Liquid/Source`, `Liquid/Receive`, `Liquid/Slosh`, `GarnishFoley`, `ShakerMechanism`, `DispenserMechanism`, `Breakage`, 그리고 별도 UI. 사용자 SFX 볼륨/음소거를 공통 적용한다. BGM/대화를 물리 Source 수명에 묶지 않는다. F/G 비교 실행에서 비활성 World와 다른 테스트 사본의 소리가 섞이지 않도록 AudioContext를 World에 귀속한다.

한 번의 충돌에서 선택할 기본 구성은 **어택 1개 + 필요한 몸통/공명 1개**다. 음원을 두 레이어로 제작해도 하나의 사건 예산으로 관리한다. 큰 물리 오류를 사운드 리미터로 숨기는 것은 수정이 아니므로, 디버그에는 재생/억제 원인을 남긴다.

## 8. 루프 수명과 예외 처리

- 소리 소유자는 개별 병/용기/표면 패치/기구다. 물건을 놓아도 해당 액체 상태/loop handle을 바꾸지 않는다. 액체의 기존 물리 속도와 오디오 위상이 연속이어야 한다.
- 손 조작 종료, 노즐 차단, 배출 거절, 재고 소진, mode None, 복귀 시작, 도구 disable 각각에서 해당 **출발** 루프만 정리한다. 이미 날아간 액체의 수신음을 함께 끊지 않는다.
- pause/시간 배율 0/포커스 상실에서는 시뮬레이션 진행 여부에 맞게 루프 정지/페이드한다. unscaled 시간으로 가상의 새 물리 사건을 만들지 않는다. 디스펜서의 실제 자동 복귀가 계속되는 정책이면 그 이동 폴리만 상태에 맞춰 남길 수 있다.
- reset/세대 변경/씬 종료/모드 재시작/World 비활성/오브젝트 풀 반환 때 소유 루프와 예약 사건을 폐기한다. 다른 World나 BGM Source를 정지하지 않는다. 한번 재사용된 instanceId만으로 이전 예약음을 새 물건에 붙이지 않는다.
- 파손 시 원래 잔의 위치를 마지막 파손점으로 캡처해 원샷 잔향을 독립 재생할 수 있어야 한다. 잔 GameObject가 바로 비활성화돼 파열음이 잘리지 않게 한다. 이후 파편 접촉은 실제 파편 위치를 따른다.
- 세션 복구/Designer 낙하 시험/비교 자동 시나리오는 `initialization/test/reset/user` 원인을 구분한다. 시험을 무음으로 돌릴 수 있어도 실제 사용자 물리음 기능을 제거하지 않는다.
- 없는 음원은 경고/카운터를 남기고 안전하게 해당 레이어만 생략한다. 매 프레임 로그를 쏟거나 `Resources.Load`를 모든 충돌마다 호출하지 않는다. 캐시/Source 풀과 세대 소유권을 둔다.

## 9. 제작 요청 단위와 음원 납품 규격 제안

CSV의 에셋군은 논리 키다. `ice_chute`, `liquid_receive_pool`처럼 여러 이벤트가 공유하며, `none`은 파일을 만들지 않는다. `future_*`는 현재 제작 필수 수량에서 제외한다. hand_swap/material_blend/fill_resonance 같은 조합·파라미터 행도 독립 파일이 필요하지 않을 수 있다. 사건 수를 WAV 수나 견적 수량으로 그대로 읽지 않는다.

| 제작 묶음 | 반드시 구분할 변형 | 전달 시 포함 사항 |
| --- | --- | --- |
| 유리 | 잔/두꺼운 병, 약·중·강 충격, 네 잔형의 공명, 미끄러짐/구름 | 어택/몸통 분리 가능; 파손과 분리; 재질 태그 |
| 금속 | 통/지거, 스푼/작은 부품, 유리와의 접촉, 탈착/안착 | 빈 통/충전 통 변형 또는 공명 레이어 |
| 얼음 | 유리/금속/얼음 접촉, 건조/젖음, 작은 군집, 출구 | 짧은 접촉 최소 6개 이상부터 반복성 평가; 물속 필터 기준 |
| 액체 | source start/loop/stop, 가는 흐름/방울, 빈 유리/금속/기존 풀/환경 수신, slosh/stir/overflow | 얇음/점성/선택 Milk 변형; 출발/도착 분리; 루프 seam 확인 |
| 가니쉬 | 두 껍질의 집기/마른 톡/젖은 톡/약한 젖음 | 얼음 같은 어택 제거; 너무 작은 원음을 과증폭하지 않음 |
| 쉐이커 | 캡/스트레이너 탈착·장착, 건조/젖은 내용물, 여과 붓기 | 내부 접촉과 레이어 중복 기준; Cap/Strainer 라벨 |
| 디스펜서 | 버튼 on/off, Hot 밸브/물줄기, Ice 출구, 호스/수납, 선택 거절음 | 모터/릴은 선택 연출임을 표시; 빈 재고와 실제 배출 분리 |
| 파손 | Natural/Explosive 파열, 큰/작은 파편 착지·재튐·긁힘, 유액 방출 | 파손 어택과 파편 tail의 중복 기준; 빈 잔용 건조 버전 |
| 환경 접촉 | 실제 아트에 맞는 테이블/벽 재질의 물건·액체 반응 | 표면 재질이 확정되지 않으면 wood/metal을 임의 확정하지 않음 |

제안 원본: WAV PCM 48kHz/24bit, 짧은 원샷은 단일 위치 mono 우선, 넓은 군집/비산은 필요 시 stereo 버전 병행. 어택 앞 불필요한 무음 제거, 자연 잔향은 보존, 클릭/클리핑 없는 루프, 제작 원본과 게임용 가공본 분리. Unity import 압축/스트리밍/메모리 방식은 길이·동시 수·대상 플랫폼을 측정해 결정한다. 위 포맷은 프로젝트에서 이미 확정된 표준이 아니다.

파일 키 예: `phy_ice_glass_light_wet_01.wav`, `phy_liquid_receive_glass_thin_loop_01.wav`, `phy_shaker_cap_attach_01.wav`, `phy_glass_hurricane_break_natural_01.wav`. 각 파일에 논리 키, 변형 번호, 재질 A/B, 젖음, 강도, loop 여부, 권장 원음 길이, 제작/라이선스 출처를 함께 기록한다. 실제 확보 전에는 출처나 청취 품질을 확인했다고 적지 않는다.

## 10. 재현·청취 검사 계획

**아래는 향후 오디오 구현 후 실행할 검사 목록이며, 이번 문서 작성에서 소리 재생 검사를 통과했다는 뜻이 아니다.** 기존 물리 검사 결과는 이번 사운드의 품질/지연/동시 재생 검사를 대신하지 않는다. 각 이벤트 행의 확인 시나리오에 더해 아래 교차 조건을 확인한다.

| 번호 | 재현 동작/교차 조건 | 소리 판정 |
| --- | --- | --- |
| Q01 | 28개 카탈로그 항목과 추가 생성 대상의 단독 생성/선택/해제 | 모두 이벤트 정책이 할당되고 없는 기능은 무음 |
| Q02 | 네 잔/15병/지거/쉐이커/스푼/얼음통을 약·중·강으로 내려놓고 충돌 | 접촉 재질·크기·충전율에 맞음; throw 시 선재생 없음 |
| Q03 | 45 재질쌍 표에서 유효 쌍 실제 접촉/무효 쌍 겹침 | 한 접촉 한 사건; 현재 금지 쌍은 무음 |
| Q04 | 한 물체 compound collider가 여러 점으로 닿음; Enter+Stay; 양쪽 수집 | 중복 어택 없음; 정지 중력 하중은 연타 없음 |
| Q05 | 미끄러짐→구름→정지→재충돌 | 루프 정상 종료; 실제 재튐은 남음 |
| Q06 | Water/Spirit/Syrup 및 명시 Milk를 적게/많이 붓기 | 점성/유량 변형이 있으며 소스/수신 위치와 타이밍 분리 |
| Q07 | 빈/찬 네 잔, 빈/찬 금속 통, 얼음 위, 가니쉬 주변, 도구 외벽, 환경에 도착 | 모든 수신면이 분류되고 무조건 같은 큰 찰박 없음 |
| Q08 | 병→지거→쉐이커→잔→다른 잔; 줄기 중간 분리/입구 빗나감 | 새 birth가 없는 용기 유출/도착도 누락 없음 |
| Q09 | 병의 잔량 소진, 아주 느린 점적, 급히 바로세움, 마지막 실제 방울 | 요청량이 아닌 실제 유량; 가짜 잔여 방울 없음 |
| Q10 | 서로 다른 두 병/두 수신면 동시 붓기 | 전역 합산 1루프로 뭉개지지 않고 owner별 시작/종료 |
| Q11 | 채운 잔 이동→회전→정지→클릭 놓기→재집기 | 놓기 순간 액체 loop 리셋/잔 속도 동기화음 없음; 실제 관성 유지 |
| Q12 | 빈 잔/빈 쉐이커를 같은 동작으로 흔듦 | 물/얼음 레이어 없음 |
| Q13 | 실제 스푼 물 진입/젓기/정지/인출 및 벽 접촉 | 물 밖 stir loop 없음; 벽 어택 중복 없음 |
| Q14 | 얼음 1/3/8/12개, 건조/습윤, 두 액체 모드, 실제 자동 FixedUpdate | 밀집 연타/음량 폭증 없음; 각 물리 모드별 물보라 정책 준수 |
| Q15 | 마른 얼음과 물 안 얼음을 오래 정지 | 부력·항력·미세 떨림 자체 무음; 자발 랜덤 크랙 없음 |
| Q16 | 얼음통 기울임/재고 소진/생성 한도 거절 | 성공한 얼음만 출구음; 재고 폴리와 실제 접촉 구분 |
| Q17 | 두 가니쉬 공급 클릭→놓기→수면→잔 이동→림 이탈 | 가벼운 질감; 큰 물보라/표시 지연 충돌음 없음 |
| Q18 | Cap만 분리/Strainer+Cap 함께 분리/장착 성공·실패 | 실제 변한 부품만 발음; 생성 시 자동 Attach 무음 |
| Q19 | C 키로 Closed→Straining→Open→Closed | 중간 Refresh 중복 없음; 닫힌 상태 유출음 없음 |
| Q20 | 젖은/마른/빈 쉐이커 흔들기 후 여과 붓기 | 액체·얼음 레이어 정확; 걸림 접촉 매 틱 재생 없음 |
| Q21 | 디스펜서 Ice/Hot/None 전환, 동작 중 버튼 무효 입력 | 성공 버튼만 클릭; 모드만 선택했는데 배출음 없음 |
| Q22 | Hot 실제 배출→중단→노즐 차단→복구 | 실제 수락량에 따라 출발 loop; 남은 액체 수신 계속 |
| Q23 | Ice 실제 배출/마지막 10ml 미만/재고0/노즐 차단/양쪽 생성 한도 | 성공 출구음 수=성공 생성 수; 거절 경고 연타 없음 |
| Q24 | 디스펜서 들고 고체 겹침/복귀/우회/경로 대기/수납 | 잡힌 겹침 무음; 실제 복귀 접촉만; 정지 호스 loop 없음 |
| Q25 | 초기 reset/disable/포커스 상실로 도구 배출 취소 | 소유 loop 정리; 다른 World/음악 중단 없음 |
| Q26 | Hurricane 강하지만 미파손/확정 파손/부적절한 파편 정의 | 확정 파손만 파열음; 미파손 정상 어택 보존 |
| Q27 | 빈/물 든/얼음·가니쉬 혼합 잔을 Natural/Explosive로 파손 | 액체 있는 경우만 wet layer; 원래 충격+파열 어택 과중복 없음 |
| Q28 | 파편 실제 환경 접촉과 무효 파편끼리/도구 겹침 | 환경 착지/재튐만; 비활성화로 파열 잔향이 잘리지 않음 |
| Q29 | shardLiquidCollision on/off 및 proxy 예산 누락 | 실제 업로드/접촉 여부에 맞는 액체음 |
| Q30 | Table Holds Liquid on/off, 유한 테이블 가장자리, 월드 밖 회수 | 실제 표면/가장자리만 발음; 소프트웨어 회수는 배수구음 없음 |
| Q31 | 오래된/실패한 GPU readback, 실제 이동 없는 raw velocity spike | 늦은 사건 몰아 재생/가짜 물보라 없음 |
| Q32 | 30/60/120fps, substep/입자 해상도 변경, 시간 배율·pause | 같은 실제 사건의 타이밍/강도/개수가 유지되고 pause 중 증식 없음 |
| Q33 | 8/12개+동시 붓기+파손의 최악 동시 상황 | 보이스 제한/우선권·CPU/GPU·할당·메모리·클리핑 측정 |
| Q34 | 모드 전환/ResetSession/씬 종료/풀 재사용/World 둘 이상 | 과거 세대 이벤트/loop 누수 없음; 활성 World만 청취 |
| Q35 | 사용자 SFX mute/볼륨 및 BGM/대화 동시 재생 | 물리 채널 전체 적용; BGM/대화 제어와 분리 |
| Q36 | 현재 남은 비충돌 8개 잔량 감소 재현 | 실제 입구 넘침과 누출/보정을 구분; 오디오로 오류를 감추지 않음 |

이벤트 디버그 로그에는 재생뿐 아니라 **억제 이유**(quiet/resting/duplicate/ignoredContact/wrongMode/initialization/staleSnapshot/voiceBudget)를 기록한다. 검사 증거는 사건 타임스탬프, 큐/발음 수, 최대 동시 보이스, 오디오 루프 수명, 실제 캡처/청취를 나눠 남긴다. 자동 파라미터 검사만으로 음색 자연스러움·청취 피로·플랫폼 지연이 해결됐다고 판정하지 않는다.

## 11. 이번 보고서의 검증과 인수 범위

이번에는 소스의 객체·동작·상태·접촉 정책과 문서/CSV의 목록 대응을 정적으로 검사했다. 실제 사운드 녹음, 음원 청취, Unity 오디오 구현, Mixer 청취 튜닝과 위 Q01~36 실행은 하지 않았다. 따라서 “문서 조사 완료”와 “사운드 구현 완료”를 구분한다. 물리 코드는 이 보고서 작업에서 수정하지 않는다.

`physics-audio-source-audit-20261021.json`에 소스 해시, 이벤트 수, 카탈로그 목록, 재질쌍 수, 코드 근거 일치 결과를 기록한다. `physics-audio-source-coverage-20261021.csv`는 Runtime 55개 파일과 셰이더/컴퓨트/HLSL 전체를 발음 원인·자료 경로·표시/수치 계산으로 대조한 점검표다. 이후 물리 기능이 추가되면 새 이벤트를 시트에 추가하고 무음/향후 목록을 함께 갱신한다.
'''
body.append(DESIGN)
body.append('\n### 주요 근거 파일\n\n'+ '\n'.join(f'- `{k}`: {link(k)}' for k in SOURCES)+'\n')
body.append('\n### 산출물\n\n- [전체 이벤트 CSV](physics-audio-cue-sheet-20261021.csv)\n- [접촉 재질 45쌍 CSV](physics-audio-contact-matrix-20261021.csv)\n- [소스 범위 점검 CSV](physics-audio-source-coverage-20261021.csv)\n- [정적 감사 JSON](physics-audio-source-audit-20261021.json)\n')
report = ''.join(body)
report = report.replace('## 3. 전체 이벤트 시트', f'## 3. 전체 이벤트 시트 — {len(rows)}개 상황')
REPORT = OUT / f'physics-audio-requirements-{DATE}.md'
REPORT.write_text(report,encoding='utf-8')

def coverage(p):
    n=p.name
    if 'Interactor' in n: return 'H/D/N', '입력 성공/실패/취소; 조작 자체와 실제 접촉 분리'
    if 'Dispenser' in n: return 'D/I/L/N', '상태/실제 배출/복귀/수납; 누락·차단·한도 억제'
    if 'Shaker' in n: return 'S/C/I/L/N', '밀봉/부품/액체 출구/얼음 걸림; 초기 Attach 무음'
    if any(s in n for s in ('Break','Shard','Splash')): return 'B/C/L/N', '파손 확정/파편 환경 접촉/선택 액체 접촉; 생성·정리 무음'
    if any(s in n for s in ('Garnish','IceFlow','Contents','IceStock')): return 'I/P/L/C/N', '실제 고체/수면 접촉과 힘·회복·표시/재고의 무음 구분'
    if 'Pour' in n or 'Reservoir' in n: return 'L/I/N', '실제 배출/줄기/소진; 요청량·계산 자체 무음'
    if 'Effects' in n: return 'L/N', '현행 E 전용 선택 절차음; cosmetics는 별도 물리 사건 아님'
    if 'Accounting' in n: return 'L/N', '회계·성분·제공 자료; 소유권 전이만으로 발음 금지'
    if any(s in n for s in ('Rendering','Surface','Composite','Accumulation')) or p.suffix=='.shader': return 'N / L 자료', '표시/보간/가림; 실제 접촉 원인이 아닌 시각 변화는 무음'
    if 'Environment' in n: return 'C/L/N', '실제 환경 접촉과 invisible bounds 회수 구분'
    if 'Body.cs' in n or 'World.cs' in n: return 'H/C/L/I/P/S/B/D/N', '객체 종류/전이/접촉 허용/실제 틱; 초기화·보정 무음'
    if any(s in n for s in ('RuntimeRig','Comparison','Factory','Catalog')): return 'H/D/N / 전체 범위', '카탈로그/조립/비교/시험/초기화; 이벤트 원인 태그 필요'
    if 'Material' in n or 'Settings' in n or 'GpuTypes' in n: return '전체 파라미터/N', '재질/강도/모드 자료; 값 변화 자체는 발음 아님'
    return 'C/L/I/P/N 자료', 'GPU 적분/경계/접촉/형상/통계 자료; 실제 사건 집계만 발음, 수치 계산은 무음'

coverage_rows=[]
for p in tracked_sources:
    ids,role=coverage(p)
    coverage_rows.append({'소스':p.relative_to(ROOT).as_posix(),'연결범위':ids,'점검내용':role,'SHA256':digest(p)})
with (OUT/f'physics-audio-source-coverage-{DATE}.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=list(coverage_rows[0]));writer.writeheader();writer.writerows(coverage_rows)

with CSV.open(encoding='utf-8-sig',newline='') as f:
    assert list(csv.DictReader(f)) == rows
audit = {
    'documentStaticAuditPassed': True,
    'scope': 'Current FluidGpuExperiment physics audio requirements, not audio runtime validation',
    'head': subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
    'eventRows':len(rows), 'eventsByGroup':dict(Counter(r['대분류'] for r in rows)),
    'catalogEntryCount':len(entries),'catalogEntries':entries,'contactMaterialPairs':len(matrix),
    'runtimeCsFiles':55,'runtimeAndShaderSourceFiles':len(tracked_sources),
    'sourceEvidenceAnchors':len(source_refs), 'allEvidenceAnchorsResolved':True,
    'csvRoundTripExact':True,'audioImplementedThisTask':False,'audioPlaybackTestedThisTask':False,
    'knownUnresolvedPhysics':'NonColliding eight-cube movement retained 55/60ml in final prior run; liquid-only controls retained 60/60ml',
    'sourceSha256':snapshots,
    'artifacts':{p.name:digest(p) for p in OUT.glob('*') if p.suffix in {'.md','.csv'}},
}
(OUT/f'physics-audio-source-audit-{DATE}.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({k:v for k,v in audit.items() if k in {'documentStaticAuditPassed','eventRows','eventsByGroup','catalogEntryCount','contactMaterialPairs','runtimeCsFiles','runtimeAndShaderSourceFiles','sourceEvidenceAnchors','csvRoundTripExact','audioImplementedThisTask','audioPlaybackTestedThisTask'}},ensure_ascii=False))
