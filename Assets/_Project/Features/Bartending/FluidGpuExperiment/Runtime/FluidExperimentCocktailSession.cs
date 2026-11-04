using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Slainte.Bartending.FluidGpuExperiment
{
    /// <summary>Experiment-facing adapter for the game's unchanged recipe and order evaluators.</summary>
    [DisallowMultipleComponent]
    public sealed partial class FluidExperimentCocktailSession : MonoBehaviour
    {
        public FluidExperimentWorld world;
        public bool showControls = true;
        public bool panelOpen;
        public FluidExperimentBody targetGlass;
        public string requestedRecipeId = string.Empty;
        public ItemDefCatalog ItemCatalog { get; private set; }
        public CocktailRecipeCatalog RecipeCatalog { get; private set; }
        public CocktailComposition LastComposition { get; private set; }
        public CocktailEvaluationResult LastRecipeResult { get; private set; }
        public CocktailOrderEvaluationResult LastOrderResult { get; private set; }
        public string Message { get; private set; } = "잔과 요청 레시피를 선택한 뒤 판정하세요.";
        public int LastSnapshotRevision { get; private set; } = -1;
        public event Action<CocktailEvaluationResult, CocktailOrderEvaluationResult> Evaluated;
        private CocktailEvaluator evaluator;
        private CocktailOrderEvaluator orderEvaluator;
        private CocktailRecipe[] orderableRecipes = Array.Empty<CocktailRecipe>();
        private int observedGeneration = -1;
        private FluidExperimentGpuLiquid observedLiquid;
        private FluidExperimentGpuLiquid Gpu => world != null ? world.Liquid : null;
        public bool Available => isActiveAndEnabled && Gpu != null && Gpu.IsOperational;

        private void Awake()
        {
            if (world == null) world = GetComponent<FluidExperimentWorld>();
            ReloadData();
        }

        private void OnDisable() { ResetTechniqueTracking(); ClearResult(); }

        public void ReloadData()
        {
            ItemDefCatalog items = ItemDefCatalog.LoadFromResources();
            ConfigureCatalogs(items, CocktailRecipeDataLoader.LoadDefault(items));
        }

        public void ConfigureCatalogs(ItemDefCatalog items, CocktailRecipeCatalog recipes)
        {
            ItemCatalog = items; RecipeCatalog = recipes;
            evaluator = new CocktailEvaluator(recipes);
            orderEvaluator = new CocktailOrderEvaluator(evaluator);
            orderableRecipes = recipes != null ? recipes.OrderableRecipes
                .OrderBy(recipe => recipe.displayName, StringComparer.Ordinal).ThenBy(recipe => recipe.id, StringComparer.Ordinal).ToArray()
                : Array.Empty<CocktailRecipe>();
            ClearResult();
            Message = orderableRecipes.Length > 0 ? "잔과 요청 레시피를 선택한 뒤 판정하세요." : "주문 가능한 레시피를 불러오지 못했습니다.";
        }

        public bool SelectRecipe(string recipeId)
        {
            if (!string.IsNullOrWhiteSpace(recipeId)
                && (RecipeCatalog == null || !RecipeCatalog.TryGet(recipeId, out var recipe) || !recipe.isOrderable))
                return false;
            requestedRecipeId = recipeId?.Trim() ?? string.Empty;
            ClearResult();
            Message = "요청이 변경됐습니다. 선택한 잔을 다시 판정하세요.";
            return true;
        }

        public bool SelectGlass(FluidExperimentBody glass)
        {
            if (!IsServingGlass(glass)) return false;
            targetGlass = glass; ClearResult();
            Message = "대상 잔이 변경됐습니다. 다시 판정하세요.";
            return true;
        }

        private bool IsServingGlass(FluidExperimentBody glass)
        {
            if (glass == null || glass.World != world || !glass.isActiveAndEnabled || glass.kind != LabItemKind.Glass
                || !glass.IsVessel || !world.Items.Contains(glass) || string.IsNullOrWhiteSpace(glass.servingGlassId)) return false;
            var breakable = glass.GetComponent<FluidExperimentBreakableGlass>();
            return breakable == null || !breakable.IsBroken;
        }

        public bool EvaluateTarget()
        {
            SynchronizeGeneration();
            ClearResult();
            if (!Available) { Message = "액체 시뮬레이션이 준비된 뒤 판정할 수 있습니다."; return false; }
            if (!IsServingGlass(targetGlass))
            { Message = "판정할 온전한 잔을 선택하세요."; return false; }
            if (RecipeCatalog == null || RecipeCatalog.OrderableCount == 0)
            { Message = "주문 가능한 레시피가 없습니다."; return false; }
            if (!Gpu.TryBuildCocktailComposition(targetGlass, ItemCatalog, out var composition, out string error))
            { Message = error; return false; }
            var detected = evaluator.Evaluate(composition);
            CocktailOrderEvaluationResult orderResult = null;
            if (!string.IsNullOrWhiteSpace(requestedRecipeId))
            {
                if (!RecipeCatalog.TryGet(requestedRecipeId, out var recipe) || !recipe.isOrderable)
                { Message = "요청 레시피를 찾을 수 없습니다."; return false; }
                var order = new GeneratedCocktailOrder { id = "fluid-experiment-" + recipe.id,
                    orderType = CocktailOrderType.RecipeOrder, requestedRecipeId = recipe.id, requestedRecipe = recipe,
                    line = recipe.displayName + " 한 잔" };
                orderResult = orderEvaluator.Evaluate(order, composition, detected);
            }
            LastComposition = composition; LastRecipeResult = detected; LastOrderResult = orderResult;
            LastSnapshotRevision = Gpu.SnapshotRevision;
            Message = orderResult != null ? orderResult.ToDebugString() : detected.ToDebugString();
            Evaluated?.Invoke(detected, orderResult);
            return true;
        }

        private void ClearResult()
        {
            LastComposition = null; LastRecipeResult = null; LastOrderResult = null; LastSnapshotRevision = -1;
        }

        private void SynchronizeGeneration()
        {
            if (Gpu == observedLiquid && Gpu != null && observedGeneration == Gpu.SimulationGeneration) return;
            observedLiquid = Gpu; observedGeneration = Gpu != null ? Gpu.SimulationGeneration : -1;
            ClearResult(); ResetTechniqueTracking();
            Message = "새 실험입니다. 잔과 요청 레시피를 선택한 뒤 판정하세요.";
        }

        private void Update()
        {
            SynchronizeGeneration();
            if (!Available) return;
            if (!IsServingGlass(targetGlass))
            {
                var replacement = world.Items.FirstOrDefault(IsServingGlass);
                if (targetGlass != replacement || LastComposition != null)
                {
                    targetGlass = replacement; ClearResult();
                    Message = "대상 잔이 바뀌었습니다. 선택한 잔을 다시 판정하세요.";
                }
            }
            var keyboard = Keyboard.current;
            if (showControls && keyboard != null && keyboard.pKey.wasPressedThisFrame) panelOpen = !panelOpen;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
                EvaluateTarget();
        }
    }
}
