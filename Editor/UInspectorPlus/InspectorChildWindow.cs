using UnityEngine;
using UnityEditor;
using System;

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

        public static void Open(object target, bool showProps, bool showPrivate, bool showObsolete, bool showMethods, bool updateProps, MethodPropertyDrawer parent) =>
            CreateInstance<InspectorChildWindow>().InternalOpen(target, target.GetType(), showProps, showPrivate, showObsolete, showMethods, updateProps, parent);

        public static void OpenStatic(Type targetType, bool showProps, bool showPrivate, bool showObsolete, bool showMethods, bool updateProps, MethodPropertyDrawer parent) =>
            CreateInstance<InspectorChildWindow>().InternalOpen(null, targetType, showProps, showPrivate, showObsolete, showMethods, updateProps, parent);

        private void InternalOpen(object target, Type targetType, bool showProps, bool showPrivate, bool showObsolete, bool showMethods, bool updateProps, MethodPropertyDrawer parent) {
            titleContent = new GUIContent($"{target ?? targetType} - Inspector+");
            if (target == null && targetType.ContainsGenericParameters) {
                resolver = new TypeResolverGUI(targetType);
                this.showProps = showProps;
                this.showPrivate = showPrivate;
                this.showObsolete = showObsolete;
                this.showMethods = showMethods;
            } else {
                drawer = InspectorDrawer.GetDrawer(target, targetType, true, showProps, showPrivate, showObsolete, showMethods);
                drawer.OnRequireRedraw += Repaint;
            }
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
            updateProps = GUILayout.Toggle(updateProps, "Update Props", EditorStyles.toolbarButton);
            GUILayout.Space(8);
            drawer.searchText = Helper.ToolbarSearchField(drawer.searchText ?? string.Empty);
            GUILayout.FlexibleSpace();
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

        private void OnDestroy() => drawer?.Dispose();

        private void OnInspectorUpdate() {
            if (EditorGUIUtility.editingTextField)
                return;
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