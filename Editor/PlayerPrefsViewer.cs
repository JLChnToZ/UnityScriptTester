using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace JLChnToZ.EditorExtensions.PlayerPrefsHelper {
    public class PlayerPrefsViewer : EditorWindow {
        [SerializeField] PrefEntry[] entries;
        [SerializeField] bool autoSave = true;
        [SerializeField] bool autoRefresh = true;
        [SerializeField] Vector2 scrollPos;
        [SerializeField] bool isEditor;
        HashSet<PrefEntry> entrySet = new HashSet<PrefEntry>();
        string fieldName;
        FieldType watchFieldTye = FieldType.String;
        GUIContent temp, addContent, unwatchContent, deleteContent, deleteAllContent, refreshContent, saveContent, autoRefreshContent, autoSaveContent, manualRefreshContent, manualSaveContent, editorPrefsContent;

        [MenuItem("Window/JLChnToZ/Player Prefs")] static void OpenWindow() => GetWindow<PlayerPrefsViewer>().Show();

        void OnEnable() {
            titleContent = new GUIContent(isEditor ? "Editor Prefs" : "Player Prefs");
            temp = new GUIContent();
            addContent = new GUIContent(EditorGUIUtility.FindTexture("d_scenevis_visible_hover"), "Add to Watch List");
            unwatchContent = new GUIContent(EditorGUIUtility.FindTexture("d_SceneViewVisibility"), "Unwatch");
            deleteContent = new GUIContent(EditorGUIUtility.FindTexture("d_TreeEditor.Trash"), "Delete");
            deleteAllContent = new GUIContent(EditorGUIUtility.FindTexture("d_TreeEditor.Trash"), "Delete All");
            refreshContent = new GUIContent(EditorGUIUtility.FindTexture("d_Refresh"), "Refresh");
            saveContent = new GUIContent(EditorGUIUtility.FindTexture("d_SaveAs"), "Save");
            autoRefreshContent = new GUIContent("Auto", EditorGUIUtility.FindTexture("d_Refresh"), "Auto Refresh");
            autoSaveContent = new GUIContent("Auto", EditorGUIUtility.FindTexture("d_SaveAs"), "Auto Save");
            manualRefreshContent = new GUIContent("Auto", "Auto Refresh");
            manualSaveContent = new GUIContent("Auto", "Auto Save");
            editorPrefsContent = new GUIContent("E?", "Editor Prefs Mode");
            if (entries != null && entries.Length > 0) entrySet.UnionWith(entries);
        }

        void OnGUI() {
            bool refresh = false;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                using (var changes = new EditorGUI.ChangeCheckScope()) {
                    isEditor = GUILayout.Toggle(isEditor, editorPrefsContent, EditorStyles.toolbarButton);
                    if (changes.changed) titleContent.text = isEditor ? "Editor Prefs" : "Player Prefs";
                }
                EditorGUILayout.Space();
                fieldName = EditorGUILayout.TextField(fieldName, EditorStyles.toolbarTextField);
                temp.text = watchFieldTye.ToString();
                watchFieldTye = (FieldType)EditorGUILayout.EnumPopup(watchFieldTye, EditorStyles.toolbarPopup, GUILayout.MaxWidth(EditorStyles.toolbarPopup.CalcSize(temp).x));
                using (new EditorGUI.DisabledGroupScope(string.IsNullOrEmpty(fieldName))) {
                    if (GUILayout.Button(addContent, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false))) {
                        if (entrySet.Add(new PrefEntry(fieldName, watchFieldTye, isEditor))) entries = entrySet.ToArray();
                        fieldName = string.Empty;
                    }
                }
                GUILayout.FlexibleSpace();
                refresh = autoRefresh || GUILayout.Button(refreshContent, EditorStyles.toolbarButton);
                autoRefresh = GUILayout.Toggle(autoRefresh, autoRefresh ? autoRefreshContent : manualRefreshContent, EditorStyles.toolbarButton);
                EditorGUILayout.Space();
                if (!isEditor) {
                    if (!autoSave && GUILayout.Button(saveContent, EditorStyles.toolbarButton))
                        PlayerPrefs.Save();
                    autoSave = GUILayout.Toggle(autoSave, autoSave ? autoSaveContent : manualSaveContent, EditorStyles.toolbarButton);
                    EditorGUILayout.Space();
                }
                if (GUILayout.Button(deleteAllContent, EditorStyles.toolbarButton) &&
                    EditorUtility.DisplayDialog("Confirm", "Are you sure you want to delete all player preferences (include those not listed below)? This cannot be undone.", "Yes", "No")) {
                    if (isEditor)
                        EditorPrefs.DeleteAll();
                    else {
                        PlayerPrefs.DeleteAll();
                        PlayerPrefs.Save();
                    }
                }
            }
            PrefEntry deleteEntry = null;
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPos, GUILayout.ExpandHeight(true))) {
                scrollPos = scroll.scrollPosition;
                foreach (var entry in entrySet) {
                    if (entry.IsEditor != isEditor) continue;
                    var type = entry.Type;
                    using (new EditorGUILayout.HorizontalScope()) {
                        if (refresh) entry.SafeLoad();
                        entry.DrawLayoutField(false, autoSave);
                        temp.text = type.ToString();
                        entry.Type = type = (FieldType)EditorGUILayout.EnumPopup(type, EditorStyles.miniPullDown, GUILayout.MaxWidth(EditorStyles.miniPullDown.CalcSize(temp).x));
                        if (!autoRefresh && GUILayout.Button(refreshContent, EditorStyles.miniButtonLeft, GUILayout.ExpandWidth(false)))
                            entry.Load();
                        if (GUILayout.Button(unwatchContent, autoRefresh ? EditorStyles.miniButtonLeft : EditorStyles.miniButtonMid, GUILayout.ExpandWidth(false)))
                            deleteEntry = entry;
                        if (GUILayout.Button(deleteContent, EditorStyles.miniButtonRight, GUILayout.ExpandWidth(false)) &&
                            EditorUtility.DisplayDialog("Confirm", $"Are you sure you want to delete \"{entry.Key}\"? This cannot be undone.", "Yes", "No")) {
                            entry.Delete();
                            deleteEntry = entry;
                        }
                    }
                    entry.DrawLayoutField(true, autoSave);
                    if (entry.lastException != null) {
                        EditorGUILayout.HelpBox(entry.lastException.Message, MessageType.Error);
                        using (new EditorGUILayout.HorizontalScope()) {
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Dismiss", EditorStyles.miniButton))
                                entry.lastException = null;
                        }
                    }
                }
            }
            if (deleteEntry != null && entrySet.Remove(deleteEntry)) entries = entrySet.ToArray();
        }

        void OnInspectorUpdate() {
            if (autoRefresh) Repaint();
        }
    }
    
    [Serializable] internal class PrefEntry : IEquatable<PrefEntry> {
        [SerializeField] bool isEditor;
        [SerializeField] string key;
        [SerializeField] FieldType type;
        object value;
        [SerializeField] bool expanded;
        public Exception lastException;
    
        public string Key => key;
        public bool IsEditor => isEditor;

        public FieldType Type {
            get => type;
            set {
                if (type == value) return;
                type = value;
                if (this.value == null) {
                    Load();
                    return;
                }
                try {
                    this.value = Convert.ChangeType(this.value, (TypeCode)type);
                } catch {
                    this.value = null;
                }
            }
        }

        public int IntValue {
            get => value == null ? 0 : Convert.ToInt32(value);
            set => this.value = Convert.ChangeType(value, (TypeCode)type);
        }

        public float FloatValue {
            get => value == null ? 0F : Convert.ToSingle(value);
            set => this.value = Convert.ChangeType(value, (TypeCode)type);
        }

        public string StringValue {
            get => value == null ? string.Empty : Convert.ToString(value);
            set => this.value = Convert.ChangeType(value, (TypeCode)type);
        }

        string ControlName => $"UserPrefEntry_{key}";

        public PrefEntry(string key, FieldType type = default, bool isEditor = false) {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            this.key = key;
            this.isEditor = isEditor;
            if (type != default) {
                this.type = type;
                Load();
            }
        }

        public void DrawLayoutField(bool drawExpanded, bool autoSave) {
            try {
                object value = null;
                switch (type) {
                    case FieldType.Integer:
                        if (drawExpanded) return;
                        GUILayout.Space(16);
                        GUI.SetNextControlName(ControlName);
                        EditorGUI.BeginChangeCheck();
                        value = EditorGUILayout.IntField(key, IntValue);
                        break;
                    case FieldType.Float:
                        if (drawExpanded) return;
                        GUILayout.Space(16);
                        GUI.SetNextControlName(ControlName);
                        EditorGUI.BeginChangeCheck();
                        value = EditorGUILayout.FloatField(key, FloatValue);
                        break;
                    case FieldType.String:
                        if (drawExpanded) {
                            if (!expanded) return;
                            using (new EditorGUILayout.VerticalScope(EditorStyles.textArea)) {
                                EditorGUI.BeginChangeCheck();
                                GUI.SetNextControlName(ControlName);
                                value = EditorGUILayout.TextArea(StringValue, EditorStyles.wordWrappedLabel);
                            }
                            EditorGUILayout.Space();
                        } else {
                            expanded = EditorGUI.Foldout(EditorGUILayout.GetControlRect(
                                expanded ? GUILayout.ExpandWidth(true) : GUILayout.Width(10)
                            ), expanded, expanded ? key : string.Empty, false);
                            if (expanded) return;
                            GUI.SetNextControlName(ControlName);
                            EditorGUI.BeginChangeCheck();
                            value = EditorGUILayout.TextField(key, StringValue);
                        }
                        break;
                    default:
                        if (drawExpanded) return;
                        GUILayout.Space(16);
                        GUILayout.Label(key, GUILayout.ExpandWidth(true));
                        return;
                }
                if (EditorGUI.EndChangeCheck()) {
                    lastException = null;
                    this.value = value;
                    Save(autoSave);
                }
            } catch (Exception ex) {
                lastException = ex;
            }
        }

        public void Load() {
            if (!PlayerPrefs.HasKey(key)) {
                value = null;
                return;
            }
            switch (type) {
                case FieldType.Integer: value = isEditor ? EditorPrefs.GetInt(key) : PlayerPrefs.GetInt(key); break;
                case FieldType.Float: value = isEditor ? EditorPrefs.GetFloat(key) : PlayerPrefs.GetFloat(key); break;
                case FieldType.String: value = isEditor ? EditorPrefs.GetString(key) : PlayerPrefs.GetString(key); break;
            }
        }

        public void SafeLoad() {
            if (lastException == null && !string.Equals(GUI.GetNameOfFocusedControl(), ControlName, StringComparison.Ordinal)) Load();
        }

        public void Save(bool dumpValues = false) {
            if (value == null) {
                Delete();
                return;
            }
            if (isEditor) {
                switch (type) {
                    case FieldType.Integer: EditorPrefs.SetInt(key, Convert.ToInt32(value)); break;
                    case FieldType.Float: EditorPrefs.SetFloat(key, Convert.ToSingle(value)); break;
                    case FieldType.String: EditorPrefs.SetString(key, Convert.ToString(value)); break;
                }
                return;
            }
            switch (type) {
                case FieldType.Integer: PlayerPrefs.SetInt(key, Convert.ToInt32(value)); break;
                case FieldType.Float: PlayerPrefs.SetFloat(key, Convert.ToSingle(value)); break;
                case FieldType.String: PlayerPrefs.SetString(key, Convert.ToString(value)); break;
            }
            if (dumpValues) PlayerPrefs.Save();
        }

        public void Delete() {
            value = null;
            PlayerPrefs.DeleteKey(key);
        }

        public bool Equals(PrefEntry other) => string.Equals(key, other.key, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PrefEntry other && Equals(other);

        public override int GetHashCode() => key.GetHashCode();

        public override string ToString() => $"{key} = {value}";
    }

    internal enum FieldType : byte {
        String = TypeCode.String,
        Integer = TypeCode.Int32,
        Float = TypeCode.Single,
    }
}