using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace NarrativeFlow.Editor
{
    public static class NarrativeUIHelper
    {
        // --- Fluent API ---
        public static T AddClass<T>(this T el, string className) where T : VisualElement { if (!string.IsNullOrEmpty(className)) el.AddToClassList(className); return el; }
        public static T SetFlex<T>(this T el, float grow) where T : VisualElement { el.style.flexGrow = grow; return el; }
        public static T SetMargin<T>(this T el, float top = 0, float bottom = 0) where T : VisualElement { el.style.marginTop = top; el.style.marginBottom = bottom; return el; }
        public static T SetColor<T>(this T el, Color color) where T : VisualElement { el.style.color = color; return el; }
        public static T SetFontSize<T>(this T el, float size) where T : VisualElement { el.style.fontSize = size; return el; }
        public static T SetJustify<T>(this T el, Justify justify) where T : VisualElement { el.style.justifyContent = justify; return el; }

        public static T MarkError<T>(this T el, string message, bool show = true) where T : VisualElement
        {
            el.style.borderBottomColor = el.style.borderLeftColor = el.style.borderRightColor = el.style.borderTopColor = Color.red;
            el.style.borderBottomWidth = el.style.borderLeftWidth = el.style.borderRightWidth = el.style.borderTopWidth = show ? 1 : 0;
            el.tooltip = show ? message : "";
            return el;
        }

        // --- Validation UI Binding ---
        public static Label CreateWarningIcon(NarrativeNodeView view, string key)
        {
            var icon = new Label("⚠️") { style = { color = Color.red, marginLeft = 2, display = DisplayStyle.None } };
            System.Action update = () => {
                if (view == null) return;
                var err = view.GetFieldError(key);
                icon.style.display = !string.IsNullOrEmpty(err) ? DisplayStyle.Flex : DisplayStyle.None;
                icon.tooltip = err;
            };
            view.OnValidationChanged += update;
            update();
            return icon;
        }

        public static Label CreateWarningIcon(SequenceNodeView view, string key)
        {
            var icon = new Label("⚠️") { style = { color = Color.red, marginLeft = 2, display = DisplayStyle.None } };
            System.Action update = () => {
                if (view == null) return;
                var err = view.GetFieldError(key);
                icon.style.display = !string.IsNullOrEmpty(err) ? DisplayStyle.Flex : DisplayStyle.None;
                icon.tooltip = err;
            };
            view.OnValidationChanged += update;
            update();
            return icon;
        }

        // --- Core Helpers ---
        public static VisualElement CreateRow(string className = "", Justify justify = Justify.FlexStart) 
            => new VisualElement().AddClass("field-row").AddClass(className).SetJustify(justify);
        
        public static Label CreateLabel(string text, string className = "", float fontSize = 0) 
        {
            var l = new Label(text).AddClass(className);
            if (fontSize > 0) l.SetFontSize(fontSize);
            return l;
        }

        public static Button CreateButton(string text, System.Action onClick, string className = "") 
            => new Button(onClick) { text = text }.AddClass(className);

        public static VisualElement CreateDivider() => new VisualElement().AddClass("divider").With(x => { x.style.height = 1; x.style.backgroundColor = new Color(0.4f, 0.4f, 0.4f, 0.5f); }).SetMargin(5, 5);

        public static void AddPadding(VisualElement el, float all) { el.style.paddingLeft = el.style.paddingRight = el.style.paddingTop = el.style.paddingBottom = all; }

        public static void DrawList<T>(VisualElement container, List<T> list, System.Action<VisualElement, T, int> drawItem, System.Action onAdd)
        {
            container.Clear();
            if (list == null) return;
            for (int i = 0; i < list.Count; i++) drawItem(container, list[i], i);
            container.Add(CreateButton("+ Add", onAdd).SetMargin(5, 0));
        }

        public static StyleSheet LoadStyle() => AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/_Project/Features/Narrative/Editor/NarrativeStyles.uss");
    }

    public static class VisualElementExtensions 
    { 
        public static T With<T>(this T item, System.Action<T> action) { action?.Invoke(item); return item; } 
    }
}
