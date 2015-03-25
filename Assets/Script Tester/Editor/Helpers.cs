using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text;

namespace ScriptTester {
	enum PropertyType {
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
		Curve,
		String,
		Object,
		Array
	}
	
	class InspectorDrawer {
		public UnityEngine.Object target;
		public List<IReflectorDrawer> drawer;
		public bool shown;
		public bool isInternalType;
		public InspectorDrawer(UnityEngine.Object target) {
			this.target = target;
			this.drawer = new List<IReflectorDrawer>();
		}
	}
	
	struct ComponentMethod {
		public MethodInfo method;
		public UnityEngine.Object target;
	}
	
	struct ComponentFields {
		public FieldInfo field;
		public PropertyInfo property;
		public UnityEngine.Object target;
	}
	
	interface IReflectorDrawer {
		void Draw();
		bool AllowPrivateFields { get; set; }
		bool Changed { get; }
		object Value { get; }
		MemberInfo Info { get; }
		event Action OnRequireRedraw;
	}
	
	public static class Helper {
		internal static readonly Dictionary<Type, PropertyType> propertyTypeMapper = new Dictionary<Type, PropertyType>();
		
		internal static void InitPropertyTypeMapper() {
			if(propertyTypeMapper.Count > 0)
				return;
			propertyTypeMapper.Add(typeof(string), PropertyType.String);
			propertyTypeMapper.Add(typeof(bool), PropertyType.Bool);
			propertyTypeMapper.Add(typeof(byte), PropertyType.Integer);
			propertyTypeMapper.Add(typeof(sbyte), PropertyType.Integer);
			propertyTypeMapper.Add(typeof(ushort), PropertyType.Integer);
			propertyTypeMapper.Add(typeof(short), PropertyType.Integer);
			propertyTypeMapper.Add(typeof(uint), PropertyType.Integer);
			propertyTypeMapper.Add(typeof(int), PropertyType.Integer);
			propertyTypeMapper.Add(typeof(ulong), PropertyType.Long);
			propertyTypeMapper.Add(typeof(long), PropertyType.Long);
			propertyTypeMapper.Add(typeof(float), PropertyType.Single);
			propertyTypeMapper.Add(typeof(double), PropertyType.Double);
			propertyTypeMapper.Add(typeof(Vector2), PropertyType.Vector2);
			propertyTypeMapper.Add(typeof(Vector3), PropertyType.Vector3);
			propertyTypeMapper.Add(typeof(Vector4), PropertyType.Vector4);
			propertyTypeMapper.Add(typeof(Quaternion), PropertyType.Quaterion);
			propertyTypeMapper.Add(typeof(Color), PropertyType.Color);
			propertyTypeMapper.Add(typeof(Rect), PropertyType.Rect);
			propertyTypeMapper.Add(typeof(Bounds), PropertyType.Bounds);
			propertyTypeMapper.Add(typeof(AnimationCurve), PropertyType.Curve);
			propertyTypeMapper.Add(typeof(UnityEngine.Object), PropertyType.Object);
			propertyTypeMapper.Add(typeof(Array), PropertyType.Array);
		}
		
		static readonly Hashtable storedState = new Hashtable();
		
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
			float xScale, float yScale, float widthScale, float heightScale,
			float offsetX = 0, float offsetY = 0, float offsetWidth = 0, float offsetHeight = 0) {
			return new Rect(
				source.x + source.width * xScale + offsetX,
				source.y + source.height * yScale + offsetY,
				source.width * widthScale + offsetWidth,
				source.height * heightScale + offsetHeight
			);
		}
		
		internal static string GetMemberName(MemberInfo member, bool simplifed = false) {
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
			ret.Append(member.Name);
			return ret.ToString();
		}
		
		static StringBuilder JoinStringList(StringBuilder sb, IList<string> list, string separator) {
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
			cValue = EditorGUILayout.Vector4Field(label, cValue, options);
			return new Quaternion(cValue.x, cValue.y, cValue.z, cValue.w);
		}
		
		internal static Quaternion QuaternionField(Rect position, string label, Quaternion value) {
			var cValue = new Vector4(value.x, value.y, value.z, value.w);
			cValue = EditorGUI.Vector4Field(position, label, cValue);
			return new Quaternion(cValue.x, cValue.y, cValue.z, cValue.w);
		}
		
		internal static int EnumField(Rect position, string label, Type type, int value) {
			string[] itemNames;
			int[] itemValues;
			int val = EnumFieldPreProcess(type, value, out itemNames, out itemValues);
			int newVal = EditorGUI.Popup(position, label, val, itemNames);
			return EnumFieldPostProcess(itemValues, newVal);
		}
		
		internal static int EnumField(string label, Type type, int value, params GUILayoutOption[] options) {
			string[] itemNames;
			int[] itemValues;
			int val = EnumFieldPreProcess(type, value, out itemNames, out itemValues);
			int newVal = EditorGUILayout.Popup(label, val, itemNames, options);
			return EnumFieldPostProcess(itemValues, newVal);
		}
		
		static int EnumFieldPreProcess(Type type, int val, out string[] itemNames, out int[] itemValues) {
			itemNames = Enum.GetNames(type);
			itemValues = Enum.GetValues(type) as int[];
			for(int i = 0; i < itemValues.Length; i++)
				if(val == itemValues[i])
					return i;
			return 0;
		}
		
		static int EnumFieldPostProcess(int[] itemValues, int val) {
			return itemValues[val];
		}
		
		
		internal static int MaskedEnumField(Rect position, string label, Type type, int mask) {
			return MaskedEnumField(position, new GUIContent(label), type, mask);
		}
		
		internal static int MaskedEnumField(Rect position, GUIContent label, Type type, int mask) {
			string[] itemNames;
			int[] itemValues;
			int val = MaskedEnumFieldPreProcess(type, mask, out itemNames, out itemValues);
			int newVal = EditorGUI.MaskField(position, label, val, itemNames);
			return MaskedEnumFieldPostProcess(itemValues, mask, val, newVal);
		}
		
		internal static int MaskedEnumField(string label, Type type, int mask, params GUILayoutOption[] options) {
			return MaskedEnumField(new GUIContent(label), type, mask, options);
		}
		
		internal static int MaskedEnumField(GUIContent label, Type type, int mask, params GUILayoutOption[] options) {
			string[] itemNames;
			int[] itemValues;
			int val = MaskedEnumFieldPreProcess(type, mask, out itemNames, out itemValues);
			int newVal = EditorGUILayout.MaskField(label, val, itemNames, options);
			return MaskedEnumFieldPostProcess(itemValues, mask, val, newVal);
		}
		
		static int MaskedEnumFieldPreProcess(Type type, int val, out string[] itemNames, out int[] itemValues) {
			itemNames = Enum.GetNames(type);
			itemValues = Enum.GetValues(type) as int[];
			int maskVal = 0;
			for(int i = 0; i < itemValues.Length; i++) {
				if(itemValues[i] != 0) {
					if((val & itemValues[i]) == itemValues[i])
						maskVal |= 1 << i;
				} else if(val == 0)
					maskVal |= 1 << i;
			}
			return maskVal;
		}
		
		static int MaskedEnumFieldPostProcess(int[] itemValues, int val, int maskVal, int newMaskVal) {
			int changes = maskVal ^ newMaskVal;
			for(int i = 0; i < itemValues.Length; i++)
				if((changes & (1 << i)) != 0) {
					if((newMaskVal & (1 << i)) != 0) {
						if(itemValues[i] == 0) {
							val = 0;
							break;
						}
						val |= itemValues[i];
					} else
						val &= ~itemValues[i];
				}
			return val;
		}
		
		internal static bool AssignValue(MemberInfo info, object target, object value) {
			try {
				var fieldInfo = info as FieldInfo;
				var propertyInfo = info as PropertyInfo;
				if(fieldInfo != null && !fieldInfo.IsInitOnly && !fieldInfo.IsLiteral)
					fieldInfo.SetValue(target, value);
				else if(propertyInfo != null && propertyInfo.CanWrite)
					propertyInfo.SetValue(target, value, null);
				else
					return false;
			} catch {
				return false;
			}
			return true;
		}
		
		internal static bool FetchValue(MemberInfo info, object target, out object value) {
			value = null;
			try {
				var fieldInfo = info as FieldInfo;
				var propertyInfo = info as PropertyInfo;
				if(fieldInfo != null)
					value = fieldInfo.GetValue(target);
				else if(propertyInfo != null && propertyInfo.CanRead)
					value = propertyInfo.GetValue(target, null);
				else
					return false;
			} catch(Exception ex) {
				value = ex;
				return false;
			}
			return true;
		}
		
		[MenuItem("Window/Script Tester/Inspector+")]
		public static void ShowInspectorPlus() {
			EditorWindow.GetWindow(typeof(InspectorPlus));
		}
		
		[MenuItem("Window/Script Tester/Method Caller")]
		public static void ShowMethodCaller() {
			EditorWindow.GetWindow(typeof(TestingScript));
		}
	}
}