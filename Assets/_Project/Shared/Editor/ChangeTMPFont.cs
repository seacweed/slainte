using UnityEngine;
using UnityEditor;
using TMPro;

namespace Slainte.EditorTools
{
    public class ChangeTMPFont : EditorWindow
    {
        public TMP_FontAsset newFont;

        [MenuItem("Tools/Change All TMP Fonts")]
        public static void ShowWindow()
        {
            GetWindow<ChangeTMPFont>("Change TMP Fonts");
        }

        void OnGUI()
        {
            GUILayout.Label("새로운 폰트를 할당하고 버튼을 누르세요.", EditorStyles.boldLabel);
            newFont = (TMP_FontAsset)EditorGUILayout.ObjectField("New Font", newFont, typeof(TMP_FontAsset), false);

            if (GUILayout.Button("현재 씬의 모든 폰트 변경"))
            {
                if (newFont == null)
                {
                    Debug.LogWarning("폰트를 먼저 할당해주세요!");
                    return;
                }

                // 최신 유니티 버전에 맞춘 최적화된 탐색 방식
                TextMeshProUGUI[] texts = FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);

                foreach (TextMeshProUGUI txt in texts)
                {
                    Undo.RecordObject(txt, "Change Font");
                    txt.font = newFont;
                    EditorUtility.SetDirty(txt);
                }

                Debug.Log($"총 {texts.Length}개의 폰트가 성공적으로 변경되었습니다!");
            }
        }
    }
}
