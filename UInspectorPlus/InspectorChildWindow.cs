using UnityEngine;
using UnityEditor;
using System;

namespace UInspectorPlus {
    internal class InspectorChildWindow: EditorWindow {
        private InspectorDrawer drawer;
        private MethodPropertyDrawer parent;
        private Vector2 scrollPos;
        private bool updateProps;
        private bool isReadOnly;

        public static void Open(object target, bool showProps, bool showPrivate, bool showObsolete, bool showMethods, bool updateProps, MethodPropertyDrawer parent) =>
            CreateInstance<InspectorChildWindow>().InternalOpen(target, target.GetType(), showProps, showPrivate, showObsolete, showMethods, updateProps, parent);

        public static void OpenStatic(Type targetType, bool showProps, bool showPrivate, bool showObsolete, bool showMethods, bool updateProps, MethodPropertyDrawer parent) =>
            CreateInstance<InspectorChildWindow>().InternalOpen(null, targetType, showProps, showPrivate, showObsolete, showMethods, updateProps, parent);

        private void InternalOpen(object target, Type targetType, bool showProps, bool showPrivate, bool showObsolete, bool showMethods, bool updateProps, MethodPropertyDrawer parent) {
            titleContent = new GUIContent($"{target ?? targetType} - Inspector+");
            drawer = InspectorDrawer.GetDrawer(target, targetType, true, showProps, showPrivate, showObsolete, showMethods);
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
            drawer.searchText = Helper.ToolbarSearchField(drawer.searchText ?? string.Empty);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            scrollPos = GUILayout.BeginScrollView(scrollPos);
            EditorGUILayout.Space();
            drawer.Draw(false, isReadOnly);
            if(drawer.changed) {
                drawer.changed = false;
                if(parent != null && !parent.IsReadOnly &&
                    ((parent.requiredType != null && parent.requiredType.IsValueType) || parent.Value != drawer.target))
                    if(!Helper.AssignValue(parent.Info, parent.Target, drawer.target)) {
                        object reverted;
                        if(Helper.FetchValue(parent.Info, parent.Target, out reverted))
                            drawer.target = reverted;
                    }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndScrollView();
        }

        private void OnDestroy() {
            drawer.Dispose();
        }

        private void OnInspectorUpdate() {
            if(EditorGUIUtility.editingTextField)
                return;
            UpdateValues();
        }

        private void UpdateValues() => drawer.UpdateValues(updateProps);
    }
}
