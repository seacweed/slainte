using System;
using System.Collections.Generic;
using System.Linq;
using Slainte.Bartending;
using Slainte.Economy;
using Slainte.EditorTools;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
    // 그래프 에디터 왼쪽 패널에서 블록의 선택된 이벤트 하나를 편집한다.
    // onChanged: 카드 표시만 바뀌는 수정(대사·표정 등), onPortsChanged: 블록 포트가 바뀔 수 있는 수정
    // (선택지 추가/삭제/버튼 텍스트, 이벤트 타입 변경) — 호출자가 포트를 다시 맞춘다.
    public static class EventInspectorUI
    {
        public static void Draw(
            VisualElement container,
            EpisodeEvent ev,
            EpisodeNodeSO block,
            Action onChanged,
            Action onPortsChanged)
        {
            void Changed() { EditorUtility.SetDirty(block); onChanged?.Invoke(); }
            void PortsChanged() { EditorUtility.SetDirty(block); onPortsChanged?.Invoke(); }

            int index = block.Events.IndexOf(ev);
            container.Add(NarrativeUIHelper.CreateLabel($"이벤트 {index + 1} / {block.Events.Count}", "section-header").SetMargin(10, 4));
            DrawProblems(container, ev, block);

            var typeRow = NarrativeUIHelper.CreateRow();
            typeRow.Add(NarrativeUIHelper.CreateLabel("Type", "field-label"));
            var types = NarrativeBlockModel.EditableTypes.ToList();
            if (!types.Contains(ev.Type)) types.Add(ev.Type); // 레거시 이벤트도 현재 값은 보여 준다
            typeRow.Add(new PopupField<EpisodeEventType>(types, ev.Type, TypeName, TypeName).SetFlex(1).With(x => x.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(block, "Change Event Type");
                ev.Type = e.newValue;
                if (ev.Type == EpisodeEventType.Choice && ev.Choices.Count == 0)
                    ev.Choices.Add(new ChoiceOptionData { ButtonText = "Choice 0" });
                PortsChanged();
            })));
            container.Add(typeRow);

            if (NarrativeBlockModel.IsRuntimeEvent(ev))
            {
                var idRow = NarrativeUIHelper.CreateRow();
                idRow.Add(NarrativeUIHelper.CreateLabel("Node ID", "field-label"));
                idRow.Add(NarrativeUIHelper.CreateLabel(string.IsNullOrEmpty(ev.RuntimeNodeId) ? "(자동)" : ev.RuntimeNodeId, "field-value"));
                container.Add(idRow);
                container.Add(Hint("CSV의 nodeId. 이벤트를 추가·삭제·이동하거나 연결을 바꾸면 흐름에 맞춰 자동으로 다시 매겨집니다(직선 1→2, 분기 3_1_1·3_2_1, 합류 4)."));
            }

            switch (ev.Type)
            {
                case EpisodeEventType.Dialogue:
                case EpisodeEventType.Choice:
                    container.Add(SpeakerRow(ev, block, Changed));
                    container.Add(TextRow("Name", ev.OverrideSpeakerName, v => ev.OverrideSpeakerName = v, block, Changed));
                    container.Add(TextRow("Text", ev.Text, v => ev.Text = v, block, Changed, multiline: true));
                    Divider(container);
                    DrawCharacters(container, ev, block, Changed);
                    Divider(container);
                    DrawAudio(container, ev, block, Changed);
                    if (ev.Type == EpisodeEventType.Choice)
                    {
                        Divider(container);
                        DrawChoices(container, ev, block, Changed, PortsChanged);
                    }
                    break;
                case EpisodeEventType.BusinessStart:
                    DrawCrafting(container, ev, block, Changed);
                    Divider(container);
                    // 제조 노드도 진입 시 캐릭터 표정·BGM을 바꿀 수 있다(제조 화면 전 무대 연출).
                    DrawCharacters(container, ev, block, Changed);
                    DrawAudio(container, ev, block, Changed);
                    break;
                default:
                    container.Add(Hint("더 이상 쓰지 않는 예전 이벤트라 실행에서 무시됩니다. 카드에서 삭제하세요."));
                    break;
            }
        }

        private static string TypeName(EpisodeEventType type) => type switch
        {
            EpisodeEventType.Dialogue => "대사",
            EpisodeEventType.Choice => "선택지",
            EpisodeEventType.BusinessStart => "제조",
            _ => $"{type} (레거시)"
        };

        private static void DrawProblems(VisualElement container, EpisodeEvent ev, EpisodeNodeSO block)
        {
            List<string> problems = new();
            var unreachable = new List<EpisodeEvent>();
            NarrativeBlockModel.GetExecutionOrder(block, unreachable);
            if (unreachable.Contains(ev))
                problems.Add("선택지·제조 뒤에 있어 실행되지 않습니다. 블록을 나누거나 순서를 바꾸세요.");
            if (ev.Type == EpisodeEventType.Dialogue && string.IsNullOrWhiteSpace(ev.Text) && string.IsNullOrWhiteSpace(ev.SpeakerKey))
                problems.Add("화자와 대사가 모두 비어 있어 대화창 없이 바로 넘어갑니다.");
            if (ev.Type == EpisodeEventType.Choice && ev.Choices.Count == 0)
                problems.Add("선택지가 없습니다.");
            if (ev.Type == EpisodeEventType.BusinessStart && !string.IsNullOrWhiteSpace(ev.CraftingOrderTarget)
                && ev.CraftingOrderType != CocktailOrderType.EpisodeOrder
                && ev.CraftingOrderType != CocktailOrderType.TasteOrder
                && ev.CraftingOrderType != CocktailOrderType.MoodOrder)
                problems.Add("에피소드 제조는 EpisodeOrder / TasteOrder / MoodOrder만 지원합니다.");

            if (problems.Count > 0)
                container.Add(new HelpBox(string.Join("\n", problems), HelpBoxMessageType.Warning).SetMargin(0, 6));
        }

        // ── 화자 · 캐릭터 ────────────────────────────────────────────────────────

        private static VisualElement SpeakerRow(EpisodeEvent ev, EpisodeNodeSO block, Action changed)
        {
            var row = NarrativeUIHelper.CreateRow();
            row.Add(NarrativeUIHelper.CreateLabel("Speaker", "field-label"));
            var field = new TextField { value = ev.SpeakerKey }.SetFlex(1);
            field.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(block, "Set Speaker");
                ev.SpeakerKey = e.newValue;
                changed();
            });
            row.Add(field);
            row.Add(PickerButton(() => CharacterCatalog.Keys(), key => field.value = key));
            return row;
        }

        private static void DrawCharacters(VisualElement container, EpisodeEvent ev, EpisodeNodeSO block, Action changed)
        {
            container.Add(NarrativeUIHelper.CreateLabel("캐릭터 표정", "field-label").With(l => l.style.width = StyleKeyword.Auto));
            var list = new VisualElement();
            container.Add(list);

            void Refresh()
            {
                list.Clear();
                for (int i = 0; i < ev.CharacterAppearances.Count; i++)
                    list.Add(CharacterRow(ev, block, ev.CharacterAppearances[i], i, changed, Refresh));
                list.Add(NarrativeUIHelper.CreateButton("+ 캐릭터", () =>
                {
                    Undo.RecordObject(block, "Add Character");
                    ev.CharacterAppearances.Add(new CharacterSlotEntryData());
                    changed();
                    Refresh();
                }).SetMargin(2, 4));
            }
            Refresh();
        }

        private static VisualElement CharacterRow(
            EpisodeEvent ev,
            EpisodeNodeSO block,
            CharacterSlotEntryData entry,
            int index,
            Action changed,
            Action refresh)
        {
            var box = new Box().AddClass("inspector-container").SetMargin(0, 3);
            var body = NarrativeUIHelper.CreateRow();
            var preview = new Image { scaleMode = ScaleMode.ScaleToFit };
            preview.style.width = 48;
            preview.style.height = 48;
            preview.style.marginRight = 6;
            void UpdatePreview() => preview.sprite = CharacterCatalog.Sprite(entry.CharacterKey, entry.ExpressionKey);
            UpdatePreview();
            body.Add(preview);

            var fields = new VisualElement().SetFlex(1);
            body.Add(fields);
            box.Add(body);

            var keyRow = NarrativeUIHelper.CreateRow();
            keyRow.Add(NarrativeUIHelper.CreateLabel("Key", "field-label").With(l => l.style.width = 40));
            var keyField = new TextField { value = entry.CharacterKey }.SetFlex(1);
            keyField.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(block, "Set Character");
                entry.CharacterKey = e.newValue;
                UpdatePreview();
                changed();
            });
            keyRow.Add(keyField);
            keyRow.Add(PickerButton(() => CharacterCatalog.Keys(), key => keyField.value = key));
            keyRow.Add(NarrativeUIHelper.CreateButton("X", () =>
            {
                Undo.RecordObject(block, "Remove Character");
                ev.CharacterAppearances.RemoveAt(index);
                changed();
                refresh();
            }));
            fields.Add(keyRow);

            var exprRow = NarrativeUIHelper.CreateRow();
            exprRow.Add(NarrativeUIHelper.CreateLabel("표정", "field-label").With(l => l.style.width = 40));
            var exprField = new TextField { value = entry.ExpressionKey }.SetFlex(1);
            exprField.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(block, "Set Expression");
                entry.ExpressionKey = e.newValue;
                UpdatePreview();
                changed();
            });
            exprRow.Add(exprField);
            exprRow.Add(PickerButton(() => CharacterCatalog.Expressions(entry.CharacterKey), key => exprField.value = key));
            fields.Add(exprRow);

            var slotRow = NarrativeUIHelper.CreateRow();
            slotRow.Add(NarrativeUIHelper.CreateLabel("Slot", "field-label").With(l => l.style.width = 40));
            slotRow.Add(new IntegerField { value = entry.SlotIndex, tooltip = "-1 자동, 0 가운데, 1 왼쪽, 2 오른쪽, 3 왼쪽2, 4 오른쪽2, 5~8 상호작용" }
                .SetFlex(1).With(x => x.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(block, "Set Slot");
                    entry.SlotIndex = e.newValue;
                    changed();
                })));
            fields.Add(slotRow);
            return box;
        }

        // 텍스트 필드 옆 ▼ 버튼: 목록에서 고르거나 직접 입력할 수 있게 한다(주인공처럼 DB에 없는 화자도 있으므로).
        private static Button PickerButton(Func<IReadOnlyList<string>> options, Action<string> pick)
        {
            return NarrativeUIHelper.CreateButton("▼", () =>
            {
                var menu = new GenericMenu();
                IReadOnlyList<string> items = options();
                if (items.Count == 0)
                    menu.AddDisabledItem(new GUIContent("(목록 없음)"));
                foreach (string item in items)
                    menu.AddItem(new GUIContent(item), false, () => pick(item));
                menu.ShowAsContext();
            }).With(b => b.style.width = 22);
        }

        // ── 오디오 ──────────────────────────────────────────────────────────────

        private static void DrawAudio(VisualElement container, EpisodeEvent ev, EpisodeNodeSO block, Action changed)
        {
            var bgm = NarrativeUIHelper.CreateRow();
            bgm.Add(NarrativeUIHelper.CreateLabel("BGM", "field-label"));
            bgm.Add(new EnumField(ev.BgmCommand).With(x =>
            {
                x.style.width = 70;
                x.RegisterValueChangedCallback(e => { Undo.RecordObject(block, "Set BGM"); ev.BgmCommand = (BgmCommand)e.newValue; changed(); });
            }));
            bgm.Add(new TextField { value = ev.BgmClipName }.SetFlex(1).With(x =>
                x.RegisterValueChangedCallback(e => { Undo.RecordObject(block, "Set BGM"); ev.BgmClipName = e.newValue; changed(); })));
            container.Add(bgm);

            var sfx = NarrativeUIHelper.CreateRow();
            sfx.Add(NarrativeUIHelper.CreateLabel("SFX", "field-label"));
            sfx.Add(new EnumField(ev.SfxCommand).With(x =>
            {
                x.style.width = 70;
                x.RegisterValueChangedCallback(e => { Undo.RecordObject(block, "Set SFX"); ev.SfxCommand = (SfxCommand)e.newValue; changed(); });
            }));
            sfx.Add(new TextField { value = ev.SfxClipName }.SetFlex(1).With(x =>
                x.RegisterValueChangedCallback(e => { Undo.RecordObject(block, "Set SFX"); ev.SfxClipName = e.newValue; changed(); })));
            container.Add(sfx);
        }

        // ── 선택지 ──────────────────────────────────────────────────────────────

        private static void DrawChoices(VisualElement container, EpisodeEvent ev, EpisodeNodeSO block, Action changed, Action portsChanged)
        {
            container.Add(NarrativeUIHelper.CreateLabel("선택지 (버튼마다 블록 출력 포트가 하나씩 생김)", "field-label").With(l => l.style.width = StyleKeyword.Auto));
            var list = new VisualElement();
            container.Add(list);

            void Refresh()
            {
                list.Clear();
                for (int i = 0; i < ev.Choices.Count; i++)
                {
                    int index = i;
                    ChoiceOptionData choice = ev.Choices[i];
                    var box = new Box().AddClass("inspector-container").SetMargin(0, 5);
                    var head = NarrativeUIHelper.CreateRow();
                    head.Add(new TextField { value = choice.ButtonText }.SetFlex(1).With(x =>
                        x.RegisterValueChangedCallback(e =>
                        {
                            Undo.RecordObject(block, "Set Choice Text");
                            choice.ButtonText = e.newValue;
                            portsChanged();
                        })));
                    head.Add(NarrativeUIHelper.CreateButton("↑", () => { if (Swap(block, ev.Choices, index, -1)) { portsChanged(); Refresh(); } }));
                    head.Add(NarrativeUIHelper.CreateButton("↓", () => { if (Swap(block, ev.Choices, index, 1)) { portsChanged(); Refresh(); } }));
                    head.Add(NarrativeUIHelper.CreateButton("X", () =>
                    {
                        Undo.RecordObject(block, "Remove Choice");
                        ev.Choices.RemoveAt(index);
                        portsChanged();
                        Refresh();
                    }));
                    box.Add(head);
                    DrawStringList(box, "켤 플래그 (+)", choice.SetFlags, block, changed);
                    DrawStringList(box, "끌 플래그 (-)", choice.ClearFlags, block, changed);
                    DrawVarChanges(box, "변수 변화", choice.VarChanges, block, changed);
                    list.Add(box);
                }
                list.Add(NarrativeUIHelper.CreateButton("+ 선택지", () =>
                {
                    Undo.RecordObject(block, "Add Choice");
                    ev.Choices.Add(new ChoiceOptionData { ButtonText = $"Choice {ev.Choices.Count}" });
                    portsChanged();
                    Refresh();
                }).SetMargin(2, 4));
            }
            Refresh();
        }

        // 선택지 순서를 바꾸면 포트 인덱스도 바뀌므로, 호출자가 portsChanged로 연결을 다시 맞춘다.
        private static bool Swap<T>(EpisodeNodeSO block, List<T> list, int index, int delta)
        {
            int target = index + delta;
            if (target < 0 || target >= list.Count) return false;
            Undo.RecordObject(block, "Reorder");
            (list[index], list[target]) = (list[target], list[index]);
            return true;
        }

        // ── 제조 ────────────────────────────────────────────────────────────────

        private static void DrawCrafting(VisualElement container, EpisodeEvent ev, EpisodeNodeSO block, Action changed)
        {
            if (ev.CraftingOrderTicket == null && !string.IsNullOrWhiteSpace(ev.CraftingTicketKey))
            {
                var database = AssetDatabase.LoadAssetAtPath<OrderTicketDatabase>(BusinessAssetPaths.OrderTicketDatabase);
                ev.CraftingOrderTicket = database != null ? database.FindByKey(ev.CraftingTicketKey) : null;
            }

            var ticketRow = NarrativeUIHelper.CreateRow();
            ticketRow.Add(NarrativeUIHelper.CreateLabel("Order Ticket", "field-label"));
            ticketRow.Add(new ObjectField { objectType = typeof(OrderTicketData), value = ev.CraftingOrderTicket, allowSceneObjects = false }
                .SetFlex(1).With(x => x.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(block, "Set Ticket");
                    ev.CraftingOrderTicket = e.newValue as OrderTicketData;
                    if (ev.CraftingOrderTicket != null) ev.CraftingTicketKey = ev.CraftingOrderTicket.key;
                    changed();
                })));
            container.Add(ticketRow);
            container.Add(TextRow("Ticket Key", ev.CraftingTicketKey, v => ev.CraftingTicketKey = v, block, changed));

            var typeRow = NarrativeUIHelper.CreateRow();
            typeRow.Add(NarrativeUIHelper.CreateLabel("Order Type", "field-label"));
            typeRow.Add(new EnumField(ev.CraftingOrderType).SetFlex(1).With(x => x.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(block, "Set Order Type");
                ev.CraftingOrderType = (CocktailOrderType)e.newValue;
                changed();
            })));
            container.Add(typeRow);
            container.Add(TextRow("Order Target", ev.CraftingOrderTarget, v => ev.CraftingOrderTarget = v, block, changed));
            container.Add(Hint("레시피 ID(rec_XXXX) 또는 맛/분위기 태그. 태그면 자동으로 맛/분위기 주문이 됩니다."));

            var payRow = NarrativeUIHelper.CreateRow();
            payRow.Add(NarrativeUIHelper.CreateLabel("가격 지급", "field-label"));
            payRow.Add(new Toggle { value = ev.CraftingPaymentEnabled }.With(x => x.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(block, "Set Payment");
                ev.CraftingPaymentEnabled = e.newValue;
                changed();
            })));
            payRow.Add(new EnumField(ev.CraftingPaymentCurrency).SetFlex(1).With(x => x.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(block, "Set Currency");
                ev.CraftingPaymentCurrency = (GameCurrency)e.newValue;
                changed();
            })));
            payRow.Add(new FloatField { value = ev.CraftingPaymentMultiplier, tooltip = "가격 배율" }.With(x =>
            {
                x.style.width = 50;
                x.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(block, "Set Multiplier");
                    ev.CraftingPaymentMultiplier = Mathf.Max(0.01f, e.newValue);
                    changed();
                });
            }));
            container.Add(payRow);

            Divider(container);
            container.Add(NarrativeUIHelper.CreateLabel("결과별 플래그·변수", "field-label").With(l => l.style.width = StyleKeyword.Auto));
            foreach (CraftingJobResult result in CraftingJobResultPorts.Order)
            {
                CraftingJobResult current = result;
                var foldout = new Foldout { text = CraftingJobResultPorts.Label(result), value = !string.IsNullOrEmpty(ev.GetCraftingFlag(result)) };
                foldout.Add(TextRow("Flag", ev.GetCraftingFlag(result), v => ev.SetCraftingFlag(current, v), block, changed));
                DrawVarChanges(foldout, "변수 변화", ev.GetCraftingVarChangesForEdit(current), block, changed);
                container.Add(foldout);
            }
        }

        // ── 공용 ────────────────────────────────────────────────────────────────

        private static void DrawStringList(VisualElement container, string label, List<string> list, EpisodeNodeSO block, Action changed)
        {
            container.Add(NarrativeUIHelper.CreateLabel(label, "field-label").With(l => l.style.width = StyleKeyword.Auto).SetMargin(4, 0));
            var sec = new VisualElement();
            container.Add(sec);
            void Refresh()
            {
                sec.Clear();
                for (int i = 0; i < list.Count; i++)
                {
                    int index = i;
                    var row = NarrativeUIHelper.CreateRow();
                    row.Add(new TextField { value = list[i] }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e =>
                    {
                        Undo.RecordObject(block, "Edit");
                        list[index] = e.newValue;
                        changed();
                    })));
                    row.Add(NarrativeUIHelper.CreateButton("X", () => { Undo.RecordObject(block, "Remove"); list.RemoveAt(index); changed(); Refresh(); }));
                    sec.Add(row);
                }
                sec.Add(NarrativeUIHelper.CreateButton("+", () => { Undo.RecordObject(block, "Add"); list.Add(""); changed(); Refresh(); }).With(b => b.style.width = 24));
            }
            Refresh();
        }

        private static void DrawVarChanges(VisualElement container, string label, List<VarChangeData> list, EpisodeNodeSO block, Action changed)
        {
            container.Add(NarrativeUIHelper.CreateLabel(label, "field-label").With(l => l.style.width = StyleKeyword.Auto).SetMargin(4, 0));
            var sec = new VisualElement();
            container.Add(sec);
            void Refresh()
            {
                sec.Clear();
                for (int i = 0; i < list.Count; i++)
                {
                    int index = i;
                    VarChangeData item = list[i];
                    var row = NarrativeUIHelper.CreateRow();
                    row.Add(new TextField { value = item.VarName }.SetFlex(1).With(x => x.RegisterValueChangedCallback(e =>
                    {
                        Undo.RecordObject(block, "Edit Var");
                        item.VarName = e.newValue;
                        changed();
                    })));
                    row.Add(new IntegerField { value = item.Delta }.With(x =>
                    {
                        x.style.width = 50;
                        x.RegisterValueChangedCallback(e => { Undo.RecordObject(block, "Edit Var"); item.Delta = e.newValue; changed(); });
                    }));
                    row.Add(NarrativeUIHelper.CreateButton("X", () => { Undo.RecordObject(block, "Remove Var"); list.RemoveAt(index); changed(); Refresh(); }));
                    sec.Add(row);
                }
                sec.Add(NarrativeUIHelper.CreateButton("+", () => { Undo.RecordObject(block, "Add Var"); list.Add(new VarChangeData()); changed(); Refresh(); }).With(b => b.style.width = 24));
            }
            Refresh();
        }

        private static VisualElement TextRow(
            string label,
            string value,
            Action<string> set,
            EpisodeNodeSO block,
            Action changed,
            bool multiline = false)
        {
            var row = NarrativeUIHelper.CreateRow();
            row.Add(NarrativeUIHelper.CreateLabel(label, "field-label"));
            row.Add(new TextField { value = value, multiline = multiline }.SetFlex(1).With(x =>
            {
                if (multiline) x.style.whiteSpace = WhiteSpace.Normal;
                x.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(block, $"Set {label}");
                    set(e.newValue);
                    changed();
                });
            }));
            return row;
        }

        private static Label Hint(string text) =>
            NarrativeUIHelper.CreateLabel(text, "info-label").With(l => { l.style.color = Color.gray; l.style.whiteSpace = WhiteSpace.Normal; });

        private static void Divider(VisualElement container) => container.Add(NarrativeUIHelper.CreateDivider());
    }

    // 화자·표정 선택용 CharacterDatabase 조회(에디터 전용, 매 호출 로드 — AssetDatabase가 캐시함).
    public static class CharacterCatalog
    {
        private static CharacterDatabase Database =>
            AssetDatabase.LoadAssetAtPath<CharacterDatabase>(BusinessAssetPaths.CharacterDatabase);

        public static IReadOnlyList<string> Keys() =>
            Database?.characters.Where(c => c != null && !string.IsNullOrEmpty(c.key)).Select(c => c.key).OrderBy(k => k).ToList()
            ?? new List<string>();

        public static IReadOnlyList<string> Expressions(string characterKey) =>
            Database?.FindByKey(characterKey)?.expressions.Where(e => e != null && !string.IsNullOrEmpty(e.key)).Select(e => e.key).ToList()
            ?? new List<string>();

        public static Sprite Sprite(string characterKey, string expressionKey)
        {
            CharacterData character = Database?.FindByKey(characterKey);
            return character != null ? character.GetSprite(expressionKey) : null;
        }
    }
}
