using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System.Threading;

namespace JLChnToZ.EditorExtensions.UInspectorPlus {
    internal class TypeMatcher: IDisposable {
        public Thread bgWorker;
        public event Action OnRequestRedraw;
        private readonly HashSet<Type> searchedTypes = new HashSet<Type>();
        private readonly List<Assembly> pendingAssemblies = new List<Assembly>();
        private AppDomain currentDomain;
        private string searchText = string.Empty;
        private Type[] searchTypeResult = null;

        public string SearchText {
            get => searchText;
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
                $"Type Search Result ({searchTypeResult.Length}):",
                EditorStyles.boldLabel
            );
            GUILayout.Space(8);
            for(int i = 0, l = Math.Min(searchTypeResult.Length, 500); i < l; i++) {
                Type type = searchTypeResult[i];
                temp.text = type.FullName;
                temp.tooltip = type.AssemblyQualifiedName;
                if(GUILayout.Button(temp, EditorStyles.foldout))
                    InspectorChildWindow.OpenStatic(type, true, true, true, true, false, null);
            }
            if(searchTypeResult.Length > 500)
                EditorGUILayout.HelpBox(
                    "Too many results, please try more specific search phase.",
                    MessageType.Warning
                );
            GUILayout.Space(8);
            GUILayout.EndVertical();
        }

        private void InitSearch() {
            if(currentDomain == null) {
                currentDomain = AppDomain.CurrentDomain;
                searchedTypes.UnionWith(
                    from assembly in currentDomain.GetAssemblies()
                    from type in assembly.LooseGetTypes()
                    select type
                );
                currentDomain.AssemblyLoad += OnAssemblyLoad;
                currentDomain.DomainUnload += OnAppDomainUnload;
            }
            if(pendingAssemblies.Count > 0) {
                var buffer = pendingAssemblies.ToArray();
                pendingAssemblies.Clear();
                searchedTypes.UnionWith(
                    from assembly in buffer
                    from type in assembly.LooseGetTypes()
                    select type
                );
            }
        }

        private void OnAssemblyLoad(object sender, AssemblyLoadEventArgs e) {
            pendingAssemblies.Add(e.LoadedAssembly);
        }

        private void OnAppDomainUnload(object sender, EventArgs e) {
            currentDomain = null;
        }

        ~TypeMatcher() => Dispose();

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

        public void Dispose() {
            try {
                if(currentDomain != null) {
                    currentDomain.AssemblyLoad -= OnAssemblyLoad;
                    currentDomain.DomainUnload -= OnAppDomainUnload;
                }
            } catch {
            } finally {
                currentDomain = null;
            }
            try {
                if(bgWorker != null && bgWorker.IsAlive)
                    bgWorker.Abort();
            } catch {
            } finally {
                bgWorker = null;
            }
        }
    }
}