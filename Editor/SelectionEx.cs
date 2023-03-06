using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

using UnityObject = UnityEngine.Object;

namespace JLChnToZ.EditorExtensions.Selections {
    public class SelectionExtension: EditorWindow {

        [MenuItem("Window/JLChnToZ/Selection Ex")]
        public static void Open() => GetWindow<SelectionExtension>();
        readonly Dictionary<UnityObject[], (string label, bool expanded)> savedCache = new Dictionary<UnityObject[], (string, bool)>(new SelectionEqualityComparer());
        readonly HashSet<UnityObject> selectionLookup = new HashSet<UnityObject>();
        readonly HashSet<UnityObject> selectionRecords = new HashSet<UnityObject>();
        UnityObject[] selection;
        string text = "";
        Vector2 scrollPos;
        bool isActiveSelectionExpanded;
        bool isRecordSelectionExpanded;
        bool recordMode;

        void OnEnable() {
            titleContent = new GUIContent("Selection Ex.");
            UpdateSelection();
        }

        void OnFocus() => OnSelectionChange();

        void OnGUI() {
            var e = Event.current;
            var eType = e.type;
            if (eType == EventType.DragExited) DragAndDrop.PrepareStartDrag();
            if (selection == null) UpdateSelection();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            bool isEmptySelection = selection == null || selection.Length == 0;
            bool disableAdd = isEmptySelection || savedCache.ContainsKey(selection);
            EditorGUI.BeginDisabledGroup(disableAdd);
            if (disableAdd) text = "";
            text = EditorGUILayout.TextField(text, EditorStyles.toolbarTextField, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(EditorGUIUtility.IconContent("d_Toolbar Plus", "Save"), EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))) {
                text = ObjectNames.GetUniqueName(
                    (from entry in savedCache.Values select entry.label).ToArray(),
                    string.IsNullOrWhiteSpace(text) ? FormatItemNames(selection, "Selection") : text.Trim()
                );
                savedCache.Add(selection, (text, false));
                text = "";
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(savedCache.Count == 0);
            if (GUILayout.Button("Forget All", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))) savedCache.Clear();
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginChangeCheck();
            recordMode = GUILayout.Toggle(recordMode, EditorGUIUtility.IconContent("Animation.Record", "Record Selection"), EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
            if (EditorGUI.EndChangeCheck() && !recordMode) selectionRecords.Clear();
            EditorGUILayout.EndHorizontal();

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            bool updateSelection = false;
            if (!isEmptySelection) {
                EditorGUILayout.BeginHorizontal();
                isActiveSelectionExpanded = EditorGUI.Foldout(EditorGUILayout.GetControlRect(GUILayout.Width(16)), isActiveSelectionExpanded, GUIContent.none); // GUILayout.Toggle(isActiveSelectionExpanded, GUIContent.none, EditorStyles.foldout);
                if (!DraggableToggle(new GUIContent("Active Selection", EditorGUIUtility.IconContent("Clipboard").image), true, selection)) {
                    selectionLookup.Clear();
                    updateSelection = true;
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                if (isActiveSelectionExpanded && DrawSelection(selection)) updateSelection = true;
            }
            if (selectionRecords.Count > 0) {
                EditorGUILayout.BeginHorizontal();
                isRecordSelectionExpanded = EditorGUI.Foldout(EditorGUILayout.GetControlRect(GUILayout.Width(16)), isRecordSelectionExpanded, GUIContent.none); // GUILayout.Toggle(isRecordSelectionExpanded, GUIContent.none, EditorStyles.foldout);
                EditorGUILayout.LabelField(new GUIContent("Selection Records", EditorGUIUtility.IconContent("Clipboard").image));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(EditorGUIUtility.IconContent("d_TreeEditor.Trash", "Forget"), EditorStyles.label))
                    selectionRecords.Clear();
                EditorGUILayout.EndHorizontal();
                if (isRecordSelectionExpanded && DrawSelection(selectionRecords)) updateSelection = true;
            }
            UnityObject[] updateGroup = null;
            (string label, bool expanded) updateGroupState = default;
            bool isRemoveGroup = false;
            foreach (var kv in savedCache) {
                EditorGUILayout.BeginHorizontal();
                bool selected = selectionLookup.Count > 0 && selectionLookup.Overlaps(kv.Key);
                bool expanded = EditorGUI.Foldout(EditorGUILayout.GetControlRect(GUILayout.Width(16)), kv.Value.expanded, GUIContent.none); // GUILayout.Toggle(kv.Value.expanded, GUIContent.none, EditorStyles.foldout);
                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = selected && !selectionLookup.IsSupersetOf(kv.Key);
                selected = DraggableToggle(new GUIContent(kv.Value.label, EditorGUIUtility.IconContent("Clipboard").image), selected, kv.Key);
                EditorGUI.showMixedValue = false;
                bool selectedChanged = EditorGUI.EndChangeCheck();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(EditorGUIUtility.IconContent("d_TreeEditor.Trash", "Forget"), EditorStyles.label)) isRemoveGroup = true;
                EditorGUILayout.EndHorizontal();
                if (selectedChanged) {
                    if (selected) {
                        selectionLookup.Clear();
                        selectionLookup.UnionWith(kv.Key);
                    } else
                        selectionLookup.ExceptWith(kv.Key);
                    updateSelection = true;
                } else if ((isRemoveGroup || expanded != kv.Value.expanded) && updateGroup == null) {
                    updateGroup = kv.Key;
                    updateGroupState = kv.Value;
                    updateGroupState.expanded = expanded;
                }
                if (expanded && DrawSelection(kv.Key)) updateSelection = true;
            }
            EditorGUILayout.EndScrollView();
            if (DragAndDrop.objectReferences.Length > 0)
                switch (eType) {
                    case EventType.DragUpdated:
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        break;
                    case EventType.DragPerform:
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        DragAndDrop.AcceptDrag();
                        selectionLookup.UnionWith(DragAndDrop.objectReferences);
                        updateSelection = true;
                        break;
                }
            if (updateSelection) {
                if (selection == null || selection.Length != selectionLookup.Count)
                    selection = new UnityObject[selectionLookup.Count];
                selectionLookup.CopyTo(selection);
                Selection.objects = selection;
                OnSelectionChange();
            }
            if (updateGroup != null) {
                if (isRemoveGroup) savedCache.Remove(updateGroup);
                else savedCache[updateGroup] = updateGroupState;
            }
        }

        bool DrawSelection(IEnumerable<UnityObject> selection) {
            EditorGUI.indentLevel += 2;
            bool updateSelection = false;
            foreach (var entry in selection) {
                if (entry == null) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                bool isChecked = DraggableToggle(EditorGUIUtility.ObjectContent(entry, entry.GetType()), selectionLookup.Contains(entry), entry);
                if (EditorGUI.EndChangeCheck()) {
                    updateSelection = true;
                    if (isChecked) selectionLookup.Add(entry);
                    else selectionLookup.Remove(entry);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(EditorGUIUtility.IconContent("d_UnityEditor.FindDependencies", $"Ping {entry.name}"), EditorStyles.label))
                    EditorGUIUtility.PingObject(entry);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel -= 2;
            return updateSelection;
        }

        static bool DraggableToggle(GUIContent label, bool value, params UnityObject[] dragTargets) {
            var e = Event.current;
            Rect rect = EditorGUILayout.GetControlRect(true);
            if (dragTargets != null && dragTargets.Length > 0 && rect.Contains(e.mousePosition))
                switch (e.type) {
                    case EventType.MouseUp:
                        DragAndDrop.PrepareStartDrag();
                        break;
                    case EventType.MouseDown:
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = dragTargets;
                        break;
                    case EventType.MouseDrag:
                        DragAndDrop.objectReferences = dragTargets;
                        DragAndDrop.StartDrag(FormatItemNames(dragTargets));
                        e.Use();
                        break;
                }
            return EditorGUI.ToggleLeft(rect, label, value);
        }

        static string FormatItemNames(UnityObject[] items, string defaultName = null) {
            var defaultItem = items.FirstOrDefault();
            string name = defaultItem != null ? defaultItem.name : defaultName ?? string.Empty;
            return items.Length > 1 ?
                string.IsNullOrWhiteSpace(name) ?
                    $"{items.Length - 1} Items" :
                    $"{name} (+{items.Length - 1} Items)" :
                name;
        }

        void OnSelectionChange() {
            UpdateSelection();
            Repaint();
        }

        void UpdateSelection() {
            selection = Selection.objects;
            selectionLookup.Clear();
            selectionLookup.UnionWith(selection);
            if (recordMode) selectionRecords.UnionWith(selectionLookup);
        }
    }

    class SelectionEqualityComparer: IEqualityComparer<UnityObject[]> {
        readonly HashSet<UnityObject> cache = new HashSet<UnityObject>();

        public bool Equals(UnityObject[] lhs, UnityObject[] rhs) {
            if (lhs == null) return rhs == null;
            if (rhs == null) return false;
            try {
                cache.UnionWith(lhs.Where(NotNull));
                return cache.SetEquals(rhs.Where(NotNull));
            } finally {
                cache.Clear();
            }
        }

        public int GetHashCode(UnityObject[] obj) {
            if (obj == null || obj.Length == 0) return 0;
            int hashCode = 0;
            foreach (var entry in obj) unchecked {
                    if (entry != null) hashCode ^= entry.GetInstanceID();
                }
            return hashCode;
        }

        static bool NotNull(UnityObject obj) => obj != null;
    }
}