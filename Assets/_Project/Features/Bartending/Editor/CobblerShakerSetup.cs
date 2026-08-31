using Slainte.Bartending;
using UnityEditor;
using UnityEngine;

namespace Slainte.EditorTools
{
    public static class CobblerShakerSetup
    {
        private const string SourcePrefabPath = "Assets/Prefabs/Beaker.prefab";
        private const string ShakerPrefabPath = "Assets/Prefabs/CobblerShaker.prefab";
        private const string SettingsPath = "Assets/Resources/Bartending/BusinessBartendingSettings.asset";

        [MenuItem("Slainte/Business/코블러 셰이커 설정 적용")]
        public static void Apply()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (source == null)
                throw new System.IO.FileNotFoundException("코블러 셰이커의 원본 비커 프리팹이 없습니다.", SourcePrefabPath);

            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
                throw new System.InvalidOperationException("비커 프리팹 인스턴스를 만들지 못했습니다.");

            GameObject shakerPrefab;
            try
            {
                instance.name = "CobblerShaker";
                if (instance.GetComponent<CobblerShakerTechniqueController>() == null)
                    instance.AddComponent<CobblerShakerTechniqueController>();
                shakerPrefab = PrefabUtility.SaveAsPrefabAsset(instance, ShakerPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            if (shakerPrefab == null)
                throw new System.InvalidOperationException("코블러 셰이커 프리팹을 저장하지 못했습니다.");

            BusinessBartendingSettings settings =
                AssetDatabase.LoadAssetAtPath<BusinessBartendingSettings>(SettingsPath);
            if (settings == null)
                throw new System.IO.FileNotFoundException("영업 바텐딩 설정 에셋이 없습니다.", SettingsPath);

            settings.cobblerShakerPrefab = shakerPrefab;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[코블러 셰이커 설정] 통과: 스트레이너 일체형 셰이커 프리팹을 영업 제조 도구에 연결했습니다.");
        }
    }
}
