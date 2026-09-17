using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Slainte.Bartending
{
    // 스프라이트 모양을 따라가는 단색 윤곽선. 원본 메시를 8방향으로 두께만큼 밀어 뒤에 깔고,
    // 복사본 정점의 UV1에 표시를 남겨 전용 셰이더(Slainte/UI/SpriteSolidOutline)가 텍스처 색 대신
    // 지정 색 + 텍스처 알파로 칠하게 한다. 기본 Outline은 복사본도 "텍스처 색 × 지정 색"으로 그려
    // 단색이 되지 않고, 셰이더에서 주변 픽셀을 검사하는 방식은 스프라이트 여백이 없으면 사각형
    // 경계에서 잘리므로, 사각형 밖으로 그릴 수 있는 메시 복사 방식을 쓴다.
    [RequireComponent(typeof(Graphic))]
    public sealed class IngredientOutlineEffect : BaseMeshEffect
    {
        private static readonly Vector2[] Directions =
        {
            new Vector2(1f, 0f),
            new Vector2(-1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, -1f),
            new Vector2(0.7071f, 0.7071f),
            new Vector2(-0.7071f, 0.7071f),
            new Vector2(0.7071f, -0.7071f),
            new Vector2(-0.7071f, -0.7071f)
        };

        private static readonly Vector4 OutlineMarker = new Vector4(1f, 0f, 0f, 0f);

        [SerializeField] private Color outlineColor = Color.yellow;
        [SerializeField, Min(0f)] private float thickness = 5f;

        // ModifyMesh는 메시가 다시 만들어질 때마다 호출되므로 리스트를 재사용해 GC를 막는다.
        private readonly List<UIVertex> sourceStream = new List<UIVertex>();
        private readonly List<UIVertex> outputStream = new List<UIVertex>();

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureCanvasCarriesMarker();
        }

        // 캔버스는 기본적으로 UV1 채널을 버리므로, 윤곽선 표시가 셰이더까지 전달되도록 채널을 추가한다.
        // 기존 채널을 지우지 않고 더하기만 하므로 다른 UI에는 영향이 없다.
        private void EnsureCanvasCarriesMarker()
        {
            Canvas canvas = graphic != null ? graphic.canvas : null;
            if (canvas == null)
                return;

            const AdditionalCanvasShaderChannels required = AdditionalCanvasShaderChannels.TexCoord1;
            if ((canvas.additionalShaderChannels & required) != required)
                canvas.additionalShaderChannels |= required;
        }

        public override void ModifyMesh(VertexHelper vertexHelper)
        {
            if (!IsActive() || vertexHelper.currentVertCount == 0 || thickness <= 0f)
                return;

            sourceStream.Clear();
            outputStream.Clear();
            vertexHelper.GetUIVertexStream(sourceStream);

            // 복사본을 먼저 넣고 원본을 마지막에 넣어, 같은 드로우 안에서 원본이 윤곽선 위에 그려지게 한다.
            for (int d = 0; d < Directions.Length; d++)
            {
                Vector3 offset = Directions[d] * thickness;
                for (int i = 0; i < sourceStream.Count; i++)
                {
                    UIVertex vertex = sourceStream[i];
                    vertex.position += offset;
                    Color32 sourceColor = vertex.color;
                    Color tinted = outlineColor;
                    // 재고 없음 반투명 등 원본 알파 변화가 윤곽선에도 같이 반영되게 한다.
                    tinted.a *= sourceColor.a / 255f;
                    vertex.color = tinted;
                    vertex.uv1 = OutlineMarker;
                    outputStream.Add(vertex);
                }
            }

            for (int i = 0; i < sourceStream.Count; i++)
            {
                UIVertex vertex = sourceStream[i];
                vertex.uv1 = Vector4.zero;
                outputStream.Add(vertex);
            }

            vertexHelper.Clear();
            vertexHelper.AddUIVertexTriangleStream(outputStream);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (graphic != null)
                graphic.SetVerticesDirty();
        }
#endif
    }
}
