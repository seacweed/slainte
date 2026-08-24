using System;
using System.Collections.Generic;
using System.Linq;
using Slainte.TV;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Slainte.EditorTools
{
    public static class RestSceneFinalArtValidator
    {
        [MenuItem("Slainte/Rest Scene/Validate Final Artwork")]
        public static void ValidateFromMenu()
        {
            ValidateOrThrow();
            EditorUtility.DisplayDialog(
                "Rest Scene",
                "휴식 배경, 상호작용 상태, 콜라이더, TV 카드 검증을 통과했습니다.",
                "확인");
        }

        public static void ValidateFromCommandLine()
        {
            ValidateOrThrow();
        }

        public static void ValidateOrThrow()
        {
            ValidateTexture(RestSceneFinalArtInstaller.BackgroundColorPath, 1440, 1440);
            ValidateTexture(RestSceneFinalArtInstaller.BackgroundGrayPath, 1440, 1440);
            ValidateTexture(RestSceneFinalArtInstaller.TVWorldNormalPath, 1440, 1440);
            ValidateTexture(RestSceneFinalArtInstaller.TVWorldOutlinePath, 1440, 1440);
            ValidateTexture(RestSceneFinalArtInstaller.TVWorldGrayPath, 1440, 1440);
            ValidateTexture(RestSceneFinalArtInstaller.BoardNormalPath, 1440, 1440);
            ValidateTexture(RestSceneFinalArtInstaller.BoardOutlinePath, 1440, 1440);
            ValidateTexture(RestSceneFinalArtInstaller.BoardGrayPath, 1440, 1440);
            ValidateTexture(RestSceneFinalArtInstaller.ShopNormalPath, 1440, 1440);
            ValidateTexture(RestSceneFinalArtInstaller.ShopOutlinePath, 1440, 1440);
            ValidateTexture(RestSceneFinalArtInstaller.ShopGrayPath, 1440, 1440);
            ValidateTexture(RestSceneFinalArtInstaller.TVBackgroundPath, 840, 640);
            ValidateTexture(RestSceneFinalArtInstaller.TVFramePath, 1062, 810);
            ValidateTexture(RestSceneFinalArtInstaller.TVHeadlinePath, 695, 114);
            ValidateTexture(RestSceneFinalArtInstaller.TVCardBackgroundPath, 360, 290);
            for (int i = 1; i <= 6; i++)
                ValidateTexture(CardPath(i), 360, 290);

            ValidateCrop(RestSceneFinalArtInstaller.TVWorldNormalPath, RestSceneFinalArtInstaller.TVCrop);
            ValidateCrop(RestSceneFinalArtInstaller.BoardNormalPath, RestSceneFinalArtInstaller.BoardCrop);
            ValidateCrop(RestSceneFinalArtInstaller.ShopNormalPath, RestSceneFinalArtInstaller.ShopCrop);
            Require(RestSceneFinalArtInstaller.ShopCrop.xMax < 1290f,
                "일반 상점 Sprite Rect가 원본의 불필요한 노란 점을 포함합니다.");
            Require(RestSceneFinalArtInstaller.BoardCrop.xMax < 1376f,
                "전광판 Sprite Rect가 원본의 불필요한 노란 점을 포함합니다.");

            ValidateBroadcastCards();
            ValidatePrefabs();
            ValidateScene();
            Debug.Log(
                "[RestSceneFinalArtValidator] PASS: source sizes, crop rects, "
                + "six TV cards, QHD-left layout, visual states, and colliders.");
        }

        private static void ValidateTexture(string path, int expectedWidth, int expectedHeight)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Require(texture != null, $"텍스처가 없습니다: {path}");
            Require(texture.width == expectedWidth && texture.height == expectedHeight,
                $"원본 크기가 바뀌었습니다: {path} ({texture.width}x{texture.height})");

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Require(importer != null && !importer.mipmapEnabled,
                $"mipmap이 꺼져 있지 않습니다: {path}");
            Require(importer.maxTextureSize >= Math.Max(expectedWidth, expectedHeight),
                $"Max Texture Size 때문에 원본이 축소될 수 있습니다: {path}");
        }

        private static void ValidateCrop(string path, Rect expected)
        {
            Sprite sprite = LoadSprite(path);
            Require(RectApproximately(sprite.rect, expected),
                $"Sprite Rect가 예상과 다릅니다: {path}, actual={sprite.rect}, expected={expected}");
        }

        private static void ValidateBroadcastCards()
        {
            TVBroadcastDatabase database = AssetDatabase.LoadAssetAtPath<TVBroadcastDatabase>(
                RestSceneFinalArtInstaller.DatabasePath);
            Require(database != null, "TV 방송 데이터베이스가 없습니다.");
            var mapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["none"] = 1,
                ["delivery_outage"] = 2,
                ["shop_maintenance"] = 3,
                ["high_abv_orders"] = 4,
                ["tip_bonus"] = 5,
                ["district_9_patrol"] = 6
            };

            foreach (KeyValuePair<string, int> pair in mapping)
            {
                TVBroadcastEntry entry = database.FindById(pair.Key);
                Require(entry?.eventSprite != null, $"TV 카드가 연결되지 않았습니다: {pair.Key}");
                Require(string.Equals(
                        AssetDatabase.GetAssetPath(entry.eventSprite),
                        CardPath(pair.Value),
                        StringComparison.Ordinal),
                    $"잘못된 TV 카드가 연결됐습니다: {pair.Key}");
            }
        }

        private static void ValidatePrefabs()
        {
            GameObject tvPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                RestSceneFinalArtInstaller.TVPrefabPath);
            Require(tvPrefab != null, "최종 TVSystem 프리팹이 없습니다.");
            ObjectInteraction tvInteraction = tvPrefab.GetComponent<ObjectInteraction>();
            Require(tvInteraction != null
                    && tvInteraction.idleOverlay != null
                    && tvInteraction.hoverOverlay != null
                    && tvInteraction.grayOverlay != null,
                "TV 프리팹에 일반/테두리/회색 상태가 모두 연결되지 않았습니다.");
            BoxCollider2D tvCollider = tvPrefab.GetComponent<BoxCollider2D>();
            Require(tvCollider != null && VectorApproximately(
                    tvCollider.size,
                    RestSceneFinalArtInstaller.TVCrop.size / 100f),
                "TV 콜라이더가 최종 이미지 크기와 맞지 않습니다.");

            GameObject panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                RestSceneFinalArtInstaller.PanelPrefabPath);
            TVUIManager manager = panelPrefab != null
                ? panelPrefab.GetComponent<TVUIManager>()
                : null;
            Image presenterImage = panelPrefab != null
                ? panelPrefab.transform.Find("TVFrame/PresenterImage")?.GetComponent<Image>()
                : null;
            Require(manager != null
                    && manager.screenBackgroundImage?.sprite != null
                    && manager.frameImage?.sprite != null
                    && manager.headlineImage?.sprite != null
                    && manager.cardBackgroundImage?.sprite != null
                    && manager.eventImage != null
                    && presenterImage?.sprite != null
                    && manager.ticker != null,
                "TVPanel에 최종 배경/프레임/헤드라인/카드 또는 동적 이미지 참조가 없습니다.");
        }

        private static void ValidateScene()
        {
            Scene scene = EditorSceneManager.OpenScene(
                RestSceneFinalArtInstaller.ScenePath,
                OpenSceneMode.Single);
            GameObject artRoot = FindInScene(scene, "RestFinalVisualRoot");
            Require(artRoot != null, "RestFinalVisualRoot가 없습니다.");
            Require(VectorApproximately(artRoot.transform.position, new Vector3(-5.6f, 0f, 0f)),
                "1440px 휴식 배경이 QHD 왼쪽 영역에 정렬되지 않았습니다.");
            RestSceneVisualStateCoordinator coordinator =
                artRoot.GetComponent<RestSceneVisualStateCoordinator>();
            Require(coordinator != null
                    && coordinator.colorBackground != null
                    && coordinator.grayBackground != null,
                "컬러/흑백 배경 전환기가 연결되지 않았습니다.");

            ValidateInteraction(
                FindInScene(scene, "ShopButton"),
                RestSceneFinalArtInstaller.ShopCrop,
                expectShopRestriction: true,
                "일반 상점");
            ValidateInteraction(
                FindInScene(scene, "EpisodeBoardButton"),
                RestSceneFinalArtInstaller.BoardCrop,
                expectShopRestriction: false,
                "전광판");
            ValidateInteraction(
                FindInScene(scene, "TVSystemRoot"),
                RestSceneFinalArtInstaller.TVCrop,
                expectShopRestriction: false,
                "TV");

            string[] legacyNames =
            {
                "rest_idle_0", "rest_shop_idle_0", "rest_shop_hover_0",
                "rest_board_idle_0", "rest_board_hover_0"
            };
            foreach (string name in legacyNames)
            {
                GameObject legacy = FindInScene(scene, name);
                Require(legacy == null || !legacy.activeSelf,
                    $"기존 임시 휴식 이미지가 아직 활성 상태입니다: {name}");
            }
        }

        private static void ValidateInteraction(
            GameObject target,
            Rect crop,
            bool expectShopRestriction,
            string displayName)
        {
            Require(target != null, $"{displayName} 오브젝트가 없습니다.");
            Vector3 expectedPosition = new(
                -5.6f + (crop.center.x - 720f) / 100f,
                (crop.center.y - 720f) / 100f,
                0f);
            Require(VectorApproximately(target.transform.position, expectedPosition),
                $"{displayName} 위치가 최종 이미지와 맞지 않습니다.");

            ObjectInteraction interaction = target.GetComponent<ObjectInteraction>();
            Require(interaction != null
                    && interaction.idleOverlay != null
                    && interaction.hoverOverlay != null
                    && interaction.grayOverlay != null,
                $"{displayName} 일반/테두리/회색 상태가 모두 연결되지 않았습니다.");
            Require(interaction.disableWhenRestShopRestricted == expectShopRestriction,
                $"{displayName}의 일반 상점 제한 설정이 잘못됐습니다.");

            BoxCollider2D collider = target.GetComponent<BoxCollider2D>();
            Require(collider != null && VectorApproximately(collider.size, crop.size / 100f),
                $"{displayName} 콜라이더가 최종 이미지 크기와 맞지 않습니다.");
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return sprite;
            string expectedName = System.IO.Path.GetFileNameWithoutExtension(path);
            return AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<Sprite>()
                .FirstOrDefault(candidate => candidate.name == expectedName);
        }

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform.name == name) return transform.gameObject;
                }
            }

            return null;
        }

        private static bool RectApproximately(Rect a, Rect b)
        {
            return Mathf.Approximately(a.x, b.x)
                && Mathf.Approximately(a.y, b.y)
                && Mathf.Approximately(a.width, b.width)
                && Mathf.Approximately(a.height, b.height);
        }

        private static bool VectorApproximately(Vector2 a, Vector2 b)
        {
            return Vector2.SqrMagnitude(a - b) < 0.0001f;
        }

        private static bool VectorApproximately(Vector3 a, Vector3 b)
        {
            return Vector3.SqrMagnitude(a - b) < 0.0001f;
        }

        private static string CardPath(int cardNumber)
        {
            return $"Assets/RestScene/Sprites/TVFinal/tv_card_{cardNumber}.png";
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
