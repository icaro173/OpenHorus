using UnityEngine;
using System.Linq;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkView))]
public class NetworkSync : MonoBehaviour {
    private static NetworkSync instance;

    public delegate void syncedDelegate();
    private static Dictionary<string, syncInfo> syncDict = new Dictionary<string, syncInfo>();

    private class syncInfo {
        public Dictionary<NetworkPlayer, bool> syncedPlayers = null;
        public bool synced = false;
        public syncedDelegate cb = null;

        public syncInfo() {
            syncedPlayers = new Dictionary<NetworkPlayer, bool>();

            // Add all currently connected players
            Debug.LogWarning("Creating sync with " + Network.connections.Length + " players");
            foreach (NetworkPlayer player in Network.connections) {
                syncedPlayers.Add(player, false);
            }
        }
    }

	// Use this for initialization
	void Awake () {
        instance = this;
        networkView.group = 2;
	}

    private static void runSynced() {
        Dictionary<string, syncInfo> completedSyncDict = new Dictionary<string, syncInfo>();

        // Check for completion
        foreach (KeyValuePair<string, syncInfo> pair in syncDict) {
            syncInfo info = pair.Value;

            Debug.LogWarning("Waiting for players " + info.syncedPlayers.Count(x => !x.Value));

            // If all players are synced add to completed list
            if (info.cb != null && !info.syncedPlayers.Any(x => !x.Value)) {
                completedSyncDict.Add(pair.Key, info);
            }
        }

        foreach (KeyValuePair<string, syncInfo> pair in completedSyncDict) {
            // Remove from list to check
            stopSync(pair.Key);

            // Call cb
            Debug.LogWarning("runSynced " + pair.Key);
            pair.Value.cb();
        }
    }

    void OnPlayerDisconnected(NetworkPlayer player) {
        foreach (KeyValuePair<string, syncInfo> pair in syncDict) {
            syncInfo info = pair.Value;
            info.syncedPlayers.Remove(player);
        }
    }

    public static void sync(string key) {
        instance.networkView.RPC("syncRPC", RPCMode.Server, key);
    }

    [RPC]
    public void syncRPC(string key, NetworkMessageInfo msgInfo) {
        if (!Network.isServer) {
            Debug.LogError("syncRPC called in client code");
            return;
        }

        if (!syncDict.ContainsKey(key)) {
            Debug.LogError("syncRPC recieved on non-existent key [" + key + "] from " + msgInfo.sender);
            return;
        }

        Debug.LogWarning("syncRPC [" + key + "] from " + msgInfo.sender);

        // Update info
        syncInfo info = syncDict[key];
        info.syncedPlayers[msgInfo.sender] = true;

        // Check for completion
        runSynced();
    }

    public static void createSync(string key) {
        if (!Network.isServer) {
            Debug.LogError("afterSync called in client code");
            return;
        }

        if (syncDict.ContainsKey(key)) {
            Debug.LogError("createSync: sync [" + key + "] already exists");
        } else {
            Debug.LogWarning("createSync [" + key + "]");
            syncInfo info = new syncInfo();
            syncDict.Add(key, info);
        }
    }

    public static void stopSync(string key) {
        Debug.LogWarning("stopSync [" + key + "]");
        if (syncDict.ContainsKey(key)) {
            syncDict.Remove(key);
        } else {
            Debug.LogError("stopSync: sync [" + key + "] does not exist");
        }
    }

    public static void afterSync(string key, syncedDelegate cb) {
        if (!Network.isServer) {
            Debug.LogError("afterSync: called in client code");
            return;
        }

        if (!syncDict.ContainsKey(key)) {
            Debug.LogError("afterSync: sync [" + key + "] does not exist");
            return;
        }

        Debug.LogWarning("afterSync [" + key + "]");

        // Set callback
        syncInfo info = syncDict[key];
        info.cb = cb;

        // Check for completion
        runSynced();
    }
}
