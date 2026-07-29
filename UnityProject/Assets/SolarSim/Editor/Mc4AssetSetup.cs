using System.IO;
using System.Linq;
using SolarSim.Unity.Canvas;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SolarSim.Unity.Editor
{
    /// <summary>
    /// Turns the exported Blender FBX into a Resources prefab Unity can spawn on wire joins.
    /// Menu: solarSim → Setup MC4 Connection Prefab
    /// </summary>
    public static class Mc4AssetSetup
    {
        public const string FbxPath = "Assets/SolarSim/Art/MC4/MC4_Connect.fbx";
        public const string PrefabDir = "Assets/SolarSim/Resources/SolarSim/MC4";
        public const string PrefabPath = PrefabDir + "/MC4_Connection.prefab";
        public const string ControllerPath = PrefabDir + "/MC4_Connect.controller";

        [MenuItem("solarSim/Setup MC4 Connection Prefab")]
        public static void SetupMc4ConnectionPrefab()
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null)
            {
                EditorUtility.DisplayDialog(
                    "MC4 asset missing",
                    $"Could not find FBX at:\n{FbxPath}\n\nExport MC4_Connect.fbx from Blender into Art/MC4 first.",
                    "OK");
                return;
            }

            Directory.CreateDirectory(PrefabDir.Replace('\\', '/'));

            // Prefer a dedicated AnimationClip from the FBX; fall back to any clip on the asset.
            var clip = AssetDatabase.LoadAllAssetsAtPath(FbxPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            if (clip != null)
            {
                // Keep a single state named to match Mc4ConnectionPresenter.ConnectStateName.
                var sm = controller.layers[0].stateMachine;
                while (sm.states.Length > 0)
                    sm.RemoveState(sm.states[0].state);

                var state = sm.AddState(Mc4ConnectionPresenter.ConnectStateName);
                state.motion = clip;
                sm.defaultState = state;
                EditorUtility.SetDirty(controller);
            }
            else
            {
                Debug.LogWarning(
                    "No AnimationClip found inside MC4_Connect.fbx. " +
                    "In the FBX Import Settings, enable Animation and reimport, then run this menu again.");
            }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            root.name = "MC4_Connection";

            var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            var presenter = root.GetComponent<Mc4ConnectionPresenter>()
                            ?? root.AddComponent<Mc4ConnectionPresenter>();
            var so = new SerializedObject(presenter);
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("connectClip").objectReferenceValue = clip;
            so.FindProperty("displayScale").floatValue = 0.45f;
            so.FindProperty("faceCamera").boolValue = true;

            // Best-effort bind male/female roots by name from the Blender export.
            var male = FindChildContains(root.transform, "Male");
            var female = FindChildContains(root.transform, "Female");
            so.FindProperty("maleRoot").objectReferenceValue = male;
            so.FindProperty("femaleRoot").objectReferenceValue = female;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);

            EditorUtility.DisplayDialog(
                "MC4 prefab ready",
                $"Created:\n{PrefabPath}\n\nNew wire joins will spawn this prefab and play MC4_Connect.",
                "OK");
        }

        private static Transform? FindChildContains(Transform root, string token)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.IndexOf(token, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            }
            return null;
        }
    }
}
