using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using UnityEngine;

public class ServerScript : MonoBehaviour {
    public static ServerScript Instance { get; private set; }

    //Public
    public const int port = 31414;
    public const string buildVersion = "23062014";
    public const string MasterServerUri = "http://ohs.padrepio.in/";

    public const string natFacilitatorIP = "108.61.103.200"; // Padrepio.in
    public const int natFacilitatorPort = 50005;

    public bool lanMode = false;
    public GUISkin guiSkin;
    public Texture2D logo;

    public static bool Spectating;
    public static HostingState hostState {
        get { return _hostState; }
        set {
            HostingState oldstate = _hostState;
            _hostState = value;
            hostStateChanged(oldstate, _hostState);
        }
    }

    //Private
    private const int MaxPlayers = 6;
    private const int RefreshTime = 15;

    private ServerList serverList = null;
    private string serverToken;
    private ServerInfo currentServer;
    private string chosenUsername = "Anon";
    private static HostingState _hostState = HostingState.Startup;
    private delegate void stateChangedCB(HostingState oldstate, HostingState currentstate);
    private static Dictionary<HostingState, stateChangedCB> hostStateCallbacks = null;

    private static JsonSerializerSettings jsonSettings = new Newtonsoft.Json.JsonSerializerSettings {
        TypeNameHandling = TypeNameHandling.Auto,
        Formatting = Formatting.None,
        PreserveReferencesHandling = PreserveReferencesHandling.None
    };

    private float sinceRefreshedPlayers;
    private string currentStatus = "";
    private bool masterIsDown = false;
    private bool justLeft = false;
    private WebClient web;

    class ServerList {
        public string Message = null;
        public int Connections = 0;
        public int Activegames = 0;
        public ServerInfo[] Servers = null;
    }

    class ServerItem {
        public int Status;
        public string Token;
        public ServerInfo Info;
    }

    class ServerInfo {
        public string GUID = "";
        public string Map = "";
        public string Version = "";
        public int CurrentPlayers = 0;
        public int MaxPlayers = 0;

        [JsonIgnore]
        public bool ConnectionFailed;

        public override string ToString() {
            return JsonConvert.SerializeObject(this, jsonSettings);
        }
    }

    public enum HostingState {
        Startup,
        WaitingForInitialServers,
        WaitingForInput,
        ChoosingServer,
        Connecting,
        ReadyToHost,
        AttemptingToHost,
        Hosting,
        Connected
    }

    public void SetMap(string currentMap) {
        if (currentServer != null) {
            currentServer.Map = currentMap;
        }
    }

    void Awake() {
        // Set global instance
        Instance = this;

        // Make sure the owning gameObject is preserved between level loads
        DontDestroyOnLoad(gameObject);

        // Set target frame rate for the game
        Application.targetFrameRate = -1;

        // Set NAT Facilitator data
        Network.natFacilitatorIP = natFacilitatorIP;
        Network.natFacilitatorPort = natFacilitatorPort;

        // Setup state changes
        createStateChangeCallbacks();

        // Create web client for communication with Master Server
        web = new WebClient();
    }

    void Start() {
        // Set the username, or use default
        chosenUsername = PlayerPrefs.GetString("username", "Anon");

        // Select a random level as background and map for hosting
        RoundScript.Instance.currentLevel = RandomHelper.InEnumerable(RoundScript.Instance.allowedLevels);
        RoundScript.Instance.ChangeLevel(RoundScript.Instance.currentLevel, 0);

        // Startup the state chain
        hostState = HostingState.Startup;
    }

    static void hostStateChanged(HostingState oldstate, HostingState currentstate) {
        if (hostStateCallbacks != null && hostStateCallbacks.ContainsKey(currentstate)) {
            hostStateCallbacks[currentstate](oldstate, currentstate);
        }
    }

    void createStateChangeCallbacks() {
        hostStateCallbacks = new Dictionary<HostingState, stateChangedCB>();

        hostStateCallbacks.Add(HostingState.Startup, (old, current) => QueryServerList());
        hostStateCallbacks.Add(HostingState.ChoosingServer, onChoosingServer);
        hostStateCallbacks.Add(HostingState.Connecting, onConnecting);
        hostStateCallbacks.Add(HostingState.ReadyToHost, onReadyToHost);

        // UI messages
        hostStateCallbacks.Add(HostingState.AttemptingToHost, (old, current) => currentStatus = "Attempting to host...");
        hostStateCallbacks.Add(HostingState.WaitingForInput, (old, current) => {
            if (lanMode) {
                currentStatus = "Lan Mode - Master server is disabled";
            } else if (masterIsDown) {
                currentStatus = "The master server is down";
            } else if (serverList != null) {
                currentStatus = "Server activity : " + serverList.Connections + " players in " + serverList.Activegames + " games";
            }
        });
        hostStateCallbacks.Add(HostingState.WaitingForInitialServers, (old, current) => currentStatus = "Waiting for servers...");
    }

    // This state is when the payer has pressed Join, auto select a server
    void onChoosingServer(HostingState oldstate, HostingState currentstate) {
        // Set UI
        currentStatus = "Choosing server...";

        // We got not servers from the master, refresh again in 1 second
        if (serverList == null || serverList.Servers == null || serverList.Servers.Length == 0) {
            hostState = HostingState.Startup;
            return;
        }

        // Filter out useless servers
        currentServer = serverList.Servers
            .OrderBy(x => x.CurrentPlayers)
            .ThenBy(x => Guid.NewGuid())
            .FirstOrDefault(x => x.CurrentPlayers < x.MaxPlayers && x.Version == buildVersion);

        if (currentServer == null) {
            // TODO: Somehow put this on screen
            Debug.Log("Tried to find server, failed. Returning to startup state");
            serverList = null;
            // Failed to find a server, restart the search
            hostState = HostingState.Startup;
        } else {
            // Server found, connect
            hostState = HostingState.Connecting;
        }
    }

    void onConnecting(HostingState oldstate, HostingState currentstate) {
        currentStatus = "Connecting to server...";
        if (Connect()) {
            hostState = HostingState.Connected;
        } else {
            currentServer.ConnectionFailed = true;
            // TODO: Get this on the screen
            Debug.LogWarning("Couldn't connect, will try choosing another server");
            hostState = HostingState.Startup;
        }
    }

    void onReadyToHost(HostingState oldstate, HostingState currentstate) {
        if (CreateServer()) {
            hostState = HostingState.AttemptingToHost;
            AddServerToList();
            sinceRefreshedPlayers = 0;
        } else {
            Debug.LogWarning("Failed to create error");
            hostState = HostingState.Startup;
        }
    }

    void Update() {
        // Automatic host/connect logic follows
        switch (hostState) {
            case HostingState.Hosting:
                if (!Network.isServer) {
                    Debug.LogError("Hosting but is not the server...?");
                    hostState = HostingState.ReadyToHost;
                    break;
                }

                sinceRefreshedPlayers -= Time.deltaTime;
                if (sinceRefreshedPlayers <= 0) {
                    UpdateServer();
                    sinceRefreshedPlayers = RefreshTime;
                }
                break;
            // Can't do this on the callbacks because of thread stuff
            case HostingState.WaitingForInput:
                if (justLeft) {
                    FindObjectOfType<CameraSpin>().ResetTransforms();
                    justLeft = false;
                }
                break;
        }
    }

    void OnGUI() {
        NetworkPeerType peerType = Network.peerType;

        if (peerType == NetworkPeerType.Disconnected || peerType == NetworkPeerType.Connecting) {
            GUI.skin = guiSkin;

            // WFH Logo
            GUI.DrawTexture(new Rect(Screen.width / 2 - 64, Screen.height / 2 - 64, 128, 128), logo);

            // Animated dots
            string dots = "...".Substring(0, (int)Math.Floor(Time.time * 2) % 4);

            // Status box
            GUI.Box(new Rect((Screen.width / 2) - 122, Screen.height - 145, 248, 35), currentStatus.Replace("...", dots).ToUpperInvariant());
        }


        if (peerType == NetworkPeerType.Disconnected && hostState == HostingState.WaitingForInput) {
            // Write the current version somewhere
            GUI.Box(new Rect(0, 0, 80, 35), (buildVersion.ToString()).ToUpperInvariant());

            Screen.showCursor = true;
            GUILayout.Window(0, new Rect((Screen.width / 2) - 122, Screen.height - 110, 77, 35), Login, string.Empty);
        }
    }

    public static string RemoveSpecialCharacters(string str) {
        str = str.Replace("\n", string.Empty)
                 .Replace("\r", string.Empty);
        return str.Substring(0, Mathf.Min(24, str.Length));
    }

    void Login(int windowId) {
        if (Network.peerType == NetworkPeerType.Disconnected) {
            GUILayout.BeginHorizontal();
            chosenUsername = RemoveSpecialCharacters(GUILayout.TextField(chosenUsername));
            PlayerPrefs.SetString("username", chosenUsername.Trim());
            SpawnScript.Instance.SetChosenUsername(chosenUsername.Trim());

            GUILayout.Box("", new GUIStyle(guiSkin.box) { fixedWidth = 1 });
            if (GUILayout.Button("HOST")) {
                PlayerPrefs.Save();
                GlobalSoundsScript.PlayButtonPress();
                hostState = HostingState.ReadyToHost;
            }
            GUILayout.Box("", new GUIStyle(guiSkin.box) { fixedWidth = 1 });
            if (GUILayout.Button("JOIN")) {
                PlayerPrefs.Save();
                GlobalSoundsScript.PlayButtonPress();
                hostState = HostingState.ChoosingServer;
            }
            GUILayout.EndHorizontal();
            GUI.enabled = (hostState == HostingState.WaitingForInput);
        }
    }

    // Get list of servers from the master server
    void QueryServerList() {
        // If we're in LAN-only mode we skip all the fetching
        if (lanMode) { hostState = HostingState.WaitingForInput; }

        // Create server blacklist (remove servers we failed to connect to)
        string[] blackList = null;
        if (serverList != null && serverList != null) {
            blackList = serverList.Servers.Where(x => x.ConnectionFailed).Select(x => x.GUID).ToArray();
        }

        // If this is the first time to grab the servers, make it switch state
        if (hostState == HostingState.Startup) {
            hostState = HostingState.WaitingForInitialServers;
        }

        // Grab new server list
        if (ThreadPool.Instance != null) {
            ThreadPool.Instance.Fire(() => {
                // HTTP GET
                try {
                    string response = web.DownloadString(MasterServerUri + "/" + buildVersion);

                    // Master is up, hooray
                    masterIsDown = false;

                    ServerList servers = JsonConvert.DeserializeObject<ServerList>(response, jsonSettings);

                    // Blacklist things that failed before
                    if (blackList != null && blackList.Length > 0) {
                        foreach (ServerInfo s in servers.Servers) {
                            s.ConnectionFailed = blackList.Contains(s.GUID);
                        }
                    }

                    serverList = servers;
                    hostState = HostingState.WaitingForInput;
                } catch (WebException) {
                    // Master server is down, everybody panics
                    Debug.Log("Master server has gone down");
                    masterIsDown = true;
                    hostState = HostingState.WaitingForInput;
                } catch (Exception ex) {
                    Debug.LogWarning(ex.ToString());
                    throw ex;
                }
            });
        }
    }

    // Add our own hoster server to the list on the master server
    void AddServerToList() {
        // Do nothing on LAN-only mode
        if (lanMode) { serverToken = ""; }
        if (!masterIsDown && !lanMode) {
            try {
                // Serialize server info to JSON
                string currentServerJSON = JsonConvert.SerializeObject(currentServer);

                // then add new server
                serverToken = web.UploadString(MasterServerUri + "/add", currentServerJSON);
            } catch (WebException) {
                Debug.Log("Master server has gone down");
                masterIsDown = true;
            }
        }

        // We are now hosting
        hostState = HostingState.Hosting;
    }

    // Update our server status on the master server
    void UpdateServer() {
        // Do nothing on LAN-only mode
        if (lanMode || masterIsDown) { return; }

        // Setup data to send
        ServerItem serverItem = new ServerItem();
        serverItem.Status = 0; // Running
        serverItem.Token = serverToken;
        serverItem.Info = currentServer;

        // Set player count
        currentServer.CurrentPlayers = Network.connections.Length + 1;

        // Create JSON string
        string serverItemJSON = JsonConvert.SerializeObject(serverItem);

        try {
            web.UploadStringAsync(new Uri(MasterServerUri + "/update"), serverItemJSON);
        } catch (WebException) {
            Debug.Log("Master server has gone down");
            masterIsDown = true;
        }
    }

    // Delete our server from the list
    void DeleteServer() {
        // Do nothing on LAN-only mode
        if (lanMode || masterIsDown) { return; }

        web.UploadString(MasterServerUri + "/delete", serverToken);
    }

    bool CreateServer() {
        NetworkConnectionError result = Network.InitializeServer(MaxPlayers, port, !lanMode);
        if (result == NetworkConnectionError.NoError) {
            currentServer = new ServerInfo {
                GUID = Network.player.guid,
                Map = RoundScript.Instance.currentLevel,
                Version = buildVersion,
                MaxPlayers = MaxPlayers,
                CurrentPlayers = 0
            };
            return true;
        }
        return false;
    }

    bool Connect() {
        Debug.Log("Connecting to " + currentServer.GUID);
        NetworkConnectionError result = Network.Connect(currentServer.GUID);
        if (result != NetworkConnectionError.NoError) {
            return false;
        }
        return true;
    }

    void OnServerInitialized() {
        Debug.Log("GUID is " + Network.player.guid + ". Use this on clients to connect with NAT punchthrough");
        Debug.Log("Local IP/port is " + Network.player.ipAddress + "/" + Network.player.port + ". Use this on clients to connect directly");
        RoundScript.Instance.ChangeLevelAndRestart(Application.loadedLevelName);
        NetworkLeaderboard.instance.OnPlayerConnected(Network.player);
    }

    void OnApplicationQuit() {
        // Disconnect
        Network.Disconnect();
    }

    void OnFailedToConnect(NetworkConnectionError error) {
        // TODO: Inform player that it failed and why
        if (Network.isClient) {
            currentServer.ConnectionFailed = true;
            currentStatus = "Couldn't connect to server";
            Debug.LogWarning("Couldn't connect, will try choosing another server: " + error);
            hostState = HostingState.Startup;
        } else {
            // TODO: Not Tested
            currentStatus = "Unable to host, NAT Facilitator offline";
            OnDisconnectedFromServer(new NetworkDisconnection());
        }
    }

    void OnDisconnectedFromServer(NetworkDisconnection info) {
        if (Network.isServer && currentServer != null) {
            DeleteServer();
        }

        PlayerRegistry.Clear();
        NetworkLeaderboard.instance.Clear();

        // Clear players
        foreach (PlayerScript o in FindObjectsOfType<PlayerScript>()) {
            Destroy(o.gameObject);
        }

        justLeft = true;
        hostState = HostingState.Startup;
    }
}