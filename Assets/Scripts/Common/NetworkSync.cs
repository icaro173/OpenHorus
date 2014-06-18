using UnityEngine;
using System.Linq;
using System.Collections.Generic;

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

            // If all players are synced add to completed list
            if (info.cb != null && !info.syncedPlayers.Any(x => !x.Value)) {
                completedSyncDict.Add(pair.Key, info);
            }
        }

        foreach (KeyValuePair<string, syncInfo> pair in completedSyncDict) {
            // Remove from list to check
            syncDict.Remove(pair.Key);

            // Call cb
            pair.Value.cb();
        }
    }

    void OnPlayerConnected(NetworkPlayer player) {
        foreach (KeyValuePair<string, syncInfo> pair in syncDict) {
            syncInfo info = pair.Value;
            info.syncedPlayers.Add(player, false);
        }

        runSynced();
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
    private void syncRPC(string key, NetworkMessageInfo msgInfo) {
        // Create or lookup an dictionary on the key
        syncInfo info = null;
        if (syncDict.ContainsKey(key)) {
            info = syncDict[key];
        } else {
            info = new syncInfo();
            syncDict.Add(key, info);
        }

        // Update info
        info.syncedPlayers[msgInfo.sender] = true;

        runSynced();
    }

    public static void afterSync(string key, syncedDelegate cb) {
        if (!Network.isServer) {
            Debug.LogError("afterSync called in client code");
            return;
        }
        // Create or lookup an dictionary on the key
        syncInfo info = null;
        if (syncDict.ContainsKey(key)) {
            info = syncDict[key];
        } else {
            info = new syncInfo();
            syncDict.Add(key, info);
        }

        // If all players already called Sync on this key, call cb
        // Else set the cb to be called when the last player called
        runSynced();
    }
}
