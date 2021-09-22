using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityObject = UnityEngine.Object;

namespace UInspectorPlus {
    internal struct TypedDrawer: IEquatable<TypedDrawer> {
        public readonly Type drawerType;
        public readonly Type targetType;
        public readonly int priority;

        public TypedDrawer(Type drawerType, Type targetType, int priority) {
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
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CustomInspectorDrawerAttribute: Attribute {
        public Type TargetType { get; private set; }
        public int Priority { get; private set; }

        public CustomInspectorDrawerAttribute(Type targetType, int priority = 0) {
            TargetType = targetType;
            Priority = priority;
        }

        internal TypedDrawer ToDrawer(Type drawerType) => new TypedDrawer(drawerType, TargetType, Priority);

        internal static CustomInspectorDrawerAttribute[] GetAttributes(Type type) =>
            GetCustomAttributes(type, typeof(CustomInspectorDrawerAttribute)) as CustomInspectorDrawerAttribute[] ??
            new CustomInspectorDrawerAttribute[0];
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
                if(type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(InspectorDrawer)))
                    typedDrawers.UnionWith(CustomInspectorDrawerAttribute.GetAttributes(type).Select(drawer => drawer.ToDrawer(type)));
        }


        public static InspectorDrawer GetDrawer(object target, Type targetType, bool shown, bool showProps, bool showPrivateFields, bool showObsolete, bool showMethods) {
            int lastPriority = int.MinValue;
            Type drawerType = null;
            try {
                foreach(var kv in typedDrawers)
                    if(kv.targetType.IsAssignableFrom(targetType) && kv.priority > lastPriority)
                        drawerType = kv.drawerType;
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
            BindingFlags flag = BindingFlags.Static | BindingFlags.Public;
            if(!Helper.IsInvalid(target))
                flag |= BindingFlags.Instance;
            if(allowPrivate = showPrivateFields)
                flag |= BindingFlags.NonPublic;
            this.targetType = targetType;
            var fields = targetType.GetFields(flag);
            var props = !showProps ? null : targetType.GetProperties(flag).Where(prop => prop.GetIndexParameters().Length == 0).ToArray();
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
                        bool isInternalType = Helper.IsInternalType(prop.DeclaringType);
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
            if(!Helper.IsInvalid(target))
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
                if(!Helper.IsInvalid(target))
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
                if(GUILayout.Button(new GUIContent("+", "Add Method / Index Properties Watcher"), EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
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
            if(Helper.IsInvalid(target)) return;
            foreach(var drawerItem in drawer) {
                var propDrawer = drawerItem as MethodPropertyDrawer;
                if(propDrawer == null)
                    continue;
                var isPropInfo = propDrawer.Info is PropertyInfo;
                if(!Helper.IsInternalType(propDrawer.Info.DeclaringType) && (!updateProps || !propDrawer.Updatable) && isPropInfo)
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
            if(!Helper.IsInvalid(target) && OnRequireRedraw != null)
                OnRequireRedraw();
        }
    }
}
