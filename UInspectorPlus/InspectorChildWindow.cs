using UnityEngine;
using UnityEditor;

namespace UInspectorPlus {
    internal class InspectorChildWindow: EditorWindow {
        private InspectorDrawer drawer;
        private MethodPropertyDrawer parent;
        private Vector2 scrollPos;
        private bool updateProps;
        private bool isReadOnly;

        public static void Open(object target, bool showProps, bool showPrivate, bool showObsolete, bool showMethods, bool updateProps, MethodPropertyDrawer parent) {
            CreateInstance<InspectorChildWindow>().InternalOpen(target, showProps, showPrivate, showObsolete, showMethods, updateProps, parent);
        }

        private void InternalOpen(object target, bool showProps, bool showPrivate, bool showObsolete, bool showMethods, bool updateProps, MethodPropertyDrawer parent) {
            titleContent = new GUIContent(string.Format("{0} - Inspector+", target));
            drawer = new InspectorDrawer(target, true, showProps, showPrivate, showObsolete, showMethods);
            drawer.OnRequireRedraw += Repaint;
            this.parent = parent;
            this.updateProps = updateProps;
            ShowUtility();
            UpdateValues();
            isReadOnly = parent != null && parent.IsReadOnly && parent.requiredType != null && parent.requiredType.IsValueType;
        }

        private void OnGUI() {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            updateProps = GUILayout.Toggle(updateProps, "Update Props", EditorStyles.toolbarButton);
            GUILayout.Space(8);
            drawer.searchText = EditorGUILayout.TextField(drawer.searchText, Helper.GetGUIStyle("ToolbarSeachTextField"));
            if (GUILayout.Button(
                GUIContent.none,
                Helper.GetGUIStyle(
                    string.IsNullOrEmpty(drawer.searchText) ?
                    "ToolbarSeachCancelButtonEmpty" :
                    "ToolbarSeachCancelButton"
                )
            )) {
                drawer.searchText = string.Empty;
                GUI.FocusControl(null);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            scrollPos = GUILayout.BeginScrollView(scrollPos);
            EditorGUILayout.Space();
            drawer.Draw(false, isReadOnly);
            if (drawer.changed) {
                drawer.changed = false;
                if (parent != null && !parent.IsReadOnly &&
                    ((parent.requiredType != null && parent.requiredType.IsValueType) || parent.Value != drawer.target))
                    if (!Helper.AssignValue(parent.Info, parent.Target, drawer.target)) {
                        object reverted;
                        if (Helper.FetchValue(parent.Info, parent.Target, out reverted))
                            drawer.target = reverted;
                    }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndScrollView();
            if (drawer.target == null) Close();
        }

        private void OnInspectorUpdate() {
            if (EditorGUIUtility.editingTextField)
                return;
            UpdateValues();
        }

        private void UpdateValues() {
            drawer.UpdateValues(updateProps);
        }
    }
}
