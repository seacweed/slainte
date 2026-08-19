"use strict";

const assert = require("node:assert/strict");
const math = require("./color-math.js");

function approximately(actual, expected, tolerance = 0.0001) {
  assert.ok(
    Math.abs(actual - expected) <= tolerance,
    `Expected ${actual} to be within ${tolerance} of ${expected}`
  );
}

const single = math.mixIngredients([
  { r: 20, g: 40, b: 60, a: 0.5, volume: 30 }
]);
assert.deepEqual(single, { r: 20, g: 40, b: 60, a: 0.5, totalVolume: 30 });

const vodkaLemon = math.mixIngredients([
  { r: 0.4433962 * 255, g: 0.49169675 * 255, b: 255, a: 0.40392157, volume: 50 },
  { r: 255, g: 0.44841516 * 255, b: 0.28627455 * 255, a: 0.5058824, volume: 30 }
]);
approximately(vodkaLemon.r / 255, 0.652122625);
approximately(vodkaLemon.g / 255, 0.47546615375);
approximately(vodkaLemon.b / 255, 0.73235295625);
approximately(vodkaLemon.a, 0.44215688125);
assert.equal(math.rgbaToHex(vodkaLemon), "#A679BB71");

const ignoresZeroVolume = math.mixIngredients([
  { r: 255, g: 0, b: 0, a: 1, volume: 10 },
  { r: 0, g: 0, b: 255, a: 1, volume: 0 }
]);
assert.equal(math.rgbToHex(ignoresZeroVolume), "#FF0000");

assert.deepEqual(
  math.mixIngredients([{ r: 255, g: 255, b: 255, a: 1, volume: 0 }]),
  { r: 0, g: 0, b: 0, a: 0, totalVolume: 0 }
);

assert.deepEqual(math.parseHex("#abc"), { r: 170, g: 187, b: 204 });
assert.deepEqual(math.parseHex("A679BB"), { r: 166, g: 121, b: 187 });
assert.deepEqual(math.parseHex("#abcd"), { r: 170, g: 187, b: 204, a: 221 / 255 });
assert.deepEqual(math.parseHex("#A679BB71"), { r: 166, g: 121, b: 187, a: 113 / 255 });
assert.equal(math.parseHex("not-a-color"), null);

console.log("color-math: all tests passed");
