using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityObject = UnityEngine.Object;

namespace UInspectorPlus {
    internal class MethodPropertyDrawer: IReflectorDrawer {
        public readonly string name;
        private readonly GUIContent nameContent;
        private MemberInfo memberInfo;
        public Type requiredType;
        private readonly List<PropertyType> castableTypes;
        private PropertyType currentType;
        private readonly object target;
        private object rawValue;
        private bool referenceMode;
        private int grabValueMode;

        private UnityObject component;
        private readonly List<ComponentFields> fields;
        private string[] fieldNames;
        private object[] indexParams;
        private int selectedFieldIndex;
        private FieldInfo selectedField;
        private PropertyInfo selectedProperty;
        private ComponentMethodDrawer ctorDrawer;
        private Rect menuButtonRect;
        private bool changed;
        private bool allowReferenceMode = true;
        private bool privateFields = true;
        private bool optionalPrivateFields = true;
        private bool obsolete = true;
        private bool masked;
        private bool showUpdatable;
        private bool updatable;
        private bool isInfoReadonly;
        private readonly bool isStatic;
        private readonly bool isPrivate;
        public event Action OnRequireRedraw;

        public GenericMenu.MenuFunction OnClose, OnEdit;
        private Exception getException;

        public UnityObject Component {
            get { return component; }
            set {
                component = value;
                if (selectedField != null && selectedField.DeclaringType != null && !selectedField.DeclaringType.IsInstanceOfType(component))
                    selectedField = null;
                if (selectedProperty != null && selectedField.DeclaringType != null && !selectedProperty.DeclaringType.IsInstanceOfType(component))
                    selectedProperty = null;
            }
        }

        public MemberInfo Info {
            get { return memberInfo; }
            set {
                memberInfo = value;
                if (memberInfo != null)
                    isInfoReadonly = Helper.IsReadOnly(memberInfo);
            }
        }

        public MemberInfo RefFieldInfo {
            get { return selectedField ?? selectedProperty as MemberInfo; }
            set {
                if (component == null)
                    return;
                InitFieldTypes();
                if (value is FieldInfo) {
                    selectedField = value as FieldInfo;
                    selectedFieldIndex = fields.FindIndex(field => field.field == value);
                } else if (value is PropertyInfo) {
                    selectedProperty = value as PropertyInfo;
                    selectedFieldIndex = fields.FindIndex(field => field.property == value);
                }
            }
        }

        public bool Changed {
            get { return changed; }
        }

        public bool ShowUpdatable {
            get { return showUpdatable; }
            set { showUpdatable = value; }
        }

        public bool Updatable {
            get { return updatable; }
            set { updatable = value; }
        }

        public bool AllowReferenceMode {
            get { return allowReferenceMode; }
            set {
                allowReferenceMode = value;
                if (!value && referenceMode)
                    ReferenceMode = false;
            }
        }

        public bool ReferenceMode {
            get { return referenceMode; }
            set {
                referenceMode = value && allowReferenceMode;
                fields.Clear();
                fieldNames = new string[0];
                if (referenceMode) {
                    rawValue = null;
                    InitFieldTypes();
                } else {
                    rawValue = GetReferencedValue();
                }
                selectedFieldIndex = -1;
                selectedField = null;
                selectedProperty = null;
            }
        }

        public bool AllowPrivateFields {
            get {
                return privateFields;
            }
            set {
                privateFields = value;
                if (referenceMode)
                    InitFieldTypes();
                if (ctorDrawer != null)
                    ctorDrawer.AllowPrivateFields = value;
            }
        }

        public bool AllowObsolete {
            get {
                return obsolete;
            }
            set {
                obsolete = value;
                if (referenceMode)
                    InitFieldTypes();
                if (ctorDrawer != null)
                    ctorDrawer.AllowObsolete = value;
            }
        }

        public bool OptionalPrivateFields {
            get { return optionalPrivateFields; }
            set { optionalPrivateFields = value; }
        }

        public object Value {
            get {
                if (referenceMode) {
                    rawValue = GetReferencedValue();
                }
                var convertedValue = rawValue;
                if (rawValue != null && requiredType != typeof(object) && requiredType.IsInstanceOfType(rawValue)) {
                    try {
                        convertedValue = Convert.ChangeType(rawValue, requiredType);
                    } catch {
                        convertedValue = rawValue;
                    }
                }
                changed = false;
                return convertedValue;
            }
            set {
                rawValue = value;
                changed = false;
            }
        }

        public Exception GetException {
            get { return getException; }
            set {
                getException = value;
                if (updatable)
                    Helper.StoreState(memberInfo, updatable = false);
            }
        }

        public bool IsReadOnly {
            get { return isInfoReadonly; }
        }

        public object Target {
            get { return target; }
        }

        private MethodPropertyDrawer(bool allowPrivate, bool allowObsolete) {
            castableTypes = new List<PropertyType>();
            fields = new List<ComponentFields>();
            selectedFieldIndex = -1;
            privateFields = allowPrivate;
            obsolete = allowObsolete;
        }

        public MethodPropertyDrawer(FieldInfo field, object target, bool allowPrivate, bool allowObsolete)
            : this(allowPrivate, allowObsolete) {
            memberInfo = field;
            isInfoReadonly = Helper.IsReadOnly(field);
            requiredType = field.FieldType;
            name = Helper.GetMemberName(field, true);
            nameContent = new GUIContent(name, Helper.GetMemberName(field));
            rawValue = field.GetValue(target);
            this.target = target;
            isStatic = field.IsStatic;
            isPrivate = field.IsPrivate;
            InitType();
        }

        public MethodPropertyDrawer(PropertyInfo property, object target, bool allowPrivate, bool allowObsolete, bool initValue, params object[] indexParams)
            : this(allowPrivate, allowObsolete) {
            memberInfo = property;
            isInfoReadonly = Helper.IsReadOnly(property);
            requiredType = property.PropertyType;
            if (indexParams != null && indexParams.Length > 0) {
                this.indexParams = indexParams;
                var paramList = Helper.JoinStringList(null, indexParams.Select(new Func<object, string>(Convert.ToString)), ", ").ToString();
                name = string.Format("{0}[{1}]",
                    Helper.GetMemberName(property, true, false), paramList);
                nameContent = new GUIContent(name, string.Format("{0}[{1}]",
                    Helper.GetMemberName(property, false, false), paramList));
            } else {
                name = Helper.GetMemberName(property, true);
                nameContent = new GUIContent(name, Helper.GetMemberName(property));
            }
            if (initValue) rawValue = property.GetValue(target, indexParams);
            this.target = target;
            var getMethod = property.GetGetMethod();
            isStatic = getMethod != null ? getMethod.IsStatic : false;
            isPrivate = getMethod != null ? getMethod.IsPrivate : false;
            InitType();
        }

        public MethodPropertyDrawer(ParameterInfo parameter, bool allowPrivate, bool allowObsolete)
            : this(allowPrivate, allowObsolete) {
            requiredType = parameter.ParameterType;
            name = parameter.Name;
            nameContent = new GUIContent(name, name);
            if (parameter.IsOptional)
                rawValue = parameter.DefaultValue;
            InitType();
        }

        public MethodPropertyDrawer(Type type, string name, object defaultValue, bool allowPrivate, bool allowObsolete)
            : this(allowPrivate, allowObsolete) {
            requiredType = type;
            this.name = name;
            nameContent = new GUIContent(name, name);
            rawValue = defaultValue;
            InitType();
        }

        private void InitType() {
            if (requiredType.IsArray) {
                castableTypes.Add(PropertyType.Array);
                currentType = PropertyType.Array;
                return;
            }
            if (requiredType.IsByRef)
                requiredType = requiredType.GetElementType();
            if (requiredType.IsEnum) {
                castableTypes.Add(PropertyType.Enum);
                castableTypes.Add(PropertyType.Integer);
                currentType = PropertyType.Enum;
                masked = Attribute.IsDefined(requiredType, typeof(FlagsAttribute));
                return;
            }
            foreach (var map in Helper.propertyTypeMapper) {
                if (map.Key == requiredType || requiredType.IsSubclassOf(map.Key)) {
                    castableTypes.Add(map.Value);
                    currentType = map.Value;
                    return;
                }
            }
            if (requiredType == typeof(object)) {
                castableTypes.AddRange(Enum.GetValues(typeof(PropertyType)).Cast<PropertyType>());
                castableTypes.Remove(PropertyType.Unknown);
                castableTypes.Remove(PropertyType.Enum);
                currentType = PropertyType.Object;
                return;
            }
            foreach (var map in Helper.propertyTypeMapper) {
                if (map.Key.IsAssignableFrom(requiredType) && requiredType.IsAssignableFrom(map.Key))
                    castableTypes.Add(map.Value);
            }
            currentType = castableTypes.Count > 0 ? castableTypes[0] : PropertyType.Unknown;
        }

        public void Draw() {
            Draw(false);
        }

        public void Draw(bool readOnly, Rect? rect = null) {
            readOnly |= isInfoReadonly;
            var referenceModeBtn = (!allowReferenceMode && (
                    currentType == PropertyType.Unknown ||
                    currentType == PropertyType.Object ||
                    currentType == PropertyType.Array)
                ) ||
                allowReferenceMode ||
                castableTypes.Count > 1;
            if (!rect.HasValue)
                EditorGUI.indentLevel--;
            EditorGUILayout.BeginHorizontal();
            if (rect.HasValue) {
                Rect sRect = referenceModeBtn ? Helper.ScaleRect(rect.Value, offsetWidth: -EditorGUIUtility.singleLineHeight) : rect.Value;
                if (referenceMode || grabValueMode == 1)
                    DrawReferencedField(sRect);
                else
                    DrawDirectField(readOnly, sRect);
            } else {
                if (showUpdatable) {
                    updatable = EditorGUILayout.ToggleLeft(new GUIContent("", "Update Enabled"), updatable, GUILayout.Width(EditorGUIUtility.singleLineHeight));
                    Helper.StoreState(memberInfo, updatable);
                } else
                    EditorGUILayout.LabelField(GUIContent.none, GUILayout.Width(EditorGUIUtility.singleLineHeight));
                if (referenceMode || grabValueMode == 1)
                    DrawReferencedField(null);
                else
                    DrawDirectField(readOnly, null);
            }
            if (!readOnly && referenceModeBtn) {
                if (rect.HasValue) {
                    if (GUI.Button(Helper.ScaleRect(rect.Value, 1, 0.5F, 0, 0, -EditorGUIUtility.singleLineHeight, -7.5F, 15, 15), GUIContent.none, Helper.GetGUIStyle("MiniPullDown")))
                        ShowMenu(rect.Value);
                } else {
                    if (GUILayout.Button(GUIContent.none, Helper.GetGUIStyle("MiniPullDown"), GUILayout.Width(EditorGUIUtility.singleLineHeight)))
                        ShowMenu(menuButtonRect);
                    if (Event.current.type == EventType.Repaint)
                        menuButtonRect = GUILayoutUtility.GetLastRect();
                }
            }
            EditorGUILayout.EndHorizontal();
            if (grabValueMode == 2)
                DrawCtorField();
            if (!rect.HasValue)
                EditorGUI.indentLevel++;
            if (getException != null)
                EditorGUILayout.HelpBox(getException.Message, MessageType.Error);
        }

        public bool UpdateIfChanged() {
            if (!changed)
                return false;
            if (Helper.AssignValue(memberInfo, target, Value, indexParams))
                return true;
            UpdateValue();
            changed = false;
            return false;
        }

        public bool UpdateValue() {
            object value;
            if (Helper.FetchValue(memberInfo, target, out value, indexParams)) {
                rawValue = value;
                GetException = null;
                return true;
            }
            // Filter out index out of range / key not found exception
            if (indexParams != null &&
                indexParams.Length > 0 &&
                (value is IndexOutOfRangeException ||
                value is KeyNotFoundException))
                return false;
            GetException = value as Exception;
            return false;
        }

        private void AddField(UnityObject target) {
            if (target == null)
                return;
            BindingFlags flag = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
            if (privateFields)
                flag |= BindingFlags.NonPublic;
            fields.AddRange(
                target.GetType().GetFields(flag)
                .Where(t => obsolete || !Attribute.IsDefined(t, typeof(ObsoleteAttribute)))
                .Select(f => new ComponentFields {
                    field = f,
                    target = target
                })
            );
            fields.AddRange(
                target.GetType().GetProperties(flag)
                .Where(p => p.GetIndexParameters().Length == 0 && requiredType.IsAssignableFrom(p.PropertyType))
                .Select(p => new ComponentFields {
                    property = p,
                    target = target
                })
            );
        }

        private void InitFieldTypes() {
            fields.Clear();
            AddField(component);
            var gameObject = component as GameObject;
            if (gameObject != null)
                foreach (var c in gameObject.GetComponents(typeof(Component)))
                    AddField(c);
            fieldNames = fields.Select(m => string.Format(
                "{0} ({1})/{2}",
                m.target.GetType().Name,
                m.target.GetInstanceID(),
                Helper.GetMemberName(m.property == null ? m.field as MemberInfo : m.property as MemberInfo)
            )).ToArray();
            selectedFieldIndex = -1;
        }

        private object GetReferencedValue() {
            object val = null;
            return Helper.FetchValue(selectedField as MemberInfo ?? selectedProperty, component, out val) ? val : null;
        }

        private void DrawCtorField() {
            if (ctorDrawer == null) {
                ctorDrawer = new ComponentMethodDrawer(requiredType);
                ctorDrawer.OnRequireRedraw += RequireRedraw;
            }
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical();
            ctorDrawer.Draw();
            if (ctorDrawer.Info != null && GUILayout.Button("Construct"))
                ctorDrawer.Call();
            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
            if (ctorDrawer.Value != null) {
                rawValue = ctorDrawer.Value;
                grabValueMode = 0;
                RequireRedraw();
            }
        }

        private void DrawReferencedField(Rect? rect) {
            if (rect.HasValue)
                component = EditorGUI.ObjectField(Helper.ScaleRect(rect.Value, 0, 0, 0.5F, 1), name, component, typeof(UnityObject), true);
            else
                component = EditorGUILayout.ObjectField(name, component, typeof(UnityObject), true);
            if (component == null) {
                EditorGUI.BeginDisabledGroup(true);
                if (rect.HasValue)
                    EditorGUI.Popup(Helper.ScaleRect(rect.Value, 0.5F, 0, 0.5F, 1), 0, new string[0]);
                else
                    EditorGUILayout.Popup(0, new string[0]);
                EditorGUI.EndDisabledGroup();
                return;
            }
            if (GUI.changed) {
                InitFieldTypes();
                GUI.changed = false;
            }
            if (rect.HasValue)
                selectedFieldIndex = EditorGUI.Popup(Helper.ScaleRect(rect.Value, 0.5F, 0, 0.5F, 1), selectedFieldIndex, fieldNames);
            else
                selectedFieldIndex = EditorGUILayout.Popup(selectedFieldIndex, fieldNames);
            if (selectedFieldIndex > -1) {
                component = fields[selectedFieldIndex].target;
                selectedField = fields[selectedFieldIndex].field;
                selectedProperty = fields[selectedFieldIndex].property;
                if (grabValueMode == 1) {
                    rawValue = GetReferencedValue();
                    grabValueMode = 0;
                    RequireRedraw();
                }
            }
        }

        private void DrawDirectField(bool readOnly, Rect? rect) {
            object value = rawValue;
            Color color = GUI.color;
            FontStyle fontStyle = EditorStyles.label.fontStyle;
            if (isPrivate)
                GUI.color = new Color(color.r, color.g, color.b, color.a * 0.5F);
            if (isStatic)
                EditorStyles.label.fontStyle |= FontStyle.Italic;
            GUI.changed = false;
            try {
                switch (currentType) {
                    case PropertyType.Bool:
                        if (rect.HasValue)
                            value = EditorGUI.Toggle(rect.Value, nameContent, (bool)(value ?? false));
                        else
                            value = EditorGUILayout.Toggle(nameContent, (bool)(value ?? false));
                        break;
                    case PropertyType.Enum:
                        if (masked) {
                            if (rect.HasValue)
                                value = Helper.MaskedEnumField(rect.Value, nameContent, requiredType, value);
                            else
                                value = Helper.MaskedEnumField(nameContent, requiredType, value);
                            break;
                        }
                        if (rect.HasValue)
                            value = Helper.EnumField(rect.Value, nameContent, requiredType, value);
                        else
                            value = Helper.EnumField(nameContent, requiredType, value);
                        break;
                    case PropertyType.Long:
                        if (rect.HasValue)
                            value = EditorGUI.LongField(rect.Value, nameContent, Helper.GetOrDefault<long>(value));
                        else
                            value = EditorGUILayout.LongField(nameContent, Helper.GetOrDefault<long>(value));
                        break;
                    case PropertyType.Integer:
                        if (rect.HasValue)
                            value = EditorGUI.IntField(rect.Value, nameContent, Helper.GetOrDefault<int>(value));
                        else
                            value = EditorGUILayout.IntField(nameContent, Helper.GetOrDefault<int>(value));
                        break;
                    case PropertyType.Double:
                        if (rect.HasValue)
                            value = EditorGUI.DoubleField(rect.Value, nameContent, Helper.GetOrDefault<double>(value));
                        else
                            value = EditorGUILayout.DoubleField(nameContent, Helper.GetOrDefault<double>(value));
                        break;
                    case PropertyType.Single:
                        if (rect.HasValue)
                            value = EditorGUI.FloatField(rect.Value, nameContent, Helper.GetOrDefault<float>(value));
                        else
                            value = EditorGUILayout.FloatField(nameContent, Helper.GetOrDefault<float>(value));
                        break;
                    case PropertyType.Vector2:
                        if (rect.HasValue)
                            value = EditorGUI.Vector2Field(rect.Value, nameContent, Helper.GetOrDefault<Vector2>(value));
                        else
                            value = EditorGUILayout.Vector2Field(nameContent, Helper.GetOrDefault<Vector2>(value));
                        break;
                    case PropertyType.Vector2Int:
                        if (rect.HasValue)
                            value = EditorGUI.Vector2IntField(rect.Value, nameContent, Helper.GetOrDefault<Vector2Int>(value));
                        else
                            value = EditorGUILayout.Vector2IntField(nameContent, Helper.GetOrDefault<Vector2Int>(value));
                        break;
                    case PropertyType.Vector3:
                        if (rect.HasValue)
                            value = EditorGUI.Vector3Field(rect.Value, nameContent, Helper.GetOrDefault<Vector3>(value));
                        else
                            value = EditorGUILayout.Vector3Field(nameContent, Helper.GetOrDefault<Vector3>(value));
                        break;
                    case PropertyType.Vector3Int:
                        if (rect.HasValue)
                            value = EditorGUI.Vector3IntField(rect.Value, nameContent, Helper.GetOrDefault<Vector3Int>(value));
                        else
                            value = EditorGUILayout.Vector3IntField(nameContent, Helper.GetOrDefault<Vector3Int>(value));
                        break;
                    case PropertyType.Vector4:
                        if (rect.HasValue)
                            value = EditorGUI.Vector4Field(rect.Value, name, Helper.GetOrDefault<Vector4>(value));
                        else
                            value = EditorGUILayout.Vector4Field(name, Helper.GetOrDefault<Vector4>(value));
                        break;
                    case PropertyType.Quaterion:
                        if (rect.HasValue)
                            value = Helper.QuaternionField(rect.Value, name, Helper.GetOrDefault(value, Quaternion.identity));
                        else
                            value = Helper.QuaternionField(name, Helper.GetOrDefault(value, Quaternion.identity));
                        break;
                    case PropertyType.Color:
                        if (rect.HasValue)
                            value = EditorGUI.ColorField(rect.Value, nameContent, Helper.GetOrDefault<Color>(value));
                        else
                            value = EditorGUILayout.ColorField(nameContent, Helper.GetOrDefault<Color>(value));
                        break;
                    case PropertyType.Rect:
                        if (rect.HasValue)
                            value = EditorGUI.RectField(rect.Value, nameContent, Helper.GetOrDefault<Rect>(value));
                        else
                            value = EditorGUILayout.RectField(nameContent, Helper.GetOrDefault<Rect>(value));
                        break;
                    case PropertyType.RectInt:
                        if (rect.HasValue)
                            value = EditorGUI.RectIntField(rect.Value, nameContent, Helper.GetOrDefault<RectInt>(value));
                        else
                            value = EditorGUILayout.RectIntField(nameContent, Helper.GetOrDefault<RectInt>(value));
                        break;
                    case PropertyType.Bounds:
                        if (rect.HasValue)
                            value = EditorGUI.BoundsField(rect.Value, nameContent, Helper.GetOrDefault<Bounds>(value));
                        else
                            value = EditorGUILayout.BoundsField(nameContent, Helper.GetOrDefault<Bounds>(value));
                        break;
                    case PropertyType.Gradient:
                        if (rect.HasValue)
                            value = Helper.GradientField(rect.Value, nameContent, Helper.GetOrDefault<Gradient>(value));
                        else
                            value = Helper.GradientField(nameContent, Helper.GetOrDefault<Gradient>(value));
                        break;
                    case PropertyType.Curve:
                        if (rect.HasValue)
                            value = EditorGUI.CurveField(rect.Value, nameContent, value as AnimationCurve ?? new AnimationCurve());
                        else
                            value = EditorGUILayout.CurveField(nameContent, value as AnimationCurve ?? new AnimationCurve());
                        break;
                    case PropertyType.Object:
                        if (rect.HasValue)
                            value = Helper.ObjectField(rect.Value, nameContent, value as UnityObject, requiredType, true, readOnly);
                        else
                            value = Helper.ObjectField(nameContent, value as UnityObject, requiredType, true, readOnly);
                        break;
                    case PropertyType.String:
                        if (rect.HasValue)
                            value = Helper.StringField(rect.Value, nameContent, (string)value, readOnly);
                        else
                            value = Helper.StringField(nameContent, (string)value, readOnly);
                        break;
                    default:
                        var stringValue = value != null ? value.ToString() : "Null";
                        if (rect.HasValue) {
                            Helper.StringField(Helper.ScaleRect(rect.Value, 0, 0, 1, 1, 0, 0, -36), nameContent, stringValue, true);
                            DrawUnknownField(readOnly, value, Helper.ScaleRect(rect.Value, 1, 0, 0, 1, -34, 0, 32));
                        } else {
                            Helper.StringField(nameContent, stringValue, true);
                            DrawUnknownField(readOnly, value);
                        }
                        break;
                }
            } catch (InvalidCastException) {
                if (Event.current.type == EventType.Repaint)
                    value = null;
                else
                    RequireRedraw();
            }
            if (!readOnly) {
                changed |= GUI.changed;
                rawValue = value;
            }
            GUI.color = color;
            EditorStyles.label.fontStyle = fontStyle;
        }

        private void DrawUnknownField(bool readOnly, object target, Rect? position = null) {
            if (target == null)
                return;
            bool clicked = false;
            if (!position.HasValue)
                clicked = GUILayout.Button("…", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            else
                clicked = GUI.Button(position.Value, "…", EditorStyles.miniButton);
            if (clicked)
                InspectorChildWindow.Open(target, true, privateFields, obsolete, true, false, this);
        }

        private void ShowMenu(Rect position) {
            var menu = new GenericMenu();
            if (castableTypes.Count > 1)
                foreach (var type in castableTypes)
                    menu.AddItem(new GUIContent("Type/" + type), currentType == type, ChangeType, type);
            if (allowReferenceMode) {
                menu.AddItem(new GUIContent("Mode/By Value"), !referenceMode, ChangeRefMode, false);
                menu.AddItem(new GUIContent("Mode/By Reference"), referenceMode, ChangeRefMode, true);
            }
            if (!allowReferenceMode || !referenceMode) {
                if (!allowReferenceMode)
                    menu.AddItem(new GUIContent("Mode/By Value"), grabValueMode == 0, GrabValueMode, 0);
                menu.AddItem(new GUIContent("Mode/From Component"), grabValueMode == 1, GrabValueMode, 1);
                menu.AddItem(new GUIContent("Mode/Construct"), grabValueMode == 2, GrabValueMode, 2);
            }
            if (currentType == PropertyType.Enum)
                menu.AddItem(new GUIContent("Multiple Selection"), masked, ChangeMultiSelect, !masked);
            if (optionalPrivateFields) {
                if (referenceMode)
                    menu.AddItem(new GUIContent("Allow Private Members"), privateFields, ChangePrivateFields, !privateFields);
                else
                    menu.AddDisabledItem(new GUIContent("Allow Private Members"));
            }
            if (OnClose != null || OnEdit != null) {
                menu.AddSeparator("");
                if (OnEdit != null)
                    menu.AddItem(new GUIContent("Edit Query..."), false, OnEdit);
                if (OnClose != null)
                    menu.AddItem(new GUIContent("Close"), false, OnClose);
            }
            menu.DropDown(position);
        }

        private void ChangeType(object value) {
            var type = (PropertyType)value;
            if (castableTypes.Contains(type))
                currentType = type;
        }

        private void ChangeRefMode(object value) {
            ReferenceMode = (bool)value;
            grabValueMode = 0;
        }

        private void GrabValueMode(object value) {
            grabValueMode = (int)value;
        }

        private void ChangeMultiSelect(object value) {
            masked = (bool)value;
        }

        private void ChangePrivateFields(object value) {
            AllowPrivateFields = (bool)value;
        }

        private void RequireRedraw() {
            if (OnRequireRedraw != null)
                OnRequireRedraw();
        }
    }
}