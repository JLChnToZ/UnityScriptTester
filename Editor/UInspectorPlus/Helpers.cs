using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityObject = UnityEngine.Object;

namespace JLChnToZ.EditorExtensions.UInspectorPlus {
    internal enum PropertyType {
        Unknown,
        Bool,
        Enum,
        Integer,
        Long,
        Single,
        Double,
        Vector2,
        Vector3,
        Vector4,
        Quaterion,
        Color,
        Rect,
        Bounds,
        Gradient,
        Curve,
        String,
        Object,
        Array,

        Vector2Int,
        Vector3Int,
        RectInt,
    }

    internal enum MethodMode {
        Method,
        Constructor,
        Indexer
    }

    internal struct ComponentMethod {
        public MethodMode mode;
        public MemberInfo member;
        public object target;
    }

    internal struct ComponentFields {
        public FieldInfo field;
        public PropertyInfo property;
        public UnityObject target;
    }

    internal interface IReflectorDrawer {
        void Draw();
        bool UpdateIfChanged();
        bool UpdateValue();
        bool AllowPrivateFields { get; set; }
        bool AllowObsolete { get; set; }
        bool Changed { get; }
        object Value { get; }
        MemberInfo Info { get; }
        event Action OnRequireRedraw;
    }

    public static class Helper {
        internal static readonly Dictionary<Type, PropertyType> propertyTypeMapper = new Dictionary<Type, PropertyType>();
        internal static readonly Dictionary<Type, HashSet<string>> blackListedTypes = new Dictionary<Type, HashSet<string>>();
        private static double clickTime;

        // Delegate hacks to access the internal methods
        private delegate Gradient DoGradientField(GUIContent guiContent, Rect rect, Gradient gradient);
        private static readonly DoGradientField doGradientField = GetDelegate<DoGradientField>(typeof(EditorGUI), "GradientField");
        private delegate Gradient DoLayoutGradientField(GUIContent guiContent, Gradient gradient, params GUILayoutOption[] options);
        private static readonly DoLayoutGradientField doLayoutGradiantField = GetDelegate<DoLayoutGradientField>(typeof(EditorGUILayout), "GradientField");


        private delegate string DoToolbarSearchField(string text, params GUILayoutOption[] options);
        private static readonly DoToolbarSearchField doToolbarSearchField = GetDelegate<DoToolbarSearchField>(typeof(EditorGUILayout), "ToolbarSearchField");

        private delegate string DoToolbarDropDownSearchField(string text, string[] searchModes, ref int searchMode, params GUILayoutOption[] options);
        private static readonly DoToolbarDropDownSearchField doToolbarDropDownSearchField = GetDelegate<DoToolbarDropDownSearchField>(typeof(EditorGUILayout), "ToolbarSearchField");

        private static readonly Hashtable storedState = new Hashtable();

        static Helper() {
            AddPropertyTypeMap<string>(PropertyType.String);
            AddPropertyTypeMap<bool>(PropertyType.Bool);
            AddPropertyTypeMap<byte>(PropertyType.Integer);
            AddPropertyTypeMap<sbyte>(PropertyType.Integer);
            AddPropertyTypeMap<ushort>(PropertyType.Integer);
            AddPropertyTypeMap<short>(PropertyType.Integer);
            AddPropertyTypeMap<uint>(PropertyType.Long);
            AddPropertyTypeMap<int>(PropertyType.Integer);
            AddPropertyTypeMap<ulong>(PropertyType.Long);
            AddPropertyTypeMap<long>(PropertyType.Long);
            AddPropertyTypeMap<float>(PropertyType.Single);
            AddPropertyTypeMap<double>(PropertyType.Double);
            AddPropertyTypeMap<Vector2>(PropertyType.Vector2);
            AddPropertyTypeMap<Vector3>(PropertyType.Vector3);
            AddPropertyTypeMap<Vector4>(PropertyType.Vector4);
            AddPropertyTypeMap<Quaternion>(PropertyType.Quaterion);
            AddPropertyTypeMap<Color>(PropertyType.Color);
            AddPropertyTypeMap<Rect>(PropertyType.Rect);
            AddPropertyTypeMap<Bounds>(PropertyType.Bounds);
            AddPropertyTypeMap<Gradient>(PropertyType.Gradient);
            AddPropertyTypeMap<AnimationCurve>(PropertyType.Curve);
            AddPropertyTypeMap<UnityObject>(PropertyType.Object);
            AddPropertyTypeMap(typeof(IList<>), PropertyType.Array);

            AddPropertyTypeMap<Vector2Int>(PropertyType.Vector2Int);
            AddPropertyTypeMap<Vector3Int>(PropertyType.Vector3Int);
            AddPropertyTypeMap<RectInt>(PropertyType.RectInt);

            // Danger properties! Do not use them or they will instanate junks
            AddBlacklistedType<MeshFilter>(nameof(MeshFilter.mesh));
            AddBlacklistedType<Renderer>(nameof(Renderer.material), nameof(Renderer.materials));
            AddBlacklistedType<Collider>(nameof(Collider.material));
        }

        private static void AddPropertyTypeMap<T>(PropertyType propType) => AddPropertyTypeMap(typeof(T), propType);

        private static void AddPropertyTypeMap(Type type, PropertyType propType) {
            if(type != null) propertyTypeMapper.Add(type, propType);
        }

        private static void AddBlacklistedType<T>(params string[] props) => AddBlacklistedType(typeof(T), props);

        private static void AddBlacklistedType(Type type, params string[] props) {
            if(type != null) blackListedTypes.GetOrConstruct(type).UnionWith(props);
        }

        internal static void StoreState(object key, object value) {
            if(storedState.ContainsKey(key))
                storedState[key] = value;
            else
                storedState.Add(key, value);
        }

        internal static T GetState<T>(object key, T defaultValue = default) =>
            storedState.ContainsKey(key) ? (T)storedState[key] : defaultValue;

        internal static void ReadOnlyLabelField(string label, string value) {
            if(value.Contains('\r') || value.Contains('\n')) {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(value, EditorStyles.textArea);
            } else {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(value, EditorStyles.textField);
            }
        }

        internal static Rect ScaleRect(this Rect source,
            float xScale = 0, float yScale = 0, float widthScale = 1, float heightScale = 1,
            float offsetX = 0, float offsetY = 0, float offsetWidth = 0, float offsetHeight = 0) => new Rect(
            source.x + source.width * xScale + offsetX,
            source.y + source.height * yScale + offsetY,
            source.width * widthScale + offsetWidth,
            source.height * heightScale + offsetHeight
        );

        internal static bool IsInstanceMember(this MemberInfo member, bool defaultResult = false) {
            var field = member as FieldInfo;
            if(field != null) return !field.IsStatic;
            var method = member as MethodBase;
            if(method == null && member is PropertyInfo property) {
                if(property.CanWrite)
                    method = property.GetSetMethod();
                else if(property.CanRead)
                    method = property.GetGetMethod();
            }
            if(method != null) return !method.IsStatic;
            return defaultResult;
        }

        internal static string GetMemberName(this MemberInfo member, bool simplifed = false, bool appendMemberName = true) {
            var ret = new StringBuilder();
            var props = new List<string>();
            if(member is FieldInfo field) {
                if(!field.IsPublic)
                    props.Add(simplifed ? "P" : "Private");
                if(field.IsStatic)
                    props.Add(simplifed ? "S" : "Static");
                if(field.IsInitOnly)
                    props.Add(simplifed ? "R" : "Read Only");
                if(field.IsLiteral)
                    props.Add(simplifed ? "C" : "Constant");
            } else if(member is MethodInfo method) {
                if(!method.IsPublic)
                    props.Add(simplifed ? "P" : "Private");
                if(method.IsStatic)
                    props.Add(simplifed ? "S" : "Static");
            } else if(member is PropertyInfo property) {
                if(property.CanRead && property.CanWrite)
                    props.Add(simplifed ? "RW" : "Read Write");
                if(property.CanRead && (method = property.GetGetMethod()) != null) {
                    if(!property.CanWrite)
                        props.Add(simplifed ? "R" : "Read Only");
                    if(!method.IsPublic)
                        props.Add(simplifed ? "Pg" : "Private Get");
                    if(method.IsStatic)
                        props.Add(simplifed ? "Sg" : "Static Get");
                }
                if(property.CanWrite && (method = property.GetSetMethod()) != null) {
                    if(!property.CanRead)
                        props.Add(simplifed ? "W" : "Write Only");
                    if(!method.IsPublic)
                        props.Add(simplifed ? "Ps" : "Private Set");
                    if(method.IsStatic)
                        props.Add(simplifed ? "Ss" : "Static Set");
                }
            }
            if(props.Count > 0)
                ret.Append("(");
            ret.JoinStringList(props, simplifed ? "" : ", ");
            if(props.Count > 0)
                ret.Append(") ");
            if(appendMemberName)
                ret.Append(member.Name);
            return ret.ToString();
        }

        internal static StringBuilder JoinStringList(this StringBuilder sb, IEnumerable<string> list, string separator) {
            if(sb == null)
                sb = new StringBuilder();
            bool nonFirst = false;
            foreach(var item in list) {
                if(nonFirst)
                    sb.Append(separator);
                sb.Append(item);
                nonFirst = true;
            }
            return sb;
        }

        internal static Quaternion QuaternionField(string label, Quaternion value, params GUILayoutOption[] options) {
            var cValue = new Vector4(value.x, value.y, value.z, value.w);
            var changed = GUI.changed;
            GUI.changed = false;
            cValue = EditorGUILayout.Vector4Field(label, cValue, options);
            if(GUI.changed) return new Quaternion(cValue.x, cValue.y, cValue.z, cValue.w);
            if(changed) GUI.changed = true;
            return value;
        }

        internal static Quaternion QuaternionField(Rect position, string label, Quaternion value) {
            var cValue = new Vector4(value.x, value.y, value.z, value.w);
            var changed = GUI.changed;
            GUI.changed = false;
            cValue = EditorGUI.Vector4Field(position, label, cValue);
            if(GUI.changed) return new Quaternion(cValue.x, cValue.y, cValue.z, cValue.w).normalized;
            if(changed) GUI.changed = true;
            return value;
        }

        internal static Quaternion EulerField(string label, Quaternion value, params GUILayoutOption[] options) {
            var cValue = value.eulerAngles;
            var changed = GUI.changed;
            GUI.changed = false;
            cValue = EditorGUILayout.Vector3Field(label, cValue, options);
            if(GUI.changed) return Quaternion.Euler(cValue);
            if(changed) GUI.changed = true;
            return value;
        }

        internal static Quaternion EulerField(Rect position, string label, Quaternion value) {
            var cValue = value.eulerAngles;
            var changed = GUI.changed;
            GUI.changed = false;
            cValue = EditorGUI.Vector3Field(position, label, cValue);
            if(GUI.changed) return Quaternion.Euler(cValue);
            if(changed) GUI.changed = true;
            return value;
        }


        internal static string StringField(GUIContent label, string value, bool readOnly, params GUILayoutOption[] options) {
            int length = value == null ? 0 : value.Length;
            if(length > 5000) {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(label, new GUIContent($"Text too long to display ({length} characters)"));
                if(GUILayout.Button("Copy", GUILayout.ExpandWidth(false)))
                    EditorGUIUtility.systemCopyBuffer = value;
                if(!readOnly && GUILayout.Button("Paste", GUILayout.ExpandWidth(false))) {
                    value = EditorGUIUtility.systemCopyBuffer;
                    GUI.changed = true;
                }
                EditorGUILayout.EndHorizontal();
            } else {
                int lines = value.CountLines();
                if(lines > 1) {
                    var _opts = options.ToList();
                    _opts.Add(GUILayout.Height(EditorGUIUtility.singleLineHeight * lines));
                    _opts.Add(GUILayout.MaxWidth(EditorGUIUtility.currentViewWidth));
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.PrefixLabel(label);
                    if(readOnly)
                        EditorGUILayout.SelectableLabel(value, EditorStyles.textArea, _opts.ToArray());
                    else
                        value = EditorGUILayout.TextArea(value, _opts.ToArray());
                    EditorGUILayout.EndVertical();
                } else {
                    if(readOnly) {
                        var _opts = options.ToList();
                        _opts.Add(GUILayout.Height(EditorGUIUtility.singleLineHeight));
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.PrefixLabel(label);
                        EditorGUILayout.SelectableLabel(value, EditorStyles.textField, _opts.ToArray());
                        EditorGUILayout.EndHorizontal();
                    } else
                        value = EditorGUILayout.TextField(label, value, options);
                }
            }
            return value;
        }

        internal static string StringField(Rect position, GUIContent label, string value, bool readOnly) {
            if(readOnly) {
                EditorGUI.SelectableLabel(position, value);
            } else {
                int lines = position.height <= EditorGUIUtility.singleLineHeight ? 1 : value.CountLines();
                if(lines > 1)
                    EditorGUI.PrefixLabel(ScaleRect(position, heightScale: 0, offsetHeight: EditorGUIUtility.singleLineHeight), new GUIContent(label));
                value = lines > 1 ?
                    EditorGUI.TextArea(ScaleRect(position, offsetY: EditorGUIUtility.singleLineHeight, offsetHeight: -EditorGUIUtility.singleLineHeight), value) :
                    EditorGUI.TextField(position, label, value);
            }
            return value;
        }

        internal static UnityObject ObjectField(GUIContent label, UnityObject value, Type objectType, bool allowScreenObjs, bool readOnly, params GUILayoutOption[] options) {
            if(!readOnly)
                return EditorGUILayout.ObjectField(label, value, objectType, allowScreenObjs, options);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(label);
            var _opts = options.ToList();
            _opts.Add(GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if(GUILayout.Button(EditorGUIUtility.ObjectContent(value, objectType), EditorStyles.objectField, _opts.ToArray()))
                ClickObject(value);
            EditorGUILayout.EndHorizontal();
            return value;
        }

        internal static UnityObject ObjectField(Rect position, GUIContent label, UnityObject value, Type objectType, bool allowScreenObjs, bool readOnly) {
            if(!readOnly)
                return EditorGUI.ObjectField(position, label, value, objectType, allowScreenObjs);
            EditorGUI.PrefixLabel(ScaleRect(position, widthScale: 0.5F), label);
            if(GUI.Button(ScaleRect(position, 0.5F, widthScale: 0.5F), EditorGUIUtility.ObjectContent(value, objectType), EditorStyles.objectField))
                ClickObject(value);
            return value;
        }

        internal static Gradient GradientField(Rect position, GUIContent label, Gradient value) =>
            doGradientField(label, position, value);

        internal static Gradient GradientField(GUIContent label, Gradient value, params GUILayoutOption[] options) =>
            doLayoutGradiantField(label, value, options);

        internal static string ToolbarSearchField(string text, params GUILayoutOption[] options) =>
            doToolbarSearchField(text, options);

        internal static string ToolbarSearchField(string text, string[] searchModes, ref int searchMode, params GUILayoutOption[] options) =>
            doToolbarDropDownSearchField(text, searchModes, ref searchMode, options);

        private static void ClickObject(UnityObject obj) {
            var newClickTime = EditorApplication.timeSinceStartup;
            if(newClickTime - clickTime < 0.3 && obj != null)
                Selection.activeObject = obj;
            clickTime = newClickTime;
            EditorGUIUtility.PingObject(obj);
        }

        private static int CountLines(this string str) {
            if(string.IsNullOrEmpty(str))
                return 1;
            int cursor = 0, count = 0, length = str.Length;
            bool isCR = false;
            while(cursor < length) {
                int i = str.IndexOf('\r', cursor);
                if(i >= 0) {
                    count++;
                    isCR = true;
                    cursor = i + 1;
                    continue;
                }
                i = str.IndexOf('\n', cursor);
                if(i >= 0) {
                    if(!isCR || i != 0)
                        count++;
                    isCR = false;
                    cursor = i + 1;
                    continue;
                }
                break;
            }
            return Math.Max(1, count);
        }

        internal static bool AssignValue(MemberInfo info, object target, object value, params object[] index) {
            try {
                var fieldInfo = info as FieldInfo;
                var propertyInfo = info as PropertyInfo;
                if(fieldInfo != null && !fieldInfo.IsInitOnly && !fieldInfo.IsLiteral)
                    fieldInfo.SetValue(target, value);
                else if(propertyInfo != null && propertyInfo.CanWrite)
                    propertyInfo.SetValue(target, value, index);
                else
                    return false;
            } catch {
                return false;
            }
            return true;
        }

        internal static bool IsReadOnly(this MemberInfo info) {
            var fieldInfo = info as FieldInfo;
            if(fieldInfo != null)
                return fieldInfo.IsInitOnly || fieldInfo.IsLiteral;
            var propertyInfo = info as PropertyInfo;
            if(propertyInfo != null)
                return !propertyInfo.CanWrite;
            return false;
        }

        internal static bool FetchValue(this MemberInfo info, object target, out object value, params object[] index) {
            value = null;
            try {
                var fieldInfo = info as FieldInfo;
                var propertyInfo = info as PropertyInfo;
                if(fieldInfo != null)
                    value = fieldInfo.GetValue(target);
                else if(propertyInfo != null && propertyInfo.CanRead)
                    value = propertyInfo.GetValue(target, index);
                else
                    return false;
            } catch(Exception ex) {
                value = ex;
                return false;
            }
            return true;
        }

        internal static int ObjIdOrHashCode(this object obj) {
            var unityObj = obj as UnityObject;
            if(unityObj != null)
                return unityObj.GetInstanceID();
            if(obj != null)
                return obj.GetHashCode();
            return 0;
        }

        internal static bool IsInterface(this Type type, Type interfaceType) {
            foreach(var iType in type.GetInterfaces())
                if(iType == interfaceType || (iType.IsGenericType && iType.GetGenericTypeDefinition() == interfaceType))
                    return true;
            return false;
        }

        internal static Type GetGenericListType(this Type targetType) {
            if(targetType.IsArray)
                return targetType.GetElementType();
            bool hasNonGeneric = false;
            foreach(Type type in targetType.GetInterfaces()) {
                if(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>))
                    return type.GetGenericArguments()[0];
                if(type == typeof(IList))
                    hasNonGeneric = true;
            }
            return hasNonGeneric ? typeof(object) : null;
        }

        internal static GUIStyle GetGUIStyle(string styleName) => GUI.skin.FindStyle(styleName) ??
            EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).FindStyle(styleName);

        internal static T GetOrDefault<T>(object value, T defaultValue = default) {
            if (value == null) return defaultValue;
            try {
                return (T)Convert.ChangeType(value, typeof(T));
            } catch {}
            try {
                return (T)value;
            } catch {}
            return defaultValue;
        }

        internal static T GetOrConstruct<T>(object value) where T : new() => value == null ? new T() : (T)value;

        internal static TValue GetOrConstruct<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) where TValue : new() {
            if (!dict.TryGetValue(key, out var value)) {
                value = new TValue();
                if (!dict.IsReadOnly) dict.Add(key, value);
            }
            return value;
        }

        internal static TDelegate GetDelegate<TDelegate>(Type fromType, string methodName) where TDelegate : Delegate =>
            Delegate.CreateDelegate(typeof(TDelegate), fromType, methodName, false, false) as TDelegate;

        // Special checker to deal with "null" UnityEngine.Object (Internally null, but still exists in Mono heap)
        internal static bool IsInvalid(this object obj) => obj is UnityObject uObj ? !uObj : obj == null;

        internal static bool IsInternalType(this Type type) => !(
            type.IsSubclassOf(typeof(MonoBehaviour)) ||
            type.IsSubclassOf(typeof(StateMachineBehaviour))
        ) || Attribute.IsDefined(type, typeof(ExecuteInEditMode));

        public static IEnumerable<Type> LooseGetTypes(this Assembly assembly) {
            for(int retries = 0; retries < 2; retries++)
                try {
                    return assembly.GetTypes();
                } catch(ReflectionTypeLoadException typeLoadEx) {
                    // Retry first!
                    if(retries < 1) continue;
                    // Some types don't like to be loaded, then ignore them.
                    return from type in typeLoadEx.Types
                           where type != null
                           select type;
                }
            return null; // Should not reach here
        }

        [MenuItem("Window/JLChnToZ/Inspector+")]
        public static void ShowInspectorPlus() => EditorWindow.GetWindow(typeof(InspectorPlus));

        public static void PrintExceptionsWithInner(Exception ex) {
            do {
                Debug.LogException(ex);
                ex = ex.InnerException;
            } while(ex != null);
        }
    }
}