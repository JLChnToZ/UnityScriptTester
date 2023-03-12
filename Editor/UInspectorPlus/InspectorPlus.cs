using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.EditorExtensions.UInspectorPlus {
    internal class InspectorPlus: EditorWindow, IHasCustomMenu {
        private const string description = "CAUTION: USE THIS PLUGIN AT YOUR OWN RISK!\n" +
            "Unless you know exactly what you are doing, do not use this plugin " +
            "or you may likely to corrupt your project or even crashes the editor!";
        private readonly List<InspectorDrawer[]> drawers = new List<InspectorDrawer[]>();
        private TypeMatcher typeMatcher;
        private static readonly string[] searchModes = new[] { "Selected Component Members", "Types" };
        private static readonly string[] titles = new[] { "Inspector+", "Type Search" };
        private string searchText;
        private int searchMode = 0;
        private Vector2 scrollPos;
        private bool initialized;
        private bool autoUpdateValues;
        private bool privateFields;
        private bool forceUpdateProps;
        private bool showProps;
        private bool showMethods;
        private bool locked;
        private bool showObsolete;
        private bool warningDismissed;
        private int[] instanceIds = new int[0];

        private void OnEnable() {
            titleContent = new GUIContent(titles[searchMode], EditorGUIUtility.FindTexture("UnityEditor.InspectorWindow"));
            Initialize();
            OnFocus();
            typeMatcher = new TypeMatcher();
            typeMatcher.OnRequestRedraw += Repaint;
            typeMatcher.OnSelected += TypeSelected;
        }

        private void OnDisable() => typeMatcher.OnRequestRedraw -= Repaint;

        private void OnDestroy() => typeMatcher?.Dispose();

        private static void TypeSelected(Type type) => InspectorChildWindow.OpenStatic(type, true, true, true, true, false, null);

        private void Initialize() {
            if (initialized) return;
            autoUpdateValues = EditorPrefs.GetBool("inspectorplus_autoupdate", true);
            privateFields = EditorPrefs.GetBool("inspectorplus_private", true);
            forceUpdateProps = EditorPrefs.GetBool("inspectorplus_editupdate", false);
            showProps = EditorPrefs.GetBool("inspectorplus_props", true);
            showMethods = EditorPrefs.GetBool("inspectorplus_methods", true);
            locked = EditorPrefs.GetBool("inspectorplus_lock", false);
            showObsolete = EditorPrefs.GetBool("inspectorplus_obsolete", false);
            searchMode = EditorPrefs.GetInt("inspectorplus_searchmode", 0);
            initialized = true;
        }

        private void OnFocus() => OnSelectionChange();

        private void OnGUI() {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUI.changed = false;
            GUILayout.Space(8);
            searchText = Helper.ToolbarSearchField(searchText ?? string.Empty, searchModes, ref searchMode);
            if (GUI.changed)
                UpdateSearchMode();
            GUILayout.Space(8);
            EditorGUI.BeginDisabledGroup(instanceIds == null || instanceIds.Length == 0 || searchMode != 0);
            if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "Destroy Selection"),
                EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                DestroyAll();
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();
            GUI.changed = false;
            scrollPos = GUILayout.BeginScrollView(scrollPos);
            if (!warningDismissed) {
                EditorGUILayout.HelpBox(description, MessageType.Warning);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Dismiss", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    warningDismissed = true;
                EditorGUILayout.EndHorizontal();
            }
            switch (searchMode) {
                case 0:
                    foreach (var drawer in drawers.SelectMany(drawer => drawer)) {
                        drawer.searchText = searchText;
                        drawer.Draw();
                    }
                    break;
                case 1:
                    typeMatcher.SearchText = searchText;
                    typeMatcher.Draw();
                    break;
            }
            GUILayout.FlexibleSpace();
            GUILayout.Space(EditorGUIUtility.singleLineHeight / 2);
            GUILayout.EndScrollView();
        }

        private void OnInspectorUpdate() {
            if (!autoUpdateValues || EditorGUIUtility.editingTextField)
                return;
            UpdateValues();
        }

        private void ShowButton(Rect rect) {
            EditorGUI.BeginDisabledGroup(searchMode != 0);
            EditorGUI.BeginChangeCheck();
            GUI.Toggle(rect, locked && searchMode == 0, GUIContent.none, Helper.GetGUIStyle("IN LockButton"));
            if (EditorGUI.EndChangeCheck())
                TriggerLock();
            EditorGUI.EndDisabledGroup();
        }

        void IHasCustomMenu.AddItemsToMenu(GenericMenu menu) {
            for (int i = 0; i < searchModes.Length; i++)
                menu.AddItem(new GUIContent(searchModes[i]), searchMode == i, UpdateSearchMode, i);
            if (searchMode != 0) return;
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Refresh"), false, RefreshList);
            if (autoUpdateValues)
                menu.AddDisabledItem(new GUIContent("Update Values", "Auto Updating"));
            else
                menu.AddItem(new GUIContent("Update Values"), false, UpdateValues);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Lock Selection"), locked, TriggerLock);
            menu.AddItem(new GUIContent("Auto Update Values"), autoUpdateValues, () => {
                autoUpdateValues = !autoUpdateValues;
                EditorPrefs.SetBool("inspectorplus_autoupdate", autoUpdateValues);
            });
            menu.AddItem(new GUIContent("Update Properties in Edit Mode"), forceUpdateProps, () => {
                forceUpdateProps = !forceUpdateProps;
                UpdateValues();
                EditorPrefs.SetBool("inspectorplus_editupdate", forceUpdateProps);
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Show Properties"), showProps, () => {
                showProps = !showProps;
                RefreshList();
                EditorPrefs.SetBool("inspectorplus_props", showProps);
            });
            menu.AddItem(new GUIContent("Show Methods"), showMethods, () => {
                showMethods = !showMethods;
                RefreshList();
                EditorPrefs.SetBool("inspectorplus_methods", showMethods);
            });
            menu.AddItem(new GUIContent("Show Private Members"), privateFields, () => {
                privateFields = !privateFields;
                RefreshList();
                IterateDrawers<IReflectorDrawer>(methodDrawer => methodDrawer.AllowPrivateFields = privateFields);
                EditorPrefs.SetBool("inspectorplus_private", privateFields);
            });
            menu.AddItem(new GUIContent("Show Obsolete Members"), showObsolete, () => {
                showObsolete = !showObsolete;
                RefreshList();
                IterateDrawers<IReflectorDrawer>(methodDrawer => methodDrawer.AllowObsolete = showObsolete);
                EditorPrefs.SetBool("inspectorplus_obsolete", showObsolete);
            });
        }

        private void RefreshList() {
            foreach (var drawer in drawers) foreach (var d in drawer) d.Dispose();
            drawers.Clear();
            OnSelectionChange();
        }

        private void TriggerLock() {
            if (!locked && Selection.activeObject == null)
                return;
            locked = !locked;
            if (!locked)
                OnSelectionChange();
            EditorPrefs.SetBool("inspectorplus_lock", locked);
        }

        private void UpdateSearchMode(object mode) {
            searchMode = (int)mode;
            UpdateSearchMode();
            RefreshList();
        }

        private void UpdateSearchMode() {
            titleContent.text = titles[searchMode];
            EditorPrefs.SetInt("inspectorplus_searchmode", searchMode);
        }

        private void OnSelectionChange() => CheckSelection();

        private void CheckSelection(bool callFromUpdate = false) {
            if (!locked)
                instanceIds = Selection.instanceIDs;
            var pendingRemoveDrawers = new List<InspectorDrawer[]>();
            var pendingAddDrawers = new List<InspectorDrawer[]>();
            foreach (var drawer in drawers)
                if (drawer.Length <= 0 || drawer[0].target.IsInvalid() || !instanceIds.Contains(drawer[0].target.ObjIdOrHashCode())) {
                    pendingRemoveDrawers.Add(drawer);
                    foreach (var d in drawer) d.Dispose();
                }
            drawers.RemoveAll(pendingRemoveDrawers.Contains);
            foreach (var instanceID in instanceIds)
                if (drawers.FindIndex(drawer => drawer[0].target.ObjIdOrHashCode() == instanceID) < 0)
                    pendingAddDrawers.Add(CreateDrawers(instanceID));
            drawers.AddRange(pendingAddDrawers);
            if (!callFromUpdate)
                UpdateValues();
        }

        private InspectorDrawer[] CreateDrawers(int instanceID) {
            var target = EditorUtility.InstanceIDToObject(instanceID);
            if (target.IsInvalid())
                return new InspectorDrawer[0];
            var ret = new List<InspectorDrawer>();
            try {
                ret.Add(CreateDrawer(target, true));
            } catch (Exception ex) {
                Debug.LogException(ex);
            }
            if (target is GameObject gameObject)
                foreach (var component in gameObject.GetComponents(typeof(Component))) {
                    try {
                        ret.Add(CreateDrawer(component, false));
                    } catch (Exception ex) {
                        Debug.LogException(ex);
                    }
                }
            if (target is AnimatorState animatorState)
                foreach (var animBeahvior in animatorState.behaviours) {
                    try {
                        ret.Add(CreateDrawer(animBeahvior, false));
                    } catch (Exception ex) {
                        Debug.LogException(ex);
                    }
                }
            return ret.ToArray();
        }

        private InspectorDrawer CreateDrawer(UnityObject target, bool shown) {
            var drawer = InspectorDrawer.GetDrawer(target, target.GetType(), shown, showProps, privateFields, showObsolete, showMethods);
            drawer.OnRequireRedraw += Repaint;
            return drawer;
        }

        private void IterateDrawers<T>(Action<T> each) where T : IReflectorDrawer {
            foreach (var methodDrawer in drawers.SelectMany(drawer => drawer).SelectMany(drawer => drawer.drawer).OfType<T>())
                each(methodDrawer);
        }

        private void UpdateValues() => UpdateValues(forceUpdateProps || EditorApplication.isPlaying);

        private void UpdateValues(bool updateProps) {
            for (int i = 0; i < drawers.Count; i++) {
                var drawerGroup = drawers[i];
                if (drawerGroup.Length == 0 || drawerGroup[0].target.IsInvalid()) {
                    CheckSelection(true);
                    break;
                }
                int instanceID = drawerGroup[0].target.ObjIdOrHashCode();
                for (int j = 0; j < drawerGroup.Length; j++) {
                    if (drawerGroup[j].target.IsInvalid()) {
                        drawers[i] = CreateDrawers(instanceID);
                        break;
                    }
                    if (drawerGroup[j].shown)
                        drawerGroup[j].UpdateValues(updateProps);
                }
            }
            Repaint();
        }

        private void DestroyAll() {
            int[] instanceIds = this.instanceIds;
            if (instanceIds == null || instanceIds.Length == 0)
                return;
            bool deleteAll = false, showError = true;
            var remainObjects = new HashSet<int>(instanceIds);
            foreach (int id in instanceIds) {
                try {
                    UnityObject obj = EditorUtility.InstanceIDToObject(id);
                    if (obj == null) continue;
                    bool deleteThis = deleteAll;
                    if (!deleteAll)
                        switch (EditorUtility.DisplayDialogComplex(
                            "Destroy object",
                            $"Destroy {obj.GetType()} {obj.name} (Instance ID: {id})?",
                            "Yes", "No", "Yes to all"
                        )) {
                            case 0: deleteThis = true; break;
                            case 1: deleteThis = false; break;
                            case 2: {
                                    deleteAll = true;
                                    deleteThis = true;
                                    break;
                                }
                        }
                    if (deleteThis) {
                        DestroyImmediate(obj);
                        remainObjects.Remove(id);
                    }
                } catch (Exception ex) {
                    Debug.LogException(ex);
                    if (showError)
                        switch (EditorUtility.DisplayDialogComplex(
                            $"Error while destroying object {id}",
                            ex.Message,
                            "Continue", "Stop", "Don't show again"
                        )) {
                            case 0: break;
                            case 1: return;
                            case 2: showError = false; break;
                        }
                }
            }
            int[] nextInstanceIds = this.instanceIds;
            if (nextInstanceIds.Length != remainObjects.Count)
                this.instanceIds = nextInstanceIds = new int[remainObjects.Count];
            remainObjects.CopyTo(nextInstanceIds);
            Selection.instanceIDs = this.instanceIds;
            OnSelectionChange();
        }
    }
}