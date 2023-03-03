using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System.Threading;

namespace JLChnToZ.EditorExtensions.UInspectorPlus {
    internal class TypeMatcher: IDisposable {
        public Thread bgWorker;
        public event Action OnRequestRedraw;
        public event Action<Type> OnSelected;
        private readonly HashSet<Type> searchedTypes = new HashSet<Type>();
        private readonly List<Assembly> pendingAssemblies = new List<Assembly>();
        private AppDomain currentDomain;
        private string searchText = string.Empty;
        private Type[] searchTypeResult = null;
        public Type genericType;
        float maxWidth;

        public string SearchText {
            get => searchText;
            set {
                if(searchText == value) return;
                searchText = value;
                if(bgWorker == null)
                    bgWorker = new Thread(DoSearch) {
                        IsBackground = true,
                    };
                if(!bgWorker.IsAlive)
                    bgWorker.Start();
            }
        }

        public int SearchResultCount => searchTypeResult?.Length ?? 0;

        public float Width => maxWidth;

        public void Draw() {
            if(searchTypeResult == null || searchTypeResult.Length == 0) return;
            GUIContent temp = new GUIContent();
            GUILayout.BeginVertical();
            /*
            GUILayout.Space(8);
            GUILayout.Label(
                $"Type Search Result ({searchTypeResult.Length}):",
                EditorStyles.boldLabel
            );
            GUILayout.Space(8);
            */
            for(int i = 0; i < Math.Min(searchTypeResult.Length, 500); i++) {
                Type type = searchTypeResult[i];
                temp.text = type.FullName;
                temp.tooltip = type.AssemblyQualifiedName;
                maxWidth = Mathf.Max(maxWidth, EditorStyles.boldLabel.CalcSize(temp).x);
                if(GUILayout.Button(temp, EditorStyles.linkLabel))
                    OnSelected?.Invoke(type);
            }
            if(searchTypeResult.Length > 500)
                EditorGUILayout.HelpBox(
                    "Too many results, please try more specific search phase.",
                    MessageType.Warning
                );
            GUILayout.Space(8);
            GUILayout.EndVertical();
        }

        private void InitSearch() {
            if(currentDomain == null) {
                currentDomain = AppDomain.CurrentDomain;
                searchedTypes.UnionWith(
                    from assembly in currentDomain.GetAssemblies()
                    from type in assembly.LooseGetTypes()
                    select type
                );
                currentDomain.AssemblyLoad += OnAssemblyLoad;
                currentDomain.DomainUnload += OnAppDomainUnload;
            }
            if(pendingAssemblies.Count > 0) {
                var buffer = pendingAssemblies.ToArray();
                pendingAssemblies.Clear();
                searchedTypes.UnionWith(
                    from assembly in buffer
                    from type in assembly.LooseGetTypes()
                    select type
                );
            }
        }

        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e) {
            pendingAssemblies.Add(e.LoadedAssembly);
        }

        private void OnAppDomainUnload(object sender, EventArgs e) {
            currentDomain = null;
        }

        ~TypeMatcher() => Dispose();

        private void DoSearch() {
            try {
                InitSearch();
                var searchText = this.searchText;
                while(true) {
                    Thread.Sleep(100);
                    List<Type> searchTypeResult = new List<Type>();
                    if(!string.IsNullOrEmpty(searchText))
                        foreach(Type type in searchedTypes) {
                            if(searchText != this.searchText) break;
                            if(type.AssemblyQualifiedName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 && IsTypeRseolvable(type))
                                searchTypeResult.Add(type);
                        }
                    if(searchText == this.searchText) {
                        this.searchTypeResult = searchTypeResult.ToArray();
                        break;
                    } else {
                        searchText = this.searchText;
                    }
                }
                EditorApplication.update += RequestRedraw;
            } catch(Exception ex) {
                Helper.PrintExceptionsWithInner(ex);
            } finally {
                bgWorker = null;
            }
        }

        private void RequestRedraw() {
            EditorApplication.update -= RequestRedraw;
            if(OnRequestRedraw != null) OnRequestRedraw.Invoke();
        }

        private bool IsTypeRseolvable(Type type) {
            if(genericType == null) return true;
            if(genericType.IsGenericParameter) {
                var attributes = genericType.GenericParameterAttributes;

                if((
                    attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint) &&
                    type.IsValueType
                ) || (
                    attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint) &&
                    !type.IsValueType
                ) || (
                    attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) &&
                    type.GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, CallingConventions.HasThis, Helper.EmptyTypes, null) == null
                )) return false;
                foreach(var constrarint in genericType.GetGenericParameterConstraints())
                    if(!constrarint.IsAssignableFrom(type)) return false;
                return true;
            }
            return true;
        }

        public void Dispose() {
            try {
                if(currentDomain != null) {
                    currentDomain.AssemblyLoad -= OnAssemblyLoad;
                    currentDomain.DomainUnload -= OnAppDomainUnload;
                }
            } catch {
            } finally {
                currentDomain = null;
            }
            try {
                if(bgWorker != null && bgWorker.IsAlive)
                    bgWorker.Abort();
            } catch {
            } finally {
                bgWorker = null;
            }
        }
    }

    internal class TypeMatcherPopup: PopupWindowContent {
        readonly Type genericType;
        TypeMatcher typeMatcher;
        Vector2 scrollPos;

        public event Action OnRequestRedraw;

        public event Action<Type> OnSelected;

        public TypeMatcherPopup(Type genericType) {
            this.genericType = genericType;
        }

        public override Vector2 GetWindowSize() => new Vector2(
            Mathf.Max(500, typeMatcher?.Width ?? 0 + 25),
            EditorGUIUtility.singleLineHeight * Mathf.Clamp((typeMatcher?.SearchResultCount ?? 0) + 3, 5, 20)
        );

        public override void OnGUI(Rect rect) {
            if(typeMatcher == null) return;
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            typeMatcher.SearchText = Helper.ToolbarSearchField(typeMatcher.SearchText ?? string.Empty);
            EditorGUILayout.EndHorizontal();
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            typeMatcher.Draw();
            EditorGUILayout.EndScrollView();
        }

        public override void OnOpen() {
            if(typeMatcher == null) {
                typeMatcher = new TypeMatcher {
                    genericType = genericType,
                };
                if(OnRequestRedraw != null) typeMatcher.OnRequestRedraw += OnRequestRedraw;
                typeMatcher.OnSelected += Selected;
                scrollPos = Vector2.zero;
            }
        }

        public override void OnClose() {
            if(typeMatcher != null) {
                typeMatcher.Dispose();
                typeMatcher = null;
            }
        }

        void Selected(Type type) {
            OnSelected?.Invoke(type);
            editorWindow.Close();
        }
    }

    internal class TypeResolverGUI {
        private readonly Type srcType;
        private readonly MethodInfo srcMethod;

        private readonly Type[] constraints;
        private readonly TypeResolverGUI[] subGUI;
        private readonly Type[] resolvedTypes;

        public bool IsReady {
            get {
                for(int i = 0; i < resolvedTypes.Length; i++) {
                    Type entry = resolvedTypes[i];
                    if(entry == null || (entry.ContainsGenericParameters && (subGUI[i] == null || !subGUI[i].IsReady)))
                        return false;
                }
                return true;
            }
        }

        public Type ResolvedType => IsReady ? srcType?.MakeGenericType(Resolve()) : null;

        public MethodInfo ResolvedMethod => IsReady ? srcMethod?.MakeGenericMethod(Resolve()) : null;

        public TypeResolverGUI(Type type) {
            srcType = type;
            constraints = type.GetGenericArguments();
            subGUI = new TypeResolverGUI[constraints.Length];
            resolvedTypes = new Type[constraints.Length];
        }

        public TypeResolverGUI(MethodInfo method) {
            srcMethod = method;
            constraints = method.GetGenericArguments();
            subGUI = new TypeResolverGUI[constraints.Length];
            resolvedTypes = new Type[constraints.Length];
        }

        public void Draw() {
            for(int i = 0; i < constraints.Length; i++) {
                var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight, EditorStyles.textField);
                rect = EditorGUI.PrefixLabel(rect, new GUIContent(constraints[i].Name));
                if(GUI.Button(rect, resolvedTypes[i] != null ? $"T: {resolvedTypes[i].FullName}" : "", EditorStyles.textField)) {
                    int index = i;
                    var typeMatcherPopup = new TypeMatcherPopup(constraints[i]);
                    typeMatcherPopup.OnSelected += type => {
                        resolvedTypes[index] = type;
                        subGUI[index] = null;
                    };
                    PopupWindow.Show(rect, typeMatcherPopup);
                }
                if(resolvedTypes[i] != null && resolvedTypes[i].ContainsGenericParameters) {
                    if(subGUI[i] == null) subGUI[i] = new TypeResolverGUI(resolvedTypes[i]);
                    EditorGUI.indentLevel++;
                    subGUI[i].Draw();
                    EditorGUI.indentLevel--;
                }
            }
        }

        Type[] Resolve() {
            for(int i = 0; i < resolvedTypes.Length; i++)
                if(subGUI[i] != null) resolvedTypes[i] = subGUI[i].ResolvedType;
            return resolvedTypes;
        }
    }
}