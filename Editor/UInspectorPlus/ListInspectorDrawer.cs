using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections;
using System.Collections.Generic;

namespace JLChnToZ.EditorExtensions.UInspectorPlus {
    [CustomInspectorDrawer(typeof(IList), -2)]
    internal class ListInspectorDrawer: InspectorDrawer {
        private List<MethodPropertyDrawer> arrayContentDrawer;
        public new IList target => base.target as IList;
        private ReorderableList arrayHandler;
        private bool showListEdit;
        private readonly Type elementType;
        const float listItemPadding = 2;

        public ListInspectorDrawer(object target, Type targetType, bool shown, bool showProps, bool showPrivateFields, bool showObsolete, bool showMethods) :
            base(target, targetType, shown, showProps, showPrivateFields, showObsolete, showMethods) {
            elementType = targetType.GetGenericListType();
        }

        protected override void Draw(bool readOnly) {
            if(showListEdit = EditorGUILayout.Foldout(showListEdit, $"Edit List [{target.Count} Items]")) {
                if(arrayHandler == null) {
                    if(arrayContentDrawer == null) {
                        arrayContentDrawer = new List<MethodPropertyDrawer>();
                        for(int i = 0; i < target.Count; i++)
                            ListAddItem();
                    }
                    arrayHandler = new ReorderableList(target, elementType) {
                        headerHeight = EditorGUIUtility.singleLineHeight,
                        elementHeight = EditorGUIUtility.singleLineHeight + listItemPadding,
                        drawElementCallback = (r, i, c, d) => {
                            arrayContentDrawer[i].Value = target[i];
                            arrayContentDrawer[i].Draw(target.IsReadOnly, Helper.ScaleRect(r, offsetHeight: -listItemPadding));
                            if(arrayContentDrawer[i].Changed)
                                target[i] = arrayContentDrawer[i].Value;
                        },
                        drawHeaderCallback = r => GUI.Label(r, target.ToString(), EditorStyles.miniBoldLabel),
                        onCanAddCallback = l => !target.IsInvalid() && !target.IsFixedSize,
                        onCanRemoveCallback = l => !target.IsInvalid() && !target.IsFixedSize && l.index >= 0,
                        onAddCallback = l => {
                            ReorderableList.defaultBehaviours.DoAddButton(l);
                            ListAddItem();
                        },
                        onRemoveCallback = l => {
                            ReorderableList.defaultBehaviours.DoRemoveButton(l);
                            int lastItem = arrayContentDrawer.Count - 1;
                            arrayContentDrawer[lastItem].Dispose();
                            arrayContentDrawer.RemoveAt(lastItem);
                        }
                    };
                    arrayHandler.onCanRemoveCallback = arrayHandler.onCanAddCallback.Invoke;
                }
                EditorGUI.indentLevel++;
                arrayHandler.DoLayoutList();
                EditorGUI.indentLevel++;
            }
            base.Draw(readOnly);
        }

        private void ListAddItem(object value = null) {
            var drawer = new MethodPropertyDrawer(elementType, "", value, true, false);
            drawer.OnRequireRedraw += RequireRedraw;
            arrayContentDrawer.Add(drawer);
        }
    }
}