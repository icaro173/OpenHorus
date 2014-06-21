using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LeaderboardEntry {
    public NetworkPlayer NetworkPlayer;
    public int Kills;
    public int Deaths;
    public int Ping;
    public int ConsecutiveKills;
}

class NetworkLeaderboard : MonoBehaviour {
    public List<LeaderboardEntry> Entries = new List<LeaderboardEntry>();
    public static NetworkLeaderboard instance { get; private set; }
    private Dictionary<int, string> killStreakMessages = null;

    void Awake() {
        DontDestroyOnLoad(gameObject);
        instance = this;
        killStreakMessages = new Dictionary<int, string>();

        killStreakMessages.Add(3, "is threatening!");
        killStreakMessages.Add(6, "is dangerous!");
        killStreakMessages.Add(9, "is merciless!");
    }

    void Update() {
        if (Network.isServer) {
            foreach (LeaderboardEntry entry in Entries)
                entry.Ping = Network.GetLastPing(entry.NetworkPlayer);
        }

        // update colors
        bool isFirst = true;
        bool isSecond = false;
        foreach (LeaderboardEntry entry in Entries.OrderByDescending(x => x.Kills)) {
            if (!PlayerRegistry.Has(Network.player) ||
                !PlayerRegistry.Has(entry.NetworkPlayer))
                continue;

            PlayerRegistry.PlayerInfo player = PlayerRegistry.Get(entry.NetworkPlayer);
            if (isSecond)
                player.Color = new Color(114 / 255f, 222 / 255f, 194 / 255f); // cyan
            else if (isFirst)
                player.Color = new Color(255 / 255f, 166 / 255f, 27 / 255f); // orange
            else
                player.Color = new Color(226f / 255, 220f / 255, 198f / 255); // blanc cassé

            if (isFirst) {
                isSecond = true;
                isFirst = false;
            } else
                isSecond = false;
        }
    }

    void OnSerializeNetworkView(BitStream stream, NetworkMessageInfo info) {
        // Sync entry count
        int entryCount = stream.isWriting ? Entries.Count : 0;
        stream.Serialize(ref entryCount);

        // Tidy up collection size
        if (stream.isReading) {
            while (Entries.Count < entryCount) Entries.Add(new LeaderboardEntry());
            while (Entries.Count > entryCount) Entries.RemoveAt(Entries.Count - 1);
        }

        // Sync entries
        foreach (LeaderboardEntry entry in Entries) {
            stream.Serialize(ref entry.NetworkPlayer);
            stream.Serialize(ref entry.Kills);
            stream.Serialize(ref entry.Deaths);
            stream.Serialize(ref entry.Ping);
            stream.Serialize(ref entry.ConsecutiveKills);
        }
    }

    [RPC]
    public void resetLeaderboard() {
        foreach (LeaderboardEntry entry in Entries) {
            entry.Deaths = 0;
            entry.Kills = 0;
            entry.ConsecutiveKills = 0;
        }
    }

    [RPC]
    public void RegisterKill(NetworkPlayer shooter, NetworkPlayer victim) {
        if (!Network.isServer) return;

        LeaderboardEntry entry;
        entry = Entries.FirstOrDefault(x => x.NetworkPlayer == victim);
        bool endedSpree = false;
        if (entry != null) {
            entry.Deaths++;
            if (entry.ConsecutiveKills >= 3)
                endedSpree = true;
            entry.ConsecutiveKills = 0;
        }

        if (victim == shooter) {
            ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, shooter, "committed suicide", true, false);
        } else {
            ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, shooter, "killed " + (endedSpree ? "and stopped " : "") + PlayerRegistry.Get(victim).Username.ToUpper(), true, false);
            entry = Entries.FirstOrDefault(x => x.NetworkPlayer == shooter);
            if (entry != null) {
                entry.Kills++;
                entry.ConsecutiveKills++;
                KillStreak(shooter, entry.ConsecutiveKills);
            }
        }
    }

    public void OnPlayerConnected(NetworkPlayer player) {
        Entries.Add(new LeaderboardEntry {
            Ping = Network.GetLastPing(player),
            NetworkPlayer = player
        });
    }
    void OnPlayerDisconnected(NetworkPlayer player) {
        Entries.RemoveAll(x => x.NetworkPlayer == player);
    }

    public void Clear() {
        Entries.Clear();
    }

    public void KillStreak(NetworkPlayer player, int streakCount) {
        if (killStreakMessages != null && killStreakMessages.ContainsKey(streakCount)) {
            ChatScript.Instance.networkView.RPC("LogChat", RPCMode.All, player, killStreakMessages[streakCount], true, false);
        }
    }
}
