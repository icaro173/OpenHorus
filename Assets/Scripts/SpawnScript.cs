using UnityEngine;

public class SpawnScript : MonoBehaviour {
    public static SpawnScript Instance { get; private set; }

    public GameObject PlayerTemplate;
    private string chosenUsername;

    void Awake() {
        Instance = this;
        networkView.group = 1;
    }

    // TODO: Move
    void OnConnectedToServer() {
        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "connected", true, false);
    }

    [RPC]
    public void CreatePlayer() {
        if (!ServerScript.Spectating && (!PlayerRegistry.Has(Network.player) || PlayerRegistry.Get(Network.player).Player == null)) {
            Debug.Log("Creating player object for: " + Network.player);
            GameObject obj = Network.Instantiate(PlayerTemplate, RespawnZone.GetRespawnPoint(), Quaternion.identity, 0) as GameObject;
            obj.networkView.RPC("setOwner", RPCMode.AllBuffered, Network.player);
            PlayerRegistry.RegisterCurrentPlayer(chosenUsername);
        }

        if (Network.isClient) {
            NetworkSync.sync("RegisterPlayer");
        }
    }

    void OnPlayerDisconnected(NetworkPlayer player) {
        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, player, "disconnected", true, false);
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
                Debug.Log("Successfully disconnected from the server");
            }
        }
    }

    public void SetChosenUsername(string chosenUsername) {
        this.chosenUsername = chosenUsername;
    }
}
