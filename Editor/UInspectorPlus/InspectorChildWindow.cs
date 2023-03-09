using UnityEngine;
using UnityEditor;
using System;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.EditorExtensions.UInspectorPlus {
    internal class InspectorChildWindow: EditorWindow {
        private InspectorDrawer drawer;
        private TypeResolverGUI resolver;
        private MethodPropertyDrawer parent;
        private Vector2 scrollPos;
        private bool updateProps;
        private bool isReadOnly;
        private bool showProps;
        private bool showPrivate;
        private bool showObsolete;
        private bool showMethods;
        private Rect menuButtonRect;

        public static void Open(object target, bool showProps, bool showPrivate, bool showObsolete, bool showMethods, bool updateProps, MethodPropertyDrawer parent) =>
            CreateInstance<InspectorChildWindow>().InternalOpen(target, target.GetType(), showProps, showPrivate, showObsolete, showMethods, updateProps, parent);

        public static void OpenStatic(Type targetType, bool showProps, bool showPrivate, bool showObsolete, bool showMethods, bool updateProps, MethodPropertyDrawer parent) =>
            CreateInstance<InspectorChildWindow>().InternalOpen(null, targetType, showProps, showPrivate, showObsolete, showMethods, updateProps, parent);

        private void InternalOpen(object target, Type targetType, bool showProps, bool showPrivate, bool showObsolete, bool showMethods, bool updateProps, MethodPropertyDrawer parent) {
            titleContent = new GUIContent($"{target ?? targetType} - Inspector+");
            if (target == null && targetType.ContainsGenericParameters) {
                resolver = new TypeResolverGUI(targetType);
            } else {
                drawer = InspectorDrawer.GetDrawer(target, targetType, true, showProps, showPrivate, showObsolete, showMethods);
                drawer.OnRequireRedraw += Repaint;
            }
            this.showProps = showProps;
            this.showPrivate = showPrivate;
            this.showObsolete = showObsolete;
            this.showMethods = showMethods;
            this.parent = parent;
            this.updateProps = updateProps;
            ShowUtility();
            UpdateValues();
            isReadOnly = parent != null && parent.IsReadOnly && parent.requiredType != null && parent.requiredType.IsValueType;
        }

        private void OnGUI() {
            if (resolver != null) {
                EditorGUILayout.HelpBox($"{resolver.srcType} is a generic type, and therefore it is required to fill all generic parameters before using it.", MessageType.Info);
                GUILayout.Space(8);
                resolver.Draw();
                GUILayout.Space(8);
                if (GUILayout.Button("Resolve Generic Type") && resolver.IsReady) {
                    var targetType = resolver.ResolvedType;
                    if (targetType != null) {
                        resolver = null;
                        titleContent = new GUIContent($"{targetType} - Inspector+");
                        drawer = InspectorDrawer.GetDrawer(null, targetType, true, showProps, showPrivate, showObsolete, showMethods);
                        drawer.OnRequireRedraw += Repaint;
                    }
                }
                return;
            }
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            drawer.searchText = Helper.ToolbarSearchField(drawer.searchText ?? string.Empty);
            GUILayout.FlexibleSpace();
            if (drawer.target is UnityObject uObject) {
                if (GUILayout.Button("Ping", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                    EditorGUIUtility.PingObject(uObject);
                if (GUILayout.Button("Select", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                    Selection.activeObject = uObject;
                if (GUILayout.Button(EditorGUIUtility.IconContent("TreeEditor.Trash", "Destroy"),
                    EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)) && EditorUtility.DisplayDialog(
                        "Destroy object",
                        $"Destroy {uObject.GetType()} {uObject.name} (Instance ID: {uObject.GetInstanceID()})?",
                        "Yes", "No"
                    )) {
                    DestroyImmediate(uObject);
                    Close();
                }
                GUILayout.Space(8);
            }
            if (GUILayout.Button(EditorGUIUtility.IconContent("_Menu", "Menu"), EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                OpenMenu(menuButtonRect);
            if (Event.current.type == EventType.Repaint)
                menuButtonRect = GUILayoutUtility.GetLastRect();
            GUILayout.EndHorizontal();
            scrollPos = GUILayout.BeginScrollView(scrollPos);
            EditorGUILayout.Space();
            drawer.Draw(false, isReadOnly);
            if (drawer.changed) {
                drawer.changed = false;
                if (parent != null && !parent.IsReadOnly &&
                    ((parent.requiredType != null && parent.requiredType.IsValueType) || parent.Value != drawer.target) &&
                    !Helper.AssignValue(parent.Info, parent.Target, drawer.target) &&
                    parent.Info.FetchValue(parent.Target, out var reverted)
                ) drawer.target = reverted;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndScrollView();
        }

        private void OpenMenu(Rect position) {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Update Properties"), updateProps, () => {
                updateProps = !updateProps;
            });
            menu.AddItem(new GUIContent("Show Properties"), showProps, () => {
                showProps = !showProps;
                RefreshDrawer();
                EditorPrefs.SetBool("inspectorplus_props", showProps);
            });
            menu.AddItem(new GUIContent("Show Methods"), showMethods, () => {
                showMethods = !showMethods;
                RefreshDrawer();
                EditorPrefs.SetBool("inspectorplus_methods", showMethods);
            });
            menu.AddItem(new GUIContent("Show Private Members"), showPrivate, () => {
                showPrivate = !showPrivate;
                RefreshDrawer();
                EditorPrefs.SetBool("inspectorplus_private", showPrivate);
            });
            menu.AddItem(new GUIContent("Show Obsolete Members"), showObsolete, () => {
                showObsolete = !showObsolete;
                RefreshDrawer();
                EditorPrefs.SetBool("inspectorplus_obsolete", showObsolete);
            });
            menu.DropDown(position);
        }

        private void OnDestroy() => drawer?.Dispose();

        private void OnInspectorUpdate() {
            if (EditorGUIUtility.editingTextField)
                return;
            UpdateValues();
        }

        private void RefreshDrawer() {
            if (drawer == null) return;
            var target = drawer.target;
            if (target.IsInvalid()) return;
            drawer.Dispose();
            drawer = InspectorDrawer.GetDrawer(target, target.GetType(), true, showProps, showPrivate, showObsolete, showMethods);
            drawer.OnRequireRedraw += Repaint;
            UpdateValues();
        }

        private void UpdateValues() {
            if (drawer == null) {
                if (resolver == null) Close();
                return;
            }
            drawer.UpdateValues(updateProps);
        }
    }
}