using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Mono.Nat;
using UnityEngine;

public class ServerScript : MonoBehaviour {
    public static ServerScript Instance { get; private set; }

    //Public
    public const int port = 31415;
    public const string buildVersion = "13062014";
    public const string MasterServerUri = "http://ohs.padrepio.in/";

    public string[] allowedLevels = { "pi_rah", "pi_jst", "pi_mar", "pi_ven", "pi_gho", "pi_set" };
    public bool lanMode = false;
    public NetworkPeerType peerType;
    public GUISkin guiSkin;
    public List<LeaderboardEntry> SavedLeaderboardEntries = new List<LeaderboardEntry>();

    public bool isLoading = false;
    public static bool Spectating;
    public static HostingState hostState { get; set; }

    //Private
    private const int MaxPlayers = 6;
    private const int RefreshTime = 15;

    private IFuture<string> wanIp;
    private IFuture<ServerList> serverList;
    private string serverToken;
    private ServerInfo currentServer;
    private string chosenUsername = "Anon";

    private static JsonSerializerSettings jsonSettings = new Newtonsoft.Json.JsonSerializerSettings {
        TypeNameHandling = TypeNameHandling.Auto,
        Formatting = Formatting.None,
        PreserveReferencesHandling = PreserveReferencesHandling.None
    };

    private enum MappingStatus {
        InProgress,
        Success,
        Failure
    }

    class MappingResult {
        public INatDevice Device;
        public MappingStatus Status = MappingStatus.InProgress;
        public Mapping Mapping;
    }
    List<MappingResult> mappingResults = new List<MappingResult>();

    private bool natDiscoveryStarted;
    private float sinceRefreshedPlayers;
    private int lastPlayerCount;
    private float sinceStartedDiscovery;
    private string lastLevelName;

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
        WaitingForInput,
        ReadyToListServers,
        WaitingForServers,
        ReadyToChooseServer,
        ReadyToDiscoverNat,
        ReadyToConnect,
        WaitingForNat,
        ReadyToHost,
        AttemptingToHost,
        Hosting,
        Connected
    }

    void Awake() {
        Instance = this;
    }

    void Start() {
        // Make sure the owning gameObject is preserved between level loads
        DontDestroyOnLoad(gameObject);

        // Set the username, or use default
        chosenUsername = PlayerPrefs.GetString("username", "Anon");

        // Set target frame rate for the game
        Application.targetFrameRate = 60;

        // Select a random level as background and map for hosting
        RoundScript.Instance.CurrentLevel = RandomHelper.InEnumerable(allowedLevels);
        ChangeLevelIfNeeded(RoundScript.Instance.CurrentLevel);

        hostState = HostingState.WaitingForInput;

        // Get list of servers
        QueryServerList();
    }

    void createStateChangeCallbacks() {

    }

    void Update() {
        // Automatic host/connect logic follows
        switch (hostState) {
            case HostingState.ReadyToListServers:
                QueryServerList();
                break;

            case HostingState.WaitingForServers:
                if (!serverList.HasValue && !serverList.InError)
                    break;

                hostState = HostingState.ReadyToChooseServer;
                break;

            case HostingState.ReadyToChooseServer:
                // We have no server list, go fetch
                if (serverList == null || !serverList.HasValue || serverList.Value.Servers == null) {
                    hostState = HostingState.ReadyToListServers;
                    return;
                }

                currentServer = serverList.Value.Servers
                    .OrderBy(x => x.CurrentPlayers)
                    .ThenBy(x => Guid.NewGuid())
                    .FirstOrDefault(x => x.CurrentPlayers < x.MaxPlayers && x.Version == buildVersion);

                if (currentServer == null) {
                    
                    Log("Tried to find server, failed. Returning to interactive state.");
                    serverList = null;
                    hostState = HostingState.WaitingForInput;
                } else {
                    hostState = HostingState.ReadyToConnect;
                }
                break;

            case HostingState.ReadyToDiscoverNat:
                if (!natDiscoveryStarted) {
                    Debug.Log("NAT discovery started");
                    StartNatDiscovery();
                }
                hostState = lanMode ? HostingState.ReadyToHost : HostingState.WaitingForNat;
                break;

            case HostingState.WaitingForNat:
                sinceStartedDiscovery += Time.deltaTime;
                if (sinceStartedDiscovery > 0.5f) {
                    NatUtility.StopDiscovery();
                    mappingResults.Clear();
                    sinceStartedDiscovery = 0;

                    if (mappingResults.Any(x => x.Status == MappingStatus.Success))
                        Debug.Log("Some mapping attempts failed, but will proceed with hosting anyway");
                    else
                        Debug.Log("Can't map UPnP ports, but will proceed with hosting anyway");
                    hostState = HostingState.ReadyToHost;
                }

                if (mappingResults.Count == 0 || mappingResults.Any(x => x.Status == MappingStatus.InProgress))
                    break;

                sinceStartedDiscovery = 0;

                if (mappingResults.All(x => x.Status == MappingStatus.Success)) {
                    Debug.Log("Ready to host!");
                    hostState = HostingState.ReadyToHost;
                } else {
                    if (mappingResults.Any(x => x.Status == MappingStatus.Success))
                        Debug.Log("Some mapping attempts failed, but will proceed with hosting anyway");
                    else
                        Debug.Log("Can't map UPnP ports, but will proceed with hosting anyway");
                    hostState = HostingState.ReadyToHost;
                }
                break;

            case HostingState.ReadyToHost:
                if (CreateServer()) {
                    hostState = HostingState.AttemptingToHost;
                    AddServerToList();
                    lastPlayerCount = 0;
                    lastLevelName = RoundScript.Instance.CurrentLevel;
                    sinceRefreshedPlayers = 0;
                } else {
                    Debug.Log("Failed to create error");
                    hostState = HostingState.ReadyToChooseServer;
                }
                break;

            case HostingState.Hosting:
                if (!Network.isServer) {
                    Debug.LogError("Hosting but is not the server...?");
                    break;
                }

                sinceRefreshedPlayers -= Time.deltaTime;

                if (lastPlayerCount != Network.connections.Length ||
                    lastLevelName != RoundScript.Instance.CurrentLevel ||
                    sinceRefreshedPlayers <= 0
                ) {
                    Debug.Log("Refreshing...");
                    UpdateServer();
                    sinceRefreshedPlayers = RefreshTime;
                    lastPlayerCount = Network.connections.Length;
                    lastLevelName = RoundScript.Instance.CurrentLevel;
                }
                break;

            case HostingState.ReadyToConnect:
                if (Connect()) {
                    hostState = HostingState.Connected;
                } else {
                    currentServer.ConnectionFailed = true;
                    Debug.Log("Couldn't connect, will try choosing another server");
                    hostState = HostingState.ReadyToChooseServer;
                }
                break;
        }
    }

    void OnGUI() {
        peerType = Network.peerType;

        GUI.skin = guiSkin;

        if (peerType == NetworkPeerType.Connecting || peerType == NetworkPeerType.Disconnected) {
            // Welcome message is now a chat prompt
            if (serverList != null && serverList.HasValue) {
                string message = "Server activity : " + serverList.Value.Connections + " players in " + serverList.Value.Activegames + " games.";
                GUI.Box(new Rect((Screen.width / 2) - 122, Screen.height - 145, 248, 35), message.ToUpperInvariant());
            }

            Screen.showCursor = true;
            GUILayout.Window(0, new Rect((Screen.width / 2) - 122, Screen.height - 110, 77, 35), Login, string.Empty);
        }
    }

    public static string RemoveSpecialCharacters(string str) {
        StringBuilder sb = new StringBuilder();
        foreach (char c in str)
            if (c != '\n' && c != '\r' && sb.Length < 24)
                sb.Append(c);
        return sb.ToString();
    }

    void Login(int windowId) {
        switch (peerType) {
            case NetworkPeerType.Disconnected:
            case NetworkPeerType.Connecting:
                GUI.enabled = hostState == HostingState.WaitingForInput;
                GUILayout.BeginHorizontal();
                    chosenUsername = RemoveSpecialCharacters(GUILayout.TextField(chosenUsername));
                    PlayerPrefs.SetString("username", chosenUsername.Trim());
                    SendMessage("SetChosenUsername", chosenUsername.Trim());

                    GUI.enabled = hostState == HostingState.WaitingForInput && chosenUsername.Trim().Length != 0;
                    GUILayout.Box("", new GUIStyle(guiSkin.box) { fixedWidth = 1 });
                    if (GUILayout.Button("HOST") && hostState == HostingState.WaitingForInput) {
                        PlayerPrefs.Save();
                        GlobalSoundsScript.PlayButtonPress();
                        hostState = HostingState.ReadyToDiscoverNat;
                    }
                    GUILayout.Box("", new GUIStyle(guiSkin.box) { fixedWidth = 1 });
                    if (GUILayout.Button("JOIN") && hostState == HostingState.WaitingForInput) {
                        PlayerPrefs.Save();
                        GlobalSoundsScript.PlayButtonPress();
                        hostState = HostingState.ReadyToListServers;
                    }
                    GUI.enabled = true;
                GUILayout.EndHorizontal();

                GUI.enabled = true;
                break;
        }
    }

    // Get list of servers from the master server
    void QueryServerList() {
        // Create server blacklist (remove servers we failed to connect to)
        string[] blackList = null;
        if (serverList != null && serverList.HasValue) {
            blackList = serverList.Value.Servers.Where(x => x.ConnectionFailed).Select(x => x.GUID).ToArray();
        }

        //Update state to waiting for server list
        hostState = HostingState.WaitingForServers;

        // Grab new server list
        serverList = ThreadPool.Instance.Evaluate(() => {
            using (WebClient client = new WebClient()) {
                // HTTP GET
                // TODO: Handle if the server is down
                string response = client.DownloadString(MasterServerUri);

                try {
                    ServerList servers = JsonConvert.DeserializeObject<ServerList>(response, jsonSettings);

                    // Blacklist things that failed before
                    if (blackList != null && blackList.Length > 0)
                    {
                        foreach (ServerInfo s in servers.Servers)
                        {
                            s.ConnectionFailed = blackList.Contains(s.GUID);
                        }
                    }

                    // Return server list
                    return servers;
                } catch (Exception ex) {
                    Debug.Log(ex.ToString());
                    throw ex;
                }
            }
        });
    }

    // Add our own hoster server to the list on the master server
    void AddServerToList() {
        // Do nothing on LAN-only mode
        if (lanMode) { serverToken = ""; }

        using (WebClient client = new WebClient()) {
            // Serialize server info to JSON
            string currentServerJSON = JsonConvert.SerializeObject(currentServer);

            // then add new server
            // TODO: Handle if the server is down
            serverToken = client.UploadString(MasterServerUri + "/add", currentServerJSON);

            // We are now hosting
            hostState = HostingState.Hosting;
        }
    }

    // Update our server status on the master server
    void UpdateServer() {
        // Do nothing on LAN-only mode
        if (lanMode) { return; }

        // Setup data to send
        ServerItem serverItem = new ServerItem();
        serverItem.Status = 0; // Running
        serverItem.Token = serverToken;
        serverItem.Info = currentServer;

        // Set player count
        currentServer.CurrentPlayers = Network.connections.Length;

        // Create JSON string
        string serverItemJSON = JsonConvert.SerializeObject(serverItem);

        // TODO: Maby replace with UploadStringAsync?
        // Has to be Async, as a slow server response would block the game thread
        ThreadPool.Instance.Fire(() => {
            using (WebClient client = new WebClient()) {
                try {
                    client.UploadString(MasterServerUri + "/update", serverItemJSON);
                } catch (System.Net.WebException ex) {
                    // TODO: Exit gracefully when server is down or expired
                    Debug.LogError(ex.Message);
                }
            }
        });
    }

    // Delete our server from the list
    void DeleteServer() {
        Debug.Log("DeleteServer");
        // Do nothing on LAN-only mode
        if (lanMode) { return; }

        using (WebClient client = new WebClient()) {
            // TODO: Handle if the server is down
            client.UploadString(MasterServerUri + "/delete", serverToken);
            Debug.Log("DeleteServer DONE");
        }
    }

    bool CreateServer() {
        NetworkConnectionError result = Network.InitializeServer(MaxPlayers, port, true);
        if (result == NetworkConnectionError.NoError) {
            currentServer = new ServerInfo {
                GUID = Network.player.guid,
                Map = RoundScript.Instance.CurrentLevel,
                Version = buildVersion,
                MaxPlayers = MaxPlayers,
                CurrentPlayers = 0
            };
            return true;
        }
        return false;
    }

    public void ChangeLevel() {
        ChangeLevelIfNeeded(RandomHelper.InEnumerable(allowedLevels.Except(new[] { RoundScript.Instance.CurrentLevel })));
    }

    void SyncAndSpawn(string newLevel) {
        ChangeLevelIfNeeded(newLevel);

        SpawnScript.Instance.Spawn();
    }

    public void ChangeLevelIfNeeded(string newLevel) {
        isLoading = true;
        Application.LoadLevel(newLevel);
        ChatScript.Instance.LogChat(Network.player, "Changed level to " + newLevel + ".", true, true);
        RoundScript.Instance.CurrentLevel = newLevel;
        if (currentServer != null) {
            currentServer.Map = RoundScript.Instance.CurrentLevel;
        } 
        isLoading = false;
    }

    bool Connect() {
        Debug.Log("Connecting to " + currentServer.GUID);
        NetworkConnectionError result = Network.Connect(currentServer.GUID);
        if (result != NetworkConnectionError.NoError) {
            return false;
        }
        peerType = NetworkPeerType.Connecting;
        return true;
    }

    void OnConnectedToServer() {
        peerType = NetworkPeerType.Client;
        isLoading = true;
    }

    void OnPlayerConnected(NetworkPlayer player) {
        RoundScript.Instance.networkView.RPC("SyncLevel", player, RoundScript.Instance.CurrentLevel);
    }

    void StartNatDiscovery() {
        natDiscoveryStarted = true;

        // Do nothing on LAN-only mode
        if (lanMode) { return; }

        // Add discovery callbacks
        NatUtility.DeviceFound += (s, ea) => {
            Debug.Log("Mapping port for device : " + ea.Device.ToString());
            mappingResults.AddRange(MapPort(ea.Device));
        };
        NatUtility.DeviceLost += (s, ea) => {
            mappingResults.RemoveAll(x => x.Device == ea.Device);
        };

        // Start discovery
        NatUtility.StartDiscovery();
    }

    AsyncCallback getMappingCallback(INatDevice device, Mapping mapping, MappingResult result, Protocol protocol) {
        return state => {
            if (state.IsCompleted) {
                Debug.Log("Mapping complete for : " + mapping.ToString());
                try {
                    Mapping m = device.GetSpecificMapping(protocol, port);

                    // Mapping failed, throw
                    if (m == null) {
                        throw new InvalidOperationException("Mapping not found");
                    } else if (m.PrivatePort != port || m.PublicPort != port) {
                        throw new InvalidOperationException("Mapping invalid");
                    }

                    result.Status = MappingStatus.Success;
                } catch (Exception ex) {
                    Debug.Log("Failed to validate mapping :\n" + ex.ToString());
                    result.Status = MappingStatus.Failure;
                }
            }
        };
    }

    IEnumerable<MappingResult> MapPort(INatDevice device) {
        // Create mappings for both udp and tcp
        Mapping udpMapping = new Mapping(Protocol.Udp, port, port) { Description = "Horus (UDP)" };
        MappingResult udpResult = new MappingResult { Device = device, Mapping = udpMapping };
        Mapping tcpMapping = new Mapping(Protocol.Tcp, port, port) { Description = "Horus (TCP)" };
        MappingResult tcpResult = new MappingResult { Device = device, Mapping = tcpMapping };

        // create delegates from factory for the mapping results
        AsyncCallback mapUdp = getMappingCallback(device, udpMapping, udpResult, Protocol.Udp);
        AsyncCallback mapTcp = getMappingCallback(device, tcpMapping, tcpResult, Protocol.Tcp);

        // try mapping
        device.BeginCreatePortMap(udpMapping, mapUdp, null);
        device.BeginCreatePortMap(tcpMapping, mapTcp, null);

        // wait for results
        yield return udpResult;
        yield return tcpResult;
    }

    void OnServerInitialized() {
        Debug.Log("GUID is " + Network.player.guid + ". Use this on clients to connect with NAT punchthrough.");
        Debug.Log("Local IP/port is " + Network.player.ipAddress + "/" + Network.player.port + ". Use this on clients to connect directly.");
    }

    void OnApplicationQuit() {
        // Delete all mappings
        foreach (MappingResult result in mappingResults) {
            if (result.Device != null && result.Mapping != null) {
                try {
                    result.Device.DeletePortMap(result.Mapping);
                    Debug.Log("Deleted port mapping : " + result.Mapping);
                } catch (Exception) {
                    if (result.Status == MappingStatus.Failure) {
                        Debug.Log("Tried to delete invalid port mapping and failed");
                    } else {
                        Debug.Log("Failed to delete port mapping : " + result.Mapping);
                    }
                }
            }
        }

        // Release mapping results from memory
        mappingResults.Clear();

        //Disable discovery if enabled
        if (natDiscoveryStarted) {
            NatUtility.StopDiscovery();
            natDiscoveryStarted = false;
        }

        // Disconnect
        Network.Disconnect();
    }

    void OnFailedToConnect(NetworkConnectionError error) {
        // TODO: Inform player that it failed and why
        currentServer.ConnectionFailed = true;
        Debug.Log("Couldn't connect, will try choosing another server");
        hostState = HostingState.ReadyToListServers;
    }

    void OnDisconnectedFromServer(NetworkDisconnection info) {
        hostState = HostingState.WaitingForInput;
    }
}