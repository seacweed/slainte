using System;
using Slainte.Business;
using Slainte.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DeliverySystemValidator
{
    private const string BusinessScenePath = ProjectScenePaths.Business;
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

            BusinessFlowBootstrap flowBootstrap =
                UnityEngine.Object.FindFirstObjectByType<BusinessFlowBootstrap>();
            BusinessShiftController shiftController =
                UnityEngine.Object.FindFirstObjectByType<BusinessShiftController>();
            if (flowBootstrap != null)
            {
                flowBootstrap.StopAllCoroutines();
                flowBootstrap.enabled = false;
            }
            if (shiftController != null) shiftController.enabled = false;

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
                DeliveryCharacterPresenter presenter =
                    UnityEngine.Object.FindFirstObjectByType<DeliveryCharacterPresenter>(
                        FindObjectsInactive.Include);
                if (presenter != null && presenter.gameObject.activeSelf)
                {
                    if (EditorApplication.timeSinceStartup - stageStartedAt > 5d)
                        throw new InvalidOperationException(
                            "Delivery character remained visible after the purchase presentation.");
                    return;
                }

                RecipeBookUI recipeBook =
                    UnityEngine.Object.FindFirstObjectByType<RecipeBookUI>(FindObjectsInactive.Include);
                Require(presenter != null && !presenter.gameObject.activeSelf,
                    "Delivery character remained visible after the exit slide.");
                Require(shelf.IsDeliveryOpen,
                    "Delivery panel closed when the purchase presentation ended.");
                Require(shelf.BlocksRecipeBook,
                    "Delivery session ended when only the purchase presentation should end.");
                Require(recipeBook == null || recipeBook.IsTemporarilyBlocked,
                    "Recipe-book controls were restored while delivery remained open.");

                shelf.SetDeliveryAvailable(false, "검증용 배송 금지");
                Require(!shelf.IsDeliveryOpen, "Disabled delivery panel remained open.");
                Require(!shelf.DeliveryTabButton.interactable,
                    "Disabled delivery tab remained interactable.");
                Require(!shelf.TryOpenDelivery(), "Disabled delivery tab reopened the panel.");
                Require(!shelf.BlocksRecipeBook,
                    "Recipe-book input remained blocked after the hidden character session ended.");
                Require(recipeBook == null || !recipeBook.IsTemporarilyBlocked,
                    "Recipe-book controls remained disabled after delivery closed.");

                shelf.SetDeliveryAvailable(true);
                Require(shelf.DeliveryTabButton.interactable,
                    "Re-enabled delivery tab did not become interactable.");
                Finish(true,
                    "customer coexistence, behind-counter layer, copied shop prefab, success-only purchase presentation, rapid purchases, timer, recipe-book lock, and disabled state passed.");
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
        Require(catalog.bottles != null && catalog.bottles.Count == 15,
            "Shared shop catalog does not contain the fifteen CSV products.");
        Require(catalog.deliveryPanelPrefab != null,
            "The copied delivery shop prefab is not connected.");
        Require(catalog.deliveryPanelPrefab.GetComponent<ShopUIManager>() == null,
            "The delivery prefab still depends on the RestScene ShopUIManager.");
        Require(catalog.deliveryItemSlotPrefab != null,
            "The delivery item slot prefab is not connected to the catalog.");
        Require(catalog.deliveryCategoryButtonPrefab != null,
            "The delivery category button prefab is not connected to the catalog.");
        Require(catalog.deliveryPortrait != null,
            "The delivery character portrait is not connected.");
        Require(DeliveryItemSlotUI.CalculatePrice(2000, 2f) == 4000,
            "Delivery price is not exactly twice the normal price.");
    }

    private static void ValidateRuntimeStart(LiquorShelfUI shelf, GameProgress progress)
    {
        ValidateZeroPricePurchase(progress);

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
        Require(presenter != null && !presenter.gameObject.activeSelf && !presenter.IsShown,
            "Opening delivery showed the character before a purchase succeeded.");
        Require(presenter.HiddenAnchoredX < presenter.VisibleAnchoredX,
            "Delivery character hidden position is not left of its visible position.");
        Require(presenter.VisibleAnchoredX - presenter.HiddenAnchoredX
                > ((RectTransform)presenter.transform).rect.width,
            "Delivery character hidden position is not fully outside the left screen edge.");
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

        DeliveryItemSlotUI[] slots = panel.GetComponentsInChildren<DeliveryItemSlotUI>(true);
        Require(slots.Length > 0 && shelf.DeliveryVisibleItemCount > 0,
            "Delivery panel did not expose its item slots.");
        DeliveryItemSlotUI slot = Array.Find(slots, candidate =>
            candidate != null
            && candidate.Definition != null
            && candidate.Definition.MaxAmount >= candidate.Definition.unitVolume * 2f);
        Require(slot != null,
            "Delivery test could not find an item with room for two rapid purchases.");
        LiquorBottleDef definition = slot.Definition;
        Require(definition != null, "Delivery item slot has no product definition.");

        progress.AddMoney(-progress.CurrentMoney);
        progress.SetBottleAmount(definition.id, 0f);
        slot.Refresh();
        Require(!slot.TryPurchase(),
            "Delivery purchase succeeded without sufficient funds.");
        Require(!presenter.gameObject.activeSelf && !presenter.IsShown,
            "Failed delivery purchase showed the character.");

        progress.AddMoney(1000000);
        int beforeMoney = progress.CurrentMoney;
        int expectedPrice = definition.price * 2;
        Require(slot.CurrentPrice == expectedPrice, "Displayed delivery price is not 2x.");
        Require(slot.TryPurchase(), "Delivery purchase was rejected despite sufficient funds.");
        Require(presenter.gameObject.activeSelf && presenter.IsShown && presenter.IsTransitioning,
            "Successful delivery purchase did not start the character entrance.");
        Require(slot.TryPurchase(), "Rapid second delivery purchase was rejected.");
        Require(progress.CurrentMoney == beforeMoney - expectedPrice * 2,
            "Rapid delivery purchases deducted the wrong amount.");
        Require(Mathf.Approximately(
                progress.GetBottleAmount(definition.id, 0f),
                definition.unitVolume * 2f),
            "Rapid delivery purchases did not add two bottle units.");
        Require(presenter.gameObject.activeSelf && presenter.IsShown && presenter.IsTransitioning,
            "Rapid delivery purchase left the character presentation in an invalid state.");
        Require(Mathf.Approximately(
                presenter.TargetAnchoredX,
                presenter.VisibleAnchoredX),
            "Rapid delivery purchase is not targeting the visible position.");
    }

    private static void ValidateZeroPricePurchase(GameProgress progress)
    {
        const string TestBottleId = "__delivery_validator_free_purchase";
        GameObject host = new GameObject("__ZeroPricePurchaseValidation");
        LiquorBottleDef definition = ScriptableObject.CreateInstance<LiquorBottleDef>();

        try
        {
            definition.id = TestBottleId;
            definition.displayName = "Zero Price Validation";
            definition.bottleCount = 2;
            definition.unitVolume = 1f;
            definition.strangeCoinPrice = 0;

            progress.SetBottleAmount(TestBottleId, 0f);
            int beforeCoins = progress.GetAffinity(StrangeCoinShopCurrency.VarName);

            ItemSlotUI slot = host.AddComponent<ItemSlotUI>();
            slot.Setup(definition, new StrangeCoinShopCurrency());

            Require(slot.CurrentPrice == 0, "Zero strange-coin price was not displayed as zero.");
            Require(slot.TryPurchase(), "Zero strange-coin price was not purchasable.");
            Require(progress.GetAffinity(StrangeCoinShopCurrency.VarName) == beforeCoins,
                "Zero-price purchase changed the strange-coin balance.");
            Require(Mathf.Approximately(progress.GetBottleAmount(TestBottleId, 0f), 1f),
                "Zero-price purchase did not add inventory.");

            progress.SetBottleAmount(TestBottleId, 0f);
            definition.strangeCoinPrice = -1;
            slot.Setup(definition, new StrangeCoinShopCurrency());
            Require(!slot.TryPurchase(), "Negative strange-coin price was purchasable.");
        }
        finally
        {
            UnityEngine.Object.Destroy(host);
            UnityEngine.Object.Destroy(definition);
        }
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
