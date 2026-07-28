using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using SolarSim.Unity.Canvas;
using SolarSim.Unity.UI;

namespace SolarSim.Unity.Editor
{
    /// <summary>
    /// Creates the Main design scene with camera, UI Toolkit shell, and canvas controller.
    /// Menu: solarSim → Setup Main Scene
    /// Batch: -executeMethod SolarSim.Unity.Editor.SceneBootstrap.EnsureMainSceneBatch
    /// </summary>
    public static class SceneBootstrap
    {
        public const string ScenePath = "Assets/SolarSim/Scenes/Main.unity";
        public const string PanelSettingsPath = "Assets/SolarSim/UI/AppShellPanelSettings.asset";
        public const string UxmlPath = "Assets/SolarSim/UI/AppShell.uxml";

        [MenuItem("solarSim/Setup Main Scene")]
        public static void EnsureMainScene()
        {
            Directory.CreateDirectory("Assets/SolarSim/Scenes");
            Directory.CreateDirectory("Assets/SolarSim/UI");

            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1920, 1080);
                panelSettings.sortingOrder = 100;
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("DesignCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 8f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.transform.position = new Vector3(2f, 1f, -10f);
            camGo.tag = "MainCamera";
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<DesignCameraController>();

            var shellGo = new GameObject("AppShell");
            var uiDoc = shellGo.AddComponent<UIDocument>();
            uiDoc.panelSettings = panelSettings;
            uiDoc.sortingOrder = 100;
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (uxml != null)
                uiDoc.visualTreeAsset = uxml;
            else
                Debug.LogWarning($"Missing UXML at {UxmlPath}");

            var shell = shellGo.AddComponent<AppShellController>();
            var canvas = shellGo.AddComponent<DesignCanvasController>();
            // Force serialized refs so Play-mode wiring is reliable.
            var soShell = new SerializedObject(shell);
            soShell.FindProperty("uiDocument").objectReferenceValue = uiDoc;
            soShell.ApplyModifiedPropertiesWithoutUndo();
            var soCanvas = new SerializedObject(canvas);
            soCanvas.FindProperty("appShell").objectReferenceValue = shell;
            soCanvas.FindProperty("designCamera").objectReferenceValue = cam;
            soCanvas.FindProperty("uiDocument").objectReferenceValue = uiDoc;
            soCanvas.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"solarSim Main scene ready at {ScenePath}. Press Play.");
        }

        /// <summary>Entry point for Unity -batchmode -executeMethod.</summary>
        public static void EnsureMainSceneBatch()
        {
            try
            {
                EnsureMainScene();
                EditorApplication.Exit(0);
            }
            catch (System.Exception ex)
            {
                Debug.LogError(ex);
                EditorApplication.Exit(1);
            }
        }
    }
}
