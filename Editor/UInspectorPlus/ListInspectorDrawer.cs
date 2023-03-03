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
            if(target != null && (showListEdit = EditorGUILayout.Foldout(showListEdit, $"Edit List [{target.Count} Items]"))) {
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

    internal class ArrayConstructorDrawer: IDisposable {
        const float listItemPadding = 2;
        private readonly Type elementType;
        private readonly List<MethodPropertyDrawer> drawers = new List<MethodPropertyDrawer>();
        private readonly ReorderableList arrayHandler;
        public event Action OnRequireRedraw;

        public ArrayConstructorDrawer(Type elementType) {
            if(elementType == null) throw new ArgumentNullException(nameof(elementType));
            this.elementType = elementType;
            arrayHandler = new ReorderableList(drawers, typeof(MethodPropertyDrawer)) {
                headerHeight = EditorGUIUtility.singleLineHeight,
                elementHeightCallback = GetElementHeight,
                drawElementCallback = DrawElement,
                drawHeaderCallback = DrawTitle,
                onAddCallback = AddItem,
                onRemoveCallback = RemoveItem,
            };
        }

        private void DrawElement(Rect rect, int index, bool isChecked, bool isDisabled) =>
            drawers[index].Draw(isDisabled, Helper.ScaleRect(rect, offsetHeight: -listItemPadding));

        private void DrawTitle(Rect rect) =>
            GUI.Label(rect, $"{this.elementType.FullName}[]", EditorStyles.miniBoldLabel);

        private void AddItem(ReorderableList list) {
            var drawer = new MethodPropertyDrawer(elementType, "", null, true, false);
            drawer.OnRequireRedraw += RequireRedraw;
            drawers.Add(drawer);
        }

        private float GetElementHeight(int index) {
            return EditorGUIUtility.singleLineHeight + listItemPadding;
        }

        private void RemoveItem(ReorderableList list) {
            int selectedIndex = list.index;
            if(selectedIndex < 0) selectedIndex = drawers.Count - 1;
            drawers[selectedIndex].Dispose();
            drawers.RemoveAt(selectedIndex);
        }

        public Array ToArray() {
            int count = drawers.Count;
            var array = Array.CreateInstance(elementType, drawers.Count);
            for (int i = 0; i < count; i++)
                array.SetValue(drawers[i].Value, i);
            return array;
        }

        protected void RequireRedraw() => OnRequireRedraw?.Invoke();

        public void Dispose() {
            foreach(var entry in drawers)
                entry?.Dispose();
            drawers.Clear();
        }

        public void Draw() => arrayHandler.DoLayoutList();
    }
}
