using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ScriptTester;

namespace ScriptTester {
	class MethodPropertyDrawer:IReflectorDrawer {
		public readonly string name;
		MemberInfo memberInfo;
		public Type requiredType;
		readonly List<PropertyType> castableTypes;
		PropertyType currentType;
		object rawValue;
		bool referenceMode;
		
		UnityEngine.Object component;
		readonly List<ComponentFields> fields;
		string[] fieldNames;
		int selectedFieldIndex;
		FieldInfo selectedField;
		PropertyInfo selectedProperty;
		Rect menuButtonRect;
		bool changed;
		bool allowReferenceMode = true;
		bool privateFields = true;
		bool optionalPrivateFields = true;
		bool showUpdatable;
		bool updatable;
		public event Action OnRequireRedraw;
		
		readonly List<MethodPropertyDrawer> arrayContentDrawer;
		ReorderableList arrayHandler;
		
		public UnityEngine.Object Component {
			get { return component; }
			set {
				component = value;
				if(selectedField != null && selectedField.DeclaringType != null && !selectedField.DeclaringType.IsInstanceOfType(component))
					selectedField = null;
				if(selectedProperty != null && selectedField.DeclaringType != null && !selectedProperty.DeclaringType.IsInstanceOfType(component))
					selectedProperty = null;
			}
		}

		public MemberInfo Info {
			get { return memberInfo; }
			set { memberInfo = value; }
		}
		
		public MemberInfo RefFieldInfo {
			get { return selectedField == null ? (MemberInfo)selectedProperty : (MemberInfo)selectedField; }
			set {
				if(component == null)
					return;
				InitFieldTypes();
				if(value is FieldInfo) {
					selectedField = value as FieldInfo;
					selectedFieldIndex = fields.FindIndex(field => field.field == value);
				} else if(value is PropertyInfo) {
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
				if(!value && referenceMode)
					ReferenceMode = false;
			}
		}
		
		public bool ReferenceMode {
			get { return referenceMode; }
			set {
				referenceMode = value && allowReferenceMode;
				fields.Clear();
				fieldNames = new string[0];
				if(referenceMode) {
					rawValue = null;
					InitFieldTypes();
				} else {
					object val = rawValue;
					try {
						if(selectedField != null)
							val = selectedField.GetValue(component);
						else if(selectedProperty != null)
							val = selectedProperty.GetValue(component, null);
					} catch {
					}
					rawValue = val;
					SetArray();
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
				if(referenceMode)
					InitFieldTypes();
			}
		}

		public bool OptionalPrivateFields {
			get { return optionalPrivateFields; }
			set { optionalPrivateFields = value; }
		}
		
		public object Value {
			get {
				if(referenceMode) {
					if(selectedField != null)
						rawValue = selectedField.GetValue(component);
					else if(selectedProperty != null)
						rawValue = selectedProperty.GetValue(component, null);
				} else if(currentType == PropertyType.Array) {
					var array = Array.CreateInstance(requiredType.GetElementType(), arrayContentDrawer.Count);
					for(int i = 0; i < arrayContentDrawer.Count; i++)
						array.SetValue(arrayContentDrawer[i].Value, i);
					rawValue = array;
				}
				var convertedValue = rawValue;
				if(rawValue != null && requiredType != typeof(object) && requiredType.IsInstanceOfType(rawValue)) {
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
				SetArray();
			}
		}
		
		public MethodPropertyDrawer(Type type, string name, object defaultValue) {
			this.requiredType = type;
			this.name = name;
			this.rawValue = defaultValue;
			this.castableTypes = new List<PropertyType>();
			this.fields = new List<ComponentFields>();
			this.selectedFieldIndex = -1;
			this.arrayContentDrawer = new List<MethodPropertyDrawer>();
			this.arrayHandler = new ReorderableList(arrayContentDrawer, typeof(MethodPropertyDrawer));
			InitType();
		}
		
		void ListAddItem(object value = null) {
			var drawer = new MethodPropertyDrawer(requiredType.GetElementType(), "", value);
			drawer.OnRequireRedraw += RequireRedraw;
			arrayContentDrawer.Add(drawer);
		}
		
		void InitType() {
			Helper.InitPropertyTypeMapper();
			if(requiredType.IsArray) {
				castableTypes.Add(PropertyType.Array);
				currentType = PropertyType.Array;
				arrayHandler.headerHeight = EditorGUIUtility.singleLineHeight;
				arrayHandler.elementHeight = EditorGUIUtility.singleLineHeight;
				arrayHandler.drawHeaderCallback = r => EditorGUI.LabelField(r, name);
				arrayHandler.drawElementCallback = (r, i, c, d) => arrayContentDrawer[i].Draw(false, r);
				arrayHandler.onAddCallback = l => ListAddItem();
				arrayContentDrawer.Clear();
				if(rawValue != null)
					foreach(object item in (Array)rawValue)
						ListAddItem(item);
				return;
			}
			if(requiredType.IsByRef)
				requiredType = requiredType.GetElementType();
			if(requiredType.IsEnum) {
				castableTypes.Add(PropertyType.Enum);
				castableTypes.Add(PropertyType.Integer);
				currentType = PropertyType.Enum;
				return;
			}
			if(requiredType == typeof(object)) {
				castableTypes.AddRange(Enum.GetValues(typeof(PropertyType)).Cast<PropertyType>());
				castableTypes.Remove(PropertyType.Unknown);
				castableTypes.Remove(PropertyType.Enum);
				currentType = PropertyType.Object;
				return;
			}
			foreach(var map in Helper.propertyTypeMapper) {
				if(map.Key == requiredType || requiredType.IsSubclassOf(map.Key)) {
					castableTypes.Add(map.Value);
					currentType = map.Value;
					return;
				}
			}
			foreach(var map in Helper.propertyTypeMapper) {
				if(map.Key.IsAssignableFrom(requiredType) && requiredType.IsAssignableFrom(map.Key))
					castableTypes.Add(map.Value);
			}
			currentType = castableTypes.Count > 0 ? castableTypes[0] : PropertyType.Unknown;
		}
		
		public void Draw() {
			Draw(false);
		}
		
		public void Draw(bool readOnly, Rect? rect = null) {
			var referenceModeBtn = allowReferenceMode || (allowReferenceMode && optionalPrivateFields) || castableTypes.Count > 1;
			if(!rect.HasValue)
				EditorGUI.indentLevel--;
			EditorGUILayout.BeginHorizontal();
			if(rect.HasValue) {
				Rect sRect = referenceModeBtn ? Helper.ScaleRect(rect.Value, 0, 0, 1, 1, offsetWidth: -EditorGUIUtility.singleLineHeight) : rect.Value;
				if(referenceMode)
					DrawReferencedField(sRect);
				else
					DrawDirectField(readOnly, sRect);
			} else {
				if(showUpdatable) {
					updatable = EditorGUILayout.ToggleLeft(new GUIContent("", "Update Enabled"), updatable, GUILayout.Width(EditorGUIUtility.singleLineHeight));
					Helper.StoreState(memberInfo, updatable);
				} else
					EditorGUILayout.LabelField(GUIContent.none, GUILayout.Width(EditorGUIUtility.singleLineHeight));
				if(referenceMode)
					DrawReferencedField(null);
				else
					DrawDirectField(readOnly, null);
			}
			if(!readOnly && referenceModeBtn) {
				if(rect.HasValue) {
					if(GUI.Button(Helper.ScaleRect(rect.Value, 1, 0.5F, 0, 0, -EditorGUIUtility.singleLineHeight, -7.5F, 15, 15), "...", EditorStyles.miniButton))
						ShowMenu(rect.Value);
				} else {
					if(GUILayout.Button("...", EditorStyles.miniButton, GUILayout.Width(EditorGUIUtility.singleLineHeight)))
						ShowMenu(menuButtonRect);
					if(Event.current.type == EventType.Repaint)
						menuButtonRect = GUILayoutUtility.GetLastRect();
				}
			}
			EditorGUILayout.EndHorizontal();
			if(!rect.HasValue)
				EditorGUI.indentLevel++;
			if(!readOnly) {
				if(!referenceMode && castableTypes.Count == 0)
					EditorGUILayout.HelpBox(string.Format("{0} is not supported.", requiredType.Name), MessageType.Warning);
			}
		}
		
		void AddField(UnityEngine.Object target) {
			if(target == null)
				return;
			BindingFlags flag = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
			if(privateFields)
				flag |= BindingFlags.NonPublic;
			fields.AddRange(
				target.GetType().GetFields(flag)
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
		
		void InitFieldTypes() {
			fields.Clear();
			AddField(component);
			var gameObject = component as GameObject;
			if(gameObject != null)
				foreach(var c in gameObject.GetComponents(typeof(Component)))
					AddField(c);
			fieldNames = fields.Select(m => string.Format(
				"{0} ({1})/{2}",
				m.target.GetType().Name,
				m.target.GetInstanceID(),
				Helper.GetMemberName(m.property == null ? m.field as MemberInfo : m.property as MemberInfo)
			)).ToArray();
			selectedFieldIndex = -1;
		}
		
		void SetArray() {
			arrayContentDrawer.Clear();
			if(requiredType.IsArray && rawValue != null) {
				foreach(object item in (Array)rawValue)
					arrayContentDrawer.Add(new MethodPropertyDrawer(requiredType.GetElementType(), "", item));
			}
		}
		
		void DrawReferencedField(Rect? rect) {
			if(rect.HasValue)
				component = EditorGUI.ObjectField(Helper.ScaleRect(rect.Value, 0, 0, 0.5F, 1), name, component, typeof(UnityEngine.Object), true);
			else
				component = EditorGUILayout.ObjectField(name, component, typeof(UnityEngine.Object), true);
			if(component == null) {
				EditorGUI.BeginDisabledGroup(true);
				if(rect.HasValue)
					EditorGUI.Popup(Helper.ScaleRect(rect.Value, 0.5F, 0, 0.5F, 1), 0, new string[0]);
				else
					EditorGUILayout.Popup(0, new string[0]);
				EditorGUI.EndDisabledGroup();
				return;
			}
			if(GUI.changed) {
				InitFieldTypes();
				GUI.changed = false;
			}
			if(rect.HasValue)
				selectedFieldIndex = EditorGUI.Popup(Helper.ScaleRect(rect.Value, 0.5F, 0, 0.5F, 1), selectedFieldIndex, fieldNames);
			else
				selectedFieldIndex = EditorGUILayout.Popup(selectedFieldIndex, fieldNames);
			if(selectedFieldIndex > -1) {
				component = fields[selectedFieldIndex].target;
				selectedField = fields[selectedFieldIndex].field;
				selectedProperty = fields[selectedFieldIndex].property;
			}
		}
		
		void DrawDirectField(bool readOnly, Rect? rect) {
			object value = rawValue;
			string stringValue = string.Empty;
			GUI.changed = false;
			try {
				switch(currentType) {
					case PropertyType.Bool:
						if(rect.HasValue)
							value = EditorGUI.Toggle(rect.Value, name, (bool)(value ?? false));
						else
							value = EditorGUILayout.Toggle(name, (bool)(value ?? false));
						break;
					case PropertyType.Enum:
						if(rect.HasValue)
							value = EditorGUI.Popup(rect.Value, name, (int)(value ?? 0), Enum.GetNames(requiredType));
						else
							value = EditorGUILayout.Popup(name, (int)(value ?? 0), Enum.GetNames(requiredType));
						break;
					case PropertyType.Long:
					#if UNITY_5
						if(rect.HasValue)
							value = EditorGUI.LongField(rect.Value, name, (long)(value ?? 0L));
						else
							value = EditorGUILayout.LongField(name, (long)(value ?? 0L));
						break;
					#endif
					case PropertyType.Integer:
						if(rect.HasValue)
							value = EditorGUI.IntField(rect.Value, name, (int)(value ?? 0));
						else
							value = EditorGUILayout.IntField(name, (int)(value ?? 0));
						break;
					case PropertyType.Double:
					#if UNITY_5
						if(rect.HasValue)
							value = EditorGUI.DoubleField(rect.Value, name, (double)(value ?? 0));
						else
							value = EditorGUILayout.DoubleField(name, (double)(value ?? 0));
						break;
					#endif
					case PropertyType.Single:
						if(rect.HasValue)
							value = EditorGUI.FloatField(rect.Value, name, (float)(value ?? 0F));
						else
							value = EditorGUILayout.FloatField(name, (float)(value ?? 0F));
						break;
					case PropertyType.Vector2:
						if(rect.HasValue)
							value = EditorGUI.Vector2Field(rect.Value, name, (Vector2)(value ?? Vector2.zero));
						else
							value = EditorGUILayout.Vector2Field(name, (Vector2)(value ?? Vector2.zero));
						break;
					case PropertyType.Vector3:
						if(rect.HasValue)
							value = EditorGUI.Vector3Field(rect.Value, name, (Vector3)(value ?? Vector3.zero));
						else
							value = EditorGUILayout.Vector3Field(name, (Vector3)(value ?? Vector3.zero));
						break;
					case PropertyType.Vector4:
						if(rect.HasValue)
							value = EditorGUI.Vector4Field(rect.Value, name, (Vector4)(value ?? Vector4.zero));
						else
							value = EditorGUILayout.Vector4Field(name, (Vector4)(value ?? Vector4.zero));
						break;
					case PropertyType.Quaterion:
						if(rect.HasValue)
							value = Helper.QuaterionField(rect.Value, name, (Quaternion)(value ?? Quaternion.identity));
						else
							value = Helper.QuaterionField(name, (Quaternion)(value ?? Quaternion.identity));
						break;
					case PropertyType.Color:
						if(rect.HasValue)
							value = EditorGUI.ColorField(rect.Value, name, (Color)(value ?? Color.white));
						else
							value = EditorGUILayout.ColorField(name, (Color)(value ?? Color.white));
						break;
					case PropertyType.Rect:
						if(rect.HasValue)
							value = EditorGUI.RectField(rect.Value, name, (Rect)(value ?? default(Rect)));
						else
							value = EditorGUILayout.RectField(name, (Rect)(value ?? default(Rect)));
						break;
					case PropertyType.Bounds:
						if(rect.HasValue)
							value = EditorGUI.BoundsField(rect.Value, new GUIContent(name), (Bounds)(value ?? default(Bounds)));
						else
							value = EditorGUILayout.BoundsField(name, (Bounds)(value ?? default(Bounds)));
						break;
					case PropertyType.Curve:
						if(rect.HasValue)
							value = EditorGUI.CurveField(rect.Value, name, (AnimationCurve)(value ?? new AnimationCurve()));
						else
							value = EditorGUILayout.CurveField(name, (AnimationCurve)(value ?? new AnimationCurve()));
						break;
					case PropertyType.Object:
						if(rect.HasValue)
							value = EditorGUI.ObjectField(rect.Value, name, (UnityEngine.Object)value, requiredType, true);
						else
							value = EditorGUILayout.ObjectField(name, (UnityEngine.Object)value, requiredType, true);
						break;
					case PropertyType.Array:
						if(rect.HasValue) {
							arrayHandler.DoList(rect.Value);
							break;
						}
						EditorGUILayout.EndHorizontal();
						arrayHandler.DoLayoutList();
						EditorGUILayout.BeginHorizontal();
						break;
					case PropertyType.String:
						stringValue = (string)value;
						if(rect.HasValue) {
							value = EditorGUI.TextField(rect.Value, name, stringValue);
							break;
						}
						if(!string.IsNullOrEmpty(stringValue) && (stringValue.Contains('\r') || stringValue.Contains('\n'))) {
							EditorGUILayout.PrefixLabel(name);
							if(readOnly)
								EditorGUILayout.SelectableLabel(stringValue, EditorStyles.textArea);
							else
								value = EditorGUILayout.TextArea(stringValue);
						} else {
							if(readOnly) {
								EditorGUILayout.PrefixLabel(name);
								EditorGUILayout.SelectableLabel(stringValue, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight * Mathf.Max(1, stringValue.Length - stringValue.Replace("\r", "").Replace("\n", "").Length)));
							} else
								value = EditorGUILayout.TextField(name, stringValue);
						}
						break;
					default:
						if(value != null)
							stringValue = value.ToString();
						if(rect.HasValue) {
							value = EditorGUI.TextField(rect.Value, name, stringValue);
							break;
						}
						EditorGUILayout.PrefixLabel(name);
						if(!string.IsNullOrEmpty(stringValue) && (stringValue.Contains('\r') || stringValue.Contains('\n')))
							EditorGUILayout.SelectableLabel(stringValue, EditorStyles.textArea, GUILayout.Height(EditorGUIUtility.singleLineHeight * Mathf.Max(1, stringValue.Length - stringValue.Replace("\r", "").Replace("\n", "").Length)));
						else
							EditorGUILayout.SelectableLabel(stringValue, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
						break;
				}
			} catch(InvalidCastException) {
				if(Event.current.type == EventType.Repaint)
					value = null;
				else
					RequireRedraw();
			}
			if(!readOnly) {
				changed |= GUI.changed;
				rawValue = value;
			}
		}
		
		void ShowMenu(Rect position) {
			var menu = new GenericMenu();
			if(castableTypes.Count > 1)
				foreach(var type in castableTypes)
					menu.AddItem(new GUIContent("Type/" + type), currentType == type, ChangeType, type);
			if(allowReferenceMode) {
				menu.AddItem(new GUIContent("Mode/By Value"), !referenceMode, ChangeMode, false);
				menu.AddItem(new GUIContent("Mode/By Reference"), referenceMode, ChangeMode, true);
			}
			if(optionalPrivateFields) {
				if(referenceMode)
					menu.AddItem(new GUIContent("Allow Private Members"), privateFields, ChangePrivateFields, privateFields);
				else
					menu.AddDisabledItem(new GUIContent("Allow Private Members"));
			}
			menu.DropDown(position);
		}
		
		void ChangeType(object value) {
			var type = (PropertyType)value;
			if(castableTypes.Contains(type))
				currentType = type;
		}
		
		void ChangeMode(object value) {
			ReferenceMode = (bool)value;
		}
		
		void ChangePrivateFields(object value) {
			AllowPrivateFields = (bool)value;
		}
		
		void RequireRedraw() {
			if(OnRequireRedraw != null)
				OnRequireRedraw();
		}
	}
}