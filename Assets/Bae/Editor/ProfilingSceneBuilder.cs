using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Optimization;

namespace Bae.EditorScripts
{
    public class ProfilingSceneBuilder : EditorWindow
    {
        [MenuItem("Tools/Optimization/Create Profiling Test Scenes")]
        public static void CreateTestScenes()
        {
            string mainScenePath = "Assets/Bae/Scenes/Main.unity";
            if (!System.IO.File.Exists(mainScenePath))
            {
                mainScenePath = "Assets/Scenes/Main.unity";
            }

            // 현재 씬이 수정되었다면 저장 프롬프트 띄우기
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // 1. 메인 씬 열기
            Scene mainScene = EditorSceneManager.OpenScene(mainScenePath, OpenSceneMode.Single);

            // 2. After 씬으로 바로 다른 이름으로 저장 (원본 훼손 방지)
            string afterPath = "Assets/Test_After_Optimization.unity";
            EditorSceneManager.SaveScene(mainScene, afterPath);

            // 3. Before 씬으로 저장하기 위해 상태 변경
            // FloorSystemManager를 찾아서 끄고, NaiveFloorGenerator를 붙입니다.
            FloorSystemManager systemMgr = Object.FindObjectOfType<FloorSystemManager>();
            if (systemMgr != null)
            {
                GameObject managerObj = systemMgr.gameObject;
                GameObject prefabToUse = systemMgr.floorPrefab; // 실제 메인 씬에서 쓰는 진짜 프리팹 가져오기

                // 최적화 컴포넌트들 끄기
                systemMgr.enabled = false;
                if (managerObj.GetComponent<FloorChunkManager>() != null) managerObj.GetComponent<FloorChunkManager>().enabled = false;
                if (managerObj.GetComponent<FloorViewportCuller>() != null) managerObj.GetComponent<FloorViewportCuller>().enabled = false;
                if (managerObj.GetComponent<FloorObjectPool>() != null) managerObj.GetComponent<FloorObjectPool>().enabled = false;

                // 무식한 생성기 붙이기
                var naiveGen = managerObj.AddComponent<NaiveFloorGenerator>();
                naiveGen.floorPrefab = prefabToUse;
                naiveGen.mapWidth = 120; // 에디터 뻗음 방지용 크기
                naiveGen.mapLength = 120;
            }
            else
            {
                Debug.LogWarning("메인 씬에서 FloorSystemManager를 찾지 못했습니다. 레이아웃만 복사됩니다.");
            }

            string beforePath = "Assets/Test_Before_Optimization.unity";
            EditorSceneManager.SaveScene(mainScene, beforePath);

            Debug.Log($"<color=green>완벽하게 동일한 레이아웃의 프로파일링 씬이 생성되었습니다!</color>\nBefore: {beforePath}\nAfter: {afterPath}");
            
            // Before 씬이 열린 상태로 종료
        }
    }
}
