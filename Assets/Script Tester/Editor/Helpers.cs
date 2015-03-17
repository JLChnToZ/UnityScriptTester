using UnityEngine;
using UnityEditor;
using System;
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
	
	static class Helper {
		public static readonly Dictionary<Type, PropertyType> propertyTypeMapper = new Dictionary<Type, PropertyType>();
		
		public static void InitPropertyTypeMapper() {
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
			propertyTypeMapper.Add(typeof(Color), PropertyType.Color);
			propertyTypeMapper.Add(typeof(Rect), PropertyType.Rect);
			propertyTypeMapper.Add(typeof(Bounds), PropertyType.Bounds);
			propertyTypeMapper.Add(typeof(AnimationCurve), PropertyType.Curve);
			propertyTypeMapper.Add(typeof(UnityEngine.Object), PropertyType.Object);
			propertyTypeMapper.Add(typeof(Array), PropertyType.Array);
		}
	
		public static void ReadOnlyLabelField(string label, string value) {
			if(value.Contains('\r') || value.Contains('\n')) {
				EditorGUILayout.PrefixLabel(label);
				EditorGUILayout.SelectableLabel(value, EditorStyles.textArea);
			} else {
				EditorGUILayout.PrefixLabel(label);
				EditorGUILayout.SelectableLabel(value, EditorStyles.textField);
			}
		}
	
		public static Rect ScaleRect(Rect source,
			float xScale, float yScale, float widthScale, float heightScale,
			float offsetX = 0, float offsetY = 0, float offsetWidth = 0, float offsetHeight = 0) {
			return new Rect(
				source.x + source.width * xScale + offsetX,
				source.y + source.height * yScale + offsetY,
				source.width * widthScale + offsetWidth,
				source.height * heightScale + offsetHeight
			);
		}
		
		public static string GetMemberName(MemberInfo member) {
			var ret = new StringBuilder();
			var props = new List<string>();
			var field = member as FieldInfo;
			var property = member as PropertyInfo;
			var method = member as MethodInfo;
			if(field != null) {
				if(!field.IsPublic)
					props.Add("Private");
				if(field.IsStatic)
					props.Add("Static");
			} else if(method != null) {
				if(!method.IsPublic)
					props.Add("Private");
				if(method.IsStatic)
					props.Add("Static");
			} else if(property != null) {
				if(property.CanRead && (method = property.GetGetMethod()) != null) {
					if(!method.IsPublic)
						props.Add("Private Get");
					if(method.IsStatic)
						props.Add("Static Get");
				}
				if(property.CanWrite && (method = property.GetSetMethod()) != null) {
					if(!method.IsPublic)
						props.Add("Private Set");
					if(method.IsStatic)
						props.Add("Static Set");
				}
			}
			if(props.Count > 0)
				ret.Append("(");
			JoinStringList(ret, props);
			if(props.Count > 0)
				ret.Append(") ");
			ret.Append(member.Name);
			return ret.ToString();
		}
		
		static StringBuilder JoinStringList(StringBuilder sb, IList<string> list) {
			if(sb == null)
				sb = new StringBuilder();
			bool nonFirst = false;
			foreach(var item in list) {
				if(nonFirst)
					sb.Append(", ");
				sb.Append(item);
				nonFirst = true;
			}
			return sb;
		}
		
		public static bool AssignValue(MemberInfo info, object target, object value) {
			try {
				var fieldInfo = info as FieldInfo;
				var propertyInfo = info as PropertyInfo;
				if(fieldInfo != null)
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
		
		public static bool FetchValue(MemberInfo info, object target, out object value) {
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
			} catch {
				return false;
			}
			return true;
		}
	}
}