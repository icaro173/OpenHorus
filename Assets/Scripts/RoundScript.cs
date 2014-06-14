using System.Linq;
using UnityEngine;
using System.Collections;

public class RoundScript : MonoBehaviour {
    const float RoundDuration = 60 * 5;
    const float PauseDuration = 20;
    const int SameLevelRounds = 2;
    int[] warnings = { 60, 30, 10, -1 };
    public string[] allowedLevels = { "pi_rah", "pi_jst", "pi_mar", "pi_ven", "pi_gho", "pi_set" };

    float sinceRoundTransition;
    public bool RoundStopped { get; private set; }
    public string CurrentLevel { get; set; }
    float sinceInteround;
    bool startWarning;
    int endWarning, currentWarning = 0;
    int toLevelChange;

    public static RoundScript Instance { get; private set; }

    void Awake() {
        Instance = this;
        currentWarning = 0;
        endWarning = warnings[currentWarning];
        startWarning = false;
        toLevelChange = SameLevelRounds;
    }

    void Update() {
        if (Network.isServer) {
            sinceRoundTransition += Time.deltaTime;

            if (!RoundStopped) {
                if (RoundDuration - sinceRoundTransition < endWarning) {
                    ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player,
                                                        endWarning.ToString()+" seconds remaining...", true, true);
                    endWarning = warnings[++currentWarning];
                }
            } else {
                if (!startWarning && PauseDuration - sinceRoundTransition < 5) {
                    ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player,
                                                        "Game starts in 5 seconds...", true, true);
                    startWarning = true;
                }
            }


            if (sinceRoundTransition >= (RoundStopped ? PauseDuration : RoundDuration)) {
                RoundStopped = !RoundStopped;
                if (RoundStopped) {
                    networkView.RPC("StopRound", RPCMode.All);
                    ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player,
                                    "Round over!", true, true);
                    toLevelChange--;

                    if (toLevelChange == 0)
                        ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player,
                                                            "Level will change on the next round.", true, true);
                } else {
                    if (toLevelChange == 0) {
                        ChangeLevel();
                        Debug.Log("Loaded level is now " + CurrentLevel);
                        networkView.RPC("ChangeLevelTo", RPCMode.Others, CurrentLevel);
                    }

                    networkView.RPC("RestartRound", RPCMode.All);
                    ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, Network.player,
                                    "Game start!", true, true);
                }
                sinceRoundTransition = 0;
                startWarning = false;
                currentWarning = 0;
                endWarning = warnings[currentWarning];
            }
        }
    }

    [RPC]
    public void ChangeLevelTo(string levelName) {
        ChangeLevelIfNeeded(levelName);
        toLevelChange = SameLevelRounds;
    }

    [RPC]
    public void SyncLevel(string toLevel) {
        SyncAndSpawn(toLevel);
    }

    [RPC]
    public void StopRound() {
        foreach (PlayerScript player in FindObjectsOfType(typeof(PlayerScript)).Cast<PlayerScript>())
            player.Paused = true;
        RoundStopped = true;
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

        RoundStopped = false;
    }

    [RPC]
    public void ChangeLevelAndRestart(string toLevelName) {
        ChangeLevelTo(toLevelName);
        RestartRound();
    }

    public void ChangeLevel() {
        ChangeLevelIfNeeded(RandomHelper.InEnumerable(allowedLevels.Except(new[] { RoundScript.Instance.CurrentLevel })));
    }

    void SyncAndSpawn(string newLevel) {
        ChangeLevelIfNeeded(newLevel);
        SpawnScript.Instance.Spawn();
    }

    public void ChangeLevelIfNeeded(string newLevel) {
        if (Application.loadedLevelName != newLevel) {
            Application.LoadLevel(newLevel);
            ChatScript.Instance.LogChat(Network.player, "Changed level to " + newLevel + ".", true, true);
            RoundScript.Instance.CurrentLevel = newLevel;
            ServerScript.Instance.SetMap(RoundScript.Instance.CurrentLevel);
        }
    }


}
