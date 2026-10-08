using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
    // 그래프 노드 카드. 블록(EpisodeNodeSO)은 이벤트를 한 줄씩 보여 주고 그 자리에서 편집한다:
    // 줄 클릭 = 선택(왼쪽 패널에 상세 편집), 대사 더블클릭 = 카드 위에서 바로 수정, ↑↓✕ 버튼과 우클릭 메뉴로
    // 추가·이동·삭제·블록 나누기. 데이터 변경은 NarrativeBlockEditing, 포트·검증 갱신은 NarrativeGraphView.RefreshBlock.
    public class NarrativeNodeView : Node
    {
        private const int CollapsedRowCount = 4;
        private const float CardWidth = NarrativeGraphLayout.CardWidth;

        public NodeDataSO nodeData;
        private VisualElement _body;
        private Label _warningIcon;
        private Dictionary<string, string> _fieldErrors = new();
        private bool _inlineEditing;
        public event System.Action OnValidationChanged;

        // 카드에서 선택한 이벤트(왼쪽 패널 상세 편집 대상). 범위를 벗어나면 선택 없음.
        public int SelectedEventIndex { get; private set; } = -1;

        public NarrativeNodeView(NodeDataSO data)
        {
            nodeData = data;
            title = data.name;
            viewDataKey = data.Guid;
            SetPosition(data.Position);
            this.styleSheets.Add(NarrativeUIHelper.LoadStyle());

            capabilities |= Capabilities.Collapsible;
            if (data is EpisodeNodeSO)
            {
                style.width = CardWidth;
                mainContainer.style.width = CardWidth;
            }

            SetupTitleBar();
            CreateInputPorts();
            CreateOutputPorts();

            _body = new VisualElement().AddClass("node-body");
            extensionContainer.Add(_body);

            RefreshVisuals();

            expanded = true;
            RefreshExpandedState();
        }

        private NarrativeGraphView GraphView => GetFirstAncestorOfType<NarrativeGraphView>();
        public EpisodeEvent SelectedEvent =>
            nodeData is EpisodeNodeSO ep && SelectedEventIndex >= 0 && SelectedEventIndex < ep.Events.Count
                ? ep.Events[SelectedEventIndex]
                : null;

        private void SetupTitleBar()
        {
            _warningIcon = new Label("⚠️")
            {
                style =
                {
                    color = new Color(1f, 0.3f, 0.3f),
                    fontSize = 14,
                    marginRight = 4,
                    marginLeft = 4,
                    display = DisplayStyle.None
                }
            };
            titleContainer.Insert(0, _warningIcon);
            var cb = titleButtonContainer.Q<VisualElement>("collapse-button");
            if (cb != null) cb.style.display = DisplayStyle.Flex;
        }

        public void ClearValidationEvents() => OnValidationChanged = null;

        public void SetWarning(bool show, string message = "", Dictionary<string, string> fieldErrors = null)
        {
            if (_warningIcon == null) return;

            _warningIcon.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show && !string.IsNullOrEmpty(message)) _warningIcon.tooltip = message;

            _fieldErrors = fieldErrors ?? new Dictionary<string, string>();

            // 카드 위에서 대사를 고치는 중에는 다시 그리지 않는다(입력 중인 텍스트 필드가 사라지므로).
            if (!_inlineEditing) RefreshVisuals();
            UpdatePortErrors();
            OnValidationChanged?.Invoke();
        }

        public string GetWarningMessage() => _warningIcon != null && _warningIcon.style.display == DisplayStyle.Flex ? _warningIcon.tooltip : "";
        public string GetFieldError(string key) => _fieldErrors.TryGetValue(key, out var msg) ? msg : null;

        public void RefreshVisuals()
        {
            _body.Clear();
            string nodeTitle = nodeData is EpisodeNodeSO ? "Episode" : nodeData is TriggerNodeSO ? "Trigger" : "Node";

            if (nodeData.CustomFields != null)
            {
                for (int i = 0; i < nodeData.CustomFields.Count; i++)
                {
                    var field = nodeData.CustomFields[i];
                    if (field.FieldName.ToLower() == "title") { if (!string.IsNullOrEmpty(field.FieldValue)) nodeTitle = field.FieldValue; continue; }

                    var row = NarrativeUIHelper.CreateRow();
                    row.Add(NarrativeUIHelper.CreateLabel($"{field.FieldName}: ", "field-label"));
                    row.Add(NarrativeUIHelper.CreateLabel(field.FieldValue, "field-value"));
                    var err = GetFieldError($"field_{i}");
                    row.MarkError(err, !string.IsNullOrEmpty(err));
                    _body.Add(row);
                }
            }

            title = nodeTitle;
            if (_body.childCount > 0) _body.Insert(0, NarrativeUIHelper.CreateDivider());

            if (nodeData is EpisodeNodeSO ep) DrawEpisode(ep);
            else if (nodeData is TriggerNodeSO tr) DrawTrigger(tr);

            RefreshExpandedState();
        }

        // ── 블록 이벤트 목록 ────────────────────────────────────────────────────

        private void DrawEpisode(EpisodeNodeSO ep)
        {
            if (SelectedEventIndex >= ep.Events.Count) SelectedEventIndex = -1;

            var unreachable = new List<EpisodeEvent>();
            NarrativeBlockModel.GetExecutionOrder(ep, unreachable);

            int visible = ep.ShowAllEvents ? ep.Events.Count : Mathf.Min(CollapsedRowCount, ep.Events.Count);
            // 접힌 상태에서도 선택한 줄은 보여야 편집 중인 위치를 잃지 않는다.
            if (!ep.ShowAllEvents && SelectedEventIndex >= visible) visible = SelectedEventIndex + 1;

            if (ep.Events.Count == 0)
                _body.Add(NarrativeUIHelper.CreateLabel("(이벤트 없음 — 아래 + 버튼으로 추가)", "info-label").With(l => l.style.color = Color.gray));

            for (int i = 0; i < visible; i++)
                _body.Add(CreateEventRow(ep, i, unreachable.Contains(ep.Events[i])));

            if (ep.Events.Count > CollapsedRowCount)
            {
                int hidden = ep.Events.Count - visible;
                _body.Add(NarrativeUIHelper.CreateButton(
                    ep.ShowAllEvents ? "▲ 접기" : $"▼ {hidden}줄 더 보기",
                    () =>
                    {
                        Undo.RecordObject(ep, "Toggle Events");
                        ep.ShowAllEvents = !ep.ShowAllEvents;
                        EditorUtility.SetDirty(ep);
                        RefreshVisuals();
                    }).With(b => { b.style.fontSize = 10; b.style.height = 18; }));
            }

            var addRow = NarrativeUIHelper.CreateRow().SetMargin(4, 0);
            addRow.Add(NarrativeUIHelper.CreateButton("+ 대사", () => InsertAfterSelection(ep, EpisodeEventType.Dialogue)).SetFlex(1));
            addRow.Add(NarrativeUIHelper.CreateButton("+ 선택지", () => InsertAfterSelection(ep, EpisodeEventType.Choice)));
            addRow.Add(NarrativeUIHelper.CreateButton("+ 제조", () => InsertAfterSelection(ep, EpisodeEventType.BusinessStart)));
            _body.Add(addRow);
        }

        private VisualElement CreateEventRow(EpisodeNodeSO ep, int index, bool unreachable)
        {
            EpisodeEvent ev = ep.Events[index];
            bool selected = index == SelectedEventIndex;

            var card = new VisualElement();
            card.style.borderTopWidth = card.style.borderBottomWidth = card.style.borderLeftWidth = card.style.borderRightWidth = 1;
            Color border = selected ? new Color(0.35f, 0.65f, 1f) : new Color(0.25f, 0.25f, 0.25f);
            card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor = border;
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius = card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 3;
            card.style.marginBottom = 3;
            card.style.paddingTop = card.style.paddingBottom = card.style.paddingLeft = card.style.paddingRight = 4;
            if (selected) card.style.backgroundColor = new Color(0.2f, 0.28f, 0.38f, 0.6f);
            if (unreachable) card.style.opacity = 0.4f;

            // 머리줄: 종류 · 화자 · #nodeId · ↑↓✕
            var header = NarrativeUIHelper.CreateRow();
            header.style.alignItems = Align.Center;
            header.Add(NarrativeUIHelper.CreateLabel(TypeBadge(ev), "field-label").With(l =>
            {
                l.style.width = StyleKeyword.Auto;
                l.style.color = TypeColor(ev.Type);
                l.style.marginRight = 4;
            }));
            if (!string.IsNullOrEmpty(ev.SpeakerKey))
                header.Add(NarrativeUIHelper.CreateLabel(ev.SpeakerKey, "field-label").With(l => { l.style.width = StyleKeyword.Auto; l.style.color = new Color(1f, 0.8f, 0.4f); }));
            header.Add(new VisualElement().SetFlex(1));
            if (!string.IsNullOrEmpty(ev.RuntimeNodeId))
                header.Add(NarrativeUIHelper.CreateLabel($"#{ev.RuntimeNodeId}", "info-label", 9).With(l => l.style.marginRight = 4));
            header.Add(SmallButton("↑", "위로", () => Apply(ep, () => NarrativeBlockEditing.MoveEvent(ep, index, -1), index - 1)));
            header.Add(SmallButton("↓", "아래로", () => Apply(ep, () => NarrativeBlockEditing.MoveEvent(ep, index, 1), index + 1)));
            header.Add(SmallButton("✕", "삭제", () => Apply(ep, () => { NarrativeBlockEditing.RemoveEvent(ep, index); return true; }, -1)));
            card.Add(header);

            AddEventBody(card, ep, ev);

            card.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != 0) return;
                SelectEvent(index);
            });
            card.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("아래에 대사 추가", _ => InsertAt(ep, index + 1, EpisodeEventType.Dialogue));
                evt.menu.AppendAction("위에 대사 추가", _ => InsertAt(ep, index, EpisodeEventType.Dialogue));
                evt.menu.AppendAction("아래에 선택지 추가", _ => InsertAt(ep, index + 1, EpisodeEventType.Choice));
                evt.menu.AppendAction("아래에 제조 추가", _ => InsertAt(ep, index + 1, EpisodeEventType.BusinessStart));
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("이 줄부터 새 블록으로 나누기", _ => GraphView?.SplitBlock(this, index),
                    NarrativeBlockEditing.CanSplit(ep, index) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("삭제", _ => Apply(ep, () => { NarrativeBlockEditing.RemoveEvent(ep, index); return true; }, -1));
                // 노드·그래프의 기본 메뉴(잘라내기/삭제 등)와 섞이면 블록 자체를 지우는 실수가 나기 쉬워 여기서 멈춘다.
                evt.StopPropagation();
            }));
            return card;
        }

        private void AddEventBody(VisualElement card, EpisodeNodeSO ep, EpisodeEvent ev)
        {
            switch (ev.Type)
            {
                case EpisodeEventType.Dialogue:
                case EpisodeEventType.Choice:
                    card.Add(CreateEditableText(ep, ev));
                    AddCharacters(card, ev);
                    if (ev.Type == EpisodeEventType.Choice)
                    {
                        foreach (var c in ev.Choices)
                        {
                            var parts = new List<string>();
                            foreach (var f in c.SetFlags) parts.Add($"+{f}");
                            foreach (var f in c.ClearFlags) parts.Add($"-{f}");
                            foreach (var v in c.VarChanges) parts.Add($"{v.VarName}{(v.Delta >= 0 ? "+" : "")}{v.Delta}");
                            string suffix = parts.Count > 0 ? "   " + string.Join(" ", parts) : "";
                            card.Add(NarrativeUIHelper.CreateLabel($"▸ {(string.IsNullOrEmpty(c.ButtonText) ? "(빈 버튼)" : c.ButtonText)}{suffix}", "info-label")
                                .With(l => { l.style.color = Color.white; l.style.whiteSpace = WhiteSpace.Normal; }));
                        }
                    }
                    break;
                case EpisodeEventType.BusinessStart:
                    string ticketKey = ev.CraftingOrderTicket != null ? ev.CraftingOrderTicket.key : ev.CraftingTicketKey;
                    card.Add(NarrativeUIHelper.CreateLabel($"{ev.CraftingOrderType}: {ev.CraftingOrderTarget}   {ticketKey}", "info-label").With(l => l.style.color = new Color(0.9f, 0.9f, 0.9f)));
                    foreach (var result in CraftingJobResultPorts.Order)
                    {
                        string flag = ev.GetCraftingFlag(result);
                        if (!string.IsNullOrEmpty(flag))
                            card.Add(NarrativeUIHelper.CreateLabel($"[{CraftingJobResultPorts.Label(result)}] {flag}", "info-label"));
                    }
                    AddCharacters(card, ev);
                    break;
                default:
                    card.Add(NarrativeUIHelper.CreateLabel("더 이상 쓰지 않는 이벤트 — 실행에서 무시됩니다. 삭제하세요.", "info-label")
                        .With(l => { l.style.color = new Color(1f, 0.6f, 0.6f); l.style.whiteSpace = WhiteSpace.Normal; }));
                    break;
            }

            if (ev.BgmCommand != BgmCommand.None)
                card.Add(NarrativeUIHelper.CreateLabel($"BGM {ev.BgmCommand} {ev.BgmClipName}", "info-label").With(l => l.style.color = new Color(1f, 0.7f, 1f)));
            if (ev.SfxCommand != SfxCommand.None)
                card.Add(NarrativeUIHelper.CreateLabel($"SFX {ev.SfxCommand} {ev.SfxClipName}", "info-label").With(l => l.style.color = new Color(0.7f, 0.9f, 1f)));
        }

        private static void AddCharacters(VisualElement card, EpisodeEvent ev)
        {
            if (ev.CharacterAppearances.Count == 0) return;
            string summary = string.Join("  ", ev.CharacterAppearances.Select(c =>
                $"{c.CharacterKey}:{(string.IsNullOrEmpty(c.ExpressionKey) ? "-" : c.ExpressionKey)}{(c.SlotIndex >= 0 ? $"@{c.SlotIndex}" : "")}"));
            card.Add(NarrativeUIHelper.CreateLabel(summary, "info-label", 10).With(l =>
            {
                l.style.color = new Color(0.6f, 1f, 0.6f);
                l.style.whiteSpace = WhiteSpace.Normal;
            }));
        }

        // 대사 줄: 평소엔 라벨, 더블클릭하면 그 자리에서 여러 줄 텍스트 필드로 바뀐다.
        // 포커스를 잃거나 Ctrl+Enter면 저장, Esc면 취소.
        private VisualElement CreateEditableText(EpisodeNodeSO ep, EpisodeEvent ev)
        {
            var holder = new VisualElement();
            string text = ev.Text ?? "";
            var label = NarrativeUIHelper.CreateLabel(string.IsNullOrEmpty(text) ? "(대사 없음 — 더블클릭해서 입력)" : text, "field-value");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = string.IsNullOrEmpty(text) ? Color.gray : new Color(0.92f, 0.92f, 0.92f);
            label.tooltip = "더블클릭해서 수정";
            holder.Add(label);

            label.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != 0 || e.clickCount != 2) return;
                e.StopImmediatePropagation();
                BeginInlineEdit(ep, ev, holder, label);
            });
            return holder;
        }

        private void BeginInlineEdit(EpisodeNodeSO ep, EpisodeEvent ev, VisualElement holder, Label label)
        {
            _inlineEditing = true;
            var field = new TextField { value = ev.Text ?? "", multiline = true };
            field.style.whiteSpace = WhiteSpace.Normal;
            field.style.minHeight = 40;
            holder.Remove(label);
            holder.Add(field);

            bool finished = false;
            void Finish(bool commit)
            {
                if (finished) return;
                finished = true;
                _inlineEditing = false;
                if (commit && field.value != (ev.Text ?? ""))
                {
                    Undo.RecordObject(ep, "Edit Dialogue");
                    ev.Text = field.value;
                    EditorUtility.SetDirty(ep);
                }
                RefreshVisuals();
                GraphView?.window?.OnNodeSelectionChanged(this);
            }

            // Esc·Ctrl+Enter만 입력 요소보다 먼저(TrickleDown) 가로챈다. 나머지 키는 입력 요소가 글자를 넣은 뒤
            // 버블 단계에서 전파만 막는다 — 먼저 막으면 타이핑 자체가 안 되고, 안 막으면 Delete/Backspace·Space가
            // 그래프의 노드 삭제·노드 생성 단축키로 번진다.
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) { Finish(false); e.StopImmediatePropagation(); return; }
                if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && (e.ctrlKey || e.commandKey))
                {
                    Finish(true);
                    e.StopImmediatePropagation();
                }
            }, TrickleDown.TrickleDown);
            field.RegisterCallback<KeyDownEvent>(e => e.StopPropagation());
            field.RegisterCallback<ValidateCommandEvent>(e => e.StopPropagation());
            field.RegisterCallback<ExecuteCommandEvent>(e => e.StopPropagation());
            field.RegisterCallback<FocusOutEvent>(_ => Finish(true));
            // 전체 선택하면 첫 글자에 기존 대사가 통째로 지워지므로 커서만 끝에 둔다.
            field.schedule.Execute(() =>
            {
                field.Focus();
                field.SelectRange(field.value.Length, field.value.Length);
            });
        }

        public void SelectEvent(int index)
        {
            if (SelectedEventIndex == index) return;
            SelectedEventIndex = index;
            RefreshVisuals();
            GraphView?.window?.OnNodeSelectionChanged(this);
        }

        private void InsertAfterSelection(EpisodeNodeSO ep, EpisodeEventType type)
        {
            int index = SelectedEventIndex >= 0 ? SelectedEventIndex + 1 : ep.Events.Count;
            InsertAt(ep, index, type);
        }

        private void InsertAt(EpisodeNodeSO ep, int index, EpisodeEventType type)
        {
            NarrativeBlockEditing.InsertEvent(ep, index, type);
            if (index > CollapsedRowCount - 1) ep.ShowAllEvents = true;
            AfterStructureChange(Mathf.Clamp(index, 0, ep.Events.Count - 1));
        }

        private void Apply(EpisodeNodeSO ep, System.Func<bool> action, int newSelection)
        {
            if (!action()) return;
            AfterStructureChange(newSelection);
        }

        // 이벤트 구성 변경은 마지막 이벤트(=포트 구성)를 바꿀 수 있으므로 그래프 쪽에서 포트·검증을 다시 맞춘다.
        private void AfterStructureChange(int newSelection)
        {
            SelectedEventIndex = newSelection;
            var graph = GraphView;
            if (graph != null) graph.RefreshBlock(this);
            else RefreshVisuals();
            graph?.window?.OnNodeSelectionChanged(this);
        }

        private static Button SmallButton(string text, string tooltip, System.Action onClick) =>
            NarrativeUIHelper.CreateButton(text, onClick).With(b =>
            {
                b.tooltip = tooltip;
                b.style.width = 18;
                b.style.height = 16;
                b.style.fontSize = 9;
                b.style.paddingLeft = b.style.paddingRight = 0;
                b.style.marginLeft = b.style.marginRight = 1;
            });

        private static string TypeBadge(EpisodeEvent ev) => ev.Type switch
        {
            EpisodeEventType.Dialogue => "대사",
            EpisodeEventType.Choice => "선택지",
            EpisodeEventType.BusinessStart => "제조",
            _ => "레거시"
        };

        private static Color TypeColor(EpisodeEventType type) => type switch
        {
            EpisodeEventType.Dialogue => new Color(0.5f, 0.8f, 1f),
            EpisodeEventType.Choice => new Color(1f, 0.85f, 0.4f),
            EpisodeEventType.BusinessStart => new Color(1f, 0.6f, 0.4f),
            _ => Color.gray
        };

        // ── Trigger / 포트 ──────────────────────────────────────────────────────

        private void DrawTrigger(TriggerNodeSO tr)
        {
            foreach (var c in tr.Conditions)
            {
                string text = c.Type == TriggerConditionType.Episode ? $"IF {c.Key} 완료" : $"IF {c.Key} {c.Operator} {c.Value}";
                _body.Add(NarrativeUIHelper.CreateLabel(text).With(l => l.style.color = new Color(1f, 0.85f, 0.5f)));
            }
        }

        private void CreateInputPorts() => inputContainer.Add(InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool)).With(p => p.portName = "In"));

        private void CreateOutputPorts()
        {
            outputContainer.Clear();
            if (nodeData is EpisodeNodeSO ep)
            {
                // 선택지/제조로 끝나는 블록은 포트가 이벤트 내용에서 자동으로 정해진다(NarrativeBlockModel).
                var labels = NarrativeBlockModel.GetPortLabels(ep);
                for (int i = 0; i < labels.Count; i++)
                {
                    var row = NarrativeUIHelper.CreateRow("choice-row");
                    row.Add(NarrativeUIHelper.CreateLabel(labels[i], "field-value"));
                    row.Add(InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool)).With(p => { p.portName = ""; p.style.width = 20; }));
                    var err = GetFieldError($"branch_{i}");
                    row.MarkError(err, !string.IsNullOrEmpty(err));
                    outputContainer.Add(row);
                }
            }
            else if (nodeData is TriggerNodeSO tr)
            {
                for (int i = 0; i <= tr.Conditions.Count; i++)
                {
                    var row = NarrativeUIHelper.CreateRow("choice-row");
                    row.Add(NarrativeUIHelper.CreateLabel(i < tr.Conditions.Count ? $"Case {i}" : "Else", "field-value").With(l => { if (i >= tr.Conditions.Count) l.style.color = Color.gray; }));
                    row.Add(InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(bool)).With(p => { p.portName = ""; p.style.width = 20; }));
                    outputContainer.Add(row);
                }
            }
        }

        public override void SetPosition(Rect newPos) { base.SetPosition(newPos); Undo.RecordObject(nodeData, "Move"); nodeData.Position = newPos; EditorUtility.SetDirty(nodeData); }
        public override void OnSelected() { base.OnSelected(); GraphView?.window.OnNodeSelectionChanged(this); }

        // 다른 곳을 클릭해 선택이 풀리면 줄 강조도 함께 지운다 — 남겨 두면 어느 블록을 편집 중인지 헷갈린다.
        public override void OnUnselected()
        {
            base.OnUnselected();
            // 카드 위 대사 편집 중이면 다시 그리지 않는다 — 입력칸이 사라져 편집 내용을 잃는다.
            if (SelectedEventIndex < 0 || _inlineEditing) return;
            SelectedEventIndex = -1;
            RefreshVisuals();
        }
        public void RebuildPorts() => CreateOutputPorts();

        // 포트를 다시 만들면 연결선이 끊어지므로, 검증 결과는 기존 포트 줄의 테두리만 갱신한다.
        private void UpdatePortErrors()
        {
            for (int i = 0; i < outputContainer.childCount; i++)
            {
                var err = GetFieldError($"branch_{i}");
                outputContainer[i].MarkError(err, !string.IsNullOrEmpty(err));
            }
        }
    }
}
