using UnityEngine;
using UnityEditor;
using UnityEditor.AnimatedValues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.EditorExtensions.UInspectorPlus {
    internal class ComponentMethodDrawer: IReflectorDrawer, IDisposable {
        private object component;
        private readonly List<ComponentMethod> methods = new List<ComponentMethod>();
        private AnimBool showMethodOptions;
        private AnimBool showMethodSelector;
        private AnimBool showResultSelector;
        private string[] methodNames;
        private int selectedMethodIndex;
        private MemberInfo selectedMember;
        private ParameterInfo[] parameterInfo;
        private MethodPropertyDrawer[] parameters;
        private MethodPropertyDrawer result;
        private Exception thrownException;
        private string filter;
        private readonly Type ctorType, targetType;
        private bool titleFolded = true, paramsFolded = true, resultFolded = true,
            drawHeader = true, privateFields = true, obsolete = true;
        private MethodMode mode = 0;

        public bool execButton = true;

        public event Action OnRequireRedraw;

        public GenericMenu.MenuFunction OnClose;

        public bool ShouldDrawHeader {
            get => drawHeader;
            set {
                drawHeader = value;
                paramsFolded &= value;
                resultFolded &= value;
            }
        }

        public bool Changed => false;

        public object Value => result?.Value;

        public bool AllowPrivateFields {
            get => privateFields;
            set {
                privateFields = value;
                InitComponentMethods(false);
            }
        }

        public bool AllowObsolete {
            get => obsolete;
            set {
                obsolete = value;
                InitComponentMethods(false);
            }
        }

        public MemberInfo Info => selectedMember;

        public bool IsComponentNull => component == null;

        public ComponentMethodDrawer() {
            showMethodSelector = new AnimBool(false);
            showMethodOptions = new AnimBool(false);
            showResultSelector = new AnimBool(false);
            showMethodSelector.valueChanged.AddListener(RequireRedraw);
            showMethodOptions.valueChanged.AddListener(RequireRedraw);
            showResultSelector.valueChanged.AddListener(RequireRedraw);
        }

        public ComponentMethodDrawer(object target, Type type = null)
            : this() {
            component = target;
            targetType = type ?? target.GetType();
            drawHeader = false;
            showMethodSelector.value = true;
            InitComponentMethods();
        }

        public ComponentMethodDrawer(Type type)
            : this() {
            mode = MethodMode.Constructor;
            ctorType = type;
            drawHeader = false;
            showMethodSelector.value = true;
            InitComponentMethods();
        }

        public string Filter {
            get { return filter; }
            set {
                if(filter == value) return;
                filter = value;
                InitComponentMethods(false);
            }
        }

        public void Call() {
            if (selectedMember == null || parameters == null)
                return;
            switch (mode) {
                case MethodMode.Constructor: if (ctorType == null) return; break;
                case MethodMode.Method: if (component == null && selectedMember.IsInstanceMember()) return; break;
                default: if (component == null) return; break;
            }
            try {
                thrownException = null;
                var requestData = Array.ConvertAll(parameters, d => d.Value);
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
                        result = new MethodPropertyDrawer(
                            selectedMember as PropertyInfo,
                            component, privateFields, obsolete, true,
                            requestData) {
                            OnEdit = () => result = null,
                            OnClose = OnClose,
                        };
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
                for (int i = 0, l = Math.Min(parameters.Length, requestData.Length); i < l; i++) {
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
                titleFolded = EditorGUILayout.InspectorTitlebar(titleFolded, component as UnityObject);
            }
            GUI.changed = false;
            if (titleFolded || !drawHeader) {
                if (drawHeader) {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.BeginVertical();
                    component = EditorGUILayout.ObjectField("Target", component as UnityObject, typeof(UnityObject), true);
                }
                if (mode != MethodMode.Indexer || result == null)
                    EditorGUILayout.Space();
                if (mode == MethodMode.Constructor ||
                    (mode == MethodMode.Method && (component != null || !selectedMember.IsInstanceMember())) ||
                    (mode == MethodMode.Indexer && component != null && result == null)) {
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
                if (execButton)
                    DrawExecButton();
            }
        }

        public bool UpdateIfChanged() {
            if (mode == MethodMode.Indexer && result != null)
                return result.UpdateIfChanged();
            return false;
        }

        public bool UpdateValue() {
            if (mode == MethodMode.Indexer && result != null)
                return result.UpdateValue();
            return false;
        }

        public void Dispose() {
            if (parameters != null)
                foreach (var parameter in parameters)
                    parameter.Dispose();
            result?.Dispose();
        }

        private bool FilterMemberInfo(MemberInfo m) {
            return (obsolete || !Attribute.IsDefined(m, typeof(ObsoleteAttribute))) &&
                (string.IsNullOrEmpty(filter) ||
                m.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        private void AddComponentMethod(Type type) {
            BindingFlags flag = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
            if (component != null)
                flag |= BindingFlags.Instance;
            if (privateFields)
                flag |= BindingFlags.NonPublic;
            methods.AddRange(
                from m in type.GetConstructors(flag)
                where FilterMemberInfo(m)
                select new ComponentMethod {
                    member = m,
                    mode = MethodMode.Constructor,
                }
            );
            flag &= ~BindingFlags.Instance;
            methods.AddRange(
                from m in type.GetMethods(flag)
                where FilterMemberInfo(m) && m.ReturnType == type
                select new ComponentMethod {
                    member = m,
                    mode = MethodMode.Method,
                }
            );
        }

        private void AddComponentMethod(object target, Type type = null) {
            BindingFlags flag = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
            if (privateFields)
                flag |= BindingFlags.NonPublic;
            if (target != null)
                flag |= BindingFlags.Instance;
            if (type == null)
                type = target.GetType();
            methods.AddRange(
                from m in type.GetProperties(flag)
                where FilterMemberInfo(m) && m.GetIndexParameters().Length > 0
                select new ComponentMethod {
                    member = m,
                    target = target,
                    mode = MethodMode.Indexer,
                }
            );
            methods.AddRange(
                from m in type.GetMethods(flag)
                where FilterMemberInfo(m)
                select new ComponentMethod {
                    member = m,
                    target = target,
                    mode = MethodMode.Method,
                }
            );
        }

        private void InitComponentMethods(bool resetIndex = true) {
            methods.Clear();
            switch (mode) {
                case MethodMode.Constructor:
                    AddComponentMethod(ctorType);
                    methodNames = methods.Select((m, i) => GetMethodNameFormatted(m, i)).ToArray();
                    break;
                default:
                    AddComponentMethod(component, targetType);
                    break;
            }
            if (drawHeader) {
                var gameObject = component as GameObject;
                if (gameObject != null)
                    foreach (var c in gameObject.GetComponents(typeof(Component)))
                        AddComponentMethod(c);
                methodNames = methods.Select((m, i) => $"{m.target.GetType().Name} ({m.target.ObjIdOrHashCode()})/{GetMethodNameFormatted(m, i)}").ToArray();
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

        private string GetMethodNameFormatted(ComponentMethod m, int i) {
            string name, formatStr;
            ParameterInfo[] parameters;
            switch (m.mode) {
                case MethodMode.Constructor:
                    name = "[[Constructor]]";
                    break;
                case MethodMode.Indexer:
                    name = null;
                    break;
                case MethodMode.Method:
                default:
                    name = m.member.GetMemberName().Replace('_', ' ');
                    break;
            }
            switch (m.mode) {
                case MethodMode.Indexer:
                    parameters = (m.member as PropertyInfo).GetIndexParameters();
                    formatStr = "[{1}]";
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

        private void InitMethodParams() {
            selectedMember = methods[selectedMethodIndex].member;
            switch (mode = methods[selectedMethodIndex].mode) {
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

        private void DrawComponent() {
            if (OnClose != null)
                EditorGUILayout.BeginHorizontal();
            selectedMethodIndex = EditorGUILayout.Popup(mode.ToString(), selectedMethodIndex, methodNames);
            if (OnClose != null) {
                if (GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Minus"), EditorStyles.miniLabel, GUILayout.ExpandWidth(false)))
                    OnClose();
                EditorGUILayout.EndHorizontal();
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

        private void DrawMethod() {
            switch (mode) {
                case MethodMode.Constructor:
                    paramsFolded = EditorGUILayout.Foldout(paramsFolded, "Constructor");
                    break;
                case MethodMode.Indexer:
                    paramsFolded = EditorGUILayout.Foldout(paramsFolded, "Indexer");
                    break;
                case MethodMode.Method:
                default:
                    paramsFolded = EditorGUILayout.Foldout(paramsFolded, selectedMember.Name);
                    break;
            }
            if (paramsFolded) {
                GUI.changed = false;
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical();
                if (mode != MethodMode.Indexer && (selectedMember as MethodInfo).ContainsGenericParameters)
                    EditorGUILayout.HelpBox("Generic method is not supported.", MessageType.Warning);
                if (mode != MethodMode.Indexer && component == null && selectedMember.IsInstanceMember())
                    EditorGUILayout.HelpBox("Method requires an exists instance.", MessageType.Warning);
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

        private void DrawResult() {
            if (mode == MethodMode.Indexer) {
                if (result != null)
                    result.Draw(false);
                if (thrownException != null)
                    EditorGUILayout.HelpBox(thrownException.Message, MessageType.Error);
            } else if (resultFolded = EditorGUILayout.Foldout(resultFolded, "Result")) {
                GUI.changed = false;
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginVertical();
                switch (mode) {
                    case MethodMode.Constructor: break;
                    case MethodMode.Method:
                    default:
                        if (result != null)
                            result.Draw(true);
                        break;
                }
                if (thrownException != null)
                    EditorGUILayout.HelpBox(thrownException.Message, MessageType.Error);
                EditorGUILayout.EndVertical();
                EditorGUI.indentLevel--;
            }
        }

        private void DrawExecButton() {
            if (selectedMember == null) return;
            bool execute;
            switch (mode) {
                case MethodMode.Constructor:
                    execute = GUILayout.Button("Construct");
                    break;
                case MethodMode.Indexer:
                    if (result != null) return;
                    execute = GUILayout.Button("Create Property");
                    break;
                case MethodMode.Method:
                default:
                    execute = GUILayout.Button($"Execute {selectedMember.Name}");
                    break;
            }
            if (execute)
                Call();
        }

        private void RequireRedraw() => OnRequireRedraw?.Invoke();
    }
}