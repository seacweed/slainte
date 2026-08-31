using UnityEditor;
using UnityEngine;
using Slainte.Bartending;

namespace Slainte.Editor
{
    [CustomEditor(typeof(BeakerController))]
    public class BeakerControllerEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            BeakerController beaker = (BeakerController)target;
            if (beaker == null) return;

            // 로컬 트랜스폼 매트릭스 반영
            Matrix4x4 originalMatrix = Handles.matrix;
            Handles.matrix = beaker.transform.localToWorldMatrix;

            float halfBottom = beaker.bottomWidth / 2f;
            float halfTop = beaker.topWidth / 2f;
            float halfH = beaker.height / 2f;

            EditorGUI.BeginChangeCheck();

            // 1. 상단 너비(Top Width) 핸들 (우측 상단 모서리)
            Vector3 topWidthHandlePos = new Vector3(halfTop, halfH, 0f);
            Handles.color = Color.green;
            Vector3 newTopWidthPos = Handles.FreeMoveHandle(
                topWidthHandlePos,
                0.08f,
                Vector3.zero,
                Handles.DotHandleCap
            );

            // 2. 하단 너비(Bottom Width) 핸들 (우측 하단 모서리)
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

            // 4. 곡률 반경(CornerRadius) 핸들 (우측 하단 호 내접원 중심)
            float actualRadius = Mathf.Clamp(beaker.cornerRadius, 0f, Mathf.Min(halfBottom, halfH));
            
            // [대수적 정해 공식으로 핸들 위치 완벽 동기화]
            float wallLength = Mathf.Sqrt(beaker.height * beaker.height + (halfTop - halfBottom) * (halfTop - halfBottom));
            float factor = (wallLength - (halfTop - halfBottom)) / beaker.height;
            
            float cx = -halfBottom + actualRadius * factor;
            float cy = -halfH + actualRadius;
            Vector3 centerR = new Vector3(-cx, cy, 0f); // 우측 대칭

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
                Undo.RecordObject(beaker, "Modify Tapered Beaker Shape");

                // 상단 너비 업데이트
                if (newTopWidthPos != topWidthHandlePos)
                {
                    beaker.topWidth = Mathf.Max(0.2f, newTopWidthPos.x * 2f);
                }

                // 하단 너비 업데이트
                if (newBottomWidthPos != bottomWidthHandlePos)
                {
                    beaker.bottomWidth = Mathf.Max(0.2f, newBottomWidthPos.x * 2f);
                }

                // 높이 업데이트
                if (newHeightPos != heightHandlePos)
                {
                    beaker.height = Mathf.Max(0.2f, newHeightPos.y * 2f);
                }

                // 곡률 반경 업데이트 (대칭 꼭짓점 cornerR 기준으로 드래그 위치에서 반경 역산)
                if (newRadiusPos != radiusHandlePos)
                {
                    Vector3 cornerR = new Vector3(halfBottom, -halfH, 0f);
                    float newD = Vector3.Distance(cornerR, newRadiusPos);
                    
                    // r = d / factor_distance 역산 보정
                    // 중심 (cx, cy)와 꼭짓점 (halfBottom, -halfH) 간의 거리는 radius * sqrt(1 + factor^2)
                    float distFactor = Mathf.Sqrt(1f + factor * factor);
                    beaker.cornerRadius = Mathf.Max(0f, newD / distFactor);
                }

                beaker.GenerateCurvedCollider();
                EditorUtility.SetDirty(beaker);
            }

            Handles.matrix = originalMatrix;

            // 시각 도우미용 사다리꼴 가이드라인 그리기 (월드 스페이스)
            Handles.color = new Color(0f, 1f, 0f, 0.2f);
            Vector3 pTopL = beaker.transform.TransformPoint(new Vector3(-halfTop, halfH + beaker.colliderYOffset, 0f));
            Vector3 pTopR = beaker.transform.TransformPoint(new Vector3(halfTop, halfH + beaker.colliderYOffset, 0f));
            Vector3 pBotL = beaker.transform.TransformPoint(new Vector3(-halfBottom, -halfH + beaker.colliderYOffset, 0f));
            Vector3 pBotR = beaker.transform.TransformPoint(new Vector3(halfBottom, -halfH + beaker.colliderYOffset, 0f));

            Handles.DrawLine(pTopL, pTopR);
            Handles.DrawLine(pTopR, pBotR);
            Handles.DrawLine(pBotR, pBotL);
            Handles.DrawLine(pBotL, pTopL);
        }
    }
}
