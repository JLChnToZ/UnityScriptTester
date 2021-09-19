using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections;
using System.Collections.Generic;

namespace UInspectorPlus {
    internal class ListInspectorDrawer: InspectorDrawer {
        private List<MethodPropertyDrawer> arrayContentDrawer;
        private ReorderableList arrayHandler;
        private bool showListEdit;
        private readonly Type elementType;

        static ListInspectorDrawer() => RegisterCustomInspectorDrawer<ListInspectorDrawer>(typeof(IList), -2);

        public ListInspectorDrawer(object target, Type targetType, bool shown, bool showProps, bool showPrivateFields, bool showObsolete, bool showMethods) :
            base(target, targetType, shown, showProps, showPrivateFields, showObsolete, showMethods) {
            elementType = Helper.GetGenericListType(targetType);
        }

        protected override void Draw(bool readOnly) {
            if(showListEdit = EditorGUILayout.Foldout(showListEdit, string.Format("Edit List [{0} Items]", (target as IList).Count))) {
                if(arrayHandler == null) {
                    if(arrayContentDrawer == null) {
                        arrayContentDrawer = new List<MethodPropertyDrawer>();
                        for(int i = 0; i < (target as IList).Count; i++)
                            ListAddItem();
                    }
                    arrayHandler = new ReorderableList(target as IList, elementType) {
                        headerHeight = EditorGUIUtility.singleLineHeight,
                        elementHeight = EditorGUIUtility.singleLineHeight + 2,
                        drawElementCallback = (r, i, c, d) => {
                            arrayContentDrawer[i].Value = (target as IList)[i];
                            arrayContentDrawer[i].Draw(false, Helper.ScaleRect(r, offsetHeight: -2));
                            if(arrayContentDrawer[i].Changed)
                                (target as IList)[i] = arrayContentDrawer[i].Value;
                        },
                        drawHeaderCallback = r => GUI.Label(r, target.ToString(), EditorStyles.miniBoldLabel),
                        onCanAddCallback = l => target != null && !(target as IList).IsFixedSize,
                        onAddCallback = l => {
                            ReorderableList.defaultBehaviours.DoAddButton(l);
                            ListAddItem();
                        },
                        onRemoveCallback = l => {
                            ReorderableList.defaultBehaviours.DoRemoveButton(l);
                            arrayContentDrawer[0].Dispose();
                            arrayContentDrawer.RemoveAt(0);
                        }
                    };
                    arrayHandler.onCanRemoveCallback = arrayHandler.onCanAddCallback.Invoke;
                }
                arrayHandler.DoLayoutList();
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
