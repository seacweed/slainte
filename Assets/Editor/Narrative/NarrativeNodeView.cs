using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace NarrativeFlow.Editor
{
    public class NarrativeNodeView : Node
    {
        public NodeDataSO nodeData;
        private Label _titleLabel;
        private Label _descLabel;
        private Label _condLabel;
        private VisualElement _customDataContainer;

        public NarrativeNodeView(NodeDataSO data)
        {
            nodeData = data;
            title = data.name;
            viewDataKey = data.Guid;

            style.left = data.Position.x;
            style.top = data.Position.y;

            // Make sure the node allows content in the extension container but doesn't show the collapse button
            capabilities &= ~Capabilities.Collapsible;

            CreateInputPorts();
            CreateOutputPorts();
            SetupVisuals();
            RefreshVisuals();

            // 만약 유니티가 강제로 접기 버튼(화살표)을 생성했다면, 시각적으로 아예 숨겨버립니다.
            var collapseButton = titleButtonContainer.Q<VisualElement>("collapse-button");
            if (collapseButton != null)
            {
                collapseButton.style.display = DisplayStyle.None;
            }
            
            // 클래스에서 접힘 상태를 나타내는 collapsed가 붙어있다면 제거하여 무조건 펴진 상태를 유지합니다.
            RemoveFromClassList("collapsed");
        }

        // Removed expanded override

        private void SetupVisuals()
        {
            _customDataContainer = new VisualElement();
            _customDataContainer.style.paddingTop = 5;
            _customDataContainer.style.paddingLeft = 5;
            _customDataContainer.style.paddingRight = 5;
            _customDataContainer.style.paddingBottom = 5;
            _customDataContainer.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.8f);

            _titleLabel = new Label();
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            _descLabel = new Label();
            _descLabel.style.whiteSpace = WhiteSpace.Normal;
            _descLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            _descLabel.style.marginTop = 5;

            _condLabel = new Label();
            _condLabel.style.color = new Color(1f, 0.8f, 0.4f);
            _condLabel.style.marginTop = 5;

            mainContainer.Add(_customDataContainer);
            _customDataContainer.style.display = DisplayStyle.Flex;
        }

        public void RefreshVisuals()
        {
            _customDataContainer.Clear();
            
            string nodeTitle = "Node";
            if (nodeData is EpisodeNodeSO) nodeTitle = "Episode Node";
            if (nodeData is EmptyNodeSO) nodeTitle = "Empty Node";

            if (nodeData.CustomFields != null)
            {
                var titleField = nodeData.CustomFields.Find(f => f.FieldName != null && f.FieldName.ToLower() == "title");
                if (titleField != null && !string.IsNullOrEmpty(titleField.FieldValue))
                {
                    nodeTitle = titleField.FieldValue;
                }
            }

            title = nodeTitle;
            
            _titleLabel.text = nodeTitle;
            _customDataContainer.Add(_titleLabel);

            // 커스텀 필드 동적 렌더링
            if (nodeData.CustomFields != null && nodeData.CustomFields.Count > 0)
            {
                var sep = new VisualElement { style = { height = 1, backgroundColor = Color.gray, marginTop = 5, marginBottom = 5 } };
                _customDataContainer.Add(sep);

                foreach (var cf in nodeData.CustomFields)
                {
                    if (string.IsNullOrEmpty(cf.FieldName)) continue;
                    
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                    var n = new Label(cf.FieldName + ": ") { style = { color = new Color(0.6f, 0.8f, 1f), unityFontStyleAndWeight = FontStyle.Bold } };
                    var v = new Label(cf.FieldValue) { style = { whiteSpace = WhiteSpace.Normal, flexShrink = 1 } };
                    row.Add(n);
                    row.Add(v);
                    _customDataContainer.Add(row);
                }
            }
        }

        private void CreateInputPorts()
        {
            var inputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            inputPort.portName = "Input";
            inputContainer.Add(inputPort);
        }

        private void CreateOutputPorts()
        {
            var outputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            outputPort.portName = "Output";
            outputContainer.Add(outputPort);
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);
            
            Undo.RecordObject(nodeData, "Move Node Position");
            nodeData.Position = newPos;
            EditorUtility.SetDirty(nodeData);
        }

        public override void OnSelected()
        {
            base.OnSelected();
            if (GetFirstAncestorOfType<NarrativeGraphView>() is NarrativeGraphView view)
            {
                view.window.OnNodeSelectionChanged(this);
            }
        }
    }
}