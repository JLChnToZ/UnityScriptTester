using UnityEngine;
using UnityEditor;
using UnityEditor.AnimatedValues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityObject = UnityEngine.Object;

namespace UInspectorPlus {
    class ComponentMethodDrawer: IReflectorDrawer {
        object component;
        readonly List<ComponentMethod> methods = new List<ComponentMethod>();
        AnimBool showMethodOptions;
        AnimBool showMethodSelector;
        AnimBool showResultSelector;
        string[] methodNames;
        int selectedMethodIndex;
        MemberInfo selectedMember;
        //ConstructorInfo selectedCtor { get { return selectedMember as ConstructorInfo; } }
        //MethodInfo selectedMethod { get { return selectedMember as MethodInfo; } }
        //PropertyInfo selectedIndexer { get { return selectedMember as PropertyInfo; } }
        ParameterInfo[] parameterInfo;
        MethodPropertyDrawer[] parameters;
        MethodPropertyDrawer result;
        Exception thrownException;
        string filter;
        Type ctorType;
        bool titleFolded = true, paramsFolded = true, resultFolded = true,
            drawHeader = true, privateFields = true, obsolete = true;
        MethodMode mode = 0;

        public event Action OnRequireRedraw;

        public bool ShouldDrawHeader {
            get { return drawHeader; }
            set {
                drawHeader = value;
                paramsFolded &= value;
                resultFolded &= value;
            }
        }

        public bool Changed {
            get { return false; }
        }

        public object Value {
            get { return result == null ? null : result.Value; }
        }

        public bool AllowPrivateFields {
            get {
                return privateFields;
            }
            set {
                privateFields = value;
                InitComponentMethods(false);
            }
        }

        public bool AllowObsolete {
            get {
                return obsolete;
            }
            set {
                obsolete = value;
                InitComponentMethods(false);
            }
        }

        public MemberInfo Info {
            get {
                return selectedMember;
            }
        }

        public bool IsComponentNull() {
            return component == null;
        }

        public ComponentMethodDrawer() {
            showMethodSelector = new AnimBool(false);
            showMethodOptions = new AnimBool(false);
            showResultSelector = new AnimBool(false);
            showMethodSelector.valueChanged.AddListener(RequireRedraw);
            showMethodOptions.valueChanged.AddListener(RequireRedraw);
            showResultSelector.valueChanged.AddListener(RequireRedraw);
        }

        public ComponentMethodDrawer(object target)
            : this() {
            component = target;
            drawHeader = false;
            showMethodSelector.value = true;
            InitComponentMethods();
        }

        public ComponentMethodDrawer(Type type)
            : this() {
            mode = MethodMode.Constructor; ;
            ctorType = type;
            drawHeader = false;
            showMethodSelector.value = true;
            InitComponentMethods();
        }

        public string Filter {
            get { return filter; }
            set {
                filter = value;
                InitComponentMethods(false);
            }
        }

        public void Call() {
            if (selectedMember == null || parameters == null)
                return;
            switch (mode) {
                case MethodMode.Constructor: if (ctorType == null) return; break;
                case MethodMode.Method: default: if (component == null) return; break;
            }
            try {
                thrownException = null;
                var requestData = parameters.Select(d => d.Value).ToArray();
                object returnData;
                switch (mode) {
                    case MethodMode.Constructor:
                        returnData = (selectedMember as ConstructorInfo).Invoke(requestData);
                        result = new MethodPropertyDrawer(
                            selectedMember.ReflectedType,
                            "Constructed object",
                            returnData,
                            privateFields,
                            obsolete);
                        break;
                    case MethodMode.Indexer:
                        returnData = (selectedMember as PropertyInfo).GetValue(component, requestData);
                        result = new MethodPropertyDrawer(
                            (selectedMember as PropertyInfo).PropertyType,
                            Helper.JoinStringList(null, requestData.Select(o => o.ToString()), ", ").ToString(),
                            returnData,
                            privateFields,
                            obsolete);
                        break;
                    case MethodMode.Method:
                    default:
                        returnData = (selectedMember as MethodInfo).Invoke(component, requestData);
                        result = (selectedMember as MethodInfo).ReturnType == typeof(void) ?
                        null :
                        new MethodPropertyDrawer(
                            (selectedMember as MethodInfo).ReturnType,
                            "Return data",
                            returnData,
                            privateFields,
                            obsolete);
                        break;
                }
                for (int i = 0; i < Math.Min(parameters.Length, requestData.Length); i++) {
                    parameters[i].Value = requestData[i];
                    if (parameters[i].ReferenceMode)
                        Helper.AssignValue(parameters[i].RefFieldInfo, parameters[i].Component, requestData[i]);
                }
            } catch (Exception ex) {
                thrownException = ex.InnerException ?? ex;
                Debug.LogException(thrownException);
                throw;
            }
        }

        public void Draw() {
            if (drawHeader) {
                EditorGUI.BeginDisabledGroup(component == null);
                titleFolded = EditorGUILayout.InspectorTitlebar(titleFolded, component as UnityObject) || component == null;
                EditorGUI.EndDisabledGroup();
            }
            GUI.changed = false;
            if (component == null || titleFolded || !drawHeader) {
                if (drawHeader) {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.BeginVertical();
                    component = EditorGUILayout.ObjectField("Target", component as UnityObject, typeof(UnityObject), true);
                }
                if (component != null || mode == MethodMode.Constructor) {
                    if (GUI.changed) {
                        InitComponentMethods();
                        GUI.changed = false;
                    }
                    showMethodSelector.target = true;
                } else
                    showMethodSelector.target = false;
                if (EditorGUILayout.BeginFadeGroup(showMethodSelector.faded))
                    DrawComponent();
                EditorGUILayout.EndFadeGroup();
                showResultSelector.target = (mode != MethodMode.Constructor && result != null) || thrownException != null;
                if (EditorGUILayout.BeginFadeGroup(showResultSelector.faded))
                    DrawResult();
                EditorGUILayout.EndFadeGroup();
                if (drawHeader) {
                    EditorGUILayout.EndVertical();
                    EditorGUI.indentLevel--;
                }
            }
        }

        void AddComponentMethod(Type type) {
            BindingFlags flag = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
            if (privateFields)
                flag |= BindingFlags.NonPublic;
            methods.AddRange(
                type.GetConstructors(flag)
                .Where(t => obsolete || !Attribute.IsDefined(t, typeof(ObsoleteAttribute)))
                .Where(t => string.IsNullOrEmpty(filter) || t.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .Select(m => new ComponentMethod {
                    member = m,
                    mode = MethodMode.Constructor,
                })
            );
            methods.AddRange(
                type.GetProperties(flag)
                .Where(t => obsolete || !Attribute.IsDefined(t, typeof(ObsoleteAttribute)))
                .Where(t => t.GetIndexParameters().Length > 0)
                .Where(t => string.IsNullOrEmpty(filter) || t.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .Select(m => new ComponentMethod {
                    member = m,
                    mode = MethodMode.Indexer
                })
            );
            flag &= ~BindingFlags.Instance;
            methods.AddRange(
                type.GetMethods(flag)
                .Where(t => obsolete || !Attribute.IsDefined(t, typeof(ObsoleteAttribute)))
                .Where(t => t.ReturnType == type)
                .Where(t => string.IsNullOrEmpty(filter) || t.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .Select(m => new ComponentMethod {
                    member = m,
                    mode = MethodMode.Method
                })
            );
        }

        void AddComponentMethod(object target) {
            BindingFlags flag = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
            if (privateFields)
                flag |= BindingFlags.NonPublic;
            methods.AddRange(
                target.GetType().GetMethods(flag)
                .Where(t => obsolete || !Attribute.IsDefined(t, typeof(ObsoleteAttribute)))
                .Where(t => string.IsNullOrEmpty(filter) || t.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .Select(m => new ComponentMethod {
                    member = m,
                    target = target,
                    mode = MethodMode.Method,
                })
            );
        }

        void InitComponentMethods(bool resetIndex = true) {
            methods.Clear();
            switch (mode) {
                case MethodMode.Constructor:
                    AddComponentMethod(ctorType);
                    methodNames = methods.Select((m, i) => GetMethodNameFormatted(m, i)).ToArray();
                    break;
                default:
                    AddComponentMethod(component);
                    break;
            }
            if (drawHeader) {
                var gameObject = component as GameObject;
                if (gameObject != null)
                    foreach (var c in gameObject.GetComponents(typeof(Component)))
                        AddComponentMethod(c);
                methodNames = methods.Select((m, i) => string.Format(
                    "{0} ({1})/{2}",
                    m.target.GetType().Name,
                    Helper.ObjIdOrHashCode(m.target),
                    GetMethodNameFormatted(m, i)
                )).ToArray();
            } else {
                methodNames = methods.Select((m, i) => GetMethodNameFormatted(m, i)).ToArray();
            }
            if (!resetIndex && selectedMember != null) {
                selectedMethodIndex = methods.FindIndex(m => m.member == selectedMember);
                if (selectedMethodIndex >= 0)
                    return;
            }
            selectedMethodIndex = -1;
            selectedMember = null;
            parameterInfo = null;
            parameters = null;
            result = null;
            thrownException = null;
        }

        string GetMethodNameFormatted(ComponentMethod m, int i) {
            string name, formatStr;
            ParameterInfo[] parameters;
            switch(m.mode) {
                case MethodMode.Constructor:
                    name = "[Constructor]";
                    break;
                case MethodMode.Indexer:
                    name = m.member.DeclaringType.Name;
                    break;
                case MethodMode.Method:
                default:
                    name = Helper.GetMemberName(m.member as MemberInfo).Replace('_', ' ');
                    break;
            }
            switch (m.mode) {
                case MethodMode.Indexer:
                    parameters = (m.member as PropertyInfo).GetIndexParameters();
                    formatStr = "{0} [{1}]";
                    break;
                case MethodMode.Constructor:
                case MethodMode.Method:
                default:
                    parameters = (m.member as MethodBase).GetParameters();
                    formatStr = "{0} ({1})";
                    break;
            }
            return string.Format(formatStr, name, Helper.JoinStringList(null, parameters.Select(x => x.ParameterType.Name), ", "));
        }

        void InitMethodParams() {
            selectedMember = methods[selectedMethodIndex].member;
            switch (methods[selectedMethodIndex].mode) {
                case MethodMode.Constructor:
                    component = null;
                    parameterInfo = (selectedMember as ConstructorInfo).GetParameters();
                    break;
                case MethodMode.Indexer:
                    component = methods[selectedMethodIndex].target;
                    parameterInfo = (selectedMember as PropertyInfo).GetIndexParameters();
                    break;
                case MethodMode.Method:
                default:
                    component = methods[selectedMethodIndex].target;
                    parameterInfo = (selectedMember as MethodInfo).GetParameters();
                    break;
            }
            parameters = new MethodPropertyDrawer[parameterInfo.Length];
            for (int i = 0; i < parameterInfo.Length; i++) {
                var info = parameterInfo[i];
                parameters[i] = new MethodPropertyDrawer(info, privateFields, obsolete);
                parameters[i].OnRequireRedraw += RequireRedraw;
            }
            result = null;
            thrownException = null;
        }

        void DrawComponent() {
            switch (mode) {
                case MethodMode.Constructor:
                    selectedMethodIndex = EditorGUILayout.Popup("Constructor", selectedMethodIndex, methodNames);
                    break;
                case MethodMode.Method:
                    selectedMethodIndex = EditorGUILayout.Popup("Method", selectedMethodIndex, methodNames);
                    break;
            }
            if (selectedMethodIndex >= 0) {
                if (GUI.changed) {
                    InitMethodParams();
                    GUI.changed = false;
                }
                showMethodOptions.target = true;
            } else
                showMethodOptions.target = false;
            if (EditorGUILayout.BeginFadeGroup(showMethodOptions.faded))
                DrawMethod();
            EditorGUILayout.EndFadeGroup();
        }

        void DrawMethod() {
            switch(mode) {
                case MethodMode.Constructor:
                    paramsFolded = EditorGUILayout.Foldout(paramsFolded, "Constructor");
                    break;
                case MethodMode.Method:
                    paramsFolded = EditorGUILayout.Foldout(paramsFolded, selectedMember.Name);
                    break;
                case MethodMode.Indexer:
                    paramsFolded = EditorGUILayout.Foldout(paramsFolded, selectedMember.DeclaringType.Name);
                    break;
            }
            if (paramsFolded) {
                GUI.changed = false;
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical();
                if (mode != MethodMode.Indexer && (selectedMember as MethodInfo).ContainsGenericParameters)
                    EditorGUILayout.HelpBox("Generic method is not supported.", MessageType.Warning);
                else {
                    if (parameterInfo.Length == 0)
                        EditorGUILayout.HelpBox("There is no parameters required for this method.", MessageType.Info);
                    foreach (var drawer in parameters)
                        drawer.Draw();
                }
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
        }

        void DrawResult() {
            if (resultFolded = EditorGUILayout.Foldout(resultFolded, "Result")) {
                GUI.changed = false;
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical();
                switch (mode) {
                    case MethodMode.Constructor: break;
                    case MethodMode.Method:
                        if (result != null)
                            result.Draw(true);
                        break;
                    case MethodMode.Indexer:
                        // TODO: Indxer Handling
                        break;
                }
                if (thrownException != null)
                    EditorGUILayout.HelpBox(thrownException.Message, MessageType.Error);
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
        }

        void RequireRedraw() {
            if (OnRequireRedraw != null)
                OnRequireRedraw();
        }
    }
}
