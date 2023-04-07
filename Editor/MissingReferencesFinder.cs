using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MissingReferencesFinder : MonoBehaviour {
    private class ObjectData {
        public float ExpectedProgress;
        public GameObject GameObject;
    }

    [MenuItem("Tools/Find Missing References/In current scene", false, 50)]
    public static void FindMissingReferencesInCurrentScene() {
        var scene = SceneManager.GetActiveScene();
        showInitialProgressBar(scene.path);

        clearConsole();
        
        var wasCancelled = false;
        var errors = findMissingReferencesInScene(scene, 1, () => { wasCancelled = false; }, () => { wasCancelled = true; });
        showFinishDialog(wasCancelled, errors);
    }

	[MenuItem("Tools/Find Missing References/In current prefab", false, 51)]
	public static void FindMissingReferencesInCurrentPrefab() {
		var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        
#if UNITY_2020_1_OR_NEWER
        var assetPath = prefabStage.assetPath;
#else
        var assetPath = prefabStage.prefabAssetPath;
#endif
        showInitialProgressBar(assetPath);
        clearConsole();

		var errors = findMissingReferences(assetPath, prefabStage.prefabContentsRoot, true);
		showFinishDialog(false, errors);
	}

	[MenuItem("Tools/Find Missing References/In current prefab", true, 51)]
	public static bool FindMissingReferencesInCurrentPrefabValidate() => PrefabStageUtility.GetCurrentPrefabStage() != null;

	[MenuItem("Tools/Find Missing References/In all scenes in build", false, 52)]
    public static void FindMissingReferencesInAllScenesInBuild() {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToList();

        ErrorAggregator errors = new();
        var wasCancelled = true;
        foreach (var scene in scenes) {
            Scene openScene;
            try {
                openScene = EditorSceneManager.OpenScene(scene.path);
            } catch (Exception ex) {
                Debug.LogError($"Could not open scene at path \"{scene.path}\". This scene was added to the build, and it's possible that it has been deleted: Error: {ex.Message}");
                continue;
            }

            errors.Join(findMissingReferencesInScene(openScene, 1 / (float)scenes.Count(),
                () => { wasCancelled = false; }, () => { wasCancelled = true; }));
            if (wasCancelled) break;
        }
        showFinishDialog(wasCancelled, errors);
    }

    /*[MenuItem("Tools/Find Missing References/In all scenes in project", false, 52)]
    public static void FindMissingReferencesInAllScenes() {
        var scenes = EditorBuildSettings.scenes;

        var finished = true;
        foreach (var scene in scenes) {
            var s = EditorSceneManager.OpenScene(scene.path);
            finished = findMissingReferencesInScene(s, 1 /(float)scenes.Count());
            if (!finished) break;
        }
        showFinishDialog(!finished);
    }*/

    [MenuItem("Tools/Find Missing References/In assets", false, 52)]
    public static ErrorAggregator FindMissingReferencesInAssets() {
        showInitialProgressBar("all assets");
        var allAssetPaths = AssetDatabase.GetAllAssetPaths();
        var objs = allAssetPaths
                   .Where(isProjectAsset)
                   .ToArray();

        var wasCancelled = false;
        var errors = findMissingReferences("Project", objs, () => { wasCancelled = false; }, () => { wasCancelled = true; });
        showFinishDialog(wasCancelled, errors);
        return errors;
    }

    [MenuItem("Tools/Find Missing References/Everywhere", false, 53)]
    public static void FindMissingReferencesEverywhere() {
        var currentScenePath = SceneManager.GetActiveScene().path;

        #region Prevent from starting if the current scene is unsaved or has any changes.
        if (string.IsNullOrWhiteSpace(currentScenePath)) {
            if (!EditorUtility.DisplayDialog("Missing References Finder",
                "You must save the current scene before starting to find missing references in the project.", "Save",
                "Cancel")) return;
            if (EditorSceneManager.SaveOpenScenes()) {
                currentScenePath = SceneManager.GetActiveScene().path;
            }
            else {
                EditorUtility.DisplayDialog("Missing References Finder",
                    "Could not start finding missing references in the project because the current scene is not saved.",
                    "Ok");
                return;
            }
        }
        #endregion

        // Warn the user to save the scene if it has unsaved changes. If the user selects "Cancel" the process is stopped.
        // If the user selects "Don't save", saving is omitted but this still returns true so the process starts. This 
        // behavior is expected and correct (the user has been warned and they still chose not to save).
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
            return;
        }
        
        var scenes = EditorBuildSettings.scenes;
        var progressWeight = 1 / (float) (scenes.Length + 1);

        clearConsole();

        ErrorAggregator errors = new();
        var wasCancelled = true;
        var currentProgress = 0f;
        foreach (var scene in scenes) {
            Scene openScene;
            try {
                openScene = EditorSceneManager.OpenScene(scene.path);
            }
            catch (Exception ex) {
                Debug.LogError(
                    $"Could not open scene at path \"{scene.path}\". This scene was added to the build, and it's possible that it has been deleted: Error: {ex.Message}");
                continue;
            }

            errors.Join(findMissingReferencesInScene(openScene, progressWeight, () => { wasCancelled = false; },
                () => { wasCancelled = true; }, currentProgress));
            currentProgress += progressWeight;
            if (wasCancelled) break;
        }

        if (!wasCancelled) {
            var allAssetPaths = AssetDatabase.GetAllAssetPaths();
            var objs = allAssetPaths
                .Where(isProjectAsset)
                .ToArray();

            errors.Join(findMissingReferences("Project", objs, () => { wasCancelled = false; },
                () => { wasCancelled = true; }, currentProgress, progressWeight));
        }

        showFinishDialog(wasCancelled, errors);

        // Restore the scene that was originally open when the tool was started.
        if (!string.IsNullOrEmpty(currentScenePath)) EditorSceneManager.OpenScene(currentScenePath);
    }

    private static bool isProjectAsset(string path) {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        return !path.StartsWith("/");
#else
        return path.Substring(1, 2) != ":/";
#endif
    }

    private static ErrorAggregator findMissingReferences(string context, string[] paths, Action onFinished, Action onCanceled, float initialProgress = 0f, float progressWeight = 1f)
    {
        ErrorAggregator errors = new();
        var wasCancelled = false;
        for (var i = 0; i < paths.Length; i++) {
            var obj = AssetDatabase.LoadAssetAtPath(paths[i], typeof(GameObject)) as GameObject;
            if (obj == null || !obj) continue;

            if (wasCancelled || EditorUtility.DisplayCancelableProgressBar("Searching missing references in assets.",
                                                                           $"{paths[i]}",
                                                                           initialProgress + ((i / (float) paths.Length)*progressWeight))) {
                onCanceled.Invoke();
                return errors;
            }

            errors.Join(findMissingReferences(context, obj));
        }

        onFinished.Invoke();
        return errors;
    }

    private static ErrorAggregator findMissingReferences(string context, GameObject go, bool findInChildren = false)
    {
        ErrorAggregator errors = new();
        var components = go.GetComponents<Component>();

        for (var j = 0; j < components.Length; j++) {
            var c = components[j];
            if (!c)
            {
                errors.Capture(new ErrorAggregator.MissingGameObjectComponent()
                {
                    gameobject = go,
                    parentAssetPath = context,
                });
                continue;
            }

            var so = new SerializedObject(c);
            var sp = so.GetIterator();

            while (sp.NextVisible(true)) {
                if (sp.propertyType == SerializedPropertyType.ObjectReference) {
                    if (sp.objectReferenceValue           == null
                     && sp.objectReferenceInstanceIDValue != 0)
                    {
                        errors.Capture(getError(context, go, c.GetType().Name,
                            ObjectNames.NicifyVariableName(sp.name)));
                    }
                }
            }
        }

        if (findInChildren) {
            foreach (Transform child in go.transform) {
               errors.Join(findMissingReferences(context, child.gameObject, true));
            }
        }

        return errors;
    }

    private static ErrorAggregator findMissingReferencesInScene(Scene scene, float progressWeightByScene, Action onFinished, Action onCanceled, float currentProgress = 0f) {
        var rootObjects = scene.GetRootGameObjects();

        var queue = new Queue<ObjectData>();
        foreach (var rootObject in rootObjects) {
            queue.Enqueue(new ObjectData{ExpectedProgress = progressWeightByScene /(float)rootObjects.Length, GameObject = rootObject});
        }

        var errors = findMissingReferences(scene.path, queue,
                                          onFinished,
                                          onCanceled,
                                          true, currentProgress);
        return errors;
    }

    private static ErrorAggregator findMissingReferences(string context, Queue<ObjectData> queue, Action onFinished, Action onCanceled, bool findInChildren = false, float currentProgress = 0f)
    {
        ErrorAggregator errors = new();
        while (queue.Any()) {
            var data = queue.Dequeue();
            var go = data.GameObject;
            var components = go.GetComponents<Component>();

            float progressEachComponent;
            if (findInChildren) {
                progressEachComponent = (data.ExpectedProgress) / (float)(components.Length + go.transform.childCount);
            } else {
                progressEachComponent = data.ExpectedProgress / (float)components.Length;
            }

            for (var j = 0; j < components.Length; j++) {
                currentProgress += progressEachComponent;
                if (EditorUtility.DisplayCancelableProgressBar($"Searching missing references in {context}",
                                                               go.name,
                                                               currentProgress)) {
                    onCanceled.Invoke();
                    return errors;
                }

                var c = components[j];
                if (!c)
                {
                    errors.Capture(new ErrorAggregator.MissingGameObjectComponent()
                    {
                        gameobject = go,
                        parentAssetPath = context,
                    });
                    continue;
                }

                using (var so = new SerializedObject(c)) {
                    using (var sp = so.GetIterator()) {
                        while (sp.NextVisible(true)) {
                            if (sp.propertyType == SerializedPropertyType.ObjectReference) {
                                if (sp.objectReferenceValue           == null
                                 && sp.objectReferenceInstanceIDValue != 0)
                                {
                                    errors.Capture(getError(context, go, c.GetType().Name,
                                        ObjectNames.NicifyVariableName(sp.name)));
                                }
                            }
                        }
                    }
                }
            }

            if (findInChildren) {
                foreach (Transform child in go.transform) {
                    if (child.gameObject == go) continue;
                    queue.Enqueue(new ObjectData{ExpectedProgress = progressEachComponent, GameObject = child.gameObject});
                }
            }
        }

        onFinished.Invoke();
        return errors;
    }

    private static void showInitialProgressBar(string searchContext, bool clearConsole = true) {
        if (clearConsole) {
            Debug.ClearDeveloperConsole();
        }
        EditorUtility.DisplayProgressBar("Missing References Finder", $"Preparing search in {searchContext}", 0f);
    }

    private static void showFinishDialog(bool wasCancelled, ErrorAggregator errors)
    {
        var count = errors.ErrorMessages().Count();
        EditorUtility.ClearProgressBar();
        EditorUtility.DisplayDialog("Missing References Finder",
                                    wasCancelled ?
                                        $"Process cancelled.\n{count} missing references were found.\n Current results are shown as errors in the console." :
                                        $"Finished finding missing references.\n{count} missing references were found.\n Results are shown as errors in the console.",
                                    "Ok");
    }

    private static ErrorAggregator.MissingGameObjectReference getError(string context, GameObject go, string componentName, string property) {
        return new ErrorAggregator.MissingGameObjectReference()
        {
            gameobject = go,
            componentName = componentName,
            propertName = property,
            parentAssetPath = context,
        };
    }

    private static void clearConsole() {
        var logEntries = Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
        if(logEntries == null) return;

        var clearMethod = logEntries.GetMethod("Clear",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        if(clearMethod == null) return;

        clearMethod.Invoke(null, null);
    }
}