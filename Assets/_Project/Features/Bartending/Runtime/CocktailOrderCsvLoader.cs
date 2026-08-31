using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Slainte.Content;
using UnityEngine;

namespace Slainte.Bartending
{
    public static class CocktailOrderCsvLoader
    {
        public static CocktailOrderTemplateCatalog LoadTemplatesFromStreamingAssets(
            string dataFolder = ProjectStreamingAssetPaths.Bartending,
            string orderTemplatesFileName = ProjectStreamingAssetPaths.BartendingOrderTemplates)
        {
            dataFolder = ProjectStreamingAssetPaths.ResolveBartendingDirectory(dataFolder);
            string dataPath = Path.Combine(Application.streamingAssetsPath, dataFolder);
            string orderTemplatesPath = Path.Combine(dataPath, orderTemplatesFileName);
            return LoadTemplatesFromFile(orderTemplatesPath);
        }

        public static CocktailOrderTemplateCatalog LoadTemplatesFromFile(string orderTemplatesPath)
        {
            CocktailOrderTemplateCatalog catalog = new CocktailOrderTemplateCatalog();

            if (!File.Exists(orderTemplatesPath))
            {
                Debug.LogError($"주문 문장 CSV를 찾을 수 없습니다: {orderTemplatesPath}");
                return catalog;
            }

            List<CsvRow> rows = CsvTable.Parse(File.ReadAllText(orderTemplatesPath));
            for (int i = 0; i < rows.Count; i++)
            {
                CsvRow row = rows[i];
                string id = row.Get("id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    Debug.LogWarning("ID가 비어 있어 주문 문장 행을 건너뛰었습니다.");
                    continue;
                }

                if (!TryParseOrderType(row.Get("orderType"), out CocktailOrderType orderType))
                {
                    Debug.LogWarning($"주문 문장 '{id}'에 알 수 없는 주문 유형 '{row.Get("orderType")}'이 설정되어 있습니다.");
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
