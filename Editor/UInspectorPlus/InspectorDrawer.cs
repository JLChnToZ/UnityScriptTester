using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.EditorExtensions.UInspectorPlus {
    internal struct TypedDrawer: IEquatable<TypedDrawer> {
        private static readonly TypedDrawer[] emptyDrawers = new TypedDrawer[0];
        public readonly Type drawerType;
        public readonly Type targetType;
        public readonly int priority;

        private TypedDrawer(Type drawerType, Type targetType, int priority) {
            this.drawerType = drawerType;
            this.targetType = targetType;
            this.priority = priority;
        }

        public override int GetHashCode() {
            return drawerType.GetHashCode() ^ targetType.GetHashCode();
        }

        public override bool Equals(object obj) => obj is TypedDrawer other && Equals(other);

        public bool Equals(TypedDrawer other) =>
            drawerType.Equals(other.drawerType) &&
            targetType.Equals(other.targetType);

        internal static TypedDrawer[] Of(Type type) {
            if (type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(InspectorDrawer))) {
                var attributes = Attribute.GetCustomAttributes(type, typeof(CustomInspectorDrawerAttribute)) as CustomInspectorDrawerAttribute[];
                if (attributes != null && attributes.Length > 0) {
                    var drawers = new TypedDrawer[attributes.Length];
                    for (int i = 0; i < attributes.Length; i++)
                        drawers[i] = new TypedDrawer(type, attributes[i].TargetType, attributes[i].Priority);
                    return drawers;
                }
            }
            return emptyDrawers;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CustomInspectorDrawerAttribute: Attribute {
        public Type TargetType { get; private set; }
        public int Priority { get; private set; }

        public CustomInspectorDrawerAttribute(Type targetType, int priority = 0) {
            TargetType = targetType;
            Priority = priority;
        }
    }

    public class InspectorDrawer: IDisposable {
        private static readonly HashSet<TypedDrawer> typedDrawers = new HashSet<TypedDrawer>();
        public object target;
        internal readonly List<IReflectorDrawer> drawer = new List<IReflectorDrawer>();
        private readonly HashSet<IReflectorDrawer> removingDrawers = new HashSet<IReflectorDrawer>();
        public bool shown;
        public bool changed;
        public string searchText;
        public event Action OnRequireRedraw;
        protected Type targetType;
        protected bool allowPrivate;
        protected readonly bool allowMethods;

        static InspectorDrawer() {
            var currentDomain = AppDomain.CurrentDomain;
            currentDomain.AssemblyLoad += OnAssemblyLoad;
            foreach(var assembly in currentDomain.GetAssemblies())
                RegisterInspectorDrawers(assembly);
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args) => RegisterInspectorDrawers(args.LoadedAssembly);

        private static void RegisterInspectorDrawers(Assembly assembly) {
            foreach(var type in assembly.GetTypes())
                typedDrawers.UnionWith(TypedDrawer.Of(type));
        }

        public static InspectorDrawer GetDrawer(object target, Type targetType, bool shown, bool showProps, bool showPrivateFields, bool showObsolete, bool showMethods) {
            int lastPriority = int.MinValue;
            Type drawerType = null;
            try {
                foreach(var kv in typedDrawers)
                    if(kv.targetType.IsAssignableFrom(targetType) && kv.priority > lastPriority) {
                        drawerType = kv.drawerType;
                        lastPriority = kv.priority;
                    }
                if(drawerType != null)
                    return Activator.CreateInstance(drawerType, target, targetType, shown, showProps, showPrivateFields, showObsolete, showMethods) as InspectorDrawer;
            } catch(Exception ex) {
                Debug.LogException(ex);
                Debug.LogWarning($"Failed to instaniate drawer {drawerType.Name}, will fall back to default drawer.");
            }
            return new InspectorDrawer(target, targetType, shown, showProps, showPrivateFields, showObsolete, showMethods);
        }

        public InspectorDrawer(object target, Type targetType, bool shown, bool showProps, bool showPrivateFields, bool showObsolete, bool showMethods) {
            this.target = target;
            BindingFlags flag = BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy;
            if(!target.IsInvalid())
                flag |= BindingFlags.Instance;
            if(allowPrivate = showPrivateFields)
                flag |= BindingFlags.NonPublic;
            this.targetType = targetType;
            var fields = targetType.GetFields(flag);
            var props = showProps ? targetType.GetProperties(flag).Where(prop => prop.GetIndexParameters().Length == 0).ToArray() : null;
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
                if(!Helper.blackListedTypes.TryGetValue(targetType, out var blacklistedType)) {
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
                        bool isInternalType = prop.DeclaringType.IsInternalType();
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
            if(!target.IsInvalid())
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
                if(!target.IsInvalid())
                    Helper.StoreState(target, shown);
                if(!shown)
                    return;
            }
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical();
            DrawRequestRefs();
            Draw(readOnly);
            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
            if(removingDrawers.Count > 0) {
                foreach(var drawer in removingDrawers)
                    if(drawer is IDisposable disposable)
                        disposable.Dispose();
                drawer.RemoveAll(removingDrawers.Contains);
                removingDrawers.Clear();
            }
        }

        protected virtual void Draw(bool readOnly) {
            foreach(var item in drawer) {
                if(item is ComponentMethodDrawer methodDrawer) {
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
                    if(item is MethodPropertyDrawer fieldDrawer)
                        fieldDrawer.Draw(readOnly);
                    else
                        item.Draw();
                    changed |= item.UpdateIfChanged();
                }
            }
            if(allowMethods) {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if(GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Plus", "Add Method / Index Properties Watcher"), EditorStyles.miniLabel, GUILayout.ExpandWidth(false)))
                    AddMethodMenu();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawRequestRefs() {
            MethodPropertyDrawer removal = null;
            foreach(var drawer in MethodPropertyDrawer.drawerRequestingReferences)
                if(drawer.requiredType.IsAssignableFrom(targetType) &&
                    GUILayout.Button($"Assign this object to {drawer.name}")) {
                    drawer.Value = target;
                    drawer.SetDirty();
                    removal = drawer;
                }
            if(removal != null) MethodPropertyDrawer.drawerRequestingReferences.Remove(removal);
        }

        public virtual void UpdateValues(bool updateProps) {
            if(target.IsInvalid()) return;
            foreach(var drawerItem in drawer) {
                if(!(drawerItem is MethodPropertyDrawer propDrawer))
                    continue;
                var isPropInfo = propDrawer.Info is PropertyInfo;
                if(!propDrawer.Info.DeclaringType.IsInternalType() && (!updateProps || !propDrawer.Updatable) && isPropInfo)
                    continue;
                propDrawer.UpdateValue();
            }
        }

        public virtual void Dispose() {
            foreach(var d in drawer)
                if(d is IDisposable disposable)
                    disposable.Dispose();
            drawer.Clear();
        }

        protected void RequireRedraw() {
            if(!target.IsInvalid() && OnRequireRedraw != null)
                OnRequireRedraw();
        }
    }
}