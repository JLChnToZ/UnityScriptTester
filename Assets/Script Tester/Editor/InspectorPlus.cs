using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ScriptTester;

namespace ScriptTester {
	class InspectorPlus : EditorWindow {
		readonly List<InspectorDrawer[]> drawers = new List<InspectorDrawer[]>();
		Vector2 scrollPos;
		Rect toolbarMenuPos;
		bool privateFields = true;
		bool forceUpdateProps = false;
		bool showProps = true;
		bool showMethods = true;
		bool locked = false;
		bool showObsolete = false;
		int[] instanceIds = new int[0];
	
		void OnEnable() {
			title = "Inspector+";
			OnSelectionChange();
		}
		
		void OnFocus() {
			UpdateValues();
		}
		
		void OnGUI() {
			GUILayout.BeginHorizontal(EditorStyles.toolbar);
			if(GUILayout.Button("Menu", EditorStyles.toolbarDropDown))
				ShowMenu();
			if(Event.current.type == EventType.Repaint)
				toolbarMenuPos = GUILayoutUtility.GetLastRect();
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUI.changed = false;
			scrollPos = GUILayout.BeginScrollView(scrollPos);
			foreach(var drawerGroup in drawers)
				foreach(var drawer in drawerGroup) {
					drawer.shown = EditorGUILayout.InspectorTitlebar(drawer.shown, drawer.target);
					Helper.StoreState(drawer.target, drawer.shown);
					if(!drawer.shown)
						continue;
					EditorGUI.indentLevel++;
					EditorGUILayout.BeginVertical();
					foreach(var item in drawer.drawer) {
						var methodDrawer = item as ComponentMethodDrawer;
						if(methodDrawer != null) {
							EditorGUI.indentLevel--;
							EditorGUILayout.BeginHorizontal();
							EditorGUILayout.LabelField(GUIContent.none, GUILayout.Width(EditorGUIUtility.singleLineHeight));
							EditorGUILayout.BeginVertical();
							methodDrawer.Draw();
							if(methodDrawer.Info != null && GUILayout.Button("Execute " + methodDrawer.Info.Name))
								methodDrawer.Call();
							EditorGUILayout.EndVertical();
							EditorGUILayout.EndHorizontal();
							EditorGUI.indentLevel++;
						} else if(item != null) {
							item.Draw();
							if(item.Changed) {
								if(!Helper.AssignValue(item.Info, drawer.target, item.Value)) {
									object value;
									var propDrawer = item as MethodPropertyDrawer;
									if(propDrawer != null && Helper.FetchValue(propDrawer.Info, drawer.target, out value)) {
										propDrawer.Value = value;
										propDrawer.GetException = null;
									} else
										propDrawer.GetException = value as Exception;
								}
							}
						}
					}
					EditorGUILayout.EndVertical();
					EditorGUI.indentLevel--;
				}
			GUILayout.FlexibleSpace();
			GUILayout.Space(EditorGUIUtility.singleLineHeight / 2);
			GUILayout.EndScrollView();
		}
		
		void ShowMenu() {
			var menu = new GenericMenu();
			menu.AddItem(new GUIContent("Update Values"), false, UpdateValues);
			menu.AddItem(new GUIContent("Reload All"), false, RefreshList);
			menu.AddSeparator("");
			menu.AddItem(new GUIContent("Lock Selection"), locked, () => {
				locked = !locked;
				if(!locked)
					OnSelectionChange();
			});
			menu.AddItem(new GUIContent("Update Properties in Edit Mode"), forceUpdateProps, () => {
				forceUpdateProps = !forceUpdateProps;
				UpdateValues();
			});
			menu.AddSeparator("");
			menu.AddItem(new GUIContent("Show Properties"), showProps, () => {
				showProps = !showProps;
				RefreshList();
			});
			menu.AddItem(new GUIContent("Show Methods"), showMethods, () => {
				showMethods = !showMethods;
				RefreshList();
			});
			menu.AddItem(new GUIContent("Show Private Members"), privateFields, () => {
				privateFields = !privateFields;
				RefreshList();
			});
			menu.AddItem(new GUIContent("Show Obsolete Members"), showObsolete, () => {
				showObsolete = !showObsolete;
				RefreshList();
			});
			menu.DropDown(toolbarMenuPos);
		}
		
		void RefreshList() {
			drawers.Clear();
			OnSelectionChange();
		}
		
		void OnSelectionChange() {
			if(!locked)
				instanceIds = Selection.instanceIDs;
			var pendingRemoveDrawers = new List<InspectorDrawer[]>();
			var pendingAddDrawers = new List<InspectorDrawer[]>();
			foreach (var drawer in drawers)
				if (drawer.Length <= 0 || drawer[0].target == null || !instanceIds.Contains(drawer[0].target.GetInstanceID()))
					pendingRemoveDrawers.Add(drawer);
			drawers.RemoveAll(pendingRemoveDrawers.Contains);
			foreach (var instanceID in instanceIds)
				if (drawers.FindIndex(drawer => drawer[0].target.GetInstanceID() == instanceID) < 0)
					pendingAddDrawers.Add(CreateDrawers(instanceID));
			drawers.AddRange(pendingAddDrawers);
			UpdateValues();
			Repaint();
		}
		
		InspectorDrawer[] CreateDrawers(int instanceID) {
			var ret = new List<InspectorDrawer>();
			var target = EditorUtility.InstanceIDToObject(instanceID);
			try {
				ret.Add(CreateDrawer(target, Helper.GetState<bool>(target, true)));
			} catch (Exception ex) {
				Debug.LogException(ex);
			}
			var gameObject = target as GameObject;
			if (gameObject != null)
				foreach (var component in gameObject.GetComponents(typeof(Component))) {
					try {
						ret.Add(CreateDrawer(component, Helper.GetState<bool>(component, false)));
					} catch (Exception ex) {
						Debug.LogException(ex);
					}
				}
			return ret.ToArray();
		}
		
		InspectorDrawer CreateDrawer(UnityEngine.Object target, bool shown) {
			BindingFlags flag = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
			if (privateFields)
				flag |= BindingFlags.NonPublic;
			var drawer = new InspectorDrawer(target);
			var targetType = target.GetType();
			var fields = targetType.GetFields(flag);
			var props = !showProps ? null : targetType.GetProperties(flag).Where(prop => prop.GetIndexParameters().Length == 0).ToArray();
			drawer.isInternalType = !(target is MonoBehaviour) || Attribute.IsDefined(target.GetType(), typeof(ExecuteInEditMode));
			foreach (var field in fields)
				try {
					if (!showObsolete && Attribute.IsDefined(field, typeof(ObsoleteAttribute)))
						continue;
					drawer.drawer.Add(new MethodPropertyDrawer(field.FieldType, Helper.GetMemberName(field, true), field.GetValue(target)) {
						AllowReferenceMode = false,
						Info = field
					});
				} catch (Exception ex) {
					Debug.LogException(ex);
				}
			if (showProps)
				foreach (var prop in props)
					try {
						if (!showObsolete && Attribute.IsDefined(prop, typeof(ObsoleteAttribute)))
							continue;
						drawer.drawer.Add(new MethodPropertyDrawer(prop.PropertyType, Helper.GetMemberName(prop, true), prop.CanRead && EditorApplication.isPlaying ? prop.GetValue(target, null) : null) {
							AllowReferenceMode = false,
							Info = prop,
							Updatable = drawer.isInternalType || Helper.GetState<bool>(prop, true),
							ShowUpdatable = !drawer.isInternalType
						});
					} catch (Exception ex) {
						Debug.LogException(ex);
					}
			if (showMethods)
				drawer.drawer.Add(new ComponentMethodDrawer(target));
			foreach (var d in drawer.drawer)
				d.OnRequireRedraw += Repaint;
			drawer.shown = shown;
			return drawer;
		}
		
		void UpdateValues() {
			UpdateValues(forceUpdateProps || EditorApplication.isPlaying);
		}
		
		void UpdateValues(bool updateProps) {
			foreach (var drawerGroup in drawers)
				foreach (var drawer in drawerGroup)
					foreach (var drawerItem in drawer.drawer) {
						var propDrawer = drawerItem as MethodPropertyDrawer;
						if (propDrawer == null)
							continue;
						var isPropInfo = propDrawer.Info is PropertyInfo;
						if (!drawer.isInternalType && (!updateProps || !propDrawer.Updatable) && isPropInfo)
							continue;
						object value;
						if(Helper.FetchValue(propDrawer.Info, drawer.target, out value)) {
							propDrawer.Value = value;
							propDrawer.GetException = null;
						} else
							propDrawer.GetException = value as Exception;
					}
		}
	}
}