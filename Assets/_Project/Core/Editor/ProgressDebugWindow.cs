using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Slainte.Business;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public sealed class ProgressDebugWindow : EditorWindow
    {
        private EpisodeData[] episodes = Array.Empty<EpisodeData>();
        private string[] episodeLabels = Array.Empty<string>();
        private int selectedEpisodeIndex;
        private Vector2 scrollPosition;
        private string statusMessage = string.Empty;
        private MessageType statusType = MessageType.Info;

        [MenuItem("Slainte/디버그/진행 데이터 도구")]
        public static void Open()
        {
            ProgressDebugWindow window = GetWindow<ProgressDebugWindow>();
            window.titleContent = new GUIContent("진행 데이터");
            window.minSize = new Vector2(460f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshEpisodes();
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            RefreshEpisodes();
            Repaint();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField("진행 데이터 복구", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "현재 에피소드 강제 완료는 정상 종료 콜백을 거쳐 UI와 영업 상태를 정리하지만 "
                + "에피소드 정산 보상은 지급하지 않습니다.",
                MessageType.Info);

            DrawSaveLocation();
            EditorGUILayout.Space(10f);
            DrawCurrentEpisodeRecovery();
            EditorGUILayout.Space(10f);
            DrawBusinessRecovery();
            EditorGUILayout.Space(10f);
            DrawEpisodeCompletionEditor();
            EditorGUILayout.Space(10f);
            DrawFullReset();

            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                EditorGUILayout.Space(10f);
                EditorGUILayout.HelpBox(statusMessage, statusType);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSaveLocation()
        {
            EditorGUILayout.LabelField("저장 파일", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(
                DataManager.SaveFilePath,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                File.Exists(DataManager.SaveFilePath) ? "상태: 저장 데이터 있음" : "상태: 저장 데이터 없음");
            if (GUILayout.Button("저장 폴더 열기", GUILayout.Width(120f)))
            {
                string target = File.Exists(DataManager.SaveFilePath)
                    ? DataManager.SaveFilePath
                    : Application.persistentDataPath;
                EditorUtility.RevealInFinder(target);
            }
            EditorGUILayout.EndHorizontal();

            if (EditorApplication.isPlaying && DataManager.AreDiskWritesSuppressed)
            {
                EditorGUILayout.HelpBox(
                    "격리 플레이테스트 중입니다. 완료 상태는 현재 플레이 메모리에만 적용되고 "
                    + "autosave.json에는 기록되지 않습니다.",
                    MessageType.Warning);
            }
        }

        private void DrawCurrentEpisodeRecovery()
        {
            EditorGUILayout.LabelField("현재 에피소드", EditorStyles.boldLabel);

            EpisodeManager manager = EditorApplication.isPlaying
                ? EpisodeManager.Instance
                : null;
            string episodeId = manager?.CurrentPlayingEpisodeID;
            if (string.IsNullOrWhiteSpace(episodeId) && EditorApplication.isPlaying)
            {
                EpisodeRunner runner = FindFirstObjectByType<EpisodeRunner>();
                episodeId = runner?.CurrentEpisodeId;
            }

            EditorGUILayout.LabelField(
                "ID",
                string.IsNullOrWhiteSpace(episodeId) ? "진행 중인 에피소드 없음" : episodeId);
            EditorGUILayout.LabelField(
                "실행 형태",
                manager?.IsBusinessEncounterActive == true ? "영업 인카운터" : "일반 에피소드");

            bool canForceComplete = EditorApplication.isPlaying
                && manager != null
                && !string.IsNullOrWhiteSpace(episodeId);
            using (new EditorGUI.DisabledScope(!canForceComplete))
            {
                if (GUILayout.Button("현재 에피소드 강제 완료", GUILayout.Height(34f)))
                {
                    bool confirmed = EditorUtility.DisplayDialog(
                        "현재 에피소드 강제 완료",
                        $"'{episodeId}'을(를) 완료 처리하고 현재 에피소드 화면을 종료합니다.\n\n"
                        + "복구 완료에서는 에피소드 정산 보상이 지급되지 않습니다.",
                        "강제 완료",
                        "취소");
                    if (confirmed)
                        ForceCompleteCurrentEpisode(manager);
                }
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "현재 에피소드 복구는 Play Mode에서만 사용할 수 있습니다.",
                    MessageType.None);
            }
        }

        private void DrawEpisodeCompletionEditor()
        {
            EditorGUILayout.LabelField("에피소드 완료 상태", EditorStyles.boldLabel);
            if (episodes.Length == 0)
            {
                EditorGUILayout.HelpBox("Resources/EpisodeData에서 에피소드를 찾지 못했습니다.", MessageType.Warning);
                if (GUILayout.Button("목록 새로고침"))
                    RefreshEpisodes();
                return;
            }

            selectedEpisodeIndex = EditorGUILayout.Popup(
                "에피소드",
                Mathf.Clamp(selectedEpisodeIndex, 0, episodes.Length - 1),
                episodeLabels);
            EpisodeData selected = episodes[selectedEpisodeIndex];
            bool isCompleted = IsEpisodeCompleted(selected.episodeId);
            EditorGUILayout.LabelField("현재 상태", isCompleted ? "완료" : "미완료");

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(isCompleted))
            {
                if (GUILayout.Button("완료 처리"))
                    SetEpisodeCompletion(selected.episodeId, completed: true);
            }

            using (new EditorGUI.DisabledScope(!isCompleted))
            {
                if (GUILayout.Button("미완료로 되돌리기"))
                    SetEpisodeCompletion(selected.episodeId, completed: false);
            }

            if (GUILayout.Button("목록 새로고침", GUILayout.Width(110f)))
                RefreshEpisodes();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBusinessRecovery()
        {
            EditorGUILayout.LabelField("현재 영업과 주문", EditorStyles.boldLabel);

            BusinessFlowBootstrap flow = EditorApplication.isPlaying
                ? UnityEngine.Object.FindFirstObjectByType<BusinessFlowBootstrap>()
                : null;
            BusinessShiftController shift = flow?.ShiftController;
            BusinessOrderSessionController orderSession = flow?.OrderSessionController;

            EditorGUILayout.LabelField(
                "영업 상태",
                shift != null ? shift.State.ToString() : "활성 영업 없음");
            if (shift?.IsForceCompletionPending == true)
            {
                EditorGUILayout.HelpBox(
                    "영업 강제 완료가 예약되었습니다. 현재 주문 또는 인카운터가 끝나면 정산으로 이동합니다.",
                    MessageType.Warning);
            }

            bool canForceBusiness = EditorApplication.isPlaying && shift?.IsActive == true;
            using (new EditorGUI.DisabledScope(!canForceBusiness))
            {
                if (GUILayout.Button("영업 강제 완료", GUILayout.Height(32f)))
                {
                    bool confirmed = EditorUtility.DisplayDialog(
                        "영업 강제 완료",
                        "신규 주문과 필수 액션을 중단하고 정산으로 이동합니다.\n\n"
                        + "현재 주문이나 인카운터가 진행 중이면 해당 작업이 끝난 직후 정산합니다.",
                        "영업 완료",
                        "취소");
                    if (confirmed)
                        ForceCompleteBusiness(flow, shift);
                }
            }

            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField(
                "주문 세션",
                orderSession?.HasActiveOrder == true
                    ? $"{orderSession.CurrentSessionId} / {orderSession.CurrentOwner} / {orderSession.State}"
                    : "진행 중인 주문 없음");

            using (new EditorGUI.DisabledScope(
                       !EditorApplication.isPlaying || orderSession?.HasActiveOrder != true))
            {
                EditorGUILayout.BeginHorizontal();
                DrawOrderResultButton(orderSession, "Good", CraftingJobResult.Good);
                DrawOrderResultButton(orderSession, "Mid-Ice", CraftingJobResult.MidIce);
                DrawOrderResultButton(orderSession, "Mid-Glass", CraftingJobResult.MidGlass);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                DrawOrderResultButton(orderSession, "Mid-Ice+Glass", CraftingJobResult.MidIceGlass);
                DrawOrderResultButton(orderSession, "Mid-WrongMenu", CraftingJobResult.MidWrongMenu);
                DrawOrderResultButton(orderSession, "Bad", CraftingJobResult.Bad);
                EditorGUILayout.EndHorizontal();
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "영업과 현재 주문 제어는 Play Mode에서만 사용할 수 있습니다.",
                    MessageType.None);
            }
        }

        private void DrawFullReset()
        {
            EditorGUILayout.LabelField("전체 진행 데이터", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "전체 초기화는 autosave.json을 백업한 뒤 삭제합니다. 다음 Play Mode는 Day 1의 새 데이터로 시작합니다.",
                MessageType.Warning);

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("전체 진행 데이터 초기화", GUILayout.Height(30f)))
                {
                    bool confirmed = EditorUtility.DisplayDialog(
                        "전체 진행 데이터 초기화",
                        "현재 autosave.json을 백업하고 모든 진행 데이터를 초기화합니다.\n\n"
                        + DataManager.SaveFilePath,
                        "백업 후 초기화",
                        "취소");
                    if (confirmed)
                        ResetAllProgress();
                }
            }

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "메모리의 이전 진행 데이터가 다시 저장되는 것을 막기 위해 Play Mode에서는 전체 초기화를 사용할 수 없습니다.",
                    MessageType.None);
            }
        }

        private void ForceCompleteCurrentEpisode(EpisodeManager manager)
        {
            if (manager != null
                && manager.TryForceCompleteCurrentEpisode(out string completedEpisodeId))
            {
                statusMessage = DataManager.AreDiskWritesSuppressed
                    ? $"'{completedEpisodeId}'을(를) 현재 플레이 메모리에서 강제 완료했습니다."
                    : $"'{completedEpisodeId}'을(를) 강제 완료하고 저장했습니다.";
                statusType = MessageType.Info;
            }
            else
            {
                statusMessage = "현재 에피소드를 강제 완료하지 못했습니다. Console의 상태 불일치 오류를 확인하세요.";
                statusType = MessageType.Error;
            }
        }

        private void ForceCompleteBusiness(
            BusinessFlowBootstrap flow,
            BusinessShiftController shift)
        {
            bool waitingForCurrentAction = shift != null
                && (shift.State == BusinessShiftState.OrderActive
                    || shift.State == BusinessShiftState.EncounterActive);
            if (flow != null && flow.TryForceCompleteBusiness())
            {
                statusMessage = waitingForCurrentAction
                    ? "영업 강제 완료를 예약했습니다. 현재 주문 결과를 선택하거나 에피소드를 완료하면 정산합니다."
                    : "영업을 강제 완료하고 정산 단계로 이동했습니다.";
                statusType = MessageType.Info;
            }
            else
            {
                statusMessage = "강제 완료할 활성 영업을 찾지 못했습니다.";
                statusType = MessageType.Warning;
            }
        }

        private void DrawOrderResultButton(
            BusinessOrderSessionController orderSession,
            string label,
            CraftingJobResult result)
        {
            if (!GUILayout.Button(label))
                return;

            if (orderSession != null && orderSession.TryForceCurrentOrderResult(result))
            {
                statusMessage = $"현재 주문을 {label} 결과로 넘겼습니다.";
                statusType = MessageType.Info;
            }
            else
            {
                statusMessage = "결과를 적용할 현재 주문을 찾지 못했습니다.";
                statusType = MessageType.Warning;
            }
        }

        private void SetEpisodeCompletion(string episodeId, bool completed)
        {
            if (string.IsNullOrWhiteSpace(episodeId))
                return;

            if (EditorApplication.isPlaying)
            {
                EpisodeManager manager = EpisodeManager.Instance;
                if (manager != null
                    && string.Equals(
                        manager.CurrentPlayingEpisodeID,
                        episodeId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    statusMessage = "진행 중인 에피소드는 위의 '현재 에피소드 강제 완료'를 사용하세요.";
                    statusType = MessageType.Warning;
                    return;
                }

                GameProgress progress = GameProgress.Instance;
                if (progress == null)
                {
                    statusMessage = "GameProgress 인스턴스를 찾지 못했습니다.";
                    statusType = MessageType.Error;
                    return;
                }

                if (completed)
                    progress.MarkEpisodeCompleted(episodeId);
                else
                    progress.UnmarkEpisodeCompleted(episodeId);
                DataManager.Instance?.Save();

                statusMessage = DataManager.AreDiskWritesSuppressed
                    ? $"'{episodeId}' 상태를 현재 플레이 메모리에서만 변경했습니다."
                    : $"'{episodeId}' 상태를 {(completed ? "완료" : "미완료")}로 변경하고 저장했습니다.";
                statusType = MessageType.Info;
                return;
            }

            if (!TryReadSaveData(out SaveData data, out string error))
            {
                statusMessage = error;
                statusType = MessageType.Error;
                return;
            }

            data.completedEpisodeIds ??= new List<string>();
            data.completedEpisodeIds.RemoveAll(
                id => string.Equals(id, episodeId, StringComparison.OrdinalIgnoreCase));
            if (completed)
                data.completedEpisodeIds.Add(episodeId);

            if (!TryWriteSaveData(data, out error))
            {
                statusMessage = error;
                statusType = MessageType.Error;
                return;
            }

            statusMessage = $"'{episodeId}' 상태를 {(completed ? "완료" : "미완료")}로 변경했습니다.";
            statusType = MessageType.Info;
        }

        private static bool IsEpisodeCompleted(string episodeId)
        {
            if (EditorApplication.isPlaying && GameProgress.Instance != null)
                return GameProgress.Instance.IsEpisodeCompleted(episodeId);

            return TryReadSaveData(out SaveData data, out _)
                && data.completedEpisodeIds != null
                && data.completedEpisodeIds.Any(
                    id => string.Equals(id, episodeId, StringComparison.OrdinalIgnoreCase));
        }

        private void ResetAllProgress()
        {
            string path = DataManager.SaveFilePath;
            try
            {
                if (!File.Exists(path))
                {
                    statusMessage = "삭제할 저장 데이터가 없습니다. 다음 Play Mode는 새 데이터로 시작합니다.";
                    statusType = MessageType.Info;
                    return;
                }

                string directory = Path.GetDirectoryName(path);
                string backupName =
                    $"autosave.backup.{DateTime.Now:yyyyMMdd_HHmmss_fff}.json";
                string backupPath = Path.Combine(directory ?? Application.persistentDataPath, backupName);
                File.Copy(path, backupPath, overwrite: false);
                File.Delete(path);

                statusMessage = "전체 진행 데이터를 초기화했습니다. 백업: " + backupPath;
                statusType = MessageType.Info;
            }
            catch (Exception exception)
            {
                statusMessage = "진행 데이터 초기화 실패: " + exception.Message;
                statusType = MessageType.Error;
            }
        }

        private void RefreshEpisodes()
        {
            string selectedId = episodes.Length > 0
                && selectedEpisodeIndex >= 0
                && selectedEpisodeIndex < episodes.Length
                    ? episodes[selectedEpisodeIndex]?.episodeId
                    : null;

            episodes = Resources.LoadAll<EpisodeData>("EpisodeData")
                .Where(episode => episode != null && !string.IsNullOrWhiteSpace(episode.episodeId))
                .OrderBy(episode => episode.episodeId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            episodeLabels = episodes
                .Select(episode => string.IsNullOrWhiteSpace(episode.episodeTitle)
                    ? episode.episodeId
                    : $"{episode.episodeId} — {episode.episodeTitle}")
                .ToArray();

            selectedEpisodeIndex = 0;
            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                int previous = Array.FindIndex(
                    episodes,
                    episode => string.Equals(
                        episode.episodeId,
                        selectedId,
                        StringComparison.OrdinalIgnoreCase));
                if (previous >= 0)
                    selectedEpisodeIndex = previous;
            }
        }

        private static bool TryReadSaveData(out SaveData data, out string error)
        {
            string path = DataManager.SaveFilePath;
            data = new SaveData();
            error = string.Empty;
            if (!File.Exists(path))
                return true;

            try
            {
                string json = File.ReadAllText(path);
                data = JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
                return true;
            }
            catch (Exception exception)
            {
                error = "저장 데이터를 읽지 못했습니다: " + exception.Message;
                return false;
            }
        }

        private static bool TryWriteSaveData(SaveData data, out string error)
        {
            string path = DataManager.SaveFilePath;
            string temporaryPath = path + ".tmp";
            error = string.Empty;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, prettyPrint: true));
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                    catch (Exception replaceException) when (
                        replaceException is PlatformNotSupportedException
                        || replaceException is IOException)
                    {
                        File.Copy(temporaryPath, path, overwrite: true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }

                return true;
            }
            catch (Exception exception)
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
                error = "저장 데이터를 쓰지 못했습니다: " + exception.Message;
                return false;
            }
        }
    }
}
