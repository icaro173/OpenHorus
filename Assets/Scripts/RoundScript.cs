using System.Linq;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RoundScript : MonoBehaviour {
    // Public
    public string[] allowedLevels = { "pi_rah", "pi_jst", "pi_mar", "pi_ven", "pi_gho", "pi_set" };
    private static int lastLevelPrefix = 0;

    // Private
    private const int roundDuration = 60*3;
    private const int preRoundDuration = 5;
    private const int postRoundDuration = 20;
    private const int roundPerLevel = 2;
    private bool doRestart = false;

    float roundTime;
    public bool roundStopped { get; private set; }
    public string currentLevel;
    int roundsRemaining;

    // Time events
    private delegate void roundTimeCB(float time);
    private static Dictionary<float, roundTimeCB> roundTimeEvents = null;

    public static RoundScript Instance { get; private set; }

    // Unity Functions
    void Awake() {
        Instance = this;
        roundsRemaining = roundPerLevel;
        networkView.group = 1;
    }

    void Update() {
        if (Network.isServer) {
            //Increment round time
            roundTime += Time.deltaTime;

            //Trigger round events
            handleTimeEvents(roundTime);
        }
    }

    // Time Events
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

    void addTimeEvents_DeathMatch() {
        roundTimeEvents = new Dictionary<float, roundTimeCB>();

        roundTimeEvents.Add(roundDuration - 60, (time) => announceTimeLeft(60));
        roundTimeEvents.Add(roundDuration - 30, (time) => announceTimeLeft(30));
        roundTimeEvents.Add(roundDuration - 10, (time) => announceTimeLeft(10));
        roundTimeEvents.Add(roundDuration, (time) => postRound());
        roundTimeEvents.Add(roundDuration + postRoundDuration, (time) => endRound(preRoundDuration));
        roundTimeEvents.Add(roundDuration + postRoundDuration + preRoundDuration, (time) => changeRound());
    }

    void announceTimeLeft(int time) {
        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, time.ToString() + " seconds remaining...", true, true);
    }

    void postRound() {
        networkView.RPC("StopRound", RPCMode.All);
        roundsRemaining--;

        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "Round over!", true, true);

        // Was this the last round on this map? announce change
        if (roundsRemaining <= 0) {
            ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "Level will change on the next round.", true, true);
        }
    }

    void endRound(int timeout) {
        if (roundsRemaining <= 0) {
            // Have the players change level
            ChangeLevelAndRestart(getRandomMap());
        }

        // Announce new round
        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "Starting game in " + timeout + " seconds!", true, true);
    }

    void changeRound() {
        networkView.RPC("RestartRound", RPCMode.All);
        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, "Game start!", true, true);
    }

    // Remote Protocol Calls
    [RPC]
    public void StopRound() {
        foreach (PlayerScript player in FindObjectsOfType<PlayerScript>()) {
            player.Paused = true;
        }
        roundStopped = true;
    }

    [RPC]
    public void RestartRound() {
        // Get all player scripts in the current game
        PlayerScript[] players = FindObjectsOfType<PlayerScript>();

        if (!ServerScript.Spectating) {
            // Respawn all players
            foreach (PlayerScript player in players) {
                if (player.networkView.isMine) {
                    player.networkView.RPC("ImmediateRespawn", RPCMode.All);
                }
            }
        }

        // Unpause players
        foreach (PlayerScript player in players) {
            player.Paused = false;
        }

        foreach (LeaderboardEntry entry in NetworkLeaderboard.Instance.Entries) {
            entry.Deaths = 0;
            entry.Kills = 0;
            entry.ConsecutiveKills = 0;
        }

        // Reset time events
        if (Network.isServer) {
            addTimeEvents_DeathMatch();
        }

        // Hide old chat
        ChatScript.Instance.ChatLog.ForEach(x => x.Hidden = true);

        // Start round
        roundTime = 0;
        roundStopped = false;
    }

    public void ChangeLevelAndRestart(string toLevelName)
    {
        // Destroy all old calls
        Network.RemoveRPCsInGroup(0);
        Network.RemoveRPCsInGroup(1);
        networkView.RPC("ChangeLevelAndRestartRCP", RPCMode.OthersBuffered, toLevelName, lastLevelPrefix + 1);
        ChangeLevelAndRestartRCP(toLevelName, lastLevelPrefix + 1);
    }

    // Force map change (used from chat)
    [RPC]
    private void ChangeLevelAndRestartRCP(string toLevelName, int levelPrefix) {
        roundsRemaining = roundPerLevel;
        ChangeLevel(toLevelName, levelPrefix);
        doRestart = true;
    }

    // Map loading
    // Server picks new map and loads
    public string getRandomMap() {
        return RandomHelper.InEnumerable(allowedLevels.Except(new[] { RoundScript.Instance.currentLevel }));
    }

    // Load new map
    public void ChangeLevel(string newLevel, int levelPrefix) {
        lastLevelPrefix = levelPrefix;
        PlayerRegistry.Clear();

        // Disable sending
        // Stop recieving
        // Move to new level prefix
        Network.RemoveRPCs(Network.player);
        Network.SetSendingEnabled(0, false);
        Network.isMessageQueueRunning = false;
        Network.SetLevelPrefix(levelPrefix);

        Debug.Log("ChangeLevel");
        if (Application.loadedLevelName != newLevel) {
            Application.LoadLevel(newLevel);
        } else {
            // Call the callback manually
            OnLevelWasLoaded(Application.loadedLevel);
        }
    }

    void OnLevelWasLoaded(int id) {
        // Turn networking back on
        Network.isMessageQueueRunning = true;
        Network.SetSendingEnabled(0, true);

        // Notify and change
        ChatScript.Instance.LogChat(Network.player, "Changed level to " + Application.loadedLevelName + ".", true, true);
        RoundScript.Instance.currentLevel = Application.loadedLevelName;
        ServerScript.Instance.SetMap(RoundScript.Instance.currentLevel);

        if (Network.peerType != NetworkPeerType.Disconnected) {
            SpawnScript.Instance.CreatePlayer();
        }

        if (doRestart == true) {
            doRestart = false;
            RestartRound();
        }
    }
}
