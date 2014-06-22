using System.Linq;
using UnityEngine;

class LeaderboardViewerScript : MonoBehaviour {
    public GUISkin Skin = null;
    GUIStyle RowStyle, MyRowStyle, MultiRowWindowStyle;

    bool visible;

    void Awake() {
        DontDestroyOnLoad(gameObject);
        MultiRowWindowStyle = new GUIStyle(Skin.window) { padding = { bottom = 0 } };
        RowStyle = new GUIStyle(Skin.box) { };
        MyRowStyle = new GUIStyle(Skin.box) { };
    }

    void Update() {
        visible = Input.GetKey(KeyCode.Tab) || RoundScript.Instance.roundStopped;
    }

    void OnGUI() {
        if (Network.peerType == NetworkPeerType.Disconnected || 
            Network.peerType == NetworkPeerType.Connecting) 
            return;

        GUI.skin = Skin;

        if (visible) {
            int height = NetworkLeaderboard.instance.Entries.Count(x => PlayerRegistry.Has(x.NetworkPlayer) && PlayerRegistry.Get(x.NetworkPlayer).Spectating) * 32;
            GUILayout.Window(3, new Rect(Screen.width - 445, (40) - height / 2, 376, height), BoardWindow, string.Empty, MultiRowWindowStyle);
        }
    }

    void BoardRow(int windowId) {
        LeaderboardEntry log = NetworkLeaderboard.instance.Entries
                                                          .FirstOrDefault(x => x.NetworkPlayer == Network.player);
        if (log == null || !PlayerRegistry.Has(Network.player))
            return;

        GUIStyle rowStyle = RowStyle;
        // rowStyle.normal.textColor = PlayerRegistry.For(log.NetworkPlayer).Color;

        GUILayout.BeginHorizontal();
        GUILayout.Box(PlayerRegistry.Get(log.NetworkPlayer).Username.ToUpper(), rowStyle, GUILayout.MinWidth(125), GUILayout.MaxWidth(125));

        //rowStyle.normal.textColor = Color.white;

        GUILayout.Box(log.Kills.ToString()  + " K", rowStyle, GUILayout.MinWidth(90), GUILayout.MaxWidth(90));
        GUILayout.Box(log.Deaths.ToString() + " D", rowStyle, GUILayout.MinWidth(90), GUILayout.MaxWidth(90));
        GUILayout.Box(log.Ping.ToString()   + " P", rowStyle, GUILayout.MinWidth(90), GUILayout.MaxWidth(90));
        GUILayout.EndHorizontal();
    }

    void BoardWindow(int windowId) {
        foreach (LeaderboardEntry log in NetworkLeaderboard.instance.Entries.OrderByDescending(x => x.Kills)) {
            if (!PlayerRegistry.Has(Network.player))
                continue;
            if (!PlayerRegistry.Has(log.NetworkPlayer) || PlayerRegistry.Get(log.NetworkPlayer).Spectating)
                continue;

            GUIStyle rowStyle = RowStyle;
            if (log.NetworkPlayer == Network.player) {
                rowStyle = MyRowStyle;
            }

            //rowStyle.normal.textColor = PlayerRegistry.For(log.NetworkPlayer).Color;

            GUILayout.BeginHorizontal();
            GUILayout.Box(PlayerRegistry.Get(log.NetworkPlayer).Username.ToUpper(), rowStyle, GUILayout.MinWidth(125), GUILayout.MaxWidth(125));

            // rowStyle.normal.textColor = Color.white;

            GUILayout.Box(log.Kills.ToString()  + " K", rowStyle, GUILayout.MinWidth(90), GUILayout.MaxWidth(90));
            GUILayout.Box(log.Deaths.ToString() + " D", rowStyle, GUILayout.MinWidth(90), GUILayout.MaxWidth(90));
            GUILayout.Box(log.Ping.ToString()   + " P", rowStyle, GUILayout.MinWidth(90), GUILayout.MaxWidth(90));
            GUILayout.EndHorizontal();
        }
    }
}

