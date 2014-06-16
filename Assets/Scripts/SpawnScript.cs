using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SpawnScript : MonoBehaviour {
    public static SpawnScript Instance { get; private set; }

    public GameObject PlayerTemplate;
    private string chosenUsername;

    void Awake() {
        Instance = this;
    }

    // TODO: Move
    void OnConnectedToServer() {
        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "connected", true, false);
    }

    public void CreatePlayer() {
        if (ServerScript.Spectating || PlayerRegistry.Has(Network.player)) return;

        Network.Instantiate(PlayerTemplate, RespawnZone.GetRespawnPoint(), Quaternion.identity, 0);
        PlayerRegistry.RegisterCurrentPlayer(chosenUsername);
    }

    void OnPlayerDisconnected(NetworkPlayer player) {
        Debug.Log("Clean up after player " + player);
        Network.RemoveRPCs(player);
        Network.DestroyPlayerObjects(player);
    }

    void OnDisconnectedFromServer(NetworkDisconnection info) {
        if (Network.isServer) {
            Debug.Log("Local server connection disconnected");
        } else {
            if (info == NetworkDisconnection.LostConnection) {
                Debug.Log("Lost connection to the server");
            } else {
                Debug.Log("Successfully diconnected from the server");
            }
        }

        foreach (PlayerScript p in FindObjectsOfType<PlayerScript>()) {
            Destroy(p.gameObject);
        }
    }

    public void SetChosenUsername(string chosenUsername) {
        this.chosenUsername = chosenUsername;
    }
}
