using System;
using System.Linq;
using Slainte.Business;
using Slainte.Content;
using Slainte.TV;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slainte.EditorTools
{
    /// <summary>
    /// TV 방송을 저장 데이터와 분리된 상태로 직접 선택해 확인하는 플레이 모드 도구다.
    /// </summary>
    public sealed class TVSystemTestWindow : EditorWindow
    {
        private const string ScenePath = ProjectScenePaths.Rest;
        private const string DatabasePath =
            ProjectResourcePaths.AssetRoot + ProjectResourcePaths.RestTvDatabase + ".asset";

        private int selectedIndex;
        private Vector2 scroll;
        private string statusMessage = "검증 또는 플레이 테스트를 실행하세요.";
        private MessageType statusType = MessageType.Info;

        [MenuItem("Slainte/TV/Open TV Test Window")]
        private static void OpenWindow()
        {
            TVSystemTestWindow window = GetWindow<TVSystemTestWindow>("TV Test");
            window.minSize = new Vector2(460f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        }

        private void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying)
                Repaint();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("TV 시스템 테스트", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "자동 검증은 데이터/효과/프리팹/씬 연결을 확인합니다. 플레이 테스트는 "
                + "실제 저장을 막고 시작 시점의 진행 상태를 자동 복원합니다.",
                MessageType.Info);

            DrawAutomatedValidation();

            TVBroadcastDatabase database = AssetDatabase.LoadAssetAtPath<TVBroadcastDatabase>(
                DatabasePath);
            TVBroadcastEntry[] entries = database?.broadcasts?
                .Where(entry => entry != null)
                .ToArray();
            if (database == null || entries == null || entries.Length == 0)
            {
                EditorGUILayout.HelpBox("TV 방송 데이터베이스가 없거나 비어 있습니다.", MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, entries.Length - 1);
            string[] labels = entries
                .Select(entry => $"{entry.title}  [{entry.id}]")
                .ToArray();
            selectedIndex = EditorGUILayout.Popup("테스트 방송", selectedIndex, labels);
            TVBroadcastEntry selected = entries[selectedIndex];
            DrawBroadcastDetails(selected);
            DrawPlayModeControls(database, selected);

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(statusMessage, statusType);
            EditorGUILayout.EndScrollView();
        }

        private void DrawAutomatedValidation()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("1. 자동 검증", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("TV 전체 자동 검증 실행"))
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        return;

                    try
                    {
                        TVSystemValidator.RunBatchValidation();
                        SetStatus("PASS: TV 데이터, 상태 전환, 효과, 프리팹과 씬 연결이 모두 정상입니다.",
                            MessageType.Info);
                    }
                    catch (Exception exception)
                    {
                        SetStatus("FAIL: " + exception.Message, MessageType.Error);
                        Debug.LogException(exception);
                    }
                }

                if (GUILayout.Button("RestScene 열고 플레이"))
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        return;

                    EditorSceneManager.OpenScene(ScenePath);
                    EditorApplication.isPlaying = true;
                    SetStatus("Play Mode 준비 중입니다. GameProgress가 준비되면 격리 테스트를 시작하세요.",
                        MessageType.Info);
                }
            }
        }

        private static void DrawBroadcastDetails(TVBroadcastEntry entry)
        {
            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("효과", DescribeEffect(entry));
                EditorGUILayout.LabelField("가중치", entry.weight.ToString("0.##"));
                EditorGUILayout.LabelField("자막", entry.tickerText, EditorStyles.wordWrappedLabel);
            }
        }

        private void DrawPlayModeControls(
            TVBroadcastDatabase database,
            TVBroadcastEntry selected)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("2. 플레이 테스트", EditorStyles.boldLabel);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "RestScene에서 Play Mode로 진입한 뒤 이 창을 다시 확인하세요.",
                    MessageType.Warning);
                return;
            }

            GameProgress progress = GameProgress.Instance;
            if (progress == null)
            {
                EditorGUILayout.HelpBox("CoreScene의 GameProgress 초기화를 기다리는 중입니다.",
                    MessageType.Warning);
                return;
            }

            DrawRuntimeState(progress, database);

            PlaytestProgressIsolation isolation =
                progress.GetComponent<PlaytestProgressIsolation>();
            if (isolation == null)
            {
                if (GUILayout.Button("격리 테스트 시작 (저장 차단 + 상태 스냅샷)"))
                {
                    PlaytestProgressIsolation.Attach(progress.gameObject);
                    SetStatus("격리 상태를 준비 중입니다. 잠시 후 테스트 버튼이 활성화됩니다.",
                        MessageType.Info);
                }

                return;
            }

            if (!isolation.IsReady)
            {
                EditorGUILayout.HelpBox("진행 상태 스냅샷을 준비하는 중입니다.", MessageType.Info);
                return;
            }

            if (GUILayout.Button("선택 방송을 예고로 설정하고 TV 화면 열기"))
                PreviewForecast(progress, database, selected);

            if (GUILayout.Button("선택 방송 효과를 오늘 즉시 활성화"))
                ActivateEffect(progress, database, selected);

            if (GUILayout.Button("테스트 시작 시점으로 복원"))
            {
                isolation.RestoreNow();
                ApplyDeliveryState(database);
                SetStatus("진행 상태를 테스트 시작 시점으로 복원했습니다.", MessageType.Info);
            }
        }

        private static void DrawRuntimeState(
            GameProgress progress,
            TVBroadcastDatabase database)
        {
            TVBroadcastEntry active = TVBroadcastRuntime.GetActiveBroadcast(progress, database);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("현재 Day", progress.CurrentDay.ToString());
                EditorGUILayout.LabelField("예고 방송",
                    string.IsNullOrWhiteSpace(progress.TVForecastBroadcastId)
                        ? "없음"
                        : progress.TVForecastBroadcastId);
                EditorGUILayout.LabelField("활성 방송", active?.id ?? "없음");
                if (active != null)
                    EditorGUILayout.LabelField("활성 효과 확인값", DescribeRuntimeValue(progress, database, active));
            }
        }

        private void PreviewForecast(
            GameProgress progress,
            TVBroadcastDatabase database,
            TVBroadcastEntry entry)
        {
            progress.SetTVForecast(entry.id);
            TVSystemController controller = UnityEngine.Object.FindFirstObjectByType<TVSystemController>(
                FindObjectsInactive.Include);
            TVUIManager panel = controller?.PanelInstance
                ?? UnityEngine.Object.FindFirstObjectByType<TVUIManager>(FindObjectsInactive.Include);
            if (panel == null)
            {
                SetStatus(
                    "방송 예고는 설정했지만 TVPanel을 찾지 못했습니다. RestScene에서 실행 중인지 확인하세요.",
                    MessageType.Error);
                return;
            }

            panel.OpenUI();
            bool titleMatches = panel.titleText == null || panel.titleText.text == entry.title;
            bool eventMatches = panel.eventImage == null || panel.eventImage.sprite == entry.eventSprite;
            if (!titleMatches || !eventMatches)
            {
                SetStatus("TVPanel이 열렸지만 선택 방송의 제목 또는 카드 이미지가 일치하지 않습니다.",
                    MessageType.Error);
                return;
            }

            SetStatus($"PASS: '{entry.title}' 방송 화면을 열고 UI 바인딩을 확인했습니다.",
                MessageType.Info);
        }

        private void ActivateEffect(
            GameProgress progress,
            TVBroadcastDatabase database,
            TVBroadcastEntry entry)
        {
            progress.SetTVForecast(entry.id);
            TVBroadcastEntry active = TVBroadcastRuntime.ActivateForecastForBusiness(progress, database);
            ApplyDeliveryState(database);
            if (active == null || !string.Equals(active.id, entry.id, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("선택 방송을 활성 상태로 전환하지 못했습니다.", MessageType.Error);
                return;
            }

            SetStatus(
                $"PASS: '{entry.title}' 효과 활성화 — {DescribeRuntimeValue(progress, database, active)}",
                MessageType.Info);
        }

        private static void ApplyDeliveryState(TVBroadcastDatabase database)
        {
            LiquorShelfUI shelf = UnityEngine.Object.FindFirstObjectByType<LiquorShelfUI>(
                FindObjectsInactive.Include);
            if (shelf == null)
                return;

            TVBroadcastEntry active = TVBroadcastRuntime.GetActiveBroadcast(
                GameProgress.Instance,
                database);
            bool disabled = active != null
                && active.effectType == TVBroadcastEffectType.DisableDelivery;
            shelf.SetDeliveryAvailable(
                !disabled,
                disabled ? active.restrictionReason : string.Empty);
        }

        private static string DescribeEffect(TVBroadcastEntry entry)
        {
            return entry.effectType switch
            {
                TVBroadcastEffectType.None => "효과 없음",
                TVBroadcastEffectType.DisableDelivery => "영업 중 배송 금지",
                TVBroadcastEffectType.DisableRestShop => "휴식 상점 이용 금지",
                TVBroadcastEffectType.BoostOrderTagWeight =>
                    $"주문 '{entry.targetTag}' 가중치 x{entry.effectMultiplier:0.##}",
                TVBroadcastEffectType.BoostTips => $"팁 x{entry.effectMultiplier:0.##}",
                TVBroadcastEffectType.BoostCustomerTagWeight =>
                    $"손님 '{entry.targetTag}' 가중치 x{entry.effectMultiplier:0.##}",
                _ => entry.effectType.ToString()
            };
        }

        private static string DescribeRuntimeValue(
            GameProgress progress,
            TVBroadcastDatabase database,
            TVBroadcastEntry active)
        {
            switch (active.effectType)
            {
                case TVBroadcastEffectType.None:
                    return "효과 없음";
                case TVBroadcastEffectType.DisableDelivery:
                    return "배송 차단 = true";
                case TVBroadcastEffectType.DisableRestShop:
                    bool disabled = TVBroadcastRuntime.IsRestShopDisabled(
                        progress,
                        database,
                        out string reason);
                    return $"상점 차단 = {disabled}, 사유 = {reason}";
                case TVBroadcastEffectType.BoostTips:
                    return $"팁 배율 = {TVBroadcastRuntime.GetTipMultiplier(progress, database):0.##}";
                case TVBroadcastEffectType.BoostOrderTagWeight:
                    return "주문 태그 배율 = "
                        + TVBroadcastRuntime.GetTaggedWeightMultiplier(
                            progress,
                            database,
                            TVBroadcastEffectType.BoostOrderTagWeight,
                            new[] { active.targetTag }).ToString("0.##");
                case TVBroadcastEffectType.BoostCustomerTagWeight:
                    return "손님 태그 배율 = "
                        + TVBroadcastRuntime.GetTaggedWeightMultiplier(
                            progress,
                            database,
                            TVBroadcastEffectType.BoostCustomerTagWeight,
                            new[] { active.targetTag }).ToString("0.##");
                default:
                    return active.effectType.ToString();
            }
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
                SetStatus("Play Mode에 진입했습니다. GameProgress 초기화 후 격리 테스트를 시작하세요.",
                    MessageType.Info);
            else if (change == PlayModeStateChange.EnteredEditMode)
                SetStatus("Play Mode가 종료되어 테스트 진행 상태가 자동 복원됐습니다.",
                    MessageType.Info);
        }

        private void SetStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
            Repaint();
        }
    }
}
