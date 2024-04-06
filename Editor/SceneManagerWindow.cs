using System;
using System.IO;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

using UnityEditor;
using UnityEditor.SceneManagement;

using UnityEditorInternal;

namespace JLChnToZ.EditorExtensions.SceneManagement {
    public class SceneManagerWindow: EditorWindow {
        private static GUIContent homeMarker;
        private static GUIContent playFromThisScene;
        private static GUIContent addOpenSceneMenu;
        private static GUIContent addOpenScenesMenu;
        private static GUIContent addSelectedScenesMenu;
        private static readonly List<SceneData> sceneDatas = new List<SceneData>();
        private static readonly HashSet<string> openedScenePaths = new HashSet<string>();

        private EditorBuildSettingsScene[] buildScenes;
        private Scene currentScene;
        private ReorderableList listDisplay;
        private Vector2 scrollPos;
        private Vector2 labelSize;
        private readonly GUIContent sceneIndexContent = new GUIContent();
        private bool requireRefresh, isPlaying;

        [SerializeField]
        private bool hasPlayed, playScene, additiveMode;

        [SerializeField]
        private string startScene, waitScene;

        [SerializeField]
        private string[] lastScenes;

        [MenuItem("Window/JLChnToZ/Scene Manager")]
        public static SceneManagerWindow GetWindow() => GetWindow<SceneManagerWindow>();
        private SceneAsset[] selectedSceneAssets;


        private static SceneStatus IsCurrentScene(SceneData sceneData) {
            if (EditorApplication.isPlaying) {
                if (SceneManager.GetActiveScene().buildIndex == sceneData.sceneIndex)
                    return SceneStatus.Active;
                for (int i = 0, c = SceneManager.sceneCount; i < c; i++)
                    if (sceneData.sceneIndex == SceneManager.GetSceneAt(i).buildIndex)
                        return SceneStatus.Loaded;
            } else if (sceneData.editorSceneData != null) {
                if (string.Equals(
                    SceneManager.GetActiveScene().path,
                    sceneData.editorSceneData.path,
                    StringComparison.Ordinal))
                    return SceneStatus.Active;
                for (int i = 0, c = SceneManager.sceneCount; i < c; i++)
                    if (string.Equals(
                        SceneManager.GetSceneAt(i).path,
                        sceneData.editorSceneData.path,
                        StringComparison.Ordinal))
                        return SceneStatus.Loaded;
            }
            return SceneStatus.NotLoaded;
        }

        private static EditorWindow OpenInternalWindow(string typeName, bool isUtility = false, bool ignoreCase = false) {
            var windowType = Type.GetType(typeName, false, ignoreCase);
            if (windowType == null || !windowType.IsSubclassOf(typeof(EditorWindow)))
                return null;
            return GetWindow(windowType, isUtility);
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused) {
            rect.y += EditorGUIUtility.standardVerticalSpacing;
            rect.height = EditorGUIUtility.singleLineHeight;
            var sceneData = sceneDatas[index];
            var scenePath = sceneData.editorSceneData.path;
            var isEnabled = sceneData.editorSceneData.enabled;
            var isCurrentScene = IsCurrentScene(sceneData);
            var isCurrentStartScene = startScene == scenePath;
            var additiveMode = this.additiveMode || SceneManager.sceneCount > 1;
            var isToggled = false;
            sceneIndexContent.text = isEnabled ? sceneData.sceneIndex.ToString() : "-";
            Event currentEvent = Event.current;
            switch (currentEvent.type) {
                case EventType.Layout:
                    Vector2 newLabelSize = EditorStyles.miniButtonLeft.CalcSize(sceneIndexContent);
                    labelSize.x = Math.Max(labelSize.x, newLabelSize.x);
                    labelSize.y = Math.Max(labelSize.y, newLabelSize.y);
                    break;
            }
            EditorGUI.BeginDisabledGroup(isPlaying && !isEnabled);
            EditorGUI.BeginDisabledGroup(
                additiveMode &&
                isCurrentScene != SceneStatus.NotLoaded &&
                SceneManager.sceneCount < 2
            );
            EditorGUI.BeginChangeCheck();
            GUI.Toggle(
                new Rect(rect.x, rect.y, 20, rect.height),
                isCurrentScene != SceneStatus.NotLoaded,
                GUIContent.none,
                additiveMode ? EditorStyles.toggle : EditorStyles.radioButton
            );
            if (EditorGUI.EndChangeCheck())
                isToggled = true;
            EditorGUI.EndDisabledGroup();
            if (GUI.Button(
                new Rect(rect.x + 16, rect.y, rect.width - labelSize.x - 36, rect.height),
                EditorGUIUtility.ObjectContent(sceneData.SceneAsset, typeof(SceneAsset)),
                isCurrentScene == SceneStatus.Active ?
                (isActive ? EditorStyles.whiteBoldLabel : EditorStyles.boldLabel) :
                (isActive ? EditorStyles.whiteLabel : EditorStyles.label)
            )) {
                EditorGUIUtility.PingObject(sceneData.SceneAsset);
                listDisplay.index = index;
                listDisplay.GrabKeyboardFocus();
            }
            sceneDatas[index] = sceneData;
            if (isToggled) {
                listDisplay.index = index;
                if (isCurrentScene == SceneStatus.NotLoaded) {
                    if (isPlaying) {
                        SceneManager.LoadScene(
                            sceneData.sceneIndex,
                            additiveMode ?
                                LoadSceneMode.Additive :
                                LoadSceneMode.Single
                        );
                    } else if (additiveMode) {
                        EditorSceneManager.OpenScene(
                            scenePath,
                            OpenSceneMode.Additive
                        );
                    } else if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                        playScene = false;
                        waitScene = scenePath;
                        Repaint();
                        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                        EditorApplication.isPlaying = false;
                    }
                } else if (additiveMode) {
                    if (isPlaying)
                        SceneManager.UnloadSceneAsync(sceneData.sceneIndex);
                    else if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        SceneManager.UnloadSceneAsync(sceneData.Scene);
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(isPlaying);
            EditorGUI.BeginChangeCheck();
            isEnabled = GUI.Toggle(
                new Rect(rect.x + rect.width - labelSize.x - 20, rect.y, labelSize.x, rect.height),
                isEnabled, sceneIndexContent,
                EditorStyles.miniButtonLeft
            );
            if (EditorGUI.EndChangeCheck()) {
                sceneData.editorSceneData.enabled = isEnabled;
                buildScenes[index] = sceneData.editorSceneData;
                EditorBuildSettings.scenes = buildScenes;
                requireRefresh = true;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginChangeCheck();
            GUI.Toggle(
                new Rect(rect.x + rect.width - 20, rect.y, 20, rect.height),
                isCurrentStartScene,
                isPlaying && !isCurrentStartScene ? homeMarker : playFromThisScene,
                EditorStyles.miniButtonRight
            );
            if (EditorGUI.EndChangeCheck()) {
                if (isPlaying) {
                    hasPlayed = true;
                    lastScenes = new[] { scenePath };
                    waitScene = null;
                    EditorApplication.isPlaying = false;
                } else if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                    playScene = true;
                    #if UNITY_2022_2_OR_NEWER
                    int loadedSceneCount = SceneManager.loadedSceneCount;
                    #else
                    int loadedSceneCount = SceneManager.sceneCount;
                    #endif
                    var loadedScenes = new List<string>(loadedSceneCount);
                    for (int i = 0; i < loadedSceneCount; i++) {
                        Scene scene = SceneManager.GetSceneAt(i);
                        if (scene.IsValid() && !string.IsNullOrEmpty(scene.path))
                            loadedScenes.Add(scene.path);
                    }
                    lastScenes = loadedScenes.Count > 0 ? loadedScenes.ToArray() : null;
                    startScene = waitScene = scenePath;
                    Repaint();
                    EditorSceneManager.OpenScene(waitScene, OpenSceneMode.Single);
                }
            }
        }

        private void Refresh(bool reloadBuildScenes, bool repaint) {
            if (buildScenes == null || reloadBuildScenes) {
                buildScenes = EditorBuildSettings.scenes;
                if (buildScenes.Length < sceneDatas.Count)
                    sceneDatas.RemoveRange(buildScenes.Length, sceneDatas.Count - buildScenes.Length);
                else if (sceneDatas.Capacity < buildScenes.Length)
                    sceneDatas.Capacity = buildScenes.Length;
                for (int i = 0, buildIndex = -1; i < buildScenes.Length; i++) {
                    var editorBuildScene = buildScenes[i];
                    var sceneData = new SceneData(
                        editorBuildScene,
                        editorBuildScene.enabled ? ++buildIndex : -1
                    );
                    if (i < sceneDatas.Count)
                        sceneDatas[i] = sceneData;
                    else
                        sceneDatas.Add(sceneData);
                }
            }
            if (repaint)
                Repaint();
        }

        private bool CanAddOpenedScene() {
            if (!currentScene.IsValid() || string.IsNullOrEmpty(currentScene.path))
                return false;
            foreach (var buildScene in buildScenes)
                if (string.Equals(buildScene.path, currentScene.path, StringComparison.Ordinal))
                    return false;
            return true;
        }

        private void AddOpenedScene() {
            var newBuildScene = new EditorBuildSettingsScene(currentScene.path, false);
            int index = buildScenes.Length;
            Array.Resize(ref buildScenes, index + 1);
            buildScenes[index] = newBuildScene;
            EditorBuildSettings.scenes = buildScenes;
            Refresh(false, true);
        }

        private bool CanAddOpenedScenes() {
            openedScenePaths.Clear();
            for (int i = 0, c = SceneManager.sceneCount; i < c; i++)
                openedScenePaths.Add(SceneManager.GetSceneAt(i).path);
            foreach (var buildScene in buildScenes)
                if (openedScenePaths.Contains(buildScene.path))
                    return false;
            return true;
        }

        private void AddOpenedScenes() {
            int sceneCount = SceneManager.sceneCount;
            int index = buildScenes.Length;
            Array.Resize(ref buildScenes, index + sceneCount);
            for (int i = 0; i < sceneCount; i++)
                buildScenes[index + i] = new EditorBuildSettingsScene(SceneManager.GetSceneAt(i).path, false);
            EditorBuildSettings.scenes = buildScenes;
            Refresh(false, true);
        }

        private bool CanAddSelectedScenes() {
            selectedSceneAssets = Selection.GetFiltered<SceneAsset>(SelectionMode.Assets);
            if (selectedSceneAssets.Length <= 0) return false;
            openedScenePaths.Clear();
            foreach (var asset in selectedSceneAssets)
                openedScenePaths.Add(AssetDatabase.GetAssetPath(asset));
            foreach (var buildScene in buildScenes)
                if (openedScenePaths.Contains(buildScene.path))
                    return false;
            return true;
        }

        private void AddSelectedScenes() {
            int sceneCount = SceneManager.sceneCount;
            int index = buildScenes.Length;
            Array.Resize(ref buildScenes, index + sceneCount);
            foreach (var asset in selectedSceneAssets)
                buildScenes[index++] = new EditorBuildSettingsScene(AssetDatabase.GetAssetPath(asset), false);
            EditorBuildSettings.scenes = buildScenes;
            Refresh(false, true);
        }

        private void DrawHeader(Rect rect) => GUI.Label(rect, "Available Scenes", EditorStyles.boldLabel);

        private void OnAdd(Rect buttonRect, ReorderableList list) {
            var menu = new GenericMenu();
            if (CanAddOpenedScene()) menu.AddItem(addOpenSceneMenu, false, AddOpenedScene);
            else menu.AddDisabledItem(addOpenSceneMenu);
            if (CanAddOpenedScenes()) menu.AddItem(addOpenScenesMenu, false, AddOpenedScenes);
            else menu.AddDisabledItem(addOpenScenesMenu);
            if (CanAddSelectedScenes()) menu.AddItem(addSelectedScenesMenu, false, AddSelectedScenes);
            else menu.AddDisabledItem(addSelectedScenesMenu);
            menu.DropDown(buttonRect);
        }

        private void OnSelect(ReorderableList list) {
            int index = list.index;
            if (index >= 0 && index < buildScenes.Length)
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(buildScenes[list.index].path);
        }

        protected virtual void OnEnable() {
            if (homeMarker == null) {
                homeMarker = new GUIContent("⌂", "Stop and go to this scene");
                playFromThisScene = EditorGUIUtility.IconContent("PlayButton");
                addOpenSceneMenu = new GUIContent("Add Active Opened Scene");
                addOpenScenesMenu = new GUIContent("Add All Opened Scenes");
                addSelectedScenesMenu = new GUIContent("Add All Selected Scenes");
            }
            titleContent = new GUIContent("Scene Manager");
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            EditorBuildSettings.sceneListChanged += OnSceneListChanged;
            Refresh(true, false);
            listDisplay = new ReorderableList(sceneDatas, typeof(SceneData)) {
                elementHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing,
                drawHeaderCallback = DrawHeader,
                drawElementCallback = DrawElement,
                onAddDropdownCallback = OnAdd,
                onCanAddCallback = OnCanAdd,
                onCanRemoveCallback = OnCanRemove,
                onReorderCallback = OnReordered,
                onRemoveCallback = OnRemove,
                onSelectCallback = OnSelect,
            };
        }

        protected virtual void OnDestroy() {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            EditorBuildSettings.sceneListChanged -= OnSceneListChanged;
        }

        protected virtual void OnGUI() {
            currentScene = SceneManager.GetActiveScene();
            if (isPlaying = EditorApplication.isPlayingOrWillChangePlaymode) {
                if (string.Equals(currentScene.path, waitScene, StringComparison.Ordinal))
                    waitScene = null;
            } else if (playScene && !string.IsNullOrEmpty(waitScene) &&
                string.Equals(currentScene.path, waitScene, StringComparison.Ordinal)) {
                EditorApplication.isPlaying = true;
                playScene = false;
                hasPlayed = true;
            }
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            bool isMultipleSceneMode = SceneManager.sceneCount > 1;
            EditorGUI.BeginDisabledGroup(isMultipleSceneMode);
            bool additiveMode = GUILayout.Toggle(isMultipleSceneMode || this.additiveMode, "Multiple Scenes", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
            if (!isMultipleSceneMode) this.additiveMode = additiveMode;
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Build Settings", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                OpenInternalWindow("UnityEditor.BuildPlayerWindow, UnityEditor.dll", true);
            EditorGUILayout.EndHorizontal();
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            EditorGUILayout.BeginVertical(EditorStyles.inspectorFullWidthMargins);
            EditorGUI.BeginDisabledGroup(sceneDatas == null);
            EditorGUILayout.Space();
            requireRefresh = false;
            if (Event.current.type == EventType.Layout)
                labelSize = Vector2.zero;
            listDisplay.draggable = !isPlaying;
            listDisplay.DoLayoutList();
            if (requireRefresh)
                Refresh(false, true);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        protected virtual void OnFocus() => Refresh(false, false);

        protected virtual void OnLostFocus() => listDisplay.index = -1;

        private void OnPlayModeChanged(PlayModeStateChange playMode) {
            switch (playMode) {
                case PlayModeStateChange.EnteredPlayMode:
                    if (string.IsNullOrEmpty(startScene))
                        startScene = SceneManager.GetActiveScene().path;
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    if (hasPlayed && string.IsNullOrEmpty(waitScene) && lastScenes != null) {
                        for (int i = 0; i < lastScenes.Length; i++)
                            EditorSceneManager.OpenScene(
                                lastScenes[i],
                                i > 0 ? OpenSceneMode.Additive : OpenSceneMode.Single
                            );
                        lastScenes = null;
                    }
                    hasPlayed = false;
                    startScene = null;
                    break;
            }
            Repaint();
        }

        private void OnSceneListChanged() => Refresh(true, true);

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Refresh(false, true);

        private void OnSceneUnloaded(Scene scene) => Refresh(false, true);

        private void OnActiveSceneChanged(Scene scene, Scene newScene) => Refresh(false, true);

        private void OnRemove(ReorderableList list) {
            int index = list.index;
            if (index < 0) return;
            sceneDatas.RemoveAt(index);
            Array.Resize(ref buildScenes, sceneDatas.Count);
            for (int i = 0, l = sceneDatas.Count; i < l; i++)
                buildScenes[i] = sceneDatas[i].editorSceneData;
            EditorBuildSettings.scenes = buildScenes;
            Refresh(false, true);
        }

        private bool OnCanAdd(ReorderableList list) => !isPlaying && (CanAddOpenedScene() || CanAddOpenedScenes() || CanAddSelectedScenes());

        private bool OnCanRemove(ReorderableList list) => !isPlaying;

        private void OnReordered(ReorderableList list) {
            for (int i = 0, l = sceneDatas.Count; i < l; i++)
                buildScenes[i] = sceneDatas[i].editorSceneData;
            EditorBuildSettings.scenes = buildScenes;
            Refresh(false, true);
        }

        [Serializable]
        private struct SceneData {
            public readonly EditorBuildSettingsScene editorSceneData;
            public readonly bool enabled;
            private SceneAsset sceneAsset;
            public readonly string nameRaw;
            public readonly int sceneIndex;
            public SceneData(EditorBuildSettingsScene editorSceneData, int sceneIndex) {
                this.sceneIndex = sceneIndex;
                this.editorSceneData = editorSceneData;
                nameRaw = Path.GetFileNameWithoutExtension(editorSceneData.path);
                enabled = editorSceneData.enabled;
                sceneAsset = null;
            }
            public SceneAsset SceneAsset {
                get {
                    if (sceneAsset == null)
                        sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(editorSceneData.path);
                    return sceneAsset;
                }
            }
            public Scene Scene => SceneManager.GetSceneByPath(editorSceneData.path);
        }

        private enum SceneStatus: byte {
            NotLoaded,
            Loaded,
            Active,
        }
    }
}