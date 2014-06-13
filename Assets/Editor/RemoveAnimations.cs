using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RemoveAnimations : Editor {

    [MenuItem("WFH/RemoveAnimations %#g")]
    static void fix() {
        if (Selection.activeGameObject != null) {
            Animation[] animationArray = Selection.activeGameObject.GetComponentsInChildren<Animation>();
            foreach (Animation anim in animationArray) {
                GameObject.DestroyImmediate(anim);
            }
        }
    }
}
