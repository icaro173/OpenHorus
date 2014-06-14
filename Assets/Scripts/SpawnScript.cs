using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SpawnScript : MonoBehaviour {
    public static SpawnScript Instance { get; private set; }

    public GameObject PlayerTemplate;

    string chosenUsername;

    void Awake() {
        Instance = this;
    }

    void OnServerInitialized() {
        Spawn();
    }

    void OnConnectedToServer() {
        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "connected", true, false);
    }

    public void Spawn() {
        Debug.Log("Spawning");
        if (ServerScript.Spectating) return;

        TaskManager.Instance.WaitUntil(_ => PlayerRegistry.Instance != null).Then(() => PlayerRegistry.RegisterCurrentPlayer(chosenUsername, networkView.owner.guid));
        Network.Instantiate(PlayerTemplate, RespawnZone.GetRespawnPoint(), Quaternion.identity, 0);
    }

    void OnPlayerDisconnected(NetworkPlayer player) {
        if (Network.isServer)
            ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, player, "disconnected", true, false);

        Debug.Log("Clean up after player " + player);
        Network.RemoveRPCs(player);
        Network.DestroyPlayerObjects(player);
    }

    void OnDisconnectedFromServer(NetworkDisconnection info) {
        if (Network.isServer)
            Debug.Log("Local server connection disconnected");
        else
            if (info == NetworkDisconnection.LostConnection)
                Debug.Log("Lost connection to the server");
            else
                Debug.Log("Successfully diconnected from the server");

        foreach (PlayerScript p in FindObjectsOfType<PlayerScript>())
            Destroy(p.gameObject);
    }

    public void SetChosenUsername(string chosenUsername) {
        this.chosenUsername = chosenUsername;
    }
}
