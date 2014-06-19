using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;

using System.Linq;
using System.Diagnostics;



[InitializeOnLoad]
public class RPCDebugger : EditorWindow {
    static Dictionary<string, int> rpcCounter = new Dictionary<string, int>();
    static List<BufferedRPC> rpcBuffer = new List<BufferedRPC>();
    static int rpcOrder = 0;
    static bool showCount = false;

    class BufferedRPC {
        public string name;
        public bool added;
        public StackTrace trace;
        public int order = 0;
    }

    // Window data
    private Vector2 scollView = Vector2.zero;

    static RPCDebugger() {
        Application.RegisterLogCallback(HandleLog);
        EditorApplication.playmodeStateChanged += playmodeStateChanged;
    }

    // Add menu named "RPC Debugger" to the Window menu
    [MenuItem("Window/RPC Debugger")]
    static void Init() {
        // Get existing open window or if none, make a new one:
        EditorWindow.GetWindow<RPCDebugger>();
    }

    void OnGUI() {
        GUIStyle addStyle = new GUIStyle() { normal = { textColor = Color.black } };
        GUIStyle removeStyle = new GUIStyle() { normal = { textColor = Color.black } };

        EditorGUILayout.BeginVertical();
        showCount = EditorGUILayout.Foldout(showCount, "RPC Count");
        if (showCount) {
            foreach (KeyValuePair<string, int> rpc in rpcCounter) {
                EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PrefixLabel(rpc.Key);
                    EditorGUILayout.LabelField(rpc.Value.ToString(), EditorStyles.textArea);
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndVertical();

        Rect vert = EditorGUILayout.BeginVertical();
        GUI.Box(vert, "");

        scollView = EditorGUILayout.BeginScrollView(scollView, false, true);
        foreach (BufferedRPC rpc in rpcBuffer) {
            if (rpc.added) {
                EditorGUILayout.LabelField("+ " + rpc.name, addStyle);
            } else {
                EditorGUILayout.LabelField("- " + rpc.name, removeStyle);
            }
            EditorGUILayout.Space();
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    static void HandleLog(string logString, string stackTrace, LogType type) {
        calledRPC(logString);
        addBufferedRPC(logString, stackTrace);
        removeBufferedRPC(logString, stackTrace);
    }

    static void calledRPC(string log) {
        const string regexp = "Sent RPC call '([^\']*)'";
        Match match = Regex.Match(log, regexp);
        if (match.Length < 1) return;
        string func = match.Groups[1].Value;

        if (rpcCounter.ContainsKey(func)) {
            rpcCounter[func]++;
        } else {
            rpcCounter[func] = 1;
        }
    }

    static void addBufferedRPC(string log, string stackTrace) {
        // Sent RPC call 'ImmediateRespawn' to all connected clients
        const string regexp = "Added RPC '([^\']*)' to buffer";
        Match match = Regex.Match(log, regexp);
        if (match.Length < 1) return;
        string func = match.Groups[1].Value;
        rpcBuffer.Add(new BufferedRPC() {
            name = func,
            added = true,
            trace = new StackTrace(),
            order = rpcOrder
        });
        rpcOrder++;
    }

    static void removeBufferedRPC(string log, string stackTrace) {
        // RPC __RPCNetworkInstantiate with AllocatedID: 1, player ID 0 and group 0, removed from RPC buffer.
        const string instantiate_regexo = "RPC ([^\\s]*) with AllocatedID: (\\d*), player ID (\\d*) and group (\\d*), removed from RPC buffer.";
        Match match = Regex.Match(log, instantiate_regexo);
        if (match.Length < 1) {
            // RPC ChangeLevelAndRestartRPC with SceneID: 2 Level Prefix: 0, player ID 0 and group 1, removed from RPC buffer.
            const string rpc_regexo = "RPC ([^\\s]*) with SceneID: (\\d*) Level Prefix: (\\d*), player ID (\\d*) and group (\\d*), removed from RPC buffer.";
            match = Regex.Match(log, rpc_regexo);
        }

        if (match.Length > 0) {
            string func = match.Groups[1].Value;
            rpcBuffer.Add( new BufferedRPC() {
                name = func,
                added = false,
                trace = new StackTrace(),
                order = rpcOrder
            });
            rpcOrder++;
        }
    }

    void OnInspectorUpdate() {
        Repaint();
    }

    static void playmodeStateChanged() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            rpcCounter.Clear();
            rpcBuffer.Clear();
            rpcOrder = 0;
        }
    }
}