using System;
using System.Linq;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentCocktailSession
    {
        public Font uiFont;
        private Vector2 resultScroll;
        private Rect ToggleRect => new Rect(Mathf.Max(12, Screen.width - 172), Mathf.Max(12, Screen.height - 42), 160, 30);
        private Rect PanelRect
        {
            get
            {
                float width = Mathf.Min(440, Mathf.Max(220, Screen.width - 24));
                float height = Mathf.Min(365, Mathf.Max(160, Screen.height - 66));
                return new Rect(Mathf.Max(12, Screen.width - width - 12), Mathf.Max(12, Screen.height - height - 50), width, height);
            }
        }

        public bool BlocksPointer(Vector2 screenPoint)
        {
            if (!showControls || !Available) return false;
            Vector2 guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return ToggleRect.Contains(guiPoint) || (panelOpen && PanelRect.Contains(guiPoint));
        }

        private void OnGUI()
        {
            if (!showControls || !Available) return;
            Font previousFont = GUI.skin.font;
            try
            {
                if (uiFont != null) GUI.skin.font = uiFont;
                DrawCocktailControls();
            }
            finally { GUI.skin.font = previousFont; }
        }

        private void DrawCocktailControls()
        {
            if (GUI.Button(ToggleRect, panelOpen ? "칵테일 판정 닫기 [P]" : "칵테일 판정 [P]")) panelOpen = !panelOpen;
            if (!panelOpen) return;
            GUILayout.BeginArea(PanelRect, GUI.skin.box);
            GUILayout.Label("칵테일 판정  |  Enter: 선택한 잔 판정");
            var glasses = world.Items.Where(IsServingGlass).OrderBy(body => body.Id).ToArray();
            int glassIndex = Array.IndexOf(glasses, targetGlass);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30)) && glasses.Length > 0)
                SelectGlass(glasses[(glassIndex - 1 + glasses.Length) % glasses.Length]);
            GUILayout.Label("잔: " + (IsServingGlass(targetGlass) ? targetGlass.displayName : "선택 가능한 잔 없음"));
            if (GUILayout.Button(">", GUILayout.Width(30)) && glasses.Length > 0)
                SelectGlass(glasses[(glassIndex + 1) % glasses.Length]);
            GUILayout.EndHorizontal();
            int recipeIndex = Array.FindIndex(orderableRecipes, recipe => recipe.id == requestedRecipeId) + 1;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30))) CycleRecipe(recipeIndex - 1);
            GUILayout.Label("요청: " + (recipeIndex > 0 ? orderableRecipes[recipeIndex - 1].displayName : "자유 제조 (자동 탐지)"));
            if (GUILayout.Button(">", GUILayout.Width(30))) CycleRecipe(recipeIndex + 1);
            GUILayout.EndHorizontal();
            if (recipeIndex > 0)
            {
                var recipe = orderableRecipes[recipeIndex - 1];
                GUILayout.Label(string.Join(", ", recipe.ingredients.Select(part =>
                    (part.item != null ? part.item.displayName : part.ingredientId) + " " + part.targetMl.ToString("0.#") + "ml")));
                GUILayout.Label("잔 " + recipe.glassId + " / 얼음 " + recipe.iceRequirement
                    + " / 기법 " + recipe.requiredTechnique + " / 셰이킹 얼음 " + recipe.shakeIceRequirement);
            }
            if (GUILayout.Button("판정", GUILayout.Height(27))) EvaluateTarget();
            GUILayout.Label("내용물은 유지됩니다. 결과는 마지막 판정 시점 기준입니다.");
            resultScroll = GUILayout.BeginScrollView(resultScroll);
            GUILayout.Label(Message, new GUIStyle(GUI.skin.label) { wordWrap = true });
            if (LastComposition != null)
                GUILayout.Label("기법 " + LastComposition.GetEffectiveTechniques() + " / 얼음 " + LastComposition.IceCount
                    + "개 / 온도 " + LastComposition.AverageTemperatureC.ToString("0.#") + "°C");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void CycleRecipe(int index)
        {
            int count = orderableRecipes.Length + 1;
            index = (index + count) % count;
            SelectRecipe(index == 0 ? string.Empty : orderableRecipes[index - 1].id);
        }
    }
}
