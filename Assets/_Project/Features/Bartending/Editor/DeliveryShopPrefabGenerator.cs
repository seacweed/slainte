using System;
using Slainte.Editor;
using Slainte.EditorTools;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class DeliveryShopPrefabGenerator
{
    private const string RestScenePath = ProjectScenePaths.Rest;
    private const string PrefabFolder = BartendingAssetPaths.DeliveryShopPrefabRoot;
    private const string PrefabPath = PrefabFolder + "DeliveryShopPanel.prefab";
    private const string CatalogPath = "Assets/Resources/Shop/LiquorShopCatalog.asset";

    [MenuItem("Slainte/Business/Regenerate Delivery Shop Prefab")]
    public static void GenerateFromMenu()
    {
        Generate();
        Debug.Log("[DeliveryShopPrefabGenerator] 배송 상점 프리팹을 갱신했습니다.");
    }

    public static void GenerateFromCommandLine()
    {
        try
        {
            Generate();
            Debug.Log("[DeliveryShopPrefabGenerator] PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError("[DeliveryShopPrefabGenerator] FAIL: " + exception);
            EditorApplication.Exit(1);
        }
    }

    private static void Generate()
    {
        SceneSetup[] previousSetup = EditorSceneManager.GetSceneManagerSetup();
        GameObject clone = null;
        try
        {
            EnsureFolder("Assets/Prefabs", "Business");
            EditorSceneManager.OpenScene(RestScenePath, OpenSceneMode.Single);

            ShopUIManager source = UnityEngine.Object.FindFirstObjectByType<ShopUIManager>(
                FindObjectsInactive.Include);
            if (source == null)
                throw new InvalidOperationException("RestScene에서 ShopUIManager를 찾지 못했습니다.");

            clone = UnityEngine.Object.Instantiate(source.gameObject);
            clone.name = "DeliveryShopPanel";
            clone.transform.SetParent(null, false);
            clone.SetActive(true);

            ShopUIManager copiedShop = clone.GetComponent<ShopUIManager>();
            DeliveryShopPanelUI delivery = clone.AddComponent<DeliveryShopPanelUI>();
            delivery.ConfigureFromShopTemplate(copiedShop);
            RebindButtons(clone, copiedShop, delivery);

            if (copiedShop.homePanel != null) copiedShop.homePanel.SetActive(false);
            if (copiedShop.upgradePanel != null) copiedShop.upgradePanel.SetActive(false);
            UnityEngine.Object.DestroyImmediate(copiedShop);

            RectTransform rect = clone.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            clone.SetActive(false);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(clone, PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("배송 상점 프리팹 저장에 실패했습니다.");

            LiquorShopCatalog catalog = AssetDatabase.LoadAssetAtPath<LiquorShopCatalog>(CatalogPath);
            if (catalog == null)
                throw new InvalidOperationException("공용 상점 카탈로그를 찾지 못했습니다.");

            catalog.deliveryPanelPrefab = prefab.GetComponent<DeliveryShopPanelUI>();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            if (previousSetup != null && previousSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
        }
    }

    private static void RebindButtons(
        GameObject root,
        ShopUIManager copiedShop,
        DeliveryShopPanelUI delivery)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            bool bindCategories = false;
            bool bindClose = string.Equals(
                button.name,
                "ExitButton",
                StringComparison.OrdinalIgnoreCase);

            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            {
                UnityEngine.Object target = button.onClick.GetPersistentTarget(i);
                string method = button.onClick.GetPersistentMethodName(i);
                if (target != copiedShop && target != null) continue;

                bindCategories |= method == nameof(ShopUIManager.ShowHome)
                    || method == nameof(ShopUIManager.OpenIngredients);
                bindClose |= method == nameof(BaseUIManager.CloseUI);
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            }

            if (bindCategories)
                UnityEventTools.AddPersistentListener(button.onClick, delivery.ShowCategories);
            if (bindClose)
                UnityEventTools.AddPersistentListener(button.onClick, delivery.RequestClose);
        }
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
    }
}
