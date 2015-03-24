using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using ScriptTester;

namespace ScriptTester {
	class TestingScript : EditorWindow {
		readonly List<ComponentMethodDrawer> callers = new List<ComponentMethodDrawer>();
		Vector2 scrollPos;
	
		bool stopAtExceptions;
	
		void OnEnable() {
			title = "Test Call Method";
		}
    
		void OnGUI() {
			GUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUI.enabled = callers.Count > 1;
			if(GUILayout.Button("Execute", EditorStyles.toolbarButton))
				Execute();
			if(GUILayout.Button("Clear", EditorStyles.toolbarButton))
				callers.Clear();
			GUILayout.FlexibleSpace();
			GUI.enabled = true;
			stopAtExceptions = GUILayout.Toggle(stopAtExceptions, "Stop at Exceptions", EditorStyles.toolbarButton);
			GUILayout.EndHorizontal();
			bool isNull = false;
			scrollPos = GUILayout.BeginScrollView(scrollPos);
			foreach(var caller in callers) {
				isNull = caller.IsComponentNull();
				caller.Draw();
			}
			if(!isNull) {
				var cm = new ComponentMethodDrawer();
				callers.Add(cm);
				cm.OnRequireRedraw += Repaint;
				cm.Draw();
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndScrollView();
		}
	
		void Execute() {
			foreach(var caller in callers) {
				try {
					if(!caller.IsComponentNull())
						caller.Call();
				} catch {
					if(stopAtExceptions)
						break;
				}
			}
		}
	}
}