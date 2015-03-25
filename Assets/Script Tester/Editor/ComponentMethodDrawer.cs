using UnityEngine;
using UnityEditor;
using UnityEditor.AnimatedValues;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ScriptTester;

namespace ScriptTester {
	class ComponentMethodDrawer:IReflectorDrawer {
		UnityEngine.Object component;
		readonly List<ComponentMethod> methods = new List<ComponentMethod>();
		AnimBool showMethodOptions;
		AnimBool showMethodSelector;
		AnimBool showResultSelector;
		string[] methodNames;
		int selectedMethodIndex;
		MethodInfo selectedMethod;
		ParameterInfo[] parameterInfo;
		MethodPropertyDrawer[] parameters;
		MethodPropertyDrawer result;
		Exception thrownException;
		bool titleFolded = true, paramsFolded = true, resultFolded = true,
			drawHeader = true, privateFields = true;

		public event Action OnRequireRedraw;
		
		public bool ShouldDrawHeader {
			get { return drawHeader; }
			set {
				drawHeader = value;
				paramsFolded &= value;
				resultFolded &= value;
			}
		}

		public bool Changed {
			get { return false; }
		}
		
		public object Value {
			get { return result == null ? null : result.Value; }
		}
		
		public bool AllowPrivateFields {
			get {
				return privateFields;
			}
			set {
				privateFields = value;
				InitComponentMethods();
			}
		}

		public MemberInfo Info {
			get { return selectedMethod as MemberInfo; }
		}
		
		public bool IsComponentNull() {
			return component == null;
		}
		
		public ComponentMethodDrawer() {
			showMethodSelector = new AnimBool(false);
			showMethodOptions = new AnimBool(false);
			showResultSelector = new AnimBool(false);
			showMethodSelector.valueChanged.AddListener(RequireRedraw);
			showMethodOptions.valueChanged.AddListener(RequireRedraw);
			showResultSelector.valueChanged.AddListener(RequireRedraw);
		}
		
		public ComponentMethodDrawer(UnityEngine.Object target)
			: this() {
			component = target;
			drawHeader = false;
			showMethodSelector.value = true;
			InitComponentMethods();
		}
		
		public void Call() {
			if(selectedMethod == null || component == null || parameters == null)
				return;
			try {
				thrownException = null;
				var requestData = parameters.Select(d => d.Value).ToArray();
				var returnData = selectedMethod.Invoke(component, requestData);
				result = selectedMethod.ReturnType == typeof(void) ?
					null :
					new MethodPropertyDrawer(selectedMethod.ReturnType, "Return data", returnData);
				for(int i = 0; i < Math.Min(parameters.Length, requestData.Length); i++) {
					parameters[i].Value = requestData[i];
					if(parameters[i].ReferenceMode)
						Helper.AssignValue(parameters[i].RefFieldInfo, parameters[i].Component, requestData[i]);
				}
			} catch(Exception ex) {
				thrownException = ex.InnerException ?? ex;
				Debug.LogException(thrownException);
				throw;
			}
		}
		
		public void Draw() {
			if(drawHeader) {
				EditorGUI.BeginDisabledGroup(component == null);
				titleFolded = EditorGUILayout.InspectorTitlebar(titleFolded, component) || component == null;
				EditorGUI.EndDisabledGroup();
			}
			GUI.changed = false;
			if(component == null || titleFolded || !drawHeader) {
				if(drawHeader) {
					EditorGUI.indentLevel++;
					EditorGUILayout.BeginVertical();
					component = EditorGUILayout.ObjectField("Target", component, typeof(UnityEngine.Object), true);
				}
				if(component != null) {
					if(GUI.changed) {
						InitComponentMethods();
						GUI.changed = false;
					}
					showMethodSelector.target = true;
				} else
					showMethodSelector.target = false;
				if(EditorGUILayout.BeginFadeGroup(showMethodSelector.faded))
					DrawComponent();
				EditorGUILayout.EndFadeGroup();
				showResultSelector.target = result != null || thrownException != null;
				if(EditorGUILayout.BeginFadeGroup(showResultSelector.faded))
					DrawResult();
				EditorGUILayout.EndFadeGroup();
				if(drawHeader) {
					EditorGUILayout.EndVertical();
					EditorGUI.indentLevel--;
				}
			} 
		}
		
		void AddComponentMethod(UnityEngine.Object target) {
			BindingFlags flag = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
			if(privateFields)
				flag |= BindingFlags.NonPublic;
			methods.AddRange(component.GetType().GetMethods(flag).Select(m => new ComponentMethod {
				method = m,
				target = target
			}));
		}
		
		void InitComponentMethods() {
			methods.Clear();
			AddComponentMethod(component);
			if(drawHeader) {
				var gameObject = component as GameObject;
				if(gameObject != null)
					foreach(var c in gameObject.GetComponents(typeof(Component)))
						AddComponentMethod(c);
				methodNames = methods.Select(m => string.Format(
					"{0} ({1})/{2} ({3} parameters)",
					m.target.GetType().Name,
					m.target.GetInstanceID(),
					Helper.GetMemberName(m.method as MemberInfo),
					m.method.GetParameters().Length
				)).ToArray();
			} else {
				methodNames = methods.Select(m => string.Format(
					"{0} ({1} parameters)",
					Helper.GetMemberName(m.method as MemberInfo),
					m.method.GetParameters().Length
				)).ToArray();
			}
			selectedMethodIndex = -1;
			selectedMethod = null;
			parameterInfo = null;
			parameters = null;
			result = null;
			thrownException = null;
		}
		
		void InitMethodParams() {
			selectedMethod = methods[selectedMethodIndex].method;
			component = methods[selectedMethodIndex].target;
			parameterInfo = selectedMethod.GetParameters();
			parameters = new MethodPropertyDrawer[parameterInfo.Length];
			for(int i = 0; i < parameterInfo.Length; i++) {
				var info = parameterInfo[i];
				parameters[i] = new MethodPropertyDrawer(info.ParameterType, info.Name, info.IsOptional ? info.DefaultValue : null);
				parameters[i].OnRequireRedraw += RequireRedraw;
			}
			result = null;
			thrownException = null;
		}
		
		void DrawComponent() {
			selectedMethodIndex = EditorGUILayout.Popup("Method", selectedMethodIndex, methodNames);
			if(selectedMethodIndex >= 0) {
				if(GUI.changed) {
					InitMethodParams();
					GUI.changed = false;
				}
				showMethodOptions.target = true;
			} else
				showMethodOptions.target = false;
			if(EditorGUILayout.BeginFadeGroup(showMethodOptions.faded))
				DrawMethod();
			EditorGUILayout.EndFadeGroup();
		}
		
		void DrawMethod() {
			if(paramsFolded = EditorGUILayout.Foldout(paramsFolded, selectedMethod.Name)) {
				GUI.changed = false;
				EditorGUI.indentLevel++;
				EditorGUILayout.BeginVertical();
				if(selectedMethod.ContainsGenericParameters)
					EditorGUILayout.HelpBox("Generic method is not supported.", MessageType.Warning);
				else {
					if(parameterInfo.Length == 0)
						EditorGUILayout.HelpBox("There is no parameters required for this method.", MessageType.Info);
					foreach(var drawer in parameters)
						drawer.Draw();
				}
				EditorGUILayout.EndVertical();
				EditorGUI.indentLevel--;
			}
		}
		
		void DrawResult() {
			if(resultFolded = EditorGUILayout.Foldout(resultFolded, "Result")) {
				GUI.changed = false;
				EditorGUI.indentLevel++;
				EditorGUILayout.BeginVertical();
				if(result != null)
					result.Draw(true);
				if(thrownException != null)
					EditorGUILayout.HelpBox(thrownException.Message, MessageType.Error);
				EditorGUILayout.EndVertical();
				EditorGUI.indentLevel--;
			}
		}
		
		void RequireRedraw() {
			if(OnRequireRedraw != null)
				OnRequireRedraw();
		}
	}
}
