using UnityEditor;
using UnityEngine;
using Slainte.Bartending;

namespace Slainte.Editor
{
    [CustomEditor(typeof(GlassController))]
    public class GlassControllerEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            GlassController glass = (GlassController)target;
            if (glass == null) return;

            // 로컬 트랜스폼 매트릭스 반영 (회전/스케일 무관하게 핸들 정렬)
            Matrix4x4 originalMatrix = Handles.matrix;
            Handles.matrix = glass.transform.localToWorldMatrix;

            float halfBottom = glass.bottomWidth / 2f;
            float halfTop = glass.topWidth / 2f;
            float halfH = glass.height / 2f;

            EditorGUI.BeginChangeCheck();

            // 1. 상단 너비(Top Width) 핸들 (우측 상단 꼭짓점)
            Vector3 topWidthHandlePos = new Vector3(halfTop, halfH, 0f);
            Handles.color = Color.green;
            Vector3 newTopWidthPos = Handles.FreeMoveHandle(
                topWidthHandlePos,
                0.08f,
                Vector3.zero,
                Handles.DotHandleCap
            );

            // 2. 하단 너비(Bottom Width) 핸들 (우측 하단 꼭짓점)
            Vector3 bottomWidthHandlePos = new Vector3(halfBottom, -halfH, 0f);
            Handles.color = Color.green;
            Vector3 newBottomWidthPos = Handles.FreeMoveHandle(
                bottomWidthHandlePos,
                0.08f,
                Vector3.zero,
                Handles.DotHandleCap
            );

            // 3. 높이(Height) 핸들 (상단 중앙)
            Vector3 heightHandlePos = new Vector3(0f, halfH, 0f);
            Handles.color = Color.green;
            Vector3 newHeightPos = Handles.FreeMoveHandle(
                heightHandlePos,
                0.08f,
                Vector3.zero,
                Handles.DotHandleCap
            );

            // 4. 곡률 반경(CornerRadius) 핸들 (우측 하단 코너 내접원 중심)
            float actualRadius = Mathf.Clamp(glass.cornerRadius, 0f, Mathf.Min(halfBottom, halfH));
            Vector3 centerR = new Vector3(halfBottom - actualRadius, -halfH + actualRadius, 0f);

            Vector3 radiusHandlePos = centerR;
            Handles.color = Color.yellow;
            Vector3 newRadiusPos = Handles.FreeMoveHandle(
                radiusHandlePos,
                0.08f,
                Vector3.zero,
                Handles.ConeHandleCap
            );

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(glass, "Modify Universal Glass Contours");

                // 상단 너비 업데이트
                if (newTopWidthPos != topWidthHandlePos)
                {
                    glass.topWidth = Mathf.Max(0.2f, newTopWidthPos.x * 2f);
                }

                // 하단 너비 업데이트
                if (newBottomWidthPos != bottomWidthHandlePos)
                {
                    glass.bottomWidth = Mathf.Max(0.2f, newBottomWidthPos.x * 2f);
                }

                // 높이 업데이트
                if (newHeightPos != heightHandlePos)
                {
                    glass.height = Mathf.Max(0.2f, newHeightPos.y * 2f);
                }

                // 곡률 반경 업데이트 (핸들 드래그 오프셋에서 반경 정밀 역산)
                if (newRadiusPos != radiusHandlePos)
                {
                    float rX = halfBottom - newRadiusPos.x;
                    float rY = newRadiusPos.y - (-halfH);
                    glass.cornerRadius = Mathf.Max(0f, Mathf.Min(rX, rY));
                }

                glass.GenerateCurvedCollider();
                EditorUtility.SetDirty(glass);
            }

            Handles.matrix = originalMatrix;

            // 시각 가이드라인 그리기 (월드 스페이스 상에서 잔 단면 실루엣 가이드 표현)
            Handles.color = new Color(0f, 1f, 0f, 0.15f);
            
            // 왼쪽/오른쪽 윤곽선을 세그먼트 보간하여 연녹색 가이드라인으로 그려줌
            Vector3 lastL = glass.transform.TransformPoint(new Vector3(-halfTop, halfH + glass.colliderYOffset, 0f));
            Vector3 lastR = glass.transform.TransformPoint(new Vector3(halfTop, halfH + glass.colliderYOffset, 0f));

            for (int i = glass.curveSegments - 1; i >= 0; i--)
            {
                float t = (float)i / glass.curveSegments;
                float y = -halfH + (glass.height * t);
                
                float rBase = Mathf.Lerp(halfBottom, halfTop, t);
                float rFinal = rBase * (glass.glassProfile != null ? glass.glassProfile.Evaluate(t) : 1f);

                float xL = -rFinal;
                float xR = rFinal;

                // 코너 라운딩 가이드 매핑
                if (actualRadius > 0.001f && y < -halfH + actualRadius)
                {
                    float dy = y - (-halfH + actualRadius);
                    float cxL = -halfBottom + actualRadius;
                    float cxR = halfBottom - actualRadius;
                    
                    xL = Mathf.Max(xL, cxL - Mathf.Sqrt(actualRadius * actualRadius - dy * dy));
                    xR = Mathf.Min(xR, cxR + Mathf.Sqrt(actualRadius * actualRadius - dy * dy));
                }

                Vector3 currentL = glass.transform.TransformPoint(new Vector3(xL, y + glass.colliderYOffset, 0f));
                Vector3 currentR = glass.transform.TransformPoint(new Vector3(xR, y + glass.colliderYOffset, 0f));

                Handles.DrawLine(lastL, currentL);
                Handles.DrawLine(lastR, currentR);

                lastL = currentL;
                lastR = currentR;
            }

            // 상단 구경 및 하단 바닥선 연결 가이드
            Vector3 pTopL = glass.transform.TransformPoint(new Vector3(-halfTop, halfH + glass.colliderYOffset, 0f));
            Vector3 pTopR = glass.transform.TransformPoint(new Vector3(halfTop, halfH + glass.colliderYOffset, 0f));
            Vector3 pBotL = glass.transform.TransformPoint(new Vector3(-halfBottom + actualRadius, -halfH + glass.colliderYOffset, 0f));
            Vector3 pBotR = glass.transform.TransformPoint(new Vector3(halfBottom - actualRadius, -halfH + glass.colliderYOffset, 0f));

            Handles.DrawLine(pTopL, pTopR);
            Handles.DrawLine(pBotL, pBotR);
        }
    }
}
