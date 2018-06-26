using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityObject = UnityEngine.Object;

namespace UInspectorPlus {
    class InspectorDrawer {
        public object target;
        public List<IReflectorDrawer> drawer;
        public bool shown;
        public bool isInternalType;
        public bool changed;
        public string searchText;
        public event Action OnRequireRedraw;
        Type targetType, elementType;
        HexEdit hexEdit;
        List<MethodPropertyDrawer> arrayContentDrawer;
        ReorderableList arrayHandler;
        bool showListEdit;

        public InspectorDrawer(object target, bool shown, bool showProps, bool showPrivateFields, bool showObsolete, bool showMethods) {
            this.target = target;
            this.drawer = new List<IReflectorDrawer>();
            BindingFlags flag = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
            if (showPrivateFields)
                flag |= BindingFlags.NonPublic;
            targetType = target.GetType();
            elementType = Helper.GetGenericListType(targetType);
            var fields = targetType.GetFields(flag);
            var props = !showProps ? null : targetType.GetProperties(flag).Where(prop => prop.GetIndexParameters().Length == 0).ToArray();
            isInternalType = !(target is MonoBehaviour) || Attribute.IsDefined(target.GetType(), typeof(ExecuteInEditMode));
            foreach (var field in fields)
                try {
                    if (!showObsolete && Attribute.IsDefined(field, typeof(ObsoleteAttribute)))
                        continue;
                    drawer.Add(new MethodPropertyDrawer(field, target, showPrivateFields, showObsolete) {
                        AllowReferenceMode = false
                    });
                } catch (Exception ex) {
                    Debug.LogException(ex);
                }
            if (showProps) {
                HashSet<string> blacklistedType;
                if(!Helper.blackListedTypes.TryGetValue(targetType, out blacklistedType)) {
                    Helper.blackListedTypes.Add(targetType, blacklistedType = new HashSet<string>());
                    foreach (var kvp in Helper.blackListedTypes)
                        if (kvp.Key.IsAssignableFrom(targetType))
                            blacklistedType.UnionWith(kvp.Value);
                }
                foreach (var prop in props)
                    try {
                        if (blacklistedType != null && blacklistedType.Contains(prop.Name))
                            continue;
                        if (!showObsolete && Attribute.IsDefined(prop, typeof(ObsoleteAttribute)))
                            continue;
                        drawer.Add(new MethodPropertyDrawer(prop, target, showPrivateFields, showObsolete, prop.CanRead && EditorApplication.isPlaying) {
                            AllowReferenceMode = false,
                            Updatable = isInternalType || Helper.GetState(prop, false),
                            ShowUpdatable = !isInternalType
                        });
                    } catch (Exception ex) {
                        Debug.LogException(ex);
                    }
            }
            if (showMethods)
                drawer.Add(new ComponentMethodDrawer(target) { AllowPrivateFields = showPrivateFields });
            foreach (var d in drawer)
                d.OnRequireRedraw += RequireRedraw;
            this.shown = Helper.GetState(target, shown);
        }

        public void Draw(bool drawHeader = true, bool readOnly = false) {
            if (target == null) {
                EditorGUILayout.InspectorTitlebar(false, null as UnityObject);
                return;
            }
            if (drawHeader) {
                shown = EditorGUILayout.InspectorTitlebar(shown, target as UnityObject);
                Helper.StoreState(target, shown);
                if (!shown)
                    return;
            }
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical();
            if (elementType != null) {
                if (targetType == typeof(byte[])) {
                    if (hexEdit == null)
                        hexEdit = new HexEdit();
                    hexEdit.data = target as byte[];
                    if (hexEdit.data != null)
                        hexEdit.DrawGUI(false, GUILayout.MinHeight(EditorGUIUtility.singleLineHeight * 3), GUILayout.ExpandHeight(true));
                } else if (showListEdit = EditorGUILayout.Foldout(showListEdit, string.Format("Edit List [{0} Items]", (target as IList).Count))) {
                    if (arrayHandler == null) {
                        if (arrayContentDrawer == null) {
                            arrayContentDrawer = new List<MethodPropertyDrawer>();
                            for (int i = 0; i < (target as IList).Count; i++)
                                ListAddItem();
                        }
                        arrayHandler = new ReorderableList(target as IList, elementType);
                        arrayHandler.headerHeight = EditorGUIUtility.singleLineHeight;
                        arrayHandler.elementHeight = EditorGUIUtility.singleLineHeight + 2;
                        arrayHandler.drawElementCallback = (r, i, c, d) => {
                            arrayContentDrawer[i].Value = (target as IList)[i];
                            arrayContentDrawer[i].Draw(false, Helper.ScaleRect(r, offsetHeight: -2));
                            if (arrayContentDrawer[i].Changed)
                                (target as IList)[i] = arrayContentDrawer[i].Value;
                        };
                        arrayHandler.drawHeaderCallback = r => GUI.Label(r, target.ToString(), EditorStyles.boldLabel);
                        arrayHandler.onCanAddCallback = l => target != null && !(target as IList).IsFixedSize;
                        arrayHandler.onCanRemoveCallback = arrayHandler.onCanAddCallback.Invoke;
                        arrayHandler.onAddCallback = l => {
                            ReorderableList.defaultBehaviours.DoAddButton(l);
                            ListAddItem();
                        };
                        arrayHandler.onRemoveCallback = l => {
                            ReorderableList.defaultBehaviours.DoRemoveButton(l);
                            arrayContentDrawer.RemoveAt(0);
                        };
                    }
                    arrayHandler.DoLayoutList();
                }
            }
            foreach (var item in drawer) {
                var methodDrawer = item as ComponentMethodDrawer;
                var fieldDrawer = item as MethodPropertyDrawer;
                if (methodDrawer != null) {
                    EditorGUILayout.Space();
                    EditorGUI.indentLevel--;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(GUIContent.none, GUILayout.Width(EditorGUIUtility.singleLineHeight));
                    EditorGUILayout.BeginVertical();
                    methodDrawer.Draw();
                    if (methodDrawer.Info != null && GUILayout.Button("Execute " + methodDrawer.Info.Name))
                        methodDrawer.Call();
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                    EditorGUI.indentLevel++;
                } else if (item != null) {
                    if (item.Info != null && !string.IsNullOrEmpty(searchText) && item.Info.Name.IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase) < 0)
                        continue;
                    if (fieldDrawer != null)
                        fieldDrawer.Draw(readOnly);
                    else
                        item.Draw();
                    if (item.Changed) {
                        if (!Helper.AssignValue(item.Info, target, item.Value)) {
                            object value;
                            var propDrawer = item as MethodPropertyDrawer;
                            if (propDrawer != null) {
                                var success = Helper.FetchValue(propDrawer.Info, target, out value);
                                if (success) {
                                    propDrawer.Value = value;
                                    propDrawer.GetException = null;
                                } else
                                    propDrawer.GetException = value as Exception;
                            }
                        } else {
                            changed = true;
                        }
                    }
                }
            }
            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
        }

        void ListAddItem(object value = null) {
            var drawer = new MethodPropertyDrawer(elementType, "", value, true, false);
            drawer.OnRequireRedraw += RequireRedraw;
            arrayContentDrawer.Add(drawer);
        }

        public void UpdateValues(bool updateProps) {
            if(target == null) return;
            foreach (var drawerItem in drawer) {
                var propDrawer = drawerItem as MethodPropertyDrawer;
                if (propDrawer == null)
                    continue;
                var isPropInfo = propDrawer.Info is PropertyInfo;
                if (!isInternalType && (!updateProps || !propDrawer.Updatable) && isPropInfo)
                    continue;
                object value;
                if (Helper.FetchValue(propDrawer.Info, target, out value)) {
                    propDrawer.Value = value;
                    propDrawer.GetException = null;
                } else
                    propDrawer.GetException = value as Exception;
            }
        }

        void RequireRedraw() {
            if (target != null && OnRequireRedraw != null)
                OnRequireRedraw();
        }
    }
}
