using UnityEngine;
using System;

namespace JLChnToZ.EditorExtensions.UInspectorPlus {
    [CustomInspectorDrawer(typeof(Type), -1)]
    internal class TypeInspectorDrawer: InspectorDrawer {
        public TypeInspectorDrawer(object target, Type targetType, bool shown, bool showProps, bool showPrivateFields, bool showObsolete, bool showMethods) :
            base(target, targetType, shown, showProps, showPrivateFields, showObsolete, showMethods) {
        }

        protected override void Draw(bool readOnly) {
            if(target != null && GUILayout.Button(string.Format("Inspect Static Members of {0}...", target)))
                InspectorChildWindow.OpenStatic(target as Type, true, allowPrivate, false, true, false, null);
            base.Draw(readOnly);
        }
    }
}