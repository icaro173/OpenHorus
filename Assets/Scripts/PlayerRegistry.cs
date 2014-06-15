using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

class PlayerRegistry : MonoBehaviour {
    readonly Dictionary<NetworkPlayer, PlayerInfo> registry = new Dictionary<NetworkPlayer, PlayerInfo>();

    public static PlayerRegistry Instance;

    public static PlayerInfo For(NetworkPlayer player) {
        return Instance.registry[player];
    }

    public static bool Has(NetworkPlayer player) {
        return Instance != null && Instance.registry.ContainsKey(player);
    }

    void Awake() {
        if (Instance != null)
            DestroyImmediate(Instance.gameObject);
        DontDestroyOnLoad(this);
        Instance = this;
    }

    public static NetworkPlayer For(Transform player) {
        for (int i = 0; i < PlayerRegistry.Instance.registry.Count; i++) {
            PlayerRegistry.PlayerInfo otherPlayer = PlayerRegistry.Instance.registry.ElementAt(i).Value;
            if (otherPlayer.Location == player) {
                return PlayerRegistry.Instance.registry.ElementAt(i).Key;
            }
        }

        return PlayerRegistry.Instance.registry.ElementAt(0).Key; // SHOULD NEVER HAPPEN!!!
    }

    int ConnectedCount() {
        return registry.Values.Count(x => !x.Disconnected);
    }

    public static void RegisterCurrentPlayer(string username, string guid) {
        Instance.networkView.RPC("RegisterPlayer", RPCMode.All, Network.player, username, guid);
    }

    [RPC]
    public void RegisterPlayer(NetworkPlayer player, string username, string guid) {
        Color color = Color.white;
        if (registry.ContainsKey(player)) {
            Debug.Log("Tried to register player " + player + " but was already registered. Current username : " + registry[player].Username + " | wanted username : " + username);
            registry.Remove(player);
        }

        PlayerScript playerData = null;
        foreach (PlayerScript p in FindObjectsOfType<PlayerScript>()) {
            if (p.owner == player) {
                playerData = p;
            }

        }
        playerData.enabled = true;
        Transform location = playerData.transform;

        registry.Add(player, new PlayerInfo { Username = username, Color = color, Location = location, GUID = guid });
        Debug.Log("Registered this player : " + player + " = " + username + " (" + ConnectedCount() + " now)");
    }
    [RPC]
    public void RegisterPlayerFull(NetworkPlayer player, string username, string guid, Vector3 color, bool isSpectating) {
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
        Transform location = playerData.transform;

        registry.Add(player, new PlayerInfo { Username = username, Color = new Color(color.x, color.y, color.z), Spectating = isSpectating, Location = location, GUID = guid });
        Debug.Log("Registered other player : " + player + " = " + username + " (" + ConnectedCount() + " now)");
    }

    [RPC]
    public void UnregisterPlayer(NetworkPlayer player) {
        if (!registry.ContainsKey(player)) {
            Debug.Log("Tried to unregister player " + player + " but was not found");
            return;
        }

        registry[player].Disconnected = true;
        Debug.Log("Unregistering player : " + player + " (" + ConnectedCount() + " left)");
    }

    void OnNetworkInstantiate(NetworkMessageInfo info) {
        if (!Network.isServer) {
            networkView.RPC("RequestRegister", RPCMode.Server, Network.player);
        }
    }

    [RPC]
    public void RequestRegister(NetworkPlayer player) {
        Debug.Log("Propagating player registry to player " + player);

        foreach (NetworkPlayer otherPlayer in registry.Keys) {
            if (otherPlayer != player) {
                PlayerInfo info = registry[otherPlayer];
                if (info.Disconnected) continue;

                Debug.Log("RegisterPlayerFull");
                networkView.RPC("RegisterPlayerFull",
                                player,
                                otherPlayer,
                                info.Username,
                                info.GUID,
                                new Vector3(info.Color.r, info.Color.g, info.Color.b),
                                info.Spectating);

                if (info.Spectating) {
                    foreach (PlayerScript p in FindObjectsOfType<PlayerScript>()) {
                        if (p.networkView.owner == otherPlayer) {
                            p.GetComponent<HealthScript>().networkView.RPC("ToggleSpectate", player, true);
                        }
                    }
                }
            }
        }
    }

    public void OnPlayerDisconnected(NetworkPlayer player) {
        networkView.RPC("UnregisterPlayer", RPCMode.All, player);
    }

    public void Clear() {
        Destroy(gameObject);
        Instance = null;
    }

    public class PlayerInfo {
        public string Username;
        public string GUID;
        public Color Color;
        public bool Spectating;
        public bool Disconnected;
        public Transform Location;
    }
}
