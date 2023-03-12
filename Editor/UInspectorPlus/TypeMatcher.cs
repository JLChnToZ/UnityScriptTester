using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using System.Threading;

namespace JLChnToZ.EditorExtensions.UInspectorPlus {
    internal class NamespacedType {
        private static bool isRefreshing = true;
        private static AppDomain currentDomain;
        public static readonly NamespacedType root = new NamespacedType("global::");
        private static readonly HashSet<Type> allTypes = new HashSet<Type>();
        private static readonly List<Assembly> pendingAssemblies = new List<Assembly>();
        private Dictionary<string, NamespacedType> subNamespaces;
        private List<Type> types;

        public readonly string namespaceName;

        public static ICollection<Type> AllTypes => allTypes;

        public ICollection<NamespacedType> SubNamespaces => subNamespaces.Values;

        public ICollection<Type> Types => types.AsReadOnly();

        static NamespacedType() {
            new Thread(Init) { IsBackground = true }.Start();
        }

        public static void Touch() {} // Ensure the class has instructed to initialize (Actual procedure is in static constructor)

        private NamespacedType(string name) {
            namespaceName = name;
            subNamespaces = new Dictionary<string, NamespacedType>();
            types = new List<Type>();
        }

        private static void Init() {
            if (currentDomain == null) {
                currentDomain = AppDomain.CurrentDomain;
                AddTypes(
                    from assembly in currentDomain.GetAssemblies()
                    from type in assembly.LooseGetTypes()
                    select type
                );
                currentDomain.AssemblyLoad += OnAssemblyLoad;
                currentDomain.DomainUnload += OnAppDomainUnload;
            }
            Refresh();
        }

        private static void Refresh() {
            if (pendingAssemblies.Count > 0) {
                var buffer = pendingAssemblies.ToArray();
                pendingAssemblies.Clear();
                AddTypes(
                    from assembly in buffer
                    from type in assembly.LooseGetTypes()
                    select type
                );
            }
            isRefreshing = false;
        }

        private static void AddTypes(IEnumerable<Type> types) {
            foreach (var type in types) {
                if (!allTypes.Add(type)) continue;
                var current = root;
                var ns = type.Namespace;
                if (!string.IsNullOrEmpty(ns))
                    foreach (var path in ns.Split('.')) {
                        if (!current.subNamespaces.TryGetValue(path, out var child))
                            current.subNamespaces.Add(path, child = new NamespacedType(path));
                        current = child;
                    }
                current.types.Add(type);
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e) {
            pendingAssemblies.Add(e.LoadedAssembly);
            if (!isRefreshing) {
                isRefreshing = true;
                new Thread(Refresh) { IsBackground = true }.Start();
            }
        }

        private static void OnAppDomainUnload(object sender, EventArgs e) {
            currentDomain = null;
        }
    }

    internal class TypeMatcher: IDisposable {
        public Thread bgWorker;
        public event Action OnRequestRedraw;
        public event Action<Type> OnSelected;
        private string searchText = string.Empty;
        private Type[] searchTypeResult = null;
        public Type genericType;
        float maxWidth;

        public string SearchText {
            get => searchText;
            set {
                if (searchText == value) return;
                searchText = value;
                if (bgWorker == null)
                    bgWorker = new Thread(DoSearch) {
                        IsBackground = true,
                    };
                if (!bgWorker.IsAlive)
                    bgWorker.Start();
            }
        }

        public int SearchResultCount => searchTypeResult?.Length ?? 0;

        public float Width => maxWidth;

        static TypeMatcher() => NamespacedType.Touch();

        public void Draw() {
            if (searchTypeResult == null || searchTypeResult.Length == 0) return;
            GUIContent temp = new GUIContent();
            GUILayout.BeginVertical();
            for (int i = 0; i < Math.Min(searchTypeResult.Length, 500); i++) {
                Type type = searchTypeResult[i];
                temp.text = type.FullName;
                temp.tooltip = type.AssemblyQualifiedName;
                maxWidth = Mathf.Max(maxWidth, EditorStyles.boldLabel.CalcSize(temp).x);
                if (GUILayout.Button(temp, EditorStyles.linkLabel))
                    OnSelected?.Invoke(type);
            }
            if (searchTypeResult.Length > 500)
                EditorGUILayout.HelpBox(
                    "Too many results, please try more specific search phase.",
                    MessageType.Warning
                );
            GUILayout.Space(8);
            GUILayout.EndVertical();
        }
        

        private void DoSearch() {
            try {
                var searchText = this.searchText;
                while (true) {
                    Thread.Sleep(100);
                    List<Type> searchTypeResult = new List<Type>();
                    if (!string.IsNullOrEmpty(searchText))
                        foreach (Type type in NamespacedType.AllTypes) {
                            if (searchText != this.searchText) break;
                            if (type.AssemblyQualifiedName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 && Helper.IsTypeRseolvable(genericType, type))
                                searchTypeResult.Add(type);
                        }
                    if (searchText == this.searchText) {
                        this.searchTypeResult = searchTypeResult.ToArray();
                        break;
                    } else {
                        searchText = this.searchText;
                    }
                }
                EditorApplication.update += RequestRedraw;
            } catch (Exception ex) {
                Helper.PrintExceptionsWithInner(ex);
            } finally {
                bgWorker = null;
            }
        }

        private void RequestRedraw() {
            EditorApplication.update -= RequestRedraw;
            if (OnRequestRedraw != null) OnRequestRedraw.Invoke();
        }

        public void Dispose() {
            try {
                if (bgWorker != null && bgWorker.IsAlive)
                    bgWorker.Abort();
            } catch {
            } finally {
                bgWorker = null;
            }
        }

        public bool ShowPopup() {
            var searchTreeEntries = new List<SearchTreeEntry> {
                new SearchTreeGroupEntry(new GUIContent("Types")),
            };
            var typeIconContent = EditorGUIUtility.IconContent("Assembly Icon");
            var stack = new Stack<(Queue<NamespacedType>, ICollection<Type>)>();
            stack.Push((new Queue<NamespacedType>(NamespacedType.root.SubNamespaces), NamespacedType.root.Types));
            while (stack.Count > 0) {
                var (pendingChildren, types) = stack.Peek();
                if (pendingChildren.Count > 0) {
                    var child = pendingChildren.Dequeue();
                    searchTreeEntries.Add(new SearchTreeGroupEntry(new GUIContent(child.namespaceName), stack.Count));
                    stack.Push((new Queue<NamespacedType>(child.SubNamespaces), child.Types));
                    continue;
                }
                foreach (var type in types) {
                    if (!Helper.IsTypeRseolvable(genericType, type)) continue;
                    var objContent = type.IsSubclassOf(typeof(UnityEngine.Object)) ? EditorGUIUtility.ObjectContent(null, type) : typeIconContent;
                    searchTreeEntries.Add(new SearchTreeEntry(new GUIContent(objContent) { text = type.Name }) {
                        level = stack.Count,
                        userData = type,
                    });
                }
                stack.Pop();
            }
            return SearchWindowProvider.OpenSearchWindow(searchTreeEntries, o => OnSelected?.Invoke(o as Type));
        }
  }

    internal class TypeResolverGUI {
        public readonly Type srcType;
        private readonly MethodInfo srcMethod;

        private readonly Type[] constraints;
        private readonly TypeResolverGUI[] subGUI;
        private readonly Type[] resolvedTypes;

        public bool IsReady {
            get {
                for (int i = 0; i < resolvedTypes.Length; i++) {
                    Type entry = resolvedTypes[i];
                    if (entry == null || (entry.ContainsGenericParameters && (subGUI[i] == null || !subGUI[i].IsReady)))
                        return false;
                }
                return true;
            }
        }

        public Type ResolvedType => IsReady ? srcType?.MakeGenericType(Resolve()) : null;

        public MethodInfo ResolvedMethod => IsReady ? srcMethod?.MakeGenericMethod(Resolve()) : null;

        public TypeResolverGUI(Type type) {
            NamespacedType.Touch();
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
            for (int i = 0; i < constraints.Length; i++) {
                var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight, EditorStyles.textField);
                rect = EditorGUI.PrefixLabel(rect, new GUIContent(constraints[i].Name));
                if (GUI.Button(rect, resolvedTypes[i] != null ? $"T: {resolvedTypes[i].FullName}" : "", EditorStyles.textField)) {
                    int index = i;
                    var typeMatcher = new TypeMatcher {
                        genericType = constraints[i],
                    };
                    typeMatcher.OnSelected += type => {
                        resolvedTypes[index] = type;
                        subGUI[index] = null;
                    };
                    typeMatcher.ShowPopup();
                }
                if (resolvedTypes[i] != null && resolvedTypes[i].ContainsGenericParameters) {
                    if (subGUI[i] == null) subGUI[i] = new TypeResolverGUI(resolvedTypes[i]);
                    EditorGUI.indentLevel++;
                    subGUI[i].Draw();
                    EditorGUI.indentLevel--;
                }
            }
        }

        Type[] Resolve() {
            for (int i = 0; i < resolvedTypes.Length; i++)
                if (subGUI[i] != null) resolvedTypes[i] = subGUI[i].ResolvedType;
            return resolvedTypes;
        }
    }
}