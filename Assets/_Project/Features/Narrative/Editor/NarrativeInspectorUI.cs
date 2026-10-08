using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
    public static class NarrativeInspectorUI
    {
        public static void DrawInspector(VisualElement container, NarrativeNodeView nodeView, NarrativeGraphView gv, NarrativeGraphEditor editor)
        {
            container.Clear();
            container.styleSheets.Add(NarrativeUIHelper.LoadStyle());
            if (nodeView == null)
            {
                DrawGraphMetadata(container, gv);
                container.Add(NarrativeUIHelper.CreateDivider());
                DrawTemplates(container, editor);
                return;
            }

            nodeView.ClearValidationEvents();
            var data = nodeView.nodeData;

            // 1. Warning Header
            var warningBox = new HelpBox("", HelpBoxMessageType.Error).With(x => { x.style.display = DisplayStyle.None; x.style.marginBottom = 10; });
            container.Add(warningBox);
            System.Action updateHeader = () => { var msg = nodeView.GetWarningMessage(); warningBox.text = msg; warningBox.style.display = string.IsNullOrEmpty(msg) ? DisplayStyle.None : DisplayStyle.Flex; };
            nodeView.OnValidationChanged += updateHeader;
            updateHeader();

            container.Add(NarrativeUIHelper.CreateLabel(data.GetType().Name, "section-header"));

            // 2. Custom Fields
            var foldout = new Foldout { text = "General Fields", value = true };
            container.Add(foldout);
            System.Action refreshFields = null;
            refreshFields = () => NarrativeUIHelper.DrawList(foldout, data.CustomFields, (c, f, i) => {
                var row = NarrativeUIHelper.CreateRow();
                // Name
                row.Add(new TextField { value = f.FieldName }.With(x => {
                    x.style.width = 100;
                    x.RegisterValueChangedCallback(e => { f.FieldName = e.newValue; nodeView.RefreshVisuals(); gv.ValidateAllNodes(); });
                }));
                row.Add(NarrativeUIHelper.CreateWarningIcon(nodeView, $"field_{i}"));
                // Value
                row.Add(new TextField { value = f.FieldValue }.SetFlex(1).With(x => {
                    x.RegisterValueChangedCallback(e => { f.FieldValue = e.newValue; nodeView.RefreshVisuals(); gv.ValidateAllNodes(); });
                }));
                if (f.FieldName.ToLower() == "title") row.Add(NarrativeUIHelper.CreateWarningIcon(nodeView, "title"));

                row.Add(NarrativeUIHelper.CreateButton("X", () => { data.CustomFields.RemoveAt(i); refreshFields(); nodeView.RefreshVisuals(); gv.ValidateAllNodes(); }));
                c.Add(row);
            }, () => { data.CustomFields.Add(new CustomNodeField { FieldName = "New", FieldValue = "" }); refreshFields(); nodeView.RefreshVisuals(); gv.ValidateAllNodes(); });
            refreshFields();

            if (data is EpisodeNodeSO ep) DrawEpisode(container, ep, nodeView, gv);
            else if (data is TriggerNodeSO tr) DrawTrigger(container, tr, nodeView, gv);

            container.Add(NarrativeUIHelper.CreateDivider());
            container.Add(NarrativeUIHelper.CreateLabel("Save Template", "field-label").SetMargin(10, 0));
            var tName = new TextField(); container.Add(tName);
            container.Add(NarrativeUIHelper.CreateButton("Save", () => TemplateManager.SaveTemplate(data, tName.value)));
        }

        private static void DrawEpisode(VisualElement container, EpisodeNodeSO ep, NarrativeNodeView view, NarrativeGraphView gv)
        {
            DrawSelectedEvent(container, ep, view, gv);
            DrawBlockTools(container, ep, view, gv);
            
            var sec = new VisualElement().AddClass("inspector-container");
            sec.Add(NarrativeUIHelper.CreateLabel("Outcome Branches", "field-label"));
            container.Add(sec);

            var kind = NarrativeBlockModel.GetTerminalKind(ep);
            if (kind != BlockTerminalKind.Branches)
            {
                // 선택지/제조로 끝나는 블록은 포트가 이벤트 내용으로 정해지므로 여기서 편집하지 않는다.
                sec.Add(NarrativeUIHelper.CreateLabel(kind == BlockTerminalKind.Choice
                        ? "선택지 버튼이 곧 포트입니다. 시퀀스 에디터에서 선택지를 편집하세요."
                        : "제조 결과 6종이 곧 포트입니다.", "info-label")
                    .With(l => { l.style.color = Color.gray; l.style.whiteSpace = WhiteSpace.Normal; }));
                var labels = NarrativeBlockModel.GetPortLabels(ep);
                for (int i = 0; i < labels.Count; i++)
                {
                    var row = NarrativeUIHelper.CreateRow();
                    row.Add(NarrativeUIHelper.CreateLabel(labels[i], "field-value").SetFlex(1));
                    row.Add(NarrativeUIHelper.CreateWarningIcon(view, $"branch_{i}"));
                    sec.Add(row);
                }
                return;
            }

            sec.Add(new HelpBox(
                "라벨 문법 (CSV 분기 섹션과 같음)\n"
                + "• Next — 조건 없는 기본 다음\n"
                + "• flag == true / a&b == true(모두 켜짐)\n"
                + "• flag == false / a&b == false(모두 꺼짐)\n"
                + "• var >= 5  (>=, >, ==, <, <=)\n"
                + "• 에피소드ID — 그 에피소드를 완료했으면\n"
                + "확인 순서: 위 포트부터 차례로, 맞는 게 없으면 Next",
                HelpBoxMessageType.Info));

            System.Action refresh = null;
            refresh = () => NarrativeUIHelper.DrawList(sec, ep.OutgoingBranches, (c, b, i) => {
                var row = NarrativeUIHelper.CreateRow();
                row.Add(new TextField { value = b }.SetFlex(1).With(x => {
                    x.RegisterValueChangedCallback(e => { ep.OutgoingBranches[i] = e.newValue; view.RebuildPorts(); gv.ValidateAllNodes(); });
                }));
                row.Add(NarrativeUIHelper.CreateWarningIcon(view, $"branch_{i}"));
                row.Add(NarrativeUIHelper.CreateButton("X", () => { ep.OutgoingBranches.RemoveAt(i); refresh(); gv.NotifyNodeStructureChanged(view); gv.ValidateAllNodes(); }));
                c.Add(row);
            }, () => { ep.OutgoingBranches.Add("New"); refresh(); gv.NotifyNodeStructureChanged(view); gv.ValidateAllNodes(); });
            refresh();
        }

        // 카드에서 고른 이벤트의 상세 편집. 선택이 없으면 사용법만 안내한다.
        private static void DrawSelectedEvent(VisualElement container, EpisodeNodeSO ep, NarrativeNodeView view, NarrativeGraphView gv)
        {
            var section = new VisualElement().AddClass("inspector-container").SetMargin(8, 8);
            container.Add(section);

            EpisodeEvent ev = view.SelectedEvent;
            if (ev == null)
            {
                section.Add(NarrativeUIHelper.CreateLabel(
                    "카드에서 줄을 클릭하면 여기서 자세히 편집합니다.\n대사는 카드에서 더블클릭해 바로 고칠 수 있고, 우클릭 메뉴로 추가·블록 나누기를 할 수 있습니다.",
                    "info-label").With(l => { l.style.whiteSpace = WhiteSpace.Normal; }));
                return;
            }

            EventInspectorUI.Draw(
                section,
                ev,
                ep,
                onChanged: () => { view.RefreshVisuals(); gv.ValidateAllNodes(); },
                onPortsChanged: () => gv.RefreshBlock(view));
        }

        private static void DrawBlockTools(VisualElement container, EpisodeNodeSO ep, NarrativeNodeView view, NarrativeGraphView gv)
        {
            var row = NarrativeUIHelper.CreateRow().SetMargin(4, 8);
            int index = view.SelectedEventIndex;
            var split = NarrativeUIHelper.CreateButton("선택한 줄부터 블록 나누기", () => gv.SplitBlock(view, index)).SetFlex(1);
            split.SetEnabled(NarrativeBlockEditing.CanSplit(ep, index));
            split.tooltip = "선택한 이벤트부터 새 블록으로 떼어 내고 Next로 잇습니다. 기존 출력 연결은 새 블록이 물려받습니다.";
            row.Add(split);

            bool canMerge = NarrativeBlockEditing.TryGetMergeTarget(gv.currentGraph, ep, out _, out string reason);
            var merge = NarrativeUIHelper.CreateButton("다음 블록과 합치기", () => gv.MergeWithNext(view)).SetFlex(1);
            merge.SetEnabled(canMerge);
            merge.tooltip = canMerge ? "Next로 이어진 다음 블록을 이 블록 뒤에 붙입니다." : reason;
            row.Add(merge);
            container.Add(row);
        }

        private static void DrawTrigger(VisualElement container, TriggerNodeSO tr, NarrativeNodeView view, NarrativeGraphView gv)
        {
            var list = new VisualElement(); container.Add(list);
            System.Action refresh = null;
            refresh = () => NarrativeUIHelper.DrawList(list, tr.Conditions, (c, cond, i) => {
                var box = new Box().AddClass("inspector-container").SetMargin(0, 5);
                box.Add(NarrativeUIHelper.CreateRow().With(r => {
                    r.Add(new EnumField(cond.Type).SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { cond.Type = (TriggerConditionType)e.newValue; refresh(); view.RefreshVisuals(); gv.ValidateAllNodes(); })));
                    r.Add(NarrativeUIHelper.CreateButton("X", () => { tr.Conditions.RemoveAt(i); refresh(); view.RefreshVisuals(); gv.NotifyNodeStructureChanged(view); gv.ValidateAllNodes(); }));
                }));
                box.Add(NarrativeUIHelper.CreateRow().With(r => {
                    r.Add(NarrativeUIHelper.CreateLabel("Key", "field-label").With(l => l.style.width = 40));
                    r.Add(new TextField { value = cond.Key }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { cond.Key = e.newValue; view.RefreshVisuals(); gv.ValidateAllNodes(); })));
                    r.Add(NarrativeUIHelper.CreateWarningIcon(view, $"cond_key_{i}"));
                }));
                if (cond.Type != TriggerConditionType.Episode)
                box.Add(NarrativeUIHelper.CreateRow().With(r => {
                    r.Add(new TextField { value = cond.Operator }.With(x => { x.style.width = 40; x.RegisterValueChangedCallback(e => { cond.Operator = e.newValue; gv.ValidateAllNodes(); }); }));
                    r.Add(new TextField { value = cond.Value }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e => { cond.Value = e.newValue; view.RefreshVisuals(); gv.ValidateAllNodes(); })));
                    r.Add(NarrativeUIHelper.CreateWarningIcon(view, $"cond_val_{i}"));
                }));
                c.Add(box);
            }, () => { tr.Conditions.Add(new GraphTriggerCondition()); refresh(); gv.ValidateAllNodes(); });
            refresh();
        }

        private static void DrawGraphMetadata(VisualElement container, NarrativeGraphView gv)
        {
            var graph = gv.currentGraph;
            if (graph == null)
            {
                container.Add(NarrativeUIHelper.CreateLabel("No graph loaded.", "info-label").SetMargin(20, 0));
                return;
            }

            container.Add(NarrativeUIHelper.CreateLabel("Graph Settings", "section-header"));

            // Episode ID
            var idRow = NarrativeUIHelper.CreateRow();
            idRow.Add(NarrativeUIHelper.CreateLabel("Episode ID", "field-label").With(l => l.style.width = 90));
            idRow.Add(new TextField { value = graph.EpisodeId }.SetFlex(1).With(x =>
                x.RegisterValueChangedCallback(e => { Undo.RecordObject(graph, "Set EpisodeId"); graph.EpisodeId = e.newValue; EditorUtility.SetDirty(graph); })));
            container.Add(idRow);

            // Episode Title
            var titleRow = NarrativeUIHelper.CreateRow();
            titleRow.Add(NarrativeUIHelper.CreateLabel("Title", "field-label").With(l => l.style.width = 90));
            titleRow.Add(new TextField { value = graph.EpisodeTitle }.SetFlex(1).With(x =>
                x.RegisterValueChangedCallback(e => { Undo.RecordObject(graph, "Set EpisodeTitle"); graph.EpisodeTitle = e.newValue; EditorUtility.SetDirty(graph); })));
            container.Add(titleRow);

            // Start Node dropdown
            container.Add(NarrativeUIHelper.CreateDivider());
            container.Add(NarrativeUIHelper.CreateLabel("Start Node", "field-label"));
            var episodeNodes = graph.Nodes.OfType<EpisodeNodeSO>().ToList();
            if (episodeNodes.Count > 0)
            {
                var labels = episodeNodes.Select(GetNodeTitle).ToList();
                labels.Insert(0, "(None)");

                int startIdx = 0;
                if (!string.IsNullOrEmpty(graph.StartNodeGuid))
                {
                    int found = episodeNodes.FindIndex(n => n.Guid == graph.StartNodeGuid);
                    if (found >= 0) startIdx = found + 1;
                }

                var popup = new PopupField<string>(labels, startIdx);
                popup.RegisterValueChangedCallback(e =>
                {
                    int idx = labels.IndexOf(e.newValue);
                    Undo.RecordObject(graph, "Set StartNode");
                    graph.StartNodeGuid = idx > 0 ? episodeNodes[idx - 1].Guid : "";
                    EditorUtility.SetDirty(graph);
                    NarrativeNodeIdAssigner.RegenerateIds(graph);
                    gv.window.ReloadGraph();
                });
                container.Add(popup);
            }
            else
            {
                container.Add(NarrativeUIHelper.CreateLabel("(Add episode blocks first)", "info-label").With(l => l.style.color = Color.gray));
            }

            // Chapter & schedule
            container.Add(NarrativeUIHelper.CreateDivider());
            container.Add(TextRow("Chapter ID", graph.ChapterId, v => graph.ChapterId = v, graph));
            container.Add(NarrativeUIHelper.CreateLabel("영업 일정 (CSV #META day / slot / priority)", "field-label").SetMargin(6, 0));
            var scheduleInfo = NarrativeUIHelper.CreateLabel("", "info-label").With(l => l.style.whiteSpace = WhiteSpace.Normal);
            System.Action refreshSchedule = () => scheduleInfo.text = DescribeSchedule(graph);
            container.Add(IntRow("Day", graph.ScheduledDay, v => { graph.ScheduledDay = Mathf.Max(0, v); refreshSchedule(); }, graph));
            container.Add(IntRow("Slot", graph.ScheduledSlot, v => { graph.ScheduledSlot = Mathf.Max(0, v); refreshSchedule(); }, graph));
            container.Add(IntRow("Priority", graph.SlotPriority, v => { graph.SlotPriority = v; refreshSchedule(); }, graph));
            container.Add(scheduleInfo);
            refreshSchedule();

            // 등장 조건 · 정산 보상은 Unity 기본 리스트 편집기로 직접 편집한다(Undo 지원).
            container.Add(NarrativeUIHelper.CreateDivider());
            var serialized = new SerializedObject(graph);
            AddProperty(container, serialized, "TriggerCondition", "등장 조건 (CSV #TRIGGER)");
            AddProperty(container, serialized, "SettlementRewards", "정산 보상 (CSV #SETTLEMENT_REWARDS)");

            DrawCsvSync(container, gv, graph);
            return;

        }

        private static VisualElement TextRow(string label, string value, System.Action<string> set, Object target)
        {
            var row = NarrativeUIHelper.CreateRow();
            row.Add(NarrativeUIHelper.CreateLabel(label, "field-label").With(l => l.style.width = 90));
            row.Add(new TextField { value = value }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(target, $"Set {label}");
                set(e.newValue);
                EditorUtility.SetDirty(target);
            })));
            return row;
        }

        private static VisualElement IntRow(string label, int value, System.Action<int> set, Object target)
        {
            var row = NarrativeUIHelper.CreateRow();
            row.Add(NarrativeUIHelper.CreateLabel(label, "field-label").With(l => l.style.width = 90));
            row.Add(new IntegerField { value = value }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(target, $"Set {label}");
                set(e.newValue);
                EditorUtility.SetDirty(target);
            })));
            return row;
        }

        private static void AddProperty(VisualElement container, SerializedObject serialized, string propertyName, string label)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null) return;
            var field = new PropertyField(property, label);
            field.Bind(serialized);
            container.Add(field.SetMargin(2, 4));
        }

        // 같은 (챕터, day, slot)에 배정된 다른 에피소드와 하루 손님 수 범위를 알려준다.
        private static string DescribeSchedule(NarrativeGraphSO graph)
        {
            if (graph.ScheduledDay <= 0 || graph.ScheduledSlot <= 0)
                return "일정 미배정 — 영업에 자동으로 등장하지 않습니다.";

            var settings = Slainte.Business.BusinessOrderFlowSettings.LoadDefault();
            int slots = settings != null ? settings.customersPerDay : 5;
            string info = $"Day {graph.ScheduledDay}의 {graph.ScheduledSlot}번 손님 슬롯";
            if (graph.ScheduledSlot > slots)
                info += $"\n⚠ 하루 손님 수({slots})를 넘는 슬롯이라 등장하지 않습니다.";

            var others = Resources.LoadAll<EpisodeData>(Slainte.Content.ProjectResourcePaths.NarrativeEpisodes)
                .Where(e => e != null
                    && !string.Equals(e.episodeId, graph.EpisodeId, System.StringComparison.OrdinalIgnoreCase)
                    && e.scheduledDay == graph.ScheduledDay
                    && e.scheduledSlot == graph.ScheduledSlot
                    && (string.IsNullOrEmpty(e.chapterId) || string.IsNullOrEmpty(graph.ChapterId)
                        || string.Equals(e.chapterId, graph.ChapterId, System.StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => e.slotPriority)
                .ToList();
            if (others.Count == 0)
                return info + "\n같은 슬롯의 다른 후보 없음.";

            info += "\n같은 슬롯 후보(큰 priority부터 등장 조건 확인):";
            foreach (var e in others)
            {
                info += $"\n  • {e.episodeId} (priority {e.slotPriority})";
                if (e.slotPriority == graph.SlotPriority) info += " ⚠ priority 같음 — episodeId 순서로 확인됨";
            }
            return info;
        }

        private static void DrawCsvSync(VisualElement container, NarrativeGraphView gv, NarrativeGraphSO graph)
        {
            container.Add(NarrativeUIHelper.CreateDivider());
            container.Add(NarrativeUIHelper.CreateLabel("CSV 동기화", "section-header"));

            string id = string.IsNullOrWhiteSpace(graph.EpisodeId) ? graph.name : graph.EpisodeId;
            string path = NarrativeCsvSync.ResolveSourceCsvPath(graph, id);
            bool exists = System.IO.File.Exists(path);
            string status = !exists ? "아직 없음 — 컴파일하면 새로 만듭니다."
                : NarrativeCsvSync.HasExternalChanges(graph, path) ? "⚠ 마지막 동기화 이후 CSV가 바뀌었거나 동기화 기록이 없습니다."
                : "동기화됨";
            container.Add(NarrativeUIHelper.CreateLabel(path, "info-label").With(l => { l.style.whiteSpace = WhiteSpace.Normal; l.style.color = Color.gray; }));
            container.Add(NarrativeUIHelper.CreateLabel(status, "info-label").With(l => l.style.whiteSpace = WhiteSpace.Normal));

            container.Add(new Button(() =>
            {
                if (new EpisodeDataCompiler().Compile(graph))
                    gv.window.ShowNotification(new GUIContent("EpisodeData와 CSV에 저장했습니다."));
                gv.window.ReloadGraph();
            }) { text = "컴파일 (EpisodeData + 원본 CSV)" }
                .With(b => { b.style.height = 30; b.style.backgroundColor = new Color(0.15f, 0.35f, 0.15f); }).SetMargin(4, 0));

            if (exists)
            {
                container.Add(new Button(() =>
                {
                    if (!NarrativeCsvSync.HasExternalChanges(graph, path)
                        || EditorUtility.DisplayDialog("CSV에서 다시 만들기", "그래프 내용을 CSV 내용으로 교체합니다(블록 위치는 유지). 컴파일하지 않은 그래프 수정은 사라집니다.", "교체", "취소"))
                    {
                        NarrativeCsvSync.ImportCsvToGraph(path);
                        gv.window.ReloadGraph();
                    }
                }) { text = "CSV에서 그래프 다시 만들기" }.SetMargin(4, 0));
            }

            container.Add(new Button(() =>
            {
                string picked = EditorUtility.OpenFilePanel("원본 CSV 지정", Slainte.EditorTools.NarrativeAssetPaths.EpisodeSourceRoot, "csv");
                if (string.IsNullOrEmpty(picked)) return;
                Undo.RecordObject(graph, "Set Source CSV");
                graph.SourceCsvPath = NarrativeCsvSync.ToProjectRelative(picked);
                graph.LastSyncedCsvHash = string.Empty;
                EditorUtility.SetDirty(graph);
                gv.window.OnNodeSelectionChanged(null);
            }) { text = "원본 CSV 파일 지정..." }.SetMargin(4, 0));
        }

        private static string GetNodeTitle(EpisodeNodeSO node)
        {
            var titleField = node.CustomFields?.Find(f => f.FieldName?.ToLower() == "title");
            return !string.IsNullOrEmpty(titleField?.FieldValue) ? titleField.FieldValue : node.name;
        }

        private static void DrawTemplates(VisualElement container, NarrativeGraphEditor editor)
        {
            container.Add(NarrativeUIHelper.CreateLabel("Templates", "section-header"));
            foreach (var t in TemplateManager.GetAllTemplates())
            {
                var row = NarrativeUIHelper.CreateRow().SetMargin(0, 2);
                row.Add(new Label(t.name).SetFlex(1));
                row.Add(NarrativeUIHelper.CreateButton("Delete", () => { if (EditorUtility.DisplayDialog("Del", $"Delete {t.name}?", "Yes")) { TemplateManager.DeleteTemplate(t); editor.OnNodeSelectionChanged(null); } }));
                container.Add(row);
            }
        }
    }
}
