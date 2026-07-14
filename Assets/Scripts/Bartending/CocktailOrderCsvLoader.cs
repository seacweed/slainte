using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Slainte.Bartending
{
    public static class CocktailOrderCsvLoader
    {
        public static CocktailOrderTemplateCatalog LoadTemplatesFromStreamingAssets(
            string dataFolder = "Data",
            string orderTemplatesFileName = "order_templates.csv")
        {
            string dataPath = Path.Combine(Application.streamingAssetsPath, dataFolder);
            string orderTemplatesPath = Path.Combine(dataPath, orderTemplatesFileName);
            return LoadTemplatesFromFile(orderTemplatesPath);
        }

        public static CocktailOrderTemplateCatalog LoadTemplatesFromFile(string orderTemplatesPath)
        {
            CocktailOrderTemplateCatalog catalog = new CocktailOrderTemplateCatalog();

            if (!File.Exists(orderTemplatesPath))
            {
                Debug.LogError($"Order template CSV not found: {orderTemplatesPath}");
                return catalog;
            }

            List<CsvRow> rows = CsvTable.Parse(File.ReadAllText(orderTemplatesPath));
            for (int i = 0; i < rows.Count; i++)
            {
                CsvRow row = rows[i];
                string id = row.Get("id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    Debug.LogWarning("Order template row skipped because id is empty.");
                    continue;
                }

                if (!TryParseOrderType(row.Get("orderType"), out CocktailOrderType orderType))
                {
                    Debug.LogWarning($"Order template '{id}' has unknown order type '{row.Get("orderType")}'.");
                    continue;
                }

                string lineTemplate = row.Get("lineTemplate");
                if (string.IsNullOrWhiteSpace(lineTemplate))
                    lineTemplate = row.Get("line");

                catalog.Add(new CocktailOrderTemplate
                {
                    id = id,
                    orderType = orderType,
                    lineTemplate = lineTemplate,
                    weight = Mathf.Max(0f, ParseFloat(row.Get("weight"), 1f))
                });
            }

            return catalog;
        }

        private static bool TryParseOrderType(string value, out CocktailOrderType orderType)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                orderType = CocktailOrderType.RecipeOrder;
                return true;
            }

            return Enum.TryParse(value.Trim(), true, out orderType);
        }

        private static float ParseFloat(string value, float fallback = 0f)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
                ? result
                : fallback;
        }
    }
}
