using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Slainte.Bartending
{
    public class BottlePivotTestPanel : MonoBehaviour
    {
        [Header("Initial Test Values")]
        [SerializeField] private BottleRotationPivotMode mode = BottleRotationPivotMode.HeightPercentage;
        [Range(0f, 1f)]
        [SerializeField] private float heightPercentage = 0.75f;
        [Min(0f)]
        [SerializeField] private float distanceFromMouth = 0.25f;

        [Header("Display")]
        [SerializeField] private bool showPivotMarkers = true;
        [SerializeField] private Rect panelRect = new Rect(16f, 16f, 350f, 330f);

        private const float PercentageStep = 0.05f;
        private const float DistanceStep = 0.05f;

        private BottleController[] bottles;

        private void Start()
        {
            RefreshBottles();
            ApplySettings();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            bool changed = false;

            if (keyboard.pKey.wasPressedThisFrame)
            {
                mode = BottleRotationPivotMode.HeightPercentage;
                changed = true;
            }
            else if (keyboard.dKey.wasPressedThisFrame)
            {
                mode = BottleRotationPivotMode.DistanceFromMouth;
                changed = true;
            }
            else if (keyboard.oKey.wasPressedThisFrame)
            {
                mode = BottleRotationPivotMode.TransformOrigin;
                changed = true;
            }

            float direction = 0f;
            if (keyboard.leftBracketKey.wasPressedThisFrame)
                direction = -1f;
            else if (keyboard.rightBracketKey.wasPressedThisFrame)
                direction = 1f;

            if (direction != 0f)
            {
                if (mode == BottleRotationPivotMode.HeightPercentage)
                    heightPercentage = Mathf.Clamp01(heightPercentage + PercentageStep * direction);
                else if (mode == BottleRotationPivotMode.DistanceFromMouth)
                    distanceFromMouth = Mathf.Max(0f, distanceFromMouth + DistanceStep * direction);

                changed = true;
            }

            if (keyboard.mKey.wasPressedThisFrame)
                showPivotMarkers = !showPivotMarkers;

            if (keyboard.lKey.wasPressedThisFrame)
                LogCurrentSetting();

            if (changed)
                ApplySettings();
        }

        private void OnGUI()
        {
            panelRect = GUILayout.Window(GetInstanceID(), panelRect, DrawPanel, "Bottle Rotation Pivot Test");

            if (showPivotMarkers)
                DrawPivotMarkers();
        }

        private void DrawPanel(int windowId)
        {
            GUILayout.Label("회전 중심 기준");

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(mode == BottleRotationPivotMode.HeightPercentage, "높이 비율", "Button"))
                SetMode(BottleRotationPivotMode.HeightPercentage);
            if (GUILayout.Toggle(mode == BottleRotationPivotMode.DistanceFromMouth, "입구 거리", "Button"))
                SetMode(BottleRotationPivotMode.DistanceFromMouth);
            if (GUILayout.Toggle(mode == BottleRotationPivotMode.TransformOrigin, "기존 피벗", "Button"))
                SetMode(BottleRotationPivotMode.TransformOrigin);
            GUILayout.EndHorizontal();

            if (mode == BottleRotationPivotMode.HeightPercentage)
                DrawPercentageControls();
            else if (mode == BottleRotationPivotMode.DistanceFromMouth)
                DrawDistanceControls();
            else
                GUILayout.Label("현재 Transform 원점을 중심으로 회전합니다.");

            BottleController referenceBottle = GetReferenceBottle();
            if (referenceBottle != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label($"기준 병: {referenceBottle.name}");
                GUILayout.Label($"병 높이: {referenceBottle.BottleWorldHeight:F2} units");
                GUILayout.Label(
                    $"실제 중심: 바닥 기준 {referenceBottle.RotationPivotHeightPercentage * 100f:F1}% / " +
                    $"입구에서 {referenceBottle.RotationPivotDistanceFromMouth:F2} units");
            }

            GUILayout.Space(6f);
            showPivotMarkers = GUILayout.Toggle(showPivotMarkers, "병 위에 회전 중심 표시");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("현재 값 Console 기록"))
                LogCurrentSetting();
            if (GUILayout.Button("병 다시 찾기"))
            {
                RefreshBottles();
                ApplySettings();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label("P: 비율  D: 거리  O: 기존  [ / ]: 값 조절");
            GUILayout.Label("M: 마커 토글  L: Console 기록");
            GUILayout.Label("좌클릭으로 병 선택 → 우클릭을 누른 채 상하 이동");

            GUI.DragWindow(new Rect(0f, 0f, panelRect.width, 24f));
        }

        private void DrawPercentageControls()
        {
            GUILayout.Label($"바닥 기준 높이: {heightPercentage * 100f:F0}%");
            float newValue = GUILayout.HorizontalSlider(heightPercentage, 0f, 1f);
            if (!Mathf.Approximately(newValue, heightPercentage))
            {
                heightPercentage = newValue;
                ApplySettings();
            }

            GUILayout.BeginHorizontal();
            DrawPercentagePreset("25%", 0.25f);
            DrawPercentagePreset("50%", 0.5f);
            DrawPercentagePreset("75%", 0.75f);
            DrawPercentagePreset("90%", 0.9f);
            GUILayout.EndHorizontal();
        }

        private void DrawDistanceControls()
        {
            BottleController referenceBottle = GetReferenceBottle();
            float maxDistance = referenceBottle != null
                ? Mathf.Max(0.1f, referenceBottle.BottleWorldHeight)
                : 3f;

            GUILayout.Label($"입구에서 아래로: {distanceFromMouth:F2} units");
            float newValue = GUILayout.HorizontalSlider(distanceFromMouth, 0f, maxDistance);
            if (!Mathf.Approximately(newValue, distanceFromMouth))
            {
                distanceFromMouth = newValue;
                ApplySettings();
            }

            GUILayout.BeginHorizontal();
            DrawDistancePreset("0.10", 0.1f);
            DrawDistancePreset("0.25", 0.25f);
            DrawDistancePreset("0.50", 0.5f);
            DrawDistancePreset("1.00", 1f);
            GUILayout.EndHorizontal();
        }

        private void DrawPercentagePreset(string label, float value)
        {
            if (!GUILayout.Button(label))
                return;

            heightPercentage = value;
            ApplySettings();
        }

        private void DrawDistancePreset(string label, float value)
        {
            if (!GUILayout.Button(label))
                return;

            distanceFromMouth = value;
            ApplySettings();
        }

        private void SetMode(BottleRotationPivotMode newMode)
        {
            if (mode == newMode)
                return;

            mode = newMode;
            ApplySettings();
        }

        private void RefreshBottles()
        {
            bottles = FindObjectsByType<BottleController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        private void ApplySettings()
        {
            if (bottles == null)
                RefreshBottles();

            foreach (BottleController bottle in bottles)
            {
                if (bottle == null)
                    continue;

                switch (mode)
                {
                    case BottleRotationPivotMode.HeightPercentage:
                        bottle.SetRotationPivotByHeightPercentage(heightPercentage);
                        break;
                    case BottleRotationPivotMode.DistanceFromMouth:
                        bottle.SetRotationPivotByMouthDistance(distanceFromMouth);
                        break;
                    default:
                        bottle.UseTransformRotationPivot();
                        break;
                }
            }
        }

        private BottleController GetReferenceBottle()
        {
            if (bottles == null || bottles.Length == 0)
                return null;

            foreach (BottleController bottle in bottles)
            {
                if (bottle != null && bottle.IsPickedUp)
                    return bottle;
            }

            foreach (BottleController bottle in bottles)
            {
                if (bottle != null)
                    return bottle;
            }

            return null;
        }

        private void DrawPivotMarkers()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null || bottles == null)
                return;

            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 0.82f, 0.15f, 1f);

            foreach (BottleController bottle in bottles)
            {
                if (bottle == null || !bottle.gameObject.activeInHierarchy)
                    continue;

                Vector3 screenPosition = mainCamera.WorldToScreenPoint(bottle.RotationPivotWorldPosition);
                if (screenPosition.z <= 0f)
                    continue;

                float guiY = Screen.height - screenPosition.y;
                GUI.Box(new Rect(screenPosition.x - 5f, guiY - 5f, 10f, 10f), GUIContent.none);
                GUI.Label(
                    new Rect(screenPosition.x + 8f, guiY - 10f, 190f, 24f),
                    $"{bottle.name} {bottle.RotationPivotHeightPercentage * 100f:F0}%");
            }

            GUI.color = previousColor;
        }

        private void LogCurrentSetting()
        {
            BottleController referenceBottle = GetReferenceBottle();
            if (referenceBottle == null)
            {
                Debug.LogWarning("[Bottle Pivot Test] No active BottleController was found.");
                return;
            }

            StringBuilder message = new StringBuilder();
            message.Append("[Bottle Pivot Test] ");
            message.Append($"Mode={mode}, ConfiguredHeight={heightPercentage * 100f:F1}%, ");
            message.Append($"ConfiguredMouthDistance={distanceFromMouth:F2} units");

            foreach (BottleController bottle in bottles)
            {
                if (bottle == null)
                    continue;

                message.AppendLine();
                message.Append(
                    $"- {bottle.name}: Height={bottle.BottleWorldHeight:F2}, " +
                    $"Pivot={bottle.RotationPivotHeightPercentage * 100f:F1}%, " +
                    $"MouthDistance={bottle.RotationPivotDistanceFromMouth:F2}");
            }

            Debug.Log(message.ToString());
        }
    }
}
