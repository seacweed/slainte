(function () {
  "use strict";

  const math = window.RecipeColorMath;
  const recipeData = window.RECIPE_COLOR_DATA || { ingredients: [], recipes: [] };
  const ingredientCatalog = new Map(
    (recipeData.ingredients || []).map((ingredient) => [ingredient.id, ingredient])
  );

  const elements = {
    dataSummary: document.getElementById("data-summary"),
    dataNotice: document.getElementById("data-notice"),
    recipeSelect: document.getElementById("recipe-select"),
    loadRecipeButton: document.getElementById("load-recipe-button"),
    addIngredientButton: document.getElementById("add-ingredient-button"),
    ingredientsList: document.getElementById("ingredients-list"),
    ingredientTemplate: document.getElementById("ingredient-template"),
    resultLiquid: document.getElementById("result-liquid"),
    resultHex: document.getElementById("result-hex"),
    resultRgb: document.getElementById("result-rgb"),
    resultAlpha: document.getElementById("result-alpha"),
    resultUnity: document.getElementById("result-unity"),
    resultDisplayAlpha: document.getElementById("result-display-alpha"),
    mixStatus: document.getElementById("mix-status"),
    compositionList: document.getElementById("composition-list"),
    copyResultButton: document.getElementById("copy-result-button")
  };

  const fallbackPalette = [
    { r: 91, g: 129, b: 255, a: 0.72 },
    { r: 255, g: 151, b: 76, a: 0.82 },
    { r: 228, g: 92, b: 158, a: 0.74 },
    { r: 79, g: 198, b: 149, a: 0.68 },
    { r: 166, g: 104, b: 224, a: 0.8 }
  ];

  let nextIngredientId = 1;
  let ingredients = [];

  function createIngredient(overrides) {
    const base = fallbackPalette[(nextIngredientId - 1) % fallbackPalette.length];
    const ingredient = {
      localId: nextIngredientId++,
      sourceItemId: "",
      name: `재료 ${nextIngredientId - 1}`,
      r: base.r,
      g: base.g,
      b: base.b,
      a: base.a,
      volume: 30
    };

    return Object.assign(ingredient, overrides || {});
  }

  function ingredientFromRecipe(portion, index, missingItems) {
    const source = ingredientCatalog.get(portion.itemId);
    if (!source) {
      missingItems.push(portion.itemId);
      return createIngredient({
        sourceItemId: portion.itemId,
        name: portion.itemId || `재료 ${index + 1}`,
        r: 255,
        g: 255,
        b: 255,
        a: 1,
        volume: Number(portion.targetMl) || 0
      });
    }

    return createIngredient({
      sourceItemId: source.id,
      name: source.displayName || source.id,
      r: Math.round(math.clamp(source.color.r, 0, 1) * 255),
      g: Math.round(math.clamp(source.color.g, 0, 1) * 255),
      b: Math.round(math.clamp(source.color.b, 0, 1) * 255),
      a: math.clamp(source.color.a, 0, 1),
      volume: Number(portion.targetMl) || 0
    });
  }

  function populateRecipeSelect() {
    const recipes = Array.isArray(recipeData.recipes) ? recipeData.recipes : [];
    recipes.forEach((recipe) => {
      const option = document.createElement("option");
      option.value = recipe.id;
      option.textContent = `${recipe.displayName || recipe.id} · ${recipe.id}`;
      elements.recipeSelect.appendChild(option);
    });

    const ingredientCount = (recipeData.ingredients || []).length;
    elements.dataSummary.textContent = recipes.length > 0
      ? `내장 재료 ${ingredientCount}개 · 레시피 ${recipes.length}개`
      : "내장 데이터 없음 · 직접 배합 모드";

    elements.loadRecipeButton.disabled = recipes.length === 0;
  }

  function setNotice(messages) {
    const visibleMessages = (messages || []).filter(Boolean);
    elements.dataNotice.hidden = visibleMessages.length === 0;
    elements.dataNotice.textContent = visibleMessages.join(" ");
  }

  function loadSelectedRecipe() {
    const recipeId = elements.recipeSelect.value;
    const recipe = (recipeData.recipes || []).find((candidate) => candidate.id === recipeId);

    if (!recipe) {
      ingredients = [
        createIngredient({ name: "재료 1", volume: 50 }),
        createIngredient({ name: "재료 2", volume: 30 })
      ];
      setNotice([]);
      renderIngredients();
      return;
    }

    const missingItems = [];
    ingredients = (recipe.ingredients || []).map((portion, index) =>
      ingredientFromRecipe(portion, index, missingItems)
    );

    if (ingredients.length === 0) {
      ingredients = [createIngredient({ name: "빈 레시피", volume: 0 })];
    }

    const messages = [];
    if (missingItems.length > 0) {
      messages.push(`색상 데이터를 찾지 못한 재료: ${missingItems.join(", ")}. 흰색으로 불러왔습니다.`);
    }

    const allOpaqueWhite = ingredients.length > 0 && ingredients.every((ingredient) =>
      ingredient.r === 255 && ingredient.g === 255 && ingredient.b === 255 && ingredient.a === 1
    );
    if (allOpaqueWhite) {
      messages.push("이 레시피의 재료 색상이 모두 기본 흰색입니다. 아래에서 직접 색을 바꿀 수 있습니다.");
    }

    setNotice(messages);
    renderIngredients();
  }

  function renderIngredients() {
    elements.ingredientsList.replaceChildren();

    ingredients.forEach((ingredient, index) => {
      const fragment = elements.ingredientTemplate.content.cloneNode(true);
      const card = fragment.querySelector(".ingredient-card");
      card.dataset.ingredientId = String(ingredient.localId);
      card.querySelector(".ingredient-number").textContent = String(index + 1).padStart(2, "0");
      card.querySelector("[data-action='remove']").setAttribute(
        "aria-label",
        `${ingredient.name || `재료 ${index + 1}`} 삭제`
      );
      elements.ingredientsList.appendChild(fragment);
      syncIngredientCard(ingredient.localId);
    });

    updateResult();
  }

  function getCard(localId) {
    return elements.ingredientsList.querySelector(`[data-ingredient-id="${localId}"]`);
  }

  function getIngredient(localId) {
    return ingredients.find((ingredient) => ingredient.localId === localId);
  }

  function syncIngredientCard(localId, skippedField) {
    const ingredient = getIngredient(localId);
    const card = getCard(localId);
    if (!ingredient || !card) {
      return;
    }

    const values = {
      name: ingredient.name,
      "color-picker": math.rgbToHex(ingredient),
      hex: math.rgbaToHex(ingredient),
      volume: formatNumber(ingredient.volume, 2),
      r: Math.round(ingredient.r),
      g: Math.round(ingredient.g),
      b: Math.round(ingredient.b),
      "alpha-range": Math.round(ingredient.a * 100),
      alpha: Math.round(ingredient.a * 100)
    };

    Object.entries(values).forEach(([field, value]) => {
      if (field === skippedField) {
        return;
      }
      const input = card.querySelector(`[data-field="${field}"]`);
      if (input) {
        input.value = value;
        input.setAttribute("aria-invalid", "false");
      }
    });

    const cssColor = math.colorToCss(ingredient);
    card.style.setProperty("--ingredient-color", cssColor);
    card.querySelector("[data-role='color-preview']").style.backgroundColor = cssColor;
  }

  function handleIngredientInput(event) {
    const input = event.target.closest("[data-field]");
    const card = event.target.closest(".ingredient-card");
    if (!input || !card) {
      return;
    }

    const localId = Number(card.dataset.ingredientId);
    const ingredient = getIngredient(localId);
    const field = input.dataset.field;
    if (!ingredient) {
      return;
    }

    let shouldSync = false;
    switch (field) {
      case "name":
        ingredient.name = input.value;
        break;
      case "color-picker": {
        const parsed = math.parseHex(input.value);
        if (parsed) {
          Object.assign(ingredient, parsed);
          shouldSync = true;
        }
        break;
      }
      case "hex": {
        const parsed = math.parseHex(input.value);
        const includesAlpha = parsed && Number.isFinite(parsed.a);
        input.setAttribute("aria-invalid", includesAlpha ? "false" : "true");
        if (!includesAlpha) {
          return;
        }
        Object.assign(ingredient, parsed);
        shouldSync = true;
        break;
      }
      case "r":
      case "g":
      case "b": {
        if (input.value === "") {
          input.setAttribute("aria-invalid", "true");
          return;
        }
        ingredient[field] = math.clamp(input.value, 0, 255);
        input.setAttribute("aria-invalid", "false");
        shouldSync = true;
        break;
      }
      case "alpha":
      case "alpha-range": {
        if (input.value === "") {
          input.setAttribute("aria-invalid", "true");
          return;
        }
        ingredient.a = math.clamp(input.value, 0, 100) / 100;
        input.setAttribute("aria-invalid", "false");
        shouldSync = true;
        break;
      }
      case "volume":
        ingredient.volume = input.value === "" ? 0 : math.clamp(input.value, 0, 99999);
        input.setAttribute("aria-invalid", "false");
        break;
      default:
        return;
    }

    if (shouldSync) {
      syncIngredientCard(localId, field);
    }

    setNotice([]);
    updateResult();
  }

  function restoreInvalidInput(event) {
    const input = event.target.closest("[data-field]");
    const card = event.target.closest(".ingredient-card");
    if (!input || !card || input.getAttribute("aria-invalid") !== "true") {
      return;
    }

    syncIngredientCard(Number(card.dataset.ingredientId));
  }

  function handleIngredientAction(event) {
    const button = event.target.closest("[data-action='remove']");
    const card = event.target.closest(".ingredient-card");
    if (!button || !card) {
      return;
    }

    const localId = Number(card.dataset.ingredientId);
    ingredients = ingredients.filter((ingredient) => ingredient.localId !== localId);
    if (ingredients.length === 0) {
      ingredients.push(createIngredient({ name: "재료 1", volume: 0 }));
    }
    setNotice([]);
    renderIngredients();
  }

  function addIngredient() {
    ingredients.push(createIngredient());
    setNotice([]);
    renderIngredients();

    const newCard = getCard(ingredients[ingredients.length - 1].localId);
    if (newCard) {
      newCard.querySelector("[data-field='name']").focus();
      newCard.scrollIntoView({ behavior: "smooth", block: "nearest" });
    }
  }

  function updateResult() {
    const result = math.mixIngredients(ingredients);
    const totalVolume = result.totalVolume;
    const roundedRgb = [Math.round(result.r), Math.round(result.g), Math.round(result.b)];
    const displayAlpha = totalVolume > 0 ? Math.max(result.a, 0.05) : 0;

    elements.resultLiquid.style.backgroundColor = math.colorToCss(result);
    elements.resultHex.textContent = math.rgbaToHex(result);
    elements.resultRgb.textContent = roundedRgb.join(" · ");
    elements.resultAlpha.textContent = `${formatNumber(result.a * 100, 1)}%`;
    elements.resultDisplayAlpha.textContent = `${formatNumber(displayAlpha * 100, 1)}%`;
    elements.resultUnity.textContent = [
      result.r / 255,
      result.g / 255,
      result.b / 255,
      result.a
    ].map((value) => formatNumber(value, 4)).join(", ");
    elements.mixStatus.textContent = `${formatNumber(totalVolume, 2)} ml`;

    updateComposition(totalVolume);
  }

  function updateComposition(totalVolume) {
    elements.compositionList.replaceChildren();

    ingredients.forEach((ingredient) => {
      const ratio = totalVolume > 0 ? Math.max(0, ingredient.volume) / totalVolume : 0;
      const percentage = `${formatNumber(ratio * 100, 1)}%`;
      const card = getCard(ingredient.localId);
      if (card) {
        card.querySelector("[data-role='ratio']").textContent = percentage;
        card.querySelector("[data-role='ratio-bar']").style.width = percentage;
      }

      const row = document.createElement("div");
      row.className = "composition-row";

      const swatch = document.createElement("span");
      swatch.className = "composition-swatch";
      swatch.style.backgroundColor = math.colorToCss(ingredient);

      const name = document.createElement("span");
      name.className = "composition-name";
      name.textContent = ingredient.name || "이름 없는 재료";

      const value = document.createElement("span");
      value.className = "composition-value";
      value.textContent = `${formatNumber(ingredient.volume, 2)} ml · ${percentage}`;

      row.append(swatch, name, value);
      elements.compositionList.appendChild(row);
    });
  }

  function formatNumber(value, maximumFractionDigits) {
    return new Intl.NumberFormat("ko-KR", {
      maximumFractionDigits,
      useGrouping: false
    }).format(Number(value) || 0);
  }

  async function copyResult() {
    const value = elements.resultHex.textContent;
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(value);
      } else {
        const textarea = document.createElement("textarea");
        textarea.value = value;
        textarea.setAttribute("readonly", "");
        textarea.style.position = "fixed";
        textarea.style.opacity = "0";
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand("copy");
        textarea.remove();
      }
      elements.copyResultButton.textContent = "복사됨";
    } catch (error) {
      elements.copyResultButton.textContent = "복사 실패";
    }

    window.setTimeout(() => {
      elements.copyResultButton.textContent = "색상 복사";
    }, 1200);
  }

  function initialize() {
    populateRecipeSelect();

    const preferredRecipe = (recipeData.recipes || []).find((recipe) => recipe.id === "vodka_lemon")
      || (recipeData.recipes || [])[0];
    if (preferredRecipe) {
      elements.recipeSelect.value = preferredRecipe.id;
    }

    elements.loadRecipeButton.addEventListener("click", loadSelectedRecipe);
    elements.addIngredientButton.addEventListener("click", addIngredient);
    elements.ingredientsList.addEventListener("input", handleIngredientInput);
    elements.ingredientsList.addEventListener("focusout", restoreInvalidInput);
    elements.ingredientsList.addEventListener("click", handleIngredientAction);
    elements.copyResultButton.addEventListener("click", copyResult);

    loadSelectedRecipe();
  }

  initialize();
}());
