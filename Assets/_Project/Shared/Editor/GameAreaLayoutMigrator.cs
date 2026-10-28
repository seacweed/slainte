using System.Collections.Generic;
using System.Text;
using Slainte.Shared.Display;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Slainte.EditorTools
{
    // 열린 씬의 루트 Canvas들을 16:9 레터박스 레이아웃으로 일괄 변환한다.
    // 1) CanvasScaler를 Scale With Screen Size + 2560x1440 + Expand로 통일하고
    // 2) Canvas의 기존 자식들을 중앙 고정 2560x1440 루트(GameAreaRoot) 아래로 옮긴다.
    // Expand는 어느 화면비에서도 캔버스가 기준 해상도 이상이 되게 하므로, 고정 루트는 항상 정확히
    // QHD 크기를 유지하고 기존 앵커·오프셋을 그대로 쓸 수 있다. 모든 변경은 Undo로 되돌릴 수 있다.
    public static class GameAreaLayoutMigrator
    {
        private const string MenuPath = "Slainte/해상도/열린 씬 Canvas를 16:9 레이아웃으로 변환";

        [MenuItem(MenuPath)]
        private static void ConvertOpenScenes()
        {
            var report = new StringBuilder();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert Canvases To 16:9 Game Area");

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                bool changed = false;
                foreach (Canvas canvas in CollectRootCanvases(scene))
                    changed |= ConvertCanvas(canvas, scene, report);

                if (changed)
                    EditorSceneManager.MarkSceneDirty(scene);
            }

            Undo.CollapseUndoOperations(undoGroup);

            string message = report.Length > 0 ? report.ToString() : "변환할 Canvas가 없습니다.";
            Debug.Log("[GameAreaLayoutMigrator]\n" + message);
            EditorUtility.DisplayDialog("16:9 레이아웃 변환", message + "\n\n씬을 저장해야 반영됩니다.", "확인");
        }

        // 중첩 Canvas는 부모 Canvas의 스케일을 따르므로 손대지 않고, 최상위 Screen Space Canvas만 대상으로 한다.
        private static List<Canvas> CollectRootCanvases(Scene scene)
        {
            var result = new List<Canvas>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    Transform parent = canvas.transform.parent;
                    bool isRoot = parent == null || parent.GetComponentInParent<Canvas>(true) == null;
                    if (isRoot && canvas.renderMode != RenderMode.WorldSpace)
                        result.Add(canvas);
                }
            }
            return result;
        }

        private static bool ConvertCanvas(Canvas canvas, Scene scene, StringBuilder report)
        {
            string label = $"{scene.name}/{canvas.name}";

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                report.AppendLine($"- {label}: CanvasScaler가 없어 건너뜀");
                return false;
            }

            bool changed = false;
            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize
                || scaler.referenceResolution != new Vector2(GameAspect.ReferenceWidth, GameAspect.ReferenceHeight)
                || scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.Expand)
            {
                // 고정 픽셀 모드였던 Canvas(메인메뉴)는 QHD 기준으로 배치됐다는 전제로 변환되므로 결과를 눈으로 확인해야 한다.
                if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                    report.AppendLine($"- {label}: 스케일 모드가 {scaler.uiScaleMode}였음 — QHD에서 배치 확인 필요");

                Undo.RecordObject(scaler, "Set CanvasScaler");
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(GameAspect.ReferenceWidth, GameAspect.ReferenceHeight);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                changed = true;
                report.AppendLine($"- {label}: CanvasScaler → 2560x1440 Expand");
            }

            if (HasDirectGameAreaChild(canvas.transform))
                return changed;

            // 프리팹 인스턴스 내부는 자식 구조를 바꿀 수 없으므로 원본 프리팹에서 직접 처리하도록 알린다.
            if (PrefabUtility.IsPartOfPrefabInstance(canvas.gameObject))
            {
                report.AppendLine($"- {label}: 프리팹 인스턴스라 자식 이동을 건너뜀 — 원본 프리팹에서 GameAreaRoot로 감쌀 것");
                return changed;
            }

            WrapChildren(canvas.transform);
            report.AppendLine($"- {label}: 자식 {canvas.transform.GetChild(0).childCount}개를 {GameAreaRoot.DefaultName} 아래로 이동");
            return true;
        }

        private static bool HasDirectGameAreaChild(Transform canvasTransform)
        {
            for (int i = 0; i < canvasTransform.childCount; i++)
            {
                if (canvasTransform.GetChild(i).GetComponent<GameAreaRoot>() != null)
                    return true;
            }
            return false;
        }

        private static void WrapChildren(Transform canvasTransform)
        {
            // 이동 전에 목록을 고정해 둔다(옮기는 동안 childCount와 인덱스가 바뀜). 기존 형제 순서는 그대로 유지된다.
            var children = new List<Transform>(canvasTransform.childCount);
            for (int i = 0; i < canvasTransform.childCount; i++)
                children.Add(canvasTransform.GetChild(i));

            var areaObject = new GameObject(GameAreaRoot.DefaultName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(areaObject, "Create Game Area");
            var area = (RectTransform)areaObject.transform;
            area.SetParent(canvasTransform, false);
            areaObject.layer = canvasTransform.gameObject.layer;
            GameAreaRoot.ApplyLayout(area);
            Undo.AddComponent<GameAreaRoot>(areaObject);

            // worldPositionStays=false: 앵커·오프셋 값을 그대로 유지해야 2560x1440 루트 기준으로 QHD 배치가 재현된다.
            foreach (Transform child in children)
                Undo.SetTransformParent(child, area, false, "Move Into Game Area");
        }
    }
}
