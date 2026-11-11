using System;
using System.Collections;
using System.Collections.Generic;
using Slainte.Bartending.FluidGpuExperiment;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Slainte.Shared.Display;

namespace Slainte.Bartending
{
    // Canvas 출력은 기존 뷰포트를 사용하고 제조는 GPU 원본 런타임에 위임한다.
    // 주문/보상을 소유하지 않으며 유효한 잔의 조성만 한 번 제출한다.
    public sealed class GpuCraftingSession : MonoBehaviour
    {
        public FluidExperimentRuntimeRig Rig { get; private set; }
        public BartendingViewport Viewport { get; private set; }
        public CraftingSupplyService Supply { get; private set; }
        public bool Ready => activeForCrafting && modulesReady && Rig != null && Rig.Ready && !closing && !suspended;
        public bool IsPrepared => modulesReady && Rig != null && Rig.Ready && !closing;
        public string FailureReason { get; private set; }
        public event Action ReadyChanged;
        public event Action<CocktailComposition> Submitted;
        public event Action<string> SubmissionRejected;
        private BartendingInteractionOverlay overlay;
        private RectTransform tableAnchor;
        private Transform sceneBackdropRoot;
        private Camera worldCamera, sourceCamera;
        private int sourceCameraMask;
        private bool closing, submitted, suspended, modulesReady, activeForCrafting;
        private readonly Dictionary<Rigidbody2D, bool> simulatedBodies = new Dictionary<Rigidbody2D, bool>();
        private readonly List<Collider2D> pointerHits = new List<Collider2D>();

        public void Initialize(BusinessBartendingSettings settings, RectTransform counter,
            RectTransform viewportParent, RectTransform floorAnchor, CharacterStage characters,
            Func<string, Vector2> shelfWorldSize = null)
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.Initialize);
            if (settings.gpuCrafting == null || settings.gpuCrafting.runtimePrefab == null)
                throw new InvalidOperationException("GPU crafting settings and runtime prefab must be assigned.");
            // 설정을 먼저 주입한다. 활성 프리팹의 Awake가 카메라 없는 상태로 실행되지 않게 한다.
            var root = FluidExperimentObjectFactory.CreateInactive(settings.gpuCrafting.runtimePrefab.gameObject, transform, "GpuCraftingRuntime");
            Rig = root.GetComponent<FluidExperimentRuntimeRig>();
            Rig.world.itemLayer = Mathf.Clamp(settings.renderLayer, 8, 31);
            worldCamera = BartendingSessionBuilder.CreateWorldCamera(transform, settings, Rig.world.itemLayer);
            Rig.inputCamera = worldCamera;
            Rig.seedLiquids = false;
            Rig.enablePointerInput = true;
            var envObject = new GameObject("GameCraftingEnvironment");
            envObject.transform.SetParent(root.transform, false);
            Rig.environment = envObject.AddComponent<FluidExperimentEnvironmentBinding>();
            Rig.environment.tableHoldsLiquid = false;
            // Keep manufacturing in the complete game area, outside the dialogue camera's
            // zoom/pan transform. Place it above the backdrop and below the existing HUD.
            var gameArea = GameAspect.FindGameAreaRect(counter);
            Viewport = BartendingSessionBuilder.CreateViewport(counter, worldCamera, settings, true, null,
                gameArea != null ? gameArea : viewportParent);
            if (gameArea != null)
            {
                Transform backdrop = counter;
                while (backdrop.parent != null && backdrop.parent != gameArea) backdrop = backdrop.parent;
                if (backdrop.parent == gameArea) Viewport.transform.SetSiblingIndex(backdrop.GetSiblingIndex() + 1);
            }
            Viewport.SetInputRegistration(false);
            Viewport.SetOutputVisible(false);
            sceneBackdropRoot = counter.parent;
            tableAnchor = floorAnchor != null ? floorAnchor : counter;
            BuildEnvironment(settings.gpuCrafting.tableThickness, viewportParent != null ? viewportParent : counter.parent as RectTransform);
            Rig.ApplyConfiguration();
            Rig.world.Cocktails.showControls = false;
            Rig.world.Cocktails.panelOpen = false;
            Rig.interactor.ScreenToWorldOverride = screen =>
                Viewport != null && Viewport.TryMapPointerToWorldWhileSuspended(screen, out var point)
                    ? (Vector2)point : Rig.interactor.LastWorldPointer;
            Rig.interactor.WorldToScreenOverride = point => BartendingViewport.GetPointerScreenPosition(worldCamera, point);
            Rig.interactor.IsScreenPointValid = screen =>
                Viewport != null && Viewport.TryMapPointerToWorldWhileSuspended(screen, out _);
            Rig.interactor.ConsumePrimaryClick = ConsumePrimaryClick;
            Rig.interactor.IgnoreUiHit = IsNonBlockingSceneGraphic;
            Supply = new CraftingSupplyService(Rig, settings.gpuCrafting, shelfWorldSize);
            // The cabinet glasses share one authored sprite frame. Scale the runtime
            // fluid lengths with that frame so a smaller display still holds the same ml.
            float liquidScale = Supply.GetShelfScale(Supply.FindBody(LabItemKind.Glass, "rock"), "glass_rock");
            Rig.Liquid.settings.gpuLiquidParticleRadius = Rig.liquidSettings.gpuLiquidParticleRadius * liquidScale;
            Rig.Liquid.settings.referenceSmoothingRadius = Rig.liquidSettings.referenceSmoothingRadius * liquidScale;
            Rig.Liquid.settings.improvedAreaPerMl = Rig.liquidSettings.improvedAreaPerMl * liquidScale * liquidScale;
            Rig.World.SpawnedIceScale = liquidScale;
            overlay = BartendingInteractionOverlay.Create(Viewport, worldCamera, null, null, settings);
            overlay.BindGpuWorld(Rig.World);
            overlay.ConfigureServingTarget(characters, counter, false);
            overlay.gameObject.SetActive(false);
            sourceCamera = Camera.main;
            if (sourceCamera != null && sourceCamera != worldCamera)
            {
                sourceCameraMask = sourceCamera.cullingMask;
                sourceCamera.cullingMask &= ~(1 << Rig.world.itemLayer);
            }
            root.SetActive(true);
            Rig.Interactor.InputSuspended = true;
            StartCoroutine(WaitForReady(settings.gpuCrafting));
        }

        private void BuildEnvironment(float thickness, RectTransform layout)
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.Environment);
            Canvas.ForceUpdateCanvases();
            if (!Viewport.TryGetWorldRect(out var worldRect) || layout == null
                || layout.rect.width <= 0 || layout.rect.height <= 0)
                throw new InvalidOperationException("The Canvas crafting area could not be mapped into the GPU world.");
            // Read the authored table position in its layout frame, independent of a
            // dialogue zoom that may still be active while the GPU session is prepared.
            Vector3 local = layout.InverseTransformPoint(tableAnchor.TransformPoint(tableAnchor.rect.center));
            float floorY = Mathf.LerpUnclamped(worldRect.yMin, worldRect.yMax,
                (local.y - layout.rect.yMin) / layout.rect.height);
            Vector2 center = worldRect.center;
            Vector2 size = worldRect.size;
            thickness = Mathf.Max(.05f, thickness);
            BoxCollider2D Box(string label, Vector2 position, Vector2 dimensions)
            {
                var obj = new GameObject(label);
                obj.layer = Rig.world.itemLayer;
                obj.transform.SetParent(Rig.environment.transform, false);
                obj.transform.position = position;
                var collider = obj.AddComponent<BoxCollider2D>();
                collider.size = dimensions;
                return collider;
            }
            var env = Rig.environment;
            env.table = Box("Table", new Vector2(center.x, floorY - thickness * .5f), new Vector2(size.x + 2, thickness));
            Rig.World.HeldVesselFloor = env.table;
            env.leftBoundary = Box("LeftBoundary", new Vector2(center.x - size.x * .5f - .25f, center.y), new Vector2(.5f, size.y + 2));
            env.rightBoundary = Box("RightBoundary", new Vector2(center.x + size.x * .5f + .25f, center.y), new Vector2(.5f, size.y + 2));
            env.topBoundary = Box("TopBoundary", new Vector2(center.x, center.y + size.y * .5f + .25f), new Vector2(size.x + 2, .5f));
            // 고체 바닥만 native 접촉에 넣는다. 세로 경계를 LiftAboveFloor가 바닥으로 오인하지 않게 한다.
            env.solidFloors = new Collider2D[] { env.table };
        }

        private IEnumerator WaitForReady(GpuCraftingSettings settings)
        {
            yield return null; // World.Start가 실제 GPU 초기화를 수행한 다음 알린다.
            if (Rig == null || !Rig.Ready)
            {
                FailureReason = Rig != null ? Rig.Status : "GPU runtime missing";
                Debug.LogError("[GpuCrafting] " + FailureReason);
                yield break;
            }
            // 원본 실험 씬의 절대 좌표 대신 현재 Canvas의 비율 좌표로 배치한다.
            var dispenser = Rig.World.Dispenser;
            if (dispenser != null && Viewport.TryGetWorldRect(out var rect))
            {
                var normalized = settings.dispenserViewportPosition;
                var requested = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, Mathf.Clamp01(normalized.x)),
                    Mathf.Lerp(rect.yMin, rect.yMax, Mathf.Clamp01(normalized.y)));
                dispenser.dockPosition = Rig.World.ConstrainHeldPosition(dispenser.Body, requested, 0, .04f);
                dispenser.hoseAnchor = new Vector2(dispenser.dockPosition.x, rect.yMax - .04f);
                dispenser.ResetDispenser();
            }
            // 첫 compute/surface 할당도 손님 주문 단계에서 끝낸다. 제조 진입 시 GPU 자원을 다시 만들지 않는다.
            for (int i = 0; i < 3 && !closing; i++) yield return new WaitForFixedUpdate();
            yield return null;
            if (closing) yield break;
            modulesReady = true;
            if (activeForCrafting) ReadyChanged?.Invoke();
            else
            {
                SetSuspended(true);
                worldCamera.enabled = false;
                Rig.Interactor.enabled = false;
            }
        }

        public void BeginCrafting()
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.Begin);
            if (closing || activeForCrafting) return;
            activeForCrafting = true;
            submitted = false;
            worldCamera.enabled = true;
            Viewport.SetInputRegistration(true);
            Viewport.SetOutputVisible(true);
            overlay.gameObject.SetActive(true);
            SetSuspended(false);
            Rig.Interactor.enabled = true;
            Rig.Interactor.InputSuspended = false;
            if (modulesReady) ReadyChanged?.Invoke();
        }

        // 주문 내용만 정리하고 GPU 버퍼/카메라 출력은 같은 BusinessScene 안에서 재사용한다.
        public void EndCrafting()
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.End);
            if (!activeForCrafting || closing) return;
            activeForCrafting = false;
            Supply?.Finish();
            Rig.Interactor.ReleaseWithVelocity(Vector2.zero);
            if (Rig.Ready)
            {
                foreach (var body in Rig.GetComponentsInChildren<FluidExperimentBody>(true))
                    if (body.IsStored) Destroy(body.gameObject);
                Rig.ResetRig();
            }
            SetSuspended(true);
            Rig.Interactor.enabled = false;
            worldCamera.enabled = false;
            Viewport.SetInputRegistration(false);
            Viewport.SetOutputVisible(false);
            overlay.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (Rig == null || closing || !activeForCrafting) return;
            CleanupFallenIce();
            if (suspended)
                foreach (var body in Rig.GetComponentsInChildren<Rigidbody2D>())
                    if (!simulatedBodies.ContainsKey(body))
                    { simulatedBodies.Add(body, body.simulated); body.simulated = false; }
            Supply?.SynchronizeConsumption();
            var held = Rig.Interactor.Held;
            overlay?.SetExternalServingHeld(Ready && held != null && held.kind == LabItemKind.Glass);
        }

        // Ice outside vessels already ignores the native table. Retire it after it
        // leaves the liquid simulation too, so repeated dispensing stays bounded.
        private void CleanupFallenIce()
        {
            if (!Rig.Ready) return;
            for (int i = Rig.World.Items.Count - 1; i >= 0; i--)
            {
                var body = Rig.World.Items[i];
                if (body == null || body.kind != LabItemKind.Ice || body.IsHeld || body.ContainingVessel != null
                    || body.SolidBounds.max.y >= Rig.Liquid.settings.gpuLiquidWorldMin.y) continue;
                body.gameObject.SetActive(false);
                Destroy(body.gameObject);
            }
        }

        private bool ConsumePrimaryClick(Vector2 screen)
        {
            if (!Ready || submitted) return true;
            // 반납 영역만 선택 UI를 통과시킨다. 도감/주문서/다른 팝업 클릭은 제출로 새면 안 된다.
            if (Rig.Interactor.IsPointerBlocked(screen, hit => hit.GetComponentInParent<SelectionSlotUI>() != null)) return false;
            if (BottleReturnZones.TryReturnHeldBottle(screen)) return true;
            if (Supply.IsHeldTool() && ToolReturnZones.TryReturnHeldItem(screen, () =>
                {
                    if (!Supply.TryReturnTool(out string reason) && !string.IsNullOrEmpty(reason))
                        SubmissionRejected?.Invoke(reason);
                    return true;
                })) return true;
            if (Rig.Interactor.IsPointerBlocked(screen)) return false;
            var held = Rig.Interactor.Held;
            if (held == null || held.kind != LabItemKind.Glass || overlay == null
                || !overlay.TryGetServeTargetScreenRect(out var rect)
                || !rect.Contains(BartendingViewport.GetPointerScreenPosition(worldCamera, held.Position))) return false;
            TrySubmit(held, out _);
            return true;
        }

        private bool IsNonBlockingSceneGraphic(GameObject hit)
        {
            if (hit.GetComponentInParent<SelectionSlotUI>() != null)
                return IsPointerOverItem(Rig.Interactor.LogicalPointerScreen);
            if (hit.GetComponentInParent<RecipeBookUI>() != null || hit.GetComponentInParent<OrderTicketUI>() != null
                || hit.GetComponentInParent<Selectable>() != null) return false;
            // 기존 Canvas 배경의 RaycastTarget 값은 건드리지 않는다. 조작 핸들러가 있는 UI는 보존한다.
            foreach (var behaviour in hit.GetComponentsInParent<MonoBehaviour>())
                if (behaviour != null && behaviour.isActiveAndEnabled && behaviour is IEventSystemHandler) return false;
            return sceneBackdropRoot != null && hit.transform.IsChildOf(sceneBackdropRoot);
        }

        public bool TrySubmit(FluidExperimentBody glass, out string failure)
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.Submit);
            failure = string.Empty;
            if (!Ready || submitted || glass == null || glass.World != Rig.World || glass != Rig.Interactor.Held || glass.kind != LabItemKind.Glass
                || !glass.isActiveAndEnabled || string.IsNullOrEmpty(glass.servingGlassId)
                || (glass.TryGetComponent(out FluidExperimentBreakableGlass breakable) && breakable.IsBroken))
            { failure = "This glass cannot be submitted."; return false; }
            if (!Rig.Liquid.TryBuildCocktailComposition(glass, Rig.World.Cocktails.ItemCatalog, out var composition, out failure))
            { SubmissionRejected?.Invoke(failure); return false; }
            // 같은 입력의 재전송을 막는다. 완료 콜백은 동기적으로 세션을 종료할 수 있다.
            submitted = true;
            Supply.SynchronizeConsumption();
            Rig.Interactor.ReleaseWithVelocity(Vector2.zero);
            Submitted?.Invoke(composition);
            return true;
        }

        public bool IsPointerOverItem(Vector2 screen)
        {
            if (!Ready || !Viewport.TryMapPointerToWorldWhileSuspended(screen, out var position)) return false;
            pointerHits.Clear();
            int count = Physics2D.OverlapPoint(position, ContactFilter2D.noFilter, pointerHits);
            for (int i = 0; i < count; i++)
            {
                var body = pointerHits[i] != null ? pointerHits[i].GetComponentInParent<FluidExperimentBody>() : null;
                if (body != null && body.World == Rig.World) return true;
            }
            return false;
        }

        public void SetSuspended(bool value)
        {
            using var measurement = Slainte.Bartending.GpuCraftingDiagnostics.Measure(Slainte.Bartending.GpuCraftingDiagnostics.Stage.Suspend);
            if (Rig == null || suspended == value) return;
            suspended = value;
            Rig.World.SimulationSuspended = value;
            Rig.Interactor.InputSuspended = value;
            Viewport.SetInputSuspended(value);
            if (value)
            {
                simulatedBodies.Clear();
                foreach (var body in Rig.GetComponentsInChildren<Rigidbody2D>())
                { simulatedBodies[body] = body.simulated; body.simulated = false; }
            }
            else
            {
                foreach (var pair in simulatedBodies) if (pair.Key != null) pair.Key.simulated = pair.Value;
                simulatedBodies.Clear();
                Rig.Interactor.RebaseAfterViewTransition();
            }
        }

        public void Close()
        {
            if (closing) return;
            closing = true;
            Supply?.Finish(); // 컨트롤러 비활성화 전에 사용량을 확정한다.
            if (Rig != null && Rig.Interactor != null) Rig.Interactor.ReleaseWithVelocity(Vector2.zero);
            if (Viewport != null) { Viewport.gameObject.SetActive(false); Destroy(Viewport.gameObject); }
            if (overlay != null) { overlay.gameObject.SetActive(false); Destroy(overlay.gameObject); }
            if (sourceCamera != null) sourceCamera.cullingMask = sourceCameraMask;
            if (Rig != null) Rig.gameObject.SetActive(false);
        }

        private void OnDestroy() => Close();
    }
}
