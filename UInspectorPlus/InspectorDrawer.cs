﻿using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityObject = UnityEngine.Object;

namespace UInspectorPlus {
    internal class InspectorDrawer {
        public object target;
        public List<IReflectorDrawer> drawer;
        private HashSet<IReflectorDrawer> removingDrawers;
        public bool shown;
        public bool isInternalType;
        public bool changed;
        public string searchText;
        public event Action OnRequireRedraw;
        private Type targetType;
        private readonly Type elementType;
        private HexEdit hexEdit;
        private List<MethodPropertyDrawer> arrayContentDrawer;
        private ReorderableList arrayHandler;
        private bool showListEdit;
        private bool allowPrivate;
        private readonly bool allowMethods;

        public InspectorDrawer(object target, Type targetType, bool shown, bool showProps, bool showPrivateFields, bool showObsolete, bool showMethods) {
            this.target = target;
            drawer = new List<IReflectorDrawer>();
            removingDrawers = new HashSet<IReflectorDrawer>();
            BindingFlags flag = BindingFlags.Static | BindingFlags.Public;
            if(target != null)
                flag |= BindingFlags.Instance;
            if(allowPrivate = showPrivateFields)
                flag |= BindingFlags.NonPublic;
            this.targetType = targetType;
            elementType = Helper.GetGenericListType(targetType);
            var fields = targetType.GetFields(flag);
            var props = !showProps ? null : targetType.GetProperties(flag).Where(prop => prop.GetIndexParameters().Length == 0).ToArray();
            isInternalType = !targetType.IsSubclassOf(typeof(MonoBehaviour)) || Attribute.IsDefined(targetType, typeof(ExecuteInEditMode));
            foreach(var field in fields)
                try {
                    if(!showObsolete && Attribute.IsDefined(field, typeof(ObsoleteAttribute)))
                        continue;
                    drawer.Add(new MethodPropertyDrawer(field, target, showPrivateFields, showObsolete) {
                        AllowReferenceMode = false
                    });
                } catch(Exception ex) {
                    Debug.LogException(ex);
                }
            if(showProps) {
                HashSet<string> blacklistedType;
                if(!Helper.blackListedTypes.TryGetValue(targetType, out blacklistedType)) {
                    Helper.blackListedTypes.Add(targetType, blacklistedType = new HashSet<string>());
                    foreach(var kvp in Helper.blackListedTypes)
                        if(kvp.Key.IsAssignableFrom(targetType))
                            blacklistedType.UnionWith(kvp.Value);
                }
                foreach(var prop in props)
                    try {
                        if(blacklistedType != null && blacklistedType.Contains(prop.Name))
                            continue;
                        if(!showObsolete && Attribute.IsDefined(prop, typeof(ObsoleteAttribute)))
                            continue;
                        drawer.Add(new MethodPropertyDrawer(prop, target, showPrivateFields, showObsolete, prop.CanRead && EditorApplication.isPlaying) {
                            AllowReferenceMode = false,
                            Updatable = isInternalType || Helper.GetState(prop, false),
                            ShowUpdatable = !isInternalType
                        });
                    } catch(Exception ex) {
                        Debug.LogException(ex);
                    }
            }
            if(allowMethods = showMethods)
                AddMethodMenu();
            foreach(var d in drawer)
                d.OnRequireRedraw += RequireRedraw;
            if(target != null)
                this.shown = Helper.GetState(target, shown);
        }

        private void AddMethodMenu() {
            ComponentMethodDrawer newDrawer = null;
            newDrawer = new ComponentMethodDrawer(target, targetType) {
                AllowPrivateFields = allowPrivate,
                OnClose = () => removingDrawers.Add(newDrawer)
            };
            drawer.Add(newDrawer);
        }

        public void Draw(bool drawHeader = true, bool readOnly = false) {
            if(drawHeader) {
                shown = EditorGUILayout.InspectorTitlebar(shown, target as UnityObject);
                if(target != null)
                    Helper.StoreState(target, shown);
                if(!shown)
                    return;
            }
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical();
            if(target is Type && GUILayout.Button(string.Format("Inspect Static Members of {0}...", target)))
                InspectorChildWindow.OpenStatic(target as Type, true, allowPrivate, false, true, false, null);
            else if(target != null && elementType != null) {
                if(targetType == typeof(byte[])) {
                    if(hexEdit == null)
                        hexEdit = new HexEdit();
                    hexEdit.data = target as byte[];
                    if(hexEdit.data != null)
                        hexEdit.DrawGUI(false, GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 3), GUILayout.ExpandHeight(true));
                } else if(showListEdit = EditorGUILayout.Foldout(showListEdit, string.Format("Edit List [{0} Items]", (target as IList).Count))) {
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
                                arrayContentDrawer.RemoveAt(0);
                            }
                        };
                        arrayHandler.onCanRemoveCallback = arrayHandler.onCanAddCallback.Invoke;
                    }
                    arrayHandler.DoLayoutList();
                }
            }
            if(removingDrawers.Count > 0) {
                drawer.RemoveAll(removingDrawers.Contains);
                removingDrawers.Clear();
            }
            foreach(var item in drawer) {
                var methodDrawer = item as ComponentMethodDrawer;
                var fieldDrawer = item as MethodPropertyDrawer;
                if(methodDrawer != null) {
                    EditorGUI.indentLevel--;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(GUIContent.none, GUILayout.Width(EditorGUIUtility.singleLineHeight));
                    EditorGUILayout.BeginVertical();
                    methodDrawer.Filter = searchText;
                    methodDrawer.Draw();
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.indentLevel++;
                } else if(item != null) {
                    if(item.Info != null && !string.IsNullOrEmpty(searchText) && item.Info.Name.IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase) < 0)
                        continue;
                    if(fieldDrawer != null)
                        fieldDrawer.Draw(readOnly);
                    else
                        item.Draw();
                    changed |= item.UpdateIfChanged();
                }
            }
            if(allowMethods) {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if(GUILayout.Button(new GUIContent("+", "Add Method / Index Properties Watcher"), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                    AddMethodMenu();
                GUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
        }

        private void ListAddItem(object value = null) {
            var drawer = new MethodPropertyDrawer(elementType, "", value, true, false);
            drawer.OnRequireRedraw += RequireRedraw;
            arrayContentDrawer.Add(drawer);
        }

        public void UpdateValues(bool updateProps) {
            if(target == null) return;
            foreach(var drawerItem in drawer) {
                var propDrawer = drawerItem as MethodPropertyDrawer;
                if(propDrawer == null)
                    continue;
                var isPropInfo = propDrawer.Info is PropertyInfo;
                if(!isInternalType && (!updateProps || !propDrawer.Updatable) && isPropInfo)
                    continue;
                propDrawer.UpdateValue();
            }
        }

        private void RequireRedraw() {
            if(target != null && OnRequireRedraw != null)
                OnRequireRedraw();
        }
    }
}
