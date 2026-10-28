using Slainte.Shared.Lifecycle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Slainte.Shared.Display;

// Shown on hover over a locked ingredient in the shop. Explains how to unlock it.
// Mirrors LiquorBottleInfoCard's singleton/fixed-slot pattern.
public class IngredientUnlockTooltip : SceneSingleton<IngredientUnlockTooltip>
{
    [Header("Content")]
    [SerializeField] private RectTransform rect;

    [Header("Recipe Book Hint (icon/name set dynamically, hint label is static in-prefab)")]
    [SerializeField] private GameObject      recipeBookGroup;
    [SerializeField] private Image           recipeBookIconImage;
    [SerializeField] private TextMeshProUGUI recipeBookNameText;

    [Header("Episode Hint (silhouette + '???' are static in-prefab)")]
    [SerializeField] private GameObject episodeGroup;

    protected override void Awake()
    {
        base.Awake();
        gameObject.SetActive(false);
    }

    public void Show(LiquorBottleDef def, RectTransform targetRect)
    {
        if (def == null || rect == null || targetRect == null) return;

        gameObject.SetActive(true);

        bool showRecipeBook = def.unlockHintType == IngredientUnlockHintType.RecipeBook;
        bool showEpisode    = def.unlockHintType == IngredientUnlockHintType.Episode;

        if (recipeBookGroup != null) recipeBookGroup.SetActive(showRecipeBook);
        if (showRecipeBook)
        {
            if (recipeBookIconImage != null) recipeBookIconImage.sprite = def.recipeBookIcon;
            if (recipeBookNameText  != null) recipeBookNameText.text    = def.recipeBookName;
        }

        if (episodeGroup != null) episodeGroup.SetActive(showEpisode);

        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        UpdatePosition(targetRect);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void UpdatePosition(RectTransform targetRect)
    {
        GameAspect.PlaceBesideTarget(rect, targetRect);
    }
}
