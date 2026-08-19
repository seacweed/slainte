(function (root, factory) {
  const api = factory();
  root.RecipeColorMath = api;

  if (typeof module === "object" && module.exports) {
    module.exports = api;
  }
}(typeof globalThis !== "undefined" ? globalThis : this, function () {
  "use strict";

  function clamp(value, minimum, maximum) {
    const number = Number(value);
    if (!Number.isFinite(number)) {
      return minimum;
    }

    return Math.min(maximum, Math.max(minimum, number));
  }

  function mixIngredients(ingredients) {
    const validIngredients = Array.isArray(ingredients)
      ? ingredients.filter((ingredient) => Number(ingredient.volume) > 0)
      : [];

    const totalVolume = validIngredients.reduce(
      (sum, ingredient) => sum + Math.max(0, Number(ingredient.volume) || 0),
      0
    );

    if (totalVolume <= 0) {
      return { r: 0, g: 0, b: 0, a: 0, totalVolume: 0 };
    }

    const mixed = validIngredients.reduce((result, ingredient) => {
      const weight = Math.max(0, Number(ingredient.volume) || 0) / totalVolume;
      result.r += clamp(ingredient.r, 0, 255) * weight;
      result.g += clamp(ingredient.g, 0, 255) * weight;
      result.b += clamp(ingredient.b, 0, 255) * weight;
      result.a += clamp(ingredient.a, 0, 1) * weight;
      return result;
    }, { r: 0, g: 0, b: 0, a: 0 });

    return {
      r: clamp(mixed.r, 0, 255),
      g: clamp(mixed.g, 0, 255),
      b: clamp(mixed.b, 0, 255),
      a: clamp(mixed.a, 0, 1),
      totalVolume
    };
  }

  function channelToHex(value) {
    return Math.round(clamp(value, 0, 255)).toString(16).padStart(2, "0").toUpperCase();
  }

  function rgbToHex(color) {
    return `#${channelToHex(color.r)}${channelToHex(color.g)}${channelToHex(color.b)}`;
  }

  function rgbaToHex(color) {
    return `${rgbToHex(color)}${channelToHex(clamp(color.a, 0, 1) * 255)}`;
  }

  function colorToCss(color, alphaOverride) {
    const alpha = alphaOverride === undefined ? color.a : alphaOverride;
    return `rgba(${Math.round(clamp(color.r, 0, 255))}, ${Math.round(clamp(color.g, 0, 255))}, ${Math.round(clamp(color.b, 0, 255))}, ${clamp(alpha, 0, 1).toFixed(4)})`;
  }

  function parseHex(value) {
    const normalized = String(value || "").trim().replace(/^#/, "");
    if (![3, 4, 6, 8].includes(normalized.length) || !/^[0-9a-fA-F]+$/.test(normalized)) {
      return null;
    }

    const expanded = normalized.length <= 4
      ? normalized.split("").map((character) => character + character).join("")
      : normalized;

    const parsed = {
      r: parseInt(expanded.slice(0, 2), 16),
      g: parseInt(expanded.slice(2, 4), 16),
      b: parseInt(expanded.slice(4, 6), 16)
    };

    if (expanded.length === 8) {
      parsed.a = parseInt(expanded.slice(6, 8), 16) / 255;
    }

    return parsed;
  }

  return {
    clamp,
    mixIngredients,
    rgbToHex,
    rgbaToHex,
    colorToCss,
    parseHex
  };
}));
