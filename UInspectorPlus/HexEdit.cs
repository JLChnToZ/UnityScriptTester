using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace UInspectorPlus {
    internal class HexEdit {
        [SerializeField] private Vector2 scrollPos;
        public byte[] data;
        public int columns = 16;
        private GUIContent temp = new GUIContent();

        public float Height {
            get {
                if (data == null) return 0;
                return (data.Length + columns) / columns * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
            }
        }

        public void DrawGUI(bool hasLabel = false, params GUILayoutOption[] options) {
            Draw(EditorGUILayout.GetControlRect(hasLabel, Height, options));
        }

        public void Draw(Rect viewport) {
            float height = EditorGUIUtility.singleLineHeight;
            float padHeight = height + EditorGUIUtility.standardVerticalSpacing;
            if (data != null) {
                temp.text = data.Length.ToString("X8");
                Vector2 labelSize = GUI.skin.label.CalcSize(temp);
                Rect contentRect = new Rect(0, 0, labelSize.x + (columns * 1.7F + 2) * height, Height);
                GUI.Box(viewport, GUIContent.none, GUI.skin.textArea);
                scrollPos = GUI.BeginScrollView(viewport, scrollPos, contentRect);
                bool changed = GUI.changed;
                GUI.changed = false;
                for (int start = Mathf.FloorToInt(scrollPos.y / padHeight) * columns,
                    end = Math.Min(data.Length, start + Mathf.CeilToInt(viewport.height / padHeight) * columns),
                    col = start; col < end; col++) {
                    if (col % columns == 0) {
                        temp.text = col.ToString("X8");
                        GUI.Label(new Rect(0, col / columns * padHeight, labelSize.x, labelSize.y), temp);
                    }
                    string newValue = GUI.TextField(
                        new Rect(
                            labelSize.x + (col % columns * 1.2F + 1) * height,
                            col / columns * padHeight,
                            height * 1.6F, height
                        ),
                        data[col].ToString("X2"),
                        2, GUI.skin.label
                    );
                    if (GUI.changed) {
                        GUI.changed = false;
                        changed = true;
                        int val;
                        if (int.TryParse(newValue, NumberStyles.HexNumber, null, out val))
                            data[col] = unchecked((byte)val);
                    }
                    string newStr = GUI.TextField(
                        new Rect(
                            labelSize.x + (columns * 1.2F + col % columns * 0.5F + 2) * height,
                            col / columns * padHeight,
                            height * 0.8F, height
                        ),
                        Byte2String(data[col]),
                        1, GUI.skin.label
                    );
                    if (GUI.changed) {
                        GUI.changed = false;
                        changed = true;
                        data[col] = unchecked((byte)newStr[0]);
                    }
                }
                GUI.changed = changed;
                GUI.EndScrollView();
            }
        }

        private static string Byte2String(byte b) {
            if (b < 32 || b >= 127) return ".";
            return ((char)b).ToString();
        }
    }
}
