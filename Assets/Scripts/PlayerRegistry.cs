﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

class PlayerRegistry : MonoBehaviour {
    readonly Dictionary<NetworkPlayer, PlayerInfo> registry = new Dictionary<NetworkPlayer, PlayerInfo>();
    bool disposed;

    public static PlayerRegistry Instance;

    public static PlayerInfo For(NetworkPlayer player) {
        return Instance.registry[player];
    }

    public static bool Has(NetworkPlayer player) {
        return Instance.registry.ContainsKey(player);
    }

    void Awake() {
        Instance = this;
    }

    public static NetworkPlayer For(Transform player) {
        for (int i = 0; i < PlayerRegistry.Instance.registry.Count; i++) {
            var otherPlayer = PlayerRegistry.Instance.registry.ElementAt(i).Value;
            if (otherPlayer.Location == player) return PlayerRegistry.Instance.registry.ElementAt(i).Key;
        }

        return PlayerRegistry.Instance.registry.ElementAt(0).Key; // SHOULD NEVER HAPPEN!!!
    }

    int ConnectedCount() {
        return registry.Values.Count(x => !x.Disconnected);
    }

    void OnNetworkInstantiate(NetworkMessageInfo info) {
        DontDestroyOnLoad(gameObject);
    }

    public static void RegisterCurrentPlayer(string username, string guid) {
        Instance.networkView.RPC("RegisterPlayer", RPCMode.All, Network.player, username, guid);
    }

    [RPC]
    public void RegisterPlayer(NetworkPlayer player, string username, string guid) {
        if (disposed) {
            Debug.LogError("Should not access disposed object");
            return;
        }

        var color = Color.white;
        if (registry.ContainsKey(player)) {
            Debug.Log("Tried to register player " + player + " but was already registered. Current username : " + registry[player].Username + " | wanted username : " + username);
            registry.Remove(player);
        }

        PlayerScript playerData = null;
        foreach (PlayerScript p in FindObjectsOfType(typeof(PlayerScript)) as PlayerScript[])
            if (p.owner == player) playerData = p;
        playerData.enabled = true;
        Transform location = playerData.transform;

        registry.Add(player, new PlayerInfo { Username = username, Color = color, Location = location, GUID = guid });
        Debug.Log("Registered this player : " + player + " = " + username + " (" + ConnectedCount() + " now)");
    }
    [RPC]
    public void RegisterPlayerFull(NetworkPlayer player, string username, string guid, float r, float g, float b, bool isSpectating) {
        if (disposed) {
            Debug.LogError("Should not access disposed object");
            return;
        }

        if (registry.ContainsKey(player)) {
            Debug.Log("Tried to register player " + player + " but was already registered. Current username : " + registry[player].Username + " | wanted username : " + username + " (removing...)");
            registry.Remove(player);
        }

        PlayerScript playerData = null;
        foreach (PlayerScript p in FindObjectsOfType(typeof(PlayerScript)) as PlayerScript[])
            if (p.owner.Value == player) playerData = p;
        playerData.enabled = true;
        Transform location = playerData.transform;

        registry.Add(player, new PlayerInfo { Username = username, Color = new Color(r, g, b), Spectating = isSpectating, Location = location, GUID = guid });
        Debug.Log("Registered other player : " + player + " = " + username + " (" + ConnectedCount() + " now)");
    }

    [RPC]
    public void UnregisterPlayer(NetworkPlayer player) {
        if (disposed) {
            Debug.LogError("Should not access disposed object");
            return;
        }

        if (!registry.ContainsKey(player)) {
            Debug.Log("Tried to unregister player " + player + " but was not found");
            return;
        }

        registry[player].Disconnected = true;
        Debug.Log("Unregistering player : " + player + " (" + ConnectedCount() + " left)");
    }

    public void OnPlayerConnected(NetworkPlayer player) {
        Debug.Log("Propagating player registry to player " + player);

        /*while( !Has( player ) )
        {
            yield return new WaitForSeconds( 0.02f );
        }*/
        // IEnumerator

        foreach (var otherPlayer in registry.Keys)
            if (otherPlayer != player) {
                var info = registry[otherPlayer];
                if (info.Disconnected)
                    continue;

                networkView.RPC("RegisterPlayerFull", player, otherPlayer, info.Username, info.GUID,
                                info.Color.r, info.Color.g, info.Color.b,
                                info.Spectating);

                if (info.Spectating)
                    foreach (var p in FindObjectsOfType(typeof(PlayerScript)).Cast<PlayerScript>())
                        if (p.networkView.owner == otherPlayer)
                            p.GetComponent<HealthScript>().networkView.RPC("ToggleSpectate", player, true);
            }
    }

    /*public static string GetLowestGUID()
    {
        string lowestGUID = "";
        long lowestGUIDValue = 1000000000000000;
		
        foreach( var otherPlayer in PlayerRegistry.Instance.registry.Keys )
        {
            long guidValue = otherPlayer.guid.Sum( c => c - '0' );
            if( lowestGUIDValue > guidValue )
            {
                lowestGUIDValue = guidValue;
                lowestGUID = otherPlayer.guid;
            }
        }
		
        return lowestGUID;
    }*/

    public void OnPlayerDisconnected(NetworkPlayer player) {
        networkView.RPC("UnregisterPlayer", RPCMode.All, player);
    }

    public void Clear() {
        disposed = true;
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
