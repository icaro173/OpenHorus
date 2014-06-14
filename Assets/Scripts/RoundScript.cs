using System.Linq;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RoundScript : MonoBehaviour {
    // Public
    public string[] allowedLevels = { "pi_rah", "pi_jst", "pi_mar", "pi_ven", "pi_gho", "pi_set" };

    //Private
    private const float roundDuration = 60 * 5;
    private const float postRoundDuration = 20;
    private const int roundPerLevel = 2;
    private int[] warnings = { 60, 30, 10, -1 };

    float roundTime;
    public bool roundStopped { get; private set; }
    public string currentLevel;
    bool startWarning;
    int endWarning, currentWarning = 0;
    int roundsRemaining;

    //Time events
    private delegate void roundTimeCB(float time);
    private static Dictionary<int, roundTimeCB> roundTimeEvents = null;

    public static RoundScript Instance { get; private set; }

    void Awake() {
        Instance = this;
        currentWarning = 0;
        endWarning = warnings[currentWarning];
        startWarning = false;
        roundsRemaining = roundPerLevel;
    }

    void Update() {
        if (Network.isServer) {
            roundTime += Time.deltaTime;

            if (!roundStopped) {
                if (roundDuration - roundTime < endWarning) {
                    ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player,
                                                        endWarning.ToString()+" seconds remaining...", true, true);
                    endWarning = warnings[++currentWarning];
                }
            } else {
                if (!startWarning && postRoundDuration - roundTime < 5) {
                    ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player,
                                                        "Game starts in 5 seconds...", true, true);
                    startWarning = true;
                }
            }


            if (roundTime >= (roundStopped ? postRoundDuration : roundDuration)) {
                roundStopped = !roundStopped;
                if (roundStopped) {
                    networkView.RPC("StopRound", RPCMode.All);
                    ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player,
                                    "Round over!", true, true);
                    roundsRemaining--;

                    if (roundsRemaining == 0)
                        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player,
                                                            "Level will change on the next round.", true, true);
                } else {
                    if (roundsRemaining == 0) {
                        ChangeLevel();
                        Debug.Log("Loaded level is now " + currentLevel);
                        networkView.RPC("ChangeLevelTo", RPCMode.Others, currentLevel);
                    }

                    networkView.RPC("RestartRound", RPCMode.All);
                    ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player,
                                    "Game start!", true, true);
                }
                roundTime = 0;
                startWarning = false;
                currentWarning = 0;
                endWarning = warnings[currentWarning];
            }
        }
    }

    void addTimeEvents_DeathMatch() {
        roundTimeEvents = new Dictionary<int, roundTimeCB>();

        roundTimeEvents.Add(60, (time) => AnnounceTimeLeft((int) time));
    }

    void AnnounceTimeLeft(int time) {
        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player, time.ToString() + " seconds remaining...", true, true);
    }

    [RPC]
    public void ChangeLevelTo(string levelName) {
        ChangeLevelIfNeeded(levelName);
        roundsRemaining = roundPerLevel;
    }

    [RPC]
    public void SyncLevel(string toLevel) {
        SyncAndSpawn(toLevel);
    }

    [RPC]
    public void StopRound() {
        foreach (PlayerScript player in FindObjectsOfType(typeof(PlayerScript)).Cast<PlayerScript>())
            player.Paused = true;
        roundStopped = true;
    }

    [RPC]
    public void RestartRound() {
        if (!ServerScript.Spectating)
            foreach (PlayerScript player in FindObjectsOfType(typeof(PlayerScript)).Cast<PlayerScript>())
                if (player.networkView.isMine)
                    player.networkView.RPC("ImmediateRespawn", RPCMode.All);

        foreach (PlayerScript player in FindObjectsOfType(typeof(PlayerScript)).Cast<PlayerScript>())
            player.Paused = false;

        foreach (LeaderboardEntry entry in NetworkLeaderboard.Instance.Entries) {
            entry.Deaths = 0;
            entry.Kills = 0;
            entry.ConsecutiveKills = 0;
        }

        ChatScript.Instance.ChatLog.ForEach(x => x.Hidden = true);

        roundStopped = false;
    }

    [RPC]
    public void ChangeLevelAndRestart(string toLevelName) {
        ChangeLevelTo(toLevelName);
        RestartRound();
    }

    public void ChangeLevel() {
        ChangeLevelIfNeeded(RandomHelper.InEnumerable(allowedLevels.Except(new[] { RoundScript.Instance.currentLevel })));
    }

    void SyncAndSpawn(string newLevel) {
        ChangeLevelIfNeeded(newLevel);
        SpawnScript.Instance.Spawn();
    }

    public void ChangeLevelIfNeeded(string newLevel) {
        if (Application.loadedLevelName != newLevel) {
            Application.LoadLevel(newLevel);
            ChatScript.Instance.LogChat(Network.player, "Changed level to " + newLevel + ".", true, true);
            RoundScript.Instance.currentLevel = newLevel;
            ServerScript.Instance.SetMap(RoundScript.Instance.currentLevel);
        }
    }


}
