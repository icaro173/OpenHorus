using System.Collections.Generic;
using UnityEngine;

class PlayerRegistry : MonoBehaviour {

    public class PlayerInfo {
        public string Username;
        public string GUID;
        public Color Color;
        public bool Spectating;
        public PlayerScript Player;
    }

    private readonly Dictionary<NetworkPlayer, PlayerInfo> registry = new Dictionary<NetworkPlayer, PlayerInfo>();
    public static PlayerRegistry Instance;

    void Awake() {
        DontDestroyOnLoad(this);
        Instance = this;
    }

    public static void Clear() {
        Instance.registry.Clear();
    }

    public static PlayerInfo Get(NetworkPlayer player) {
        return Instance.registry[player];
    }

    public static bool Has(NetworkPlayer player) {
        return Instance.registry.ContainsKey(player);
    }

    public static void RegisterCurrentPlayer(string username) {
        Instance.networkView.RPC("RegisterPlayer", RPCMode.OthersBuffered, Network.player, username, Network.player.guid, new Vector3(1, 1, 1), false);
        Instance.RegisterPlayer(Network.player, username, Network.player.guid, new Vector3(1, 1, 1), false);
    }

    [RPC]
    public void RegisterPlayer(NetworkPlayer player, string username, string guid, Vector3 color, bool isSpectating = false) {
        if (registry.ContainsKey(player)) {
            Debug.Log("Tried to register player " + player + " but was already registered. Current username : " + registry[player].Username + " | wanted username : " + username + " (removing...)");
            registry.Remove(player);
        }

        PlayerScript playerData = null;
        foreach (PlayerScript p in FindObjectsOfType<PlayerScript>()) {
            if (p.owner == player) {
                playerData = p;
            }
        }
        playerData.enabled = true;

        registry.Add(player, new PlayerInfo { Username = username, Color = new Color(color.x, color.y, color.z), Spectating = isSpectating, Player = playerData, GUID = guid });
        Debug.Log("Registered other player : " + player + " = " + username + " now");
    }

    [RPC]
    public void UnregisterPlayer(NetworkPlayer player) {
        if (!registry.ContainsKey(player)) {
            Debug.Log("Tried to unregister player " + player + " but was not found");
            return;
        }

        registry.Remove(player);
        Debug.Log("Unregistering player : " + player + " left)");
    }

    public void OnPlayerDisconnected(NetworkPlayer player) {
        networkView.RPC("UnregisterPlayer", RPCMode.All, player);
    }
}
