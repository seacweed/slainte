using System;
using Slainte.Business;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DeliverySystemValidator
{
    private const string BusinessScenePath = "Assets/BusinessScene.unity";
    private const string RunningKey = "Slainte.DeliverySystemValidator.Running";
    private static double startedAt;
    private static int stage;
    private static double stageStartedAt;

    [MenuItem("Slainte/Business/Validate Delivery System")]
    public static void RunFromMenu()
    {
        Begin(false);
    }

    public static void RunFromCommandLine()
    {
        Begin(true);
    }

    private static void Begin(bool commandLine)
    {
        ValidateAssets();

        Scene scene = EditorSceneManager.OpenScene(BusinessScenePath, OpenSceneMode.Single);
        GameObject isolationHost = new GameObject("__DeliveryValidationIsolation");
        SceneManager.MoveGameObjectToScene(isolationHost, scene);
        PlaytestProgressIsolation.Attach(isolationHost);

        SessionState.SetBool(RunningKey, true);
        SessionState.SetBool(RunningKey + ".CommandLine", commandLine);
        startedAt = EditorApplication.timeSinceStartup;
        stage = 0;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        startedAt = EditorApplication.timeSinceStartup;
        stage = 0;
        EditorApplication.update -= ValidateOnUpdate;
        EditorApplication.update += ValidateOnUpdate;
    }

    private static void ValidateOnUpdate()
    {
        if (!EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup - startedAt > 30d)
        {
            Finish(false, "Timed out waiting for the delivery UI.");
            return;
        }

        try
        {
            LiquorShelfUI shelf = UnityEngine.Object.FindFirstObjectByType<LiquorShelfUI>();
            PlaytestProgressIsolation isolation =
                UnityEngine.Object.FindFirstObjectByType<PlaytestProgressIsolation>();
            GameModeManager modeManager =
                UnityEngine.Object.FindFirstObjectByType<GameModeManager>();
            if (shelf == null
                || isolation == null
                || !isolation.IsReady
                || GameProgress.Instance == null
                || modeManager == null
                || shelf.DeliveryTabButton == null)
            {
                return;
            }

            if (modeManager.CurrentMode != GameMode.CraftingMode)
            {
                modeManager.RequestModeChange(GameMode.CraftingMode);
                return;
            }

            if (stage == 0)
            {
                ValidateRuntimeStart(shelf, GameProgress.Instance);
                stage = 1;
                stageStartedAt = EditorApplication.timeSinceStartup;
                return;
            }

            if (stage == 1)
            {
                if (shelf.BlocksRecipeBook)
                {
                    if (EditorApplication.timeSinceStartup - stageStartedAt > 3d)
                        throw new InvalidOperationException(
                            "Recipe book input remained blocked after the delivery character exited.");
                    return;
                }

                DeliveryCharacterPresenter presenter =
                    UnityEngine.Object.FindFirstObjectByType<DeliveryCharacterPresenter>(
                        FindObjectsInactive.Include);
                RecipeBookUI recipeBook =
                    UnityEngine.Object.FindFirstObjectByType<RecipeBookUI>(FindObjectsInactive.Include);
                Require(presenter != null && !presenter.gameObject.activeSelf,
                    "Delivery character remained visible after the exit slide.");
                Require(recipeBook == null || !recipeBook.IsTemporarilyBlocked,
                    "Recipe-book controls remained disabled after the character exited.");

                shelf.SetDeliveryAvailable(true);
                Require(shelf.DeliveryTabButton.interactable,
                    "Re-enabled delivery tab did not become interactable.");
                Finish(true,
                    "customer coexistence, behind-counter layer, copied shop prefab, slides, recipe-book lock, purchase, timer, and disabled state passed.");
            }
        }
        catch (Exception exception)
        {
            Finish(false, exception.ToString());
        }
    }

    private static void ValidateAssets()
    {
        LiquorShopCatalog catalog = LiquorShopCatalog.LoadDefault();
        Require(catalog != null, "Default liquor shop catalog is missing.");
        Require(catalog.categories != null && catalog.categories.Count == 6,
            "Shared shop catalog does not contain the six configured categories.");
        Require(catalog.bottles != null && catalog.bottles.Count == 11,
            "Shared shop catalog does not contain the eleven configured products.");
        Require(catalog.categoryButtonPrefab != null && catalog.itemSlotPrefab != null,
            "Shared shop UI prefabs are missing from the catalog.");
        Require(catalog.deliveryPanelPrefab != null,
            "The copied delivery shop prefab is not connected.");
        Require(catalog.deliveryPanelPrefab.GetComponent<ShopUIManager>() == null,
            "The delivery prefab still depends on the RestScene ShopUIManager.");
        Require(catalog.deliveryPortrait != null,
            "The delivery character portrait is not connected.");
        Require(ItemSlotUI.CalculatePrice(2000, 1f) == 2000,
            "Normal shop price calculation changed.");
        Require(ItemSlotUI.CalculatePrice(2000, 2f) == 4000,
            "Delivery price is not exactly twice the normal price.");
    }

    private static void ValidateRuntimeStart(LiquorShelfUI shelf, GameProgress progress)
    {
        float timeScale = Time.timeScale;
        Require(shelf.IsDeliveryAvailable, "Delivery did not start in the available state.");
        Require(shelf.DeliveryTabButton.interactable,
            "Available delivery tab is not interactable.");

        CustomerSpawner customerSpawner =
            UnityEngine.Object.FindFirstObjectByType<CustomerSpawner>(FindObjectsInactive.Include);
        CharacterStage characterStage =
            UnityEngine.Object.FindFirstObjectByType<CharacterStage>(FindObjectsInactive.Include);
        Require(customerSpawner != null && characterStage != null,
            "Customer presentation objects were not found.");
        customerSpawner.ShowCustomers(new[] { "yukari" });
        CharacterView[] customers = characterStage.GetComponentsInChildren<CharacterView>(true);
        Require(customers.Length > 0,
            "Test customer was not visible before delivery opened.");

        RecipeBookUI recipeBook =
            UnityEngine.Object.FindFirstObjectByType<RecipeBookUI>(FindObjectsInactive.Include);
        Require(recipeBook != null, "BusinessScene recipe book was not found.");
        recipeBook.Open();
        Require(recipeBook.IsOpen, "Recipe book did not open before the delivery test.");

        Require(shelf.TryOpenDelivery(), "Delivery panel did not open.");
        Require(shelf.IsDeliveryOpen, "Delivery panel visibility state was not updated.");
        Require(shelf.BlocksRecipeBook,
            "Delivery did not block recipe-book input while active.");
        Require(recipeBook.IsTemporarilyBlocked,
            "Delivery did not disable recipe-book controls while active.");
        Require(!recipeBook.IsOpen,
            "Delivery did not close the open recipe book.");
        recipeBook.Toggle();
        Require(!recipeBook.IsOpen,
            "Recipe book reopened while delivery was active.");
        Require(Mathf.Approximately(timeScale, Time.timeScale),
            "Opening delivery changed the gameplay time scale.");

        DeliveryShopPanelUI panel = shelf.GetComponentInChildren<DeliveryShopPanelUI>(true);
        Require(panel != null && panel.ShowFirstCategory(),
            "Delivery shop did not expose the copied ingredient category screen.");
        DeliveryCharacterPresenter presenter =
            UnityEngine.Object.FindFirstObjectByType<DeliveryCharacterPresenter>(
                FindObjectsInactive.Include);
        Require(presenter != null && presenter.gameObject.activeSelf && presenter.IsShown,
            "Delivery character did not start sliding in from the left.");
        Require(presenter.IsTransitioning,
            "Delivery character entrance slide was not started.");
        Require(presenter.HiddenAnchoredX < presenter.VisibleAnchoredX,
            "Delivery character hidden position is not left of its visible position.");
        Require(presenter.VisibleAnchoredX - presenter.HiddenAnchoredX
                > ((RectTransform)presenter.transform).rect.width,
            "Delivery character hidden position is not fully outside the left screen edge.");
        Require(Mathf.Approximately(
                presenter.TargetAnchoredX,
                presenter.VisibleAnchoredX),
            "Delivery character entrance is not targeting the visible position to the right.");
        Require(presenter.transform.parent == characterStage.transform,
            "Delivery character is not in the customer back-layer container.");
        Transform barCounter = characterStage.transform.parent != null
            ? characterStage.transform.parent.Find("BarCounter")
            : null;
        Require(barCounter != null
                && characterStage.transform.GetSiblingIndex() < barCounter.GetSiblingIndex(),
            "Delivery character layer is not behind the bar counter.");
        Require(characterStage.GetComponentsInChildren<CharacterView>(true).Length >= customers.Length,
            "Opening delivery removed the active customer.");

        ItemSlotUI[] slots = panel.GetComponentsInChildren<ItemSlotUI>(true);
        Require(slots.Length > 0 && shelf.DeliveryVisibleItemCount > 0,
            "Delivery panel did not reuse the shop item slots.");
        ItemSlotUI slot = slots[0];
        LiquorBottleDef definition = slot.Definition;
        Require(definition != null, "Delivery item slot has no product definition.");

        progress.AddMoney(1000000);
        progress.SetBottleAmount(definition.id, 0f);
        slot.Refresh();
        int beforeMoney = progress.CurrentMoney;
        int expectedPrice = definition.price * 2;
        Require(slot.CurrentPrice == expectedPrice, "Displayed delivery price is not 2x.");
        Require(slot.TryPurchase(), "Delivery purchase was rejected despite sufficient funds.");
        Require(progress.CurrentMoney == beforeMoney - expectedPrice,
            "Delivery purchase deducted the wrong amount.");
        Require(Mathf.Approximately(
                progress.GetBottleAmount(definition.id, 0f),
                definition.unitVolume),
            "Delivery purchase did not add one bottle unit.");

        shelf.SetDeliveryAvailable(false, "검증용 배송 금지");
        Require(!shelf.IsDeliveryOpen, "Disabled delivery panel remained open.");
        Require(!shelf.DeliveryTabButton.interactable,
            "Disabled delivery tab remained interactable.");
        Require(Mathf.Approximately(shelf.DeliveryTabAlpha, 0.42f),
            "Disabled delivery tab did not become gray/translucent.");
        Require(!shelf.TryOpenDelivery(), "Disabled delivery tab reopened the panel.");
        Require(shelf.BlocksRecipeBook,
            "Recipe-book input was restored before the character exit slide completed.");
        Require(presenter.IsTransitioning,
            "Delivery character exit slide was not started.");
        Require(Mathf.Approximately(
                presenter.TargetAnchoredX,
                presenter.HiddenAnchoredX),
            "Delivery character exit is not targeting the hidden position to the left.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(bool success, string message)
    {
        EditorApplication.update -= ValidateOnUpdate;
        SessionState.EraseBool(RunningKey);
        bool commandLine = SessionState.GetBool(RunningKey + ".CommandLine", false);
        SessionState.EraseBool(RunningKey + ".CommandLine");

        if (success) Debug.Log("[DeliverySystemValidator] PASS: " + message);
        else Debug.LogError("[DeliverySystemValidator] FAIL: " + message);

        if (commandLine) EditorApplication.Exit(success ? 0 : 1);
        else EditorApplication.isPlaying = false;
    }
}
