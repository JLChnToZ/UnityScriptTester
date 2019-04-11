using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;
using UnityObject = UnityEngine.Object;

namespace UInspectorPlus {
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
        private static readonly DoGradientField doGradientField = GetDelegate<EditorGUI, DoGradientField>("GradientField");
        private delegate Gradient DoLayoutGradientField(GUIContent guiContent, Gradient gradient, params GUILayoutOption[] options);
        private static readonly DoLayoutGradientField doLayoutGradiantField = GetDelegate<EditorGUILayout, DoLayoutGradientField>("GradientField");

        
        private delegate string DoToolbarSearchField(string text, params GUILayoutOption[] options);
        private static readonly DoToolbarSearchField doToolbarSearchField = GetDelegate<EditorGUILayout, DoToolbarSearchField>("ToolbarSearchField");

        private delegate string DoToolbarDropDownSearchField(string text, string[] searchModes, ref int searchMode, params GUILayoutOption[] options);
        private static readonly DoToolbarDropDownSearchField doToolbarDropDownSearchField = GetDelegate<EditorGUILayout, DoToolbarDropDownSearchField>("ToolbarSearchField");

        private static readonly Hashtable storedState = new Hashtable();

        static Helper() {
            AddPropertyTypeMap("System.String", PropertyType.String);
            AddPropertyTypeMap("System.Boolean", PropertyType.Bool);
            AddPropertyTypeMap("System.Byte", PropertyType.Integer);
            AddPropertyTypeMap("System.SByte", PropertyType.Integer);
            AddPropertyTypeMap("System.UInt16", PropertyType.Integer);
            AddPropertyTypeMap("System.Int16", PropertyType.Integer);
            AddPropertyTypeMap("System.UInt32", PropertyType.Integer);
            AddPropertyTypeMap("System.Int32", PropertyType.Integer);
            AddPropertyTypeMap("System.UInt64", PropertyType.Long);
            AddPropertyTypeMap("System.Int64", PropertyType.Long);
            AddPropertyTypeMap("System.Single", PropertyType.Single);
            AddPropertyTypeMap("System.Double", PropertyType.Double);
            AddPropertyTypeMap("UnityEngine.Vector2, UnityEngine.dll", PropertyType.Vector2);
            AddPropertyTypeMap("UnityEngine.Vector3, UnityEngine.dll", PropertyType.Vector3);
            AddPropertyTypeMap("UnityEngine.Vector4, UnityEngine.dll", PropertyType.Vector4);
            AddPropertyTypeMap("UnityEngine.Quaternion, UnityEngine.dll", PropertyType.Quaterion);
            AddPropertyTypeMap("UnityEngine.Color, UnityEngine.dll", PropertyType.Color);
            AddPropertyTypeMap("UnityEngine.Rect, UnityEngine.dll", PropertyType.Rect);
            AddPropertyTypeMap("UnityEngine.Bounds, UnityEngine.dll", PropertyType.Bounds);
            AddPropertyTypeMap("UnityEngine.Gradient, UnityEngine.dll", PropertyType.Gradient);
            AddPropertyTypeMap("UnityEngine.AnimationCurve, UnityEngine.dll", PropertyType.Curve);
            AddPropertyTypeMap("UnityEngine.Object, UnityEngine.dll", PropertyType.Object);
            AddPropertyTypeMap("System.Collections.Generic.IList`1", PropertyType.Array);

            AddPropertyTypeMap("UnityEngine.Vector2Int, UnityEngine.dll", PropertyType.Vector2Int);
            AddPropertyTypeMap("UnityEngine.Vector3Int, UnityEngine.dll", PropertyType.Vector3Int);
            AddPropertyTypeMap("UnityEngine.RectInt, UnityEngine.dll", PropertyType.RectInt);

            // Danger properties! Do not use them or they will instanate junks
            AddBlacklistedType("UnityEngine.MeshFilter, UnityEngine.dll", "mesh");
            AddBlacklistedType("UnityEngine.Renderer, UnityEngine.dll", "material", "materials");
            AddBlacklistedType("UnityEngine.Collider, UnityEngine.dll", "material");
            AddBlacklistedType("UnityEngine.Collider2D, UnityEngine.dll", "material");
        }

        private static void AddPropertyTypeMap(string typeName, PropertyType propType) {
            Type type = Type.GetType(typeName, false, false);
            if(type != null)
                propertyTypeMapper.Add(type, propType);
        }

        private static void AddBlacklistedType(string typeName, params string[] props) {
            Type type = Type.GetType(typeName, false, false);
            if(type == null) return;
            HashSet<string> list;
            if(!blackListedTypes.TryGetValue(type, out list))
                blackListedTypes.Add(type, list = new HashSet<string>());
            list.UnionWith(props);
        }

        internal static void StoreState(object key, object value) {
            if(storedState.ContainsKey(key))
                storedState[key] = value;
            else
                storedState.Add(key, value);
        }

        internal static T GetState<T>(object key, T defaultValue = default(T)) {
            return storedState.ContainsKey(key) ? (T)storedState[key] : defaultValue;
        }

        internal static void ReadOnlyLabelField(string label, string value) {
            if(value.Contains('\r') || value.Contains('\n')) {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(value, EditorStyles.textArea);
            } else {
                EditorGUILayout.PrefixLabel(label);
                EditorGUILayout.SelectableLabel(value, EditorStyles.textField);
            }
        }

        internal static Rect ScaleRect(Rect source,
            float xScale = 0, float yScale = 0, float widthScale = 1, float heightScale = 1,
            float offsetX = 0, float offsetY = 0, float offsetWidth = 0, float offsetHeight = 0) {
            return new Rect(
                source.x + source.width * xScale + offsetX,
                source.y + source.height * yScale + offsetY,
                source.width * widthScale + offsetWidth,
                source.height * heightScale + offsetHeight
            );
        }

        internal static bool IsInstanceMember(MemberInfo member, bool defaultResult = false) {
            var field = member as FieldInfo;
            if(field != null) return !field.IsStatic;
            var method = member as MethodBase;
            var property = member as PropertyInfo;
            if(method == null && property != null) {
                if(property.CanWrite)
                    method = property.GetSetMethod();
                else if(property.CanRead)
                    method = property.GetGetMethod();
            }
            if(method != null) return !method.IsStatic;
            return defaultResult;
        }

        internal static string GetMemberName(MemberInfo member, bool simplifed = false, bool appendMemberName = true) {
            var ret = new StringBuilder();
            var props = new List<string>();
            var field = member as FieldInfo;
            var property = member as PropertyInfo;
            var method = member as MethodInfo;
            if(field != null) {
                if(!field.IsPublic)
                    props.Add(simplifed ? "P" : "Private");
                if(field.IsStatic)
                    props.Add(simplifed ? "S" : "Static");
                if(field.IsInitOnly)
                    props.Add(simplifed ? "R" : "Read Only");
                if(field.IsLiteral)
                    props.Add(simplifed ? "C" : "Constant");
            } else if(method != null) {
                if(!method.IsPublic)
                    props.Add(simplifed ? "P" : "Private");
                if(method.IsStatic)
                    props.Add(simplifed ? "S" : "Static");
            } else if(property != null) {
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
            JoinStringList(ret, props, simplifed ? "" : ", ");
            if(props.Count > 0)
                ret.Append(") ");
            if(appendMemberName)
                ret.Append(member.Name);
            return ret.ToString();
        }

        internal static StringBuilder JoinStringList(StringBuilder sb, IEnumerable<string> list, string separator) {
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
            var cValue = value.eulerAngles;
            var changed = GUI.changed;
            GUI.changed = false;
            cValue = EditorGUILayout.Vector3Field(label, cValue, options);
            if(GUI.changed) return Quaternion.Euler(cValue);
            GUI.changed = changed;
            return value;
        }

        internal static Quaternion QuaternionField(Rect position, string label, Quaternion value) {
            var cValue = value.eulerAngles;
            var changed = GUI.changed;
            GUI.changed = false;
            cValue = EditorGUI.Vector3Field(position, label, cValue);
            if(GUI.changed) return Quaternion.Euler(cValue);
            GUI.changed = changed;
            return value;
        }

        internal static object EnumField(Rect position, GUIContent label, Type type, object value) {
            GUIContent[] itemNames;
            Array itemValues;
            int val = EnumFieldPreProcess(type, value, out itemNames, out itemValues);
            int newVal = EditorGUI.Popup(position, label, val, itemNames);
            return EnumFieldPostProcess(itemValues, newVal);
        }

        internal static object EnumField(GUIContent label, Type type, object value, params GUILayoutOption[] options) {
            GUIContent[] itemNames;
            Array itemValues;
            int val = EnumFieldPreProcess(type, value, out itemNames, out itemValues);
            int newVal = EditorGUILayout.Popup(label, val, itemNames, options);
            return EnumFieldPostProcess(itemValues, newVal);
        }

        private static int EnumFieldPreProcess(Type type, object rawValue, out GUIContent[] itemNames, out Array itemValues) {
            itemNames = Array.ConvertAll(Enum.GetNames(type), x => new GUIContent(x));
            itemValues = Enum.GetValues(type);
            long val = Convert.ToInt64(rawValue);
            for(int i = 0; i < itemValues.Length; i++)
                if(Convert.ToInt64(itemValues.GetValue(i)) == val)
                    return i;
            return 0;
        }

        private static object EnumFieldPostProcess(Array itemValues, int val) {
            return itemValues.GetValue(val);
        }


        internal static object MaskedEnumField(Rect position, string label, Type type, object mask) {
            return MaskedEnumField(position, new GUIContent(label), type, mask);
        }

        internal static object MaskedEnumField(Rect position, GUIContent label, Type type, object mask) {
            string[] itemNames;
            Array itemValues;
            int val = MaskedEnumFieldPreProcess(type, mask, out itemNames, out itemValues);
            int newVal = EditorGUI.MaskField(position, label, val, itemNames);
            return MaskedEnumFieldPostProcess(type, itemValues, mask, val, newVal);
        }

        internal static object MaskedEnumField(string label, Type type, object mask, params GUILayoutOption[] options) {
            return MaskedEnumField(new GUIContent(label), type, mask, options);
        }

        internal static object MaskedEnumField(GUIContent label, Type type, object mask, params GUILayoutOption[] options) {
            string[] itemNames;
            Array itemValues;
            int val = MaskedEnumFieldPreProcess(type, mask, out itemNames, out itemValues);
            int newVal = EditorGUILayout.MaskField(label, val, itemNames, options);
            return MaskedEnumFieldPostProcess(type, itemValues, mask, val, newVal);
        }

        private static int MaskedEnumFieldPreProcess(Type type, object rawValue, out string[] itemNames, out Array itemValues) {
            itemNames = Enum.GetNames(type);
            itemValues = Enum.GetValues(type);
            int maskVal = 0;
            long value = Convert.ToInt64(rawValue), itemValue;
            for(int i = 0; i < itemValues.Length; i++) {
                itemValue = Convert.ToInt64(itemValues.GetValue(i));
                if(itemValue != 0) {
                    if((value & itemValue) != 0)
                        maskVal |= 1 << i;
                } else if(value == 0)
                    maskVal |= 1 << i;
            }
            return maskVal;
        }

        private static object MaskedEnumFieldPostProcess(Type enumType, Array itemValues, object rawValue, int maskVal, int newMaskVal) {
            int changes = maskVal ^ newMaskVal;
            long value = Convert.ToInt64(rawValue), itemValue;
            for(int i = 0; i < itemValues.Length; i++)
                if((changes & (1 << i)) != 0) {
                    itemValue = Convert.ToInt64(itemValues.GetValue(i));
                    if((newMaskVal & (1 << i)) != 0) {
                        if(itemValue == 0) {
                            rawValue = 0;
                            break;
                        }
                        value |= itemValue;
                    } else
                        value &= ~itemValue;
                }
            return Enum.ToObject(enumType, value);
        }

        internal static string StringField(GUIContent label, string value, bool readOnly, params GUILayoutOption[] options) {
            int length = value == null ? 0 : value.Length;
            if(length > 5000) {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(label, new GUIContent("Text too long to display (" + length + " characters)"));
                if(GUILayout.Button("Copy", GUILayout.ExpandWidth(false)))
                    EditorGUIUtility.systemCopyBuffer = value;
                if(!readOnly && GUILayout.Button("Paste", GUILayout.ExpandWidth(false))) {
                    value = EditorGUIUtility.systemCopyBuffer;
                    GUI.changed = true;
                }
                EditorGUILayout.EndHorizontal();
            } else {
                int lines = CountLines(value);
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
                int lines = position.height <= EditorGUIUtility.singleLineHeight ? 1 : CountLines(value);
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

        internal static Gradient GradientField(Rect position, GUIContent label, Gradient value) {
            return doGradientField(label, position, value);
        }

        internal static Gradient GradientField(GUIContent label, Gradient value, params GUILayoutOption[] options) {
            return doLayoutGradiantField(label, value, options);
        }

        internal static string ToolbarSearchField(string text, params GUILayoutOption[] options) {
            return doToolbarSearchField(text, options);
        }

        internal static string ToolbarSearchField(string text, string[] searchModes, ref int searchMode, params GUILayoutOption[] options) {
            return doToolbarDropDownSearchField(text, searchModes, ref searchMode, options);
        }

        private static void ClickObject(UnityObject obj) {
            var newClickTime = EditorApplication.timeSinceStartup;
            if(newClickTime - clickTime < 0.3 && obj != null)
                Selection.activeObject = obj;
            clickTime = newClickTime;
            EditorGUIUtility.PingObject(obj);
        }

        private static int CountLines(string str) {
            if(string.IsNullOrEmpty(str))
                return 1;
            int cursor = 0, count = 0, length = str.Length, i = -1;
            bool isCR = false;
            while(cursor < length) {
                i = str.IndexOf('\r', cursor);
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

        internal static bool IsReadOnly(MemberInfo info) {
            var fieldInfo = info as FieldInfo;
            if(fieldInfo != null)
                return fieldInfo.IsInitOnly || fieldInfo.IsLiteral;
            var propertyInfo = info as PropertyInfo;
            if(propertyInfo != null)
                return !propertyInfo.CanWrite;
            return false;
        }

        internal static bool FetchValue(MemberInfo info, object target, out object value, params object[] index) {
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

        internal static int ObjIdOrHashCode(object obj) {
            var unityObj = obj as UnityObject;
            if(unityObj != null)
                return unityObj.GetInstanceID();
            if(obj != null)
                return obj.GetHashCode();
            return 0;
        }

        internal static bool IsInterface(Type type, Type interfaceType) {
            foreach(var iType in type.GetInterfaces())
                if(iType == interfaceType || (iType.IsGenericType && iType.GetGenericTypeDefinition() == interfaceType))
                    return true;
            return false;
        }

        internal static Type GetGenericListType(Type targetType) {
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

        internal static GUIStyle GetGUIStyle(string styleName) {
            return GUI.skin.FindStyle(styleName) ?? EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).FindStyle(styleName);
        }

        internal static T GetOrDefault<T>(object value, T defaultValue = default(T)) {
            return value == null ? defaultValue : (T)value;
        }

        internal static TDelegate GetDelegate<TDelegate>(string fromTypeName, string methodName, object target = null) where TDelegate : class {
            if(fromTypeName == null)
                throw new ArgumentNullException("fromTypeName");
            Type fromType = Type.GetType(fromTypeName, false);
            if(fromType == null) return null;
            return GetDelegate<TDelegate>(fromType, methodName, target);
        }

        internal static TDelegate GetDelegate<TDelegate>(Type fromType, string methodName, object target = null) where TDelegate : class {
            if(fromType == null)
                throw new ArgumentNullException("fromType");
            if(methodName == null)
                throw new ArgumentNullException("methodName");
            Type delegateType = typeof(TDelegate);
            MethodInfo method = FindMethod(fromType, methodName, delegateType);
            if(method == null)
                return null;
            if(method.IsStatic)
                return Delegate.CreateDelegate(delegateType, method, false) as TDelegate;
            if(target == null && fromType.IsValueType)
                target = Activator.CreateInstance(fromType);
            return Delegate.CreateDelegate(delegateType, target, method, false) as TDelegate;
        }

        internal static TDelegate GetDelegate<TFrom, TDelegate>(string methodName, TFrom target = default(TFrom)) where TDelegate : class {
            if(methodName == null)
                throw new ArgumentNullException("methodName");
            Type delegateType = typeof(TDelegate);
            MethodInfo method = FindMethod(typeof(TFrom), methodName, delegateType);
            if(method == null)
                return null;
            if(method.IsStatic)
                return Delegate.CreateDelegate(delegateType, method, false) as TDelegate;
            return Delegate.CreateDelegate(delegateType, target, method, false) as TDelegate;
        }

        private static MethodInfo FindMethod(Type fromType, string methodName, Type delegateType) {
            const string NotADelegateMsg = "{0} is not a delegate.";
            const string MissingInvokeMsg =
                "Cannot determine what parameters does {0} have, " +
                "as no Invoke(...) signature found. " +
                "Perhaps this is not a valid delegate.";
            if(!delegateType.IsSubclassOf(typeof(Delegate)))
                throw new ArgumentException(string.Format(NotADelegateMsg, delegateType.Name));
            MethodInfo invokeMethod = delegateType.GetMethod("Invoke");
            if(invokeMethod == null)
                throw new ArgumentException(string.Format(MissingInvokeMsg, delegateType.Name));
            return fromType.GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance,
                null, CallingConventions.Any,
                Array.ConvertAll(invokeMethod.GetParameters(), p => p.ParameterType),
                null
            );
        }

        public static IEnumerable<Type> LooseGetTypes(Assembly assembly) {
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

        [MenuItem("Window/Inspector+")]
        public static void ShowInspectorPlus() {
            EditorWindow.GetWindow(typeof(InspectorPlus));
        }

        public static void PrintExceptionsWithInner(Exception ex) {
            do {
                Debug.LogException(ex);
                ex = ex.InnerException;
            } while(ex != null);
        }
    }
}