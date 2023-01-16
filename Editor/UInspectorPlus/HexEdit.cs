using System;
using System.Globalization;
using UnityEngine;
using UnityEditor;

namespace JLChnToZ.EditorExtensions.UInspectorPlus {
    [CustomInspectorDrawer(typeof(byte[]), -1)]
    internal class HexEdit: InspectorDrawer {
        [SerializeField] private Vector2 scrollPos;
        public new byte[] target => base.target as byte[];
        public int columns = 16;
        private GUIContent temp = new GUIContent();


        public float Height {
            get {
                if(target == null) return 0;
                return (target.Length + columns) / columns * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
            }
        }

        public HexEdit(object target, Type targetType, bool shown, bool showProps, bool showPrivateFields, bool showObsolete, bool showMethods) :
            base(target, targetType, shown, showProps, showPrivateFields, showObsolete, showMethods) {
        }

        protected override void Draw(bool readOnly) {
            if(target != null)
                Draw(EditorGUILayout.GetControlRect(
                    false, Height, GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 3), GUILayout.ExpandHeight(true)
                ));
            base.Draw(readOnly);
        }


        private void Draw(Rect viewport) {
            float height = EditorGUIUtility.singleLineHeight;
            float padHeight = height + EditorGUIUtility.standardVerticalSpacing;
            if(target != null) {
                temp.text = target.Length.ToString("X8");
                Vector2 labelSize = GUI.skin.label.CalcSize(temp);
                Rect contentRect = new Rect(0, 0, labelSize.x + (columns * 1.7F + 2) * height, Height);
                GUI.Box(viewport, GUIContent.none, GUI.skin.textArea);
                scrollPos = GUI.BeginScrollView(viewport, scrollPos, contentRect);
                bool changed = GUI.changed;
                GUI.changed = false;
                for(int start = Mathf.FloorToInt(scrollPos.y / padHeight) * columns,
                    end = Math.Min(target.Length, start + Mathf.CeilToInt(viewport.height / padHeight) * columns),
                    col = start; col < end; col++) {
                    if(col % columns == 0) {
                        temp.text = col.ToString("X8");
                        GUI.Label(new Rect(0, col / columns * padHeight, labelSize.x, labelSize.y), temp);
                    }
                    string newValue = GUI.TextField(
                        new Rect(
                            labelSize.x + (col % columns * 1.2F + 1) * height,
                            col / columns * padHeight,
                            height * 1.6F, height
                        ),
                        target[col].ToString("X2"),
                        2, GUI.skin.label
                    );
                    if(GUI.changed) {
                        GUI.changed = false;
                        changed = true;
                        int val;
                        if(int.TryParse(newValue, NumberStyles.HexNumber, null, out val))
                            target[col] = unchecked((byte)val);
                    }
                    string newStr = GUI.TextField(
                        new Rect(
                            labelSize.x + (columns * 1.2F + col % columns * 0.5F + 2) * height,
                            col / columns * padHeight,
                            height * 0.8F, height
                        ),
                        Byte2String(target[col]),
                        1, GUI.skin.label
                    );
                    if(GUI.changed) {
                        GUI.changed = false;
                        changed = true;
                        target[col] = newStr.Length > 0 ? unchecked((byte)newStr[0]) : (byte)0;
                    }
                }
                GUI.changed = changed;
                GUI.EndScrollView();
            }
        }

        private static string Byte2String(byte b) {
            if(b < 32) return ((char)(b | 0x2400)).ToString();
            if(b == 127) return ((char)0x2421).ToString();
            if(b > 127) return ".";
            return ((char)b).ToString();
        }
    }
}