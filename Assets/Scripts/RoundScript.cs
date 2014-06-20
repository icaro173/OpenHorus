using UnityEngine;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

public class RoundScript : MonoBehaviour {
    // Public
    public string[] allowedLevels = { "pi_rah", "pi_jst", "pi_mar", "pi_ven", "pi_gho", "pi_set" };
    private static int lastLevelPrefix = 0;

    // Private
    private const int roundDuration = 60 * 3;
    private const int preRoundDuration = 5;
    private const int postRoundDuration = 15;
    private const int roundPerLevel = 2;
    private const int readyDuration = 5;

    float roundTime;
    public bool roundStopped { get; private set; }
    public string currentLevel;
    int roundsRemaining;

    // Time events
    private delegate void roundTimeCB(float time);
    private static Dictionary<float, roundTimeCB> roundTimeEvents = null;

    public static RoundScript Instance { get; private set; }

    // Unity Functions
    //!? CLIENT & SERVER
    void Awake() {
        Instance = this;
        roundsRemaining = roundPerLevel;
        networkView.group = 1;
    }

    //!? CLIENT & SERVER
    void Update() {
        if (Network.isServer) {
            //Increment round time
            roundTime += Time.deltaTime;

            //Trigger round events
            handleTimeEvents(roundTime);
        }
    }

    IEnumerator OnPlayerConnected(NetworkPlayer player) {
        // If the first player connects, notify and wait a couple second then restart
        if (Network.connections.Length == 1) {
            yield return 0;
            ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "Another player has joined", true, true);
            ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "Starting round in 5 seconds", true, true);
            yield return new WaitForSeconds(readyDuration);
            changeRound();
        }
    }

    // Time Events
    //? SERVER ONLY
    void handleTimeEvents(float time) {
        if (roundTimeEvents == null) {
            return;
        }

        Dictionary<float, roundTimeCB> triggeredEvents = new Dictionary<float, roundTimeCB>();
        foreach (KeyValuePair<float, roundTimeCB> pair in roundTimeEvents) {
            // Time for the event is now or has passed
            if (time >= pair.Key) {
                // Since we cannot alter a list we are currently iterating on, copy it over
                triggeredEvents.Add(pair.Key, pair.Value);
            }
        }

        foreach (KeyValuePair<float, roundTimeCB> pair in triggeredEvents) {
            // Even has triggered, remove from list
            roundTimeEvents.Remove(pair.Key);
            // Run event
            pair.Value(time);
        }
    }

    //? SERVER ONLY
    void setTimeEvents_DeathMatch() {
        roundTimeEvents = new Dictionary<float, roundTimeCB>();

        roundTimeEvents.Add(roundDuration - 60, (time) => announceTimeLeft(60));
        roundTimeEvents.Add(roundDuration - 30, (time) => announceTimeLeft(30));
        roundTimeEvents.Add(roundDuration - 10, (time) => announceTimeLeft(10));
        roundTimeEvents.Add(roundDuration, (time) => postRound());
        roundTimeEvents.Add(roundDuration + postRoundDuration, (time) => endRound(preRoundDuration));
        roundTimeEvents.Add(roundDuration + postRoundDuration + preRoundDuration, (time) => changeRound());
    }

    //? SERVER ONLY
    void announceTimeLeft(int time) {
        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, time.ToString() + " seconds remaining...", true, true);
    }

    //? SERVER ONLY
    void postRound() {
        // Stop the round
        networkView.RPC("setRoundStopped", RPCMode.All, true);
        roundsRemaining--;

        // Pause all players that have a player object
        foreach (KeyValuePair<NetworkPlayer, PlayerRegistry.PlayerInfo> pair in PlayerRegistry.All()) {
            PlayerScript player = pair.Value.Player;
            if (player != null) {
                player.networkView.RPC("setPaused", RPCMode.All, true);
            }
        }

        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "Round over!", true, true);

        // Was this the last round on this map? announce change
        if (roundsRemaining <= 0) {
            ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "Level will change on the next round", true, true);
        }
    }

    //? SERVER ONLY
    void endRound(int timeout) {
        if (roundsRemaining <= 0) {
            // Have the players change level
            ChangeLevelAndRestart(getRandomMap());
        }

        // Announce new round
        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "Starting game in " + timeout + " seconds!", true, true);
    }

    //? SERVER ONLY
    void changeRound() {
        RestartRound();
        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "Game start!", true, true);
    }

    //? SERVER ONLY
    public void RestartRound() {
        // Make sure every user has a player
        SpawnScript.Instance.networkView.RPC("CreatePlayer", RPCMode.All);

        // Wait until all players have synced their registry
        NetworkSync.afterSync("RegisterPlayer", () => {
            foreach (KeyValuePair<NetworkPlayer, PlayerRegistry.PlayerInfo> pair in PlayerRegistry.All()) {
                PlayerScript player = pair.Value.Player;

                // Respawn own player and unpause
                // TODO setting a player to spectator mode should resolve check itself on spawn
                if (!pair.Value.Spectating) {
                    player.networkView.RPC("ImmediateRespawn", RPCMode.All);
                }
                player.networkView.RPC("setPaused", RPCMode.All, false);

                // Clean leaderboard
                NetworkLeaderboard.Instance.networkView.RPC("resetLeaderboard", RPCMode.All);

                // Start round
                networkView.RPC("setRoundStopped", RPCMode.All, false);
            }

            // Reset time events
            roundTime = 0;
            setTimeEvents_DeathMatch();
        });
    }

    // Remote Protocol Calls
    //!? CLIENT & SERVER
    [RPC]
    public void setRoundStopped(bool stopped) {
        roundStopped = stopped;
    }

    //!? CLIENT & SERVER
    [RPC]
    private void ChangeLevelAndRestartRPC(string toLevelName, int levelPrefix) {
        roundsRemaining = roundPerLevel;
        ChangeLevel(toLevelName, levelPrefix);
    }

    // Map loading
    //? SERVER ONLY
    public void ChangeLevelAndRestart(string toLevelName) {
        // Destroy all old calls
        Network.RemoveRPCsInGroup(1);
        networkView.RPC("ChangeLevelAndRestartRPC", RPCMode.AllBuffered, toLevelName, lastLevelPrefix + 1);
    }


    // Server picks new map and loads
    //? SERVER ONLY
    public string getRandomMap() {
        return RandomHelper.InEnumerable(allowedLevels.Except(new[] { RoundScript.Instance.currentLevel }));
    }

    // Load new map
    //!? CLIENT & SERVER
    public void ChangeLevel(string newLevel, int levelPrefix) {
        // Use a new prefix for the next level
        lastLevelPrefix = levelPrefix;

        // Clean the player register, it will be rebuild when the level is loaded
        PlayerRegistry.Clear();

        // Remove al non-leveloading rpcs from the buffers, just in case
        if (Network.isServer) {
            Network.RemoveRPCsInGroup(0);
        }

        // Disable sending
        // Stop recieving
        // Move to new level prefix
        Network.SetSendingEnabled(0, false);
        Network.isMessageQueueRunning = false;
        Network.SetLevelPrefix(levelPrefix);

        // Load the actual level
        Application.LoadLevel(newLevel);
    }

    //!? CLIENT & SERVER
    void OnLevelWasLoaded(int id) {
        // Turn networking back on
        Network.isMessageQueueRunning = true;
        Network.SetSendingEnabled(0, true);

        // Notify and change
        ChatScript.Instance.LogChat(Network.player, "Changed level to " + Application.loadedLevelName, true, true);
        RoundScript.Instance.currentLevel = Application.loadedLevelName;
        ServerScript.Instance.SetMap(RoundScript.Instance.currentLevel);

        // If we are playing, build the player again, register and restart
        if (Network.peerType != NetworkPeerType.Disconnected && Network.isServer) {
            RestartRound();
        }

    }
}
