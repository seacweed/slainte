using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public sealed class BottleMouthAnchorEditorWindow : EditorWindow
    {
        private const string PlanningBottleFolder =
            Slainte.Editor.BartendingAssetPaths.LiquorBottleRoot + "Planning";
        private const byte AlphaThreshold = 3;
        private const float DefaultOutwardPixels = 3f;

        private readonly List<LiquorBottleDef> bottles = new();
        private string[] bottleLabels = Array.Empty<string>();
        private int selectedIndex;
        private float previewRotation;
        private Vector2 scrollPosition;
        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;

        [MenuItem("Slainte/바텐딩/병 입구 위치 편집기")]
        public static void Open()
        {
            BottleMouthAnchorEditorWindow window = GetWindow<BottleMouthAnchorEditorWindow>();
            window.titleContent = new GUIContent("병 입구 위치");
            window.minSize = new Vector2(430f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            ReloadBottles();
            SelectCurrentProjectItem();
        }

        private void OnProjectChange()
        {
            ReloadBottles();
            Repaint();
        }

        private void OnSelectionChange()
        {
            SelectCurrentProjectItem();
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (bottles.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"{PlanningBottleFolder}에서 기획 술병을 찾지 못했습니다.",
                    MessageType.Warning);
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, bottles.Count - 1);
            LiquorBottleDef definition = bottles[selectedIndex];
            ItemDef item = definition != null ? definition.item : null;
            Sprite barSprite = definition != null
                ? definition.GetBarSprite(item != null ? item.icon : null)
                : null;

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawSelection(definition, item, barSprite);
            DrawMouthControls(item, barSprite);
            DrawPreview(item, barSprite);
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrWhiteSpace(statusMessage))
                EditorGUILayout.HelpBox(statusMessage, statusType);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    ReloadBottles();

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("모두 저장", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    AssetDatabase.SaveAssets();
                    SetStatus("병 입구 좌표를 저장했습니다.", MessageType.Info);
                }
            }
        }

        private void DrawSelection(
            LiquorBottleDef definition,
            ItemDef item,
            Sprite barSprite)
        {
            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = selectedIndex > 0;
                if (GUILayout.Button("◀", GUILayout.Width(34f)))
                    selectedIndex--;
                GUI.enabled = true;

                selectedIndex = EditorGUILayout.Popup(
                    "술병",
                    selectedIndex,
                    bottleLabels);

                GUI.enabled = selectedIndex < bottles.Count - 1;
                if (GUILayout.Button("▶", GUILayout.Width(34f)))
                    selectedIndex++;
                GUI.enabled = true;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("LiquorBottleDef", definition, typeof(LiquorBottleDef), false);
                EditorGUILayout.ObjectField("ItemDef", item, typeof(ItemDef), false);
                EditorGUILayout.ObjectField("실제 barSprite", barSprite, typeof(Sprite), false);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("ItemDef 선택") && item != null)
                {
                    Selection.activeObject = item;
                    EditorGUIUtility.PingObject(item);
                }

                if (GUILayout.Button("barSprite 선택") && barSprite != null)
                {
                    Selection.activeObject = barSprite;
                    EditorGUIUtility.PingObject(barSprite);
                }
            }
        }

        private void DrawMouthControls(ItemDef item, Sprite barSprite)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("입구 데이터", EditorStyles.boldLabel);
            if (item == null)
            {
                EditorGUILayout.HelpBox("선택한 술병에 ItemDef가 연결되지 않았습니다.", MessageType.Error);
                return;
            }

            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.Toggle(
                new GUIContent("개별 입구 사용", "클릭 콜라이더와 무관하게 이 스프라이트의 입구 좌표를 사용합니다."),
                item.overrideBottleLiquidSpawn);
            Vector2 normalized = EditorGUILayout.Vector2Field(
                new GUIContent("입구 정규화 좌표", "왼쪽 아래 (0,0), 오른쪽 위 (1,1)"),
                item.liquidSpawnNormalized);
            float outwardPixels = EditorGUILayout.FloatField(
                new GUIContent("바깥쪽 오프셋 (px)", "입구에서 병의 위쪽 방향으로 이동할 픽셀 수"),
                item.liquidSpawnOutwardPixels);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(item, "병 입구 설정 변경");
                item.overrideBottleLiquidSpawn = enabled;
                item.liquidSpawnNormalized = ClampNormalized(normalized);
                item.liquidSpawnOutwardPixels = Mathf.Max(0f, outwardPixels);
                EditorUtility.SetDirty(item);
                SetStatus($"{item.displayName}의 입구 설정을 변경했습니다.", MessageType.Info);
            }

            previewRotation = EditorGUILayout.Slider(
                new GUIContent("회전 미리보기", "실제 병 기울기처럼 스프라이트와 입구 표시를 함께 회전합니다."),
                previewRotation,
                -120f,
                120f);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = barSprite != null;
                if (GUILayout.Button("현재 자동 추정"))
                    EstimateCurrent(item, barSprite);
                if (GUILayout.Button("전체 자동 추정"))
                    EstimateAll();
                GUI.enabled = true;

                if (GUILayout.Button("중앙 상단"))
                    SetMouth(item, new Vector2(0.5f, 1f), DefaultOutwardPixels, "병 입구 중앙 상단 설정");
            }

            EditorGUILayout.HelpBox(
                "아래 그림에서 실제 병 입구를 클릭하세요. 청록색 십자는 클릭한 입구이고, 노란 점은 바깥쪽 오프셋까지 적용된 실제 액체 생성점입니다.",
                MessageType.None);
        }

        private void DrawPreview(ItemDef item, Sprite sprite)
        {
            float availableHeight = Mathf.Max(320f, position.height - 355f);
            Rect canvas = GUILayoutUtility.GetRect(
                320f,
                availableHeight,
                GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(canvas, new Color(0.11f, 0.12f, 0.14f, 1f));
            DrawCheckerboard(canvas);

            if (item == null || sprite == null || sprite.texture == null)
            {
                GUI.Label(canvas, "표시할 barSprite가 없습니다.", CenteredLabelStyle());
                return;
            }

            Rect drawRect = FitSpriteRect(canvas, sprite.rect.size, 24f);
            Rect textureCoordinates = GetTextureCoordinates(sprite);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(previewRotation, drawRect.center);
            GUI.DrawTextureWithTexCoords(drawRect, sprite.texture, textureCoordinates, true);
            DrawAnchor(item, sprite, drawRect);
            GUI.matrix = previousMatrix;

            Event current = Event.current;
            if (current.type != EventType.MouseDown || current.button != 0)
                return;

            Vector2 unrotatedMouse = RotatePoint(
                current.mousePosition,
                drawRect.center,
                -previewRotation);
            if (!drawRect.Contains(unrotatedMouse))
                return;

            Vector2 normalized = new Vector2(
                Mathf.InverseLerp(drawRect.xMin, drawRect.xMax, unrotatedMouse.x),
                Mathf.InverseLerp(drawRect.yMax, drawRect.yMin, unrotatedMouse.y));
            float outward = item.liquidSpawnOutwardPixels > 0f
                ? item.liquidSpawnOutwardPixels
                : DefaultOutwardPixels;
            SetMouth(item, normalized, outward, "스프라이트에서 병 입구 지정");
            current.Use();
        }

        private static void DrawAnchor(ItemDef item, Sprite sprite, Rect drawRect)
        {
            Vector2 normalized = ClampNormalized(item.liquidSpawnNormalized);
            Vector2 mouthPoint = new Vector2(
                Mathf.Lerp(drawRect.xMin, drawRect.xMax, normalized.x),
                Mathf.Lerp(drawRect.yMax, drawRect.yMin, normalized.y));

            Color mouthColor = new Color(0.15f, 0.95f, 0.95f, 1f);
            EditorGUI.DrawRect(new Rect(mouthPoint.x - 12f, mouthPoint.y - 1f, 24f, 2f), mouthColor);
            EditorGUI.DrawRect(new Rect(mouthPoint.x - 1f, mouthPoint.y - 12f, 2f, 24f), mouthColor);

            float outwardDisplay = item.liquidSpawnOutwardPixels
                / Mathf.Max(1f, sprite.rect.height)
                * drawRect.height;
            Vector2 spawnPoint = mouthPoint + Vector2.down * outwardDisplay;
            EditorGUI.DrawRect(new Rect(spawnPoint.x - 4f, spawnPoint.y - 4f, 8f, 8f), Color.yellow);
            EditorGUI.DrawRect(
                new Rect(mouthPoint.x - 1f, spawnPoint.y, 2f, Mathf.Max(1f, mouthPoint.y - spawnPoint.y)),
                new Color(1f, 0.85f, 0.15f, 0.9f));
        }

        private static void DrawCheckerboard(Rect rect)
        {
            const float cell = 18f;
            Color light = new Color(0.18f, 0.19f, 0.21f, 1f);
            Color dark = new Color(0.135f, 0.145f, 0.16f, 1f);
            int columns = Mathf.CeilToInt(rect.width / cell);
            int rows = Mathf.CeilToInt(rect.height / cell);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    Rect cellRect = new Rect(
                        rect.x + x * cell,
                        rect.y + y * cell,
                        Mathf.Min(cell, rect.xMax - (rect.x + x * cell)),
                        Mathf.Min(cell, rect.yMax - (rect.y + y * cell)));
                    EditorGUI.DrawRect(cellRect, ((x + y) & 1) == 0 ? light : dark);
                }
            }
        }

        private void EstimateCurrent(ItemDef item, Sprite sprite)
        {
            if (!TryEstimateMouth(sprite, out Vector2 mouth, out string failure))
            {
                SetStatus($"자동 추정 실패: {failure}", MessageType.Error);
                return;
            }

            SetMouth(item, mouth, DefaultOutwardPixels, "병 입구 자동 추정");
            SetStatus($"{item.displayName} 입구를 자동 추정했습니다. 그림에서 최종 확인하세요.", MessageType.Info);
        }

        private void EstimateAll()
        {
            if (!EditorUtility.DisplayDialog(
                    "전체 자동 추정",
                    "기획 술병 전체의 입구 좌표를 실제 barSprite 상단에서 다시 추정합니다. 계속할까요?",
                    "추정",
                    "취소"))
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("전체 병 입구 자동 추정");
            int updated = 0;
            var failures = new List<string>();
            for (int i = 0; i < bottles.Count; i++)
            {
                LiquorBottleDef definition = bottles[i];
                ItemDef item = definition != null ? definition.item : null;
                Sprite sprite = definition != null
                    ? definition.GetBarSprite(item != null ? item.icon : null)
                    : null;
                if (item == null)
                {
                    failures.Add(definition != null ? $"{definition.id}: ItemDef 없음" : "<null>");
                    continue;
                }

                if (!TryEstimateMouth(sprite, out Vector2 mouth, out string failure))
                {
                    failures.Add($"{definition.id}: {failure}");
                    continue;
                }

                Undo.RecordObject(item, "병 입구 자동 추정");
                item.overrideBottleLiquidSpawn = true;
                item.liquidSpawnNormalized = mouth;
                if (item.liquidSpawnOutwardPixels <= 0f)
                    item.liquidSpawnOutwardPixels = DefaultOutwardPixels;
                EditorUtility.SetDirty(item);
                updated++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            SetStatus(
                failures.Count == 0
                    ? $"기획 술병 {updated}개의 입구를 자동 추정했습니다. 모두 저장 후 개별 좌표를 확인하세요."
                    : $"{updated}개 추정, {failures.Count}개 실패: {string.Join(" / ", failures)}",
                failures.Count == 0 ? MessageType.Info : MessageType.Warning);
        }

        private void SetMouth(ItemDef item, Vector2 normalized, float outwardPixels, string undoName)
        {
            if (item == null)
                return;

            Undo.RecordObject(item, undoName);
            item.overrideBottleLiquidSpawn = true;
            item.liquidSpawnNormalized = ClampNormalized(normalized);
            item.liquidSpawnOutwardPixels = Mathf.Max(0f, outwardPixels);
            EditorUtility.SetDirty(item);
            SetStatus(
                $"{item.displayName}: ({item.liquidSpawnNormalized.x:0.000}, {item.liquidSpawnNormalized.y:0.000})",
                MessageType.Info);
            Repaint();
        }

        private void ReloadBottles()
        {
            string selectedId = bottles.Count > 0
                && selectedIndex >= 0
                && selectedIndex < bottles.Count
                && bottles[selectedIndex] != null
                    ? bottles[selectedIndex].id
                    : string.Empty;

            bottles.Clear();
            string[] guids = AssetDatabase.FindAssets(
                "t:LiquorBottleDef",
                new[] { PlanningBottleFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                LiquorBottleDef bottle = AssetDatabase.LoadAssetAtPath<LiquorBottleDef>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (bottle != null)
                    bottles.Add(bottle);
            }

            bottles.Sort((left, right) => string.Compare(
                left != null ? left.id : string.Empty,
                right != null ? right.id : string.Empty,
                StringComparison.OrdinalIgnoreCase));

            bottleLabels = new string[bottles.Count];
            for (int i = 0; i < bottles.Count; i++)
            {
                LiquorBottleDef bottle = bottles[i];
                bottleLabels[i] = bottle != null
                    ? $"{bottle.id}  {bottle.displayName}"
                    : "<null>";
                if (!string.IsNullOrEmpty(selectedId)
                    && bottle != null
                    && string.Equals(bottle.id, selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                }
            }

            selectedIndex = bottles.Count > 0
                ? Mathf.Clamp(selectedIndex, 0, bottles.Count - 1)
                : 0;
        }

        private void SelectCurrentProjectItem()
        {
            UnityEngine.Object selected = Selection.activeObject;
            for (int i = 0; i < bottles.Count; i++)
            {
                LiquorBottleDef bottle = bottles[i];
                if (bottle == selected || (bottle != null && bottle.item == selected))
                {
                    selectedIndex = i;
                    return;
                }
            }
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }

        private static Rect FitSpriteRect(Rect canvas, Vector2 spriteSize, float padding)
        {
            Rect inner = new Rect(
                canvas.x + padding,
                canvas.y + padding,
                Mathf.Max(1f, canvas.width - padding * 2f),
                Mathf.Max(1f, canvas.height - padding * 2f));
            float aspect = spriteSize.x / Mathf.Max(1f, spriteSize.y);
            float width = inner.width;
            float height = width / Mathf.Max(0.001f, aspect);
            if (height > inner.height)
            {
                height = inner.height;
                width = height * aspect;
            }

            return new Rect(
                inner.center.x - width * 0.5f,
                inner.center.y - height * 0.5f,
                width,
                height);
        }

        private static Rect GetTextureCoordinates(Sprite sprite)
        {
            Rect textureRect;
            try
            {
                textureRect = sprite.textureRect;
            }
            catch (InvalidOperationException)
            {
                textureRect = sprite.rect;
            }

            Texture2D texture = sprite.texture;
            return new Rect(
                textureRect.x / texture.width,
                textureRect.y / texture.height,
                textureRect.width / texture.width,
                textureRect.height / texture.height);
        }

        private static Vector2 RotatePoint(Vector2 point, Vector2 pivot, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            Vector2 delta = point - pivot;
            return pivot + new Vector2(
                delta.x * cosine - delta.y * sine,
                delta.x * sine + delta.y * cosine);
        }

        private static Vector2 ClampNormalized(Vector2 value)
        {
            return new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
        }

        private static GUIStyle CenteredLabelStyle()
        {
            return new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13
            };
        }

        private static bool TryEstimateMouth(
            Sprite sprite,
            out Vector2 normalized,
            out string failure)
        {
            normalized = new Vector2(0.5f, 0.98f);
            failure = string.Empty;
            if (sprite == null)
            {
                failure = "barSprite가 없습니다.";
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(sprite);
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string sourcePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(sourcePath))
            {
                failure = $"원본 이미지를 찾지 못했습니다: {assetPath}";
                return false;
            }

            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(sourcePath), false))
                {
                    failure = $"원본 이미지를 읽지 못했습니다: {assetPath}";
                    return false;
                }

                Rect spriteRect = sprite.rect;
                int minX = Mathf.Clamp(Mathf.FloorToInt(spriteRect.xMin), 0, source.width - 1);
                int minY = Mathf.Clamp(Mathf.FloorToInt(spriteRect.yMin), 0, source.height - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(spriteRect.xMax) - 1, 0, source.width - 1);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(spriteRect.yMax) - 1, 0, source.height - 1);
                Color32[] pixels = source.GetPixels32();

                int visibleTop = minY - 1;
                for (int y = maxY; y >= minY && visibleTop < minY; y--)
                {
                    int row = y * source.width;
                    for (int x = minX; x <= maxX; x++)
                    {
                        if (pixels[row + x].a <= AlphaThreshold)
                            continue;
                        visibleTop = y;
                        break;
                    }
                }

                if (visibleTop < minY)
                {
                    failure = "불투명 픽셀이 없습니다.";
                    return false;
                }

                int sampleDepth = Mathf.Clamp(Mathf.RoundToInt(spriteRect.height * 0.04f), 4, 18);
                int sampleBottom = Mathf.Max(minY, visibleTop - sampleDepth + 1);
                double weightedX = 0d;
                int sampleCount = 0;
                for (int y = sampleBottom; y <= visibleTop; y++)
                {
                    int row = y * source.width;
                    for (int x = minX; x <= maxX; x++)
                    {
                        byte alpha = pixels[row + x].a;
                        if (alpha <= AlphaThreshold)
                            continue;
                        weightedX += (x + 0.5d) * alpha;
                        sampleCount += alpha;
                    }
                }

                if (sampleCount <= 0)
                {
                    failure = "입구 상단 픽셀을 계산하지 못했습니다.";
                    return false;
                }

                float mouthX = (float)(weightedX / sampleCount);
                normalized = ClampNormalized(new Vector2(
                    (mouthX - spriteRect.xMin) / spriteRect.width,
                    (visibleTop + 1f - spriteRect.yMin) / spriteRect.height));
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
            finally
            {
                DestroyImmediate(source);
            }
        }
    }
}
