using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System.Threading;

namespace UInspectorPlus {
    internal class TypeMatcher {
        public Thread bgWorker;
        public event Action OnRequestRedraw;
        private readonly HashSet<Type> searchedTypes = new HashSet<Type>();
        private string searchText = string.Empty;
        private Type[] searchTypeResult = null;

        public string SearchText {
            get { return searchText; }
            set {
                if(searchText == value) return;
                searchText = value;
                if(bgWorker == null)
                    bgWorker = new Thread(DoSearch) {
                        IsBackground = true,
                    };
                if(!bgWorker.IsAlive)
                    bgWorker.Start();
            }
        }

        public void Draw() {
            if(searchTypeResult == null || searchTypeResult.Length == 0) return;
            GUIContent temp = new GUIContent();
            GUILayout.BeginVertical();
            GUILayout.Space(8);
            GUILayout.Label(
                string.Format("Type Search Result ({0}):", searchTypeResult.Length),
                EditorStyles.boldLabel
            );
            GUILayout.Space(8);
            int i = 0;
            foreach(Type type in searchTypeResult) {
                if(i++ >= 500) break;
                temp.text = type.FullName;
                temp.tooltip = type.AssemblyQualifiedName;
                if(GUILayout.Button(temp, EditorStyles.foldout))
                    InspectorChildWindow.OpenStatic(type, true, true, true, true, false, null);
            }
            if(searchTypeResult.Length != i)
                EditorGUILayout.HelpBox(
                    "Too many results, please try more specific search phase.",
                    MessageType.Warning
                );
            GUILayout.Space(8);
            GUILayout.EndVertical();
        }

        private void InitSearch() {
            if(searchedTypes.Count > 0) return;
            searchedTypes.UnionWith(
                from assembly in AppDomain.CurrentDomain.GetAssemblies()
                from type in Helper.LooseGetTypes(assembly)
                select type
            );
        }

        private void DoSearch() {
            try {
                InitSearch();
                var searchText = this.searchText;
                while(true) {
                    Thread.Sleep(100);
                    List<Type> searchTypeResult = new List<Type>();
                    if(!string.IsNullOrEmpty(searchText))
                        foreach(Type type in searchedTypes) {
                            if(searchText != this.searchText) break;
                            if(type.AssemblyQualifiedName.Contains(searchText))
                                searchTypeResult.Add(type);
                        }
                    if(searchText == this.searchText) {
                        this.searchTypeResult = searchTypeResult.ToArray();
                        break;
                    } else {
                        searchText = this.searchText;
                    }
                }
                EditorApplication.update += RequestRedraw;
            } catch(Exception ex) {
                Helper.PrintExceptionsWithInner(ex);
            } finally {
                bgWorker = null;
            }
        }

        private void RequestRedraw() {
            EditorApplication.update -= RequestRedraw;
            if(OnRequestRedraw != null) OnRequestRedraw.Invoke();
        }
    }
}
