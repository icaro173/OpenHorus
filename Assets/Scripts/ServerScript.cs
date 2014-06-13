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

    //Publioc
    public const int port = 31415;
    public string[] allowedLevels = { "pi_rah", "pi_jst", "pi_mar", "pi_ven", "pi_gho", "pi_set" };
    public const int buildVersion = 13062014;
    public bool localMode = false;
    public const string MasterServerUri = "http://api.xxiivv.com/?key=wfh";
    public NetworkPeerType peerType;
    public GUISkin guiSkin;

    public List<LeaderboardEntry> SavedLeaderboardEntries = new List<LeaderboardEntry>();

    public static bool Spectating;

    //Private
    private const int MaxPlayers = 6;

    private static JsonSerializerSettings jsonSettings = new Newtonsoft.Json.JsonSerializerSettings {
        TypeNameHandling = TypeNameHandling.Auto,
        Formatting = Formatting.None,
        PreserveReferencesHandling = PreserveReferencesHandling.None
    };

    private IFuture<string> wanIp;
    private IFuture<ReadResponse> readResponse;
    private IFuture<int> thisServerId;
    private  ServerInfo currentServer;
    private string chosenUsername = "Anon";

    private enum MappingStatus { InProgress, Success, Failure }
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

    class ReadResponse {
        public string Message = null;
        public int Connections = 0;
        public int Activegames = 0;
        public ServerInfo[] Servers = null;
    }

    class ServerInfo {
        public string Ip;
        public int Players;
        public string Map;
        public int Id;
        public bool ConnectionFailed;
        public int Version;

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
        Hosting,
        Connected
    }
    public static HostingState hostState = HostingState.WaitingForInput;

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

        // Get list of servers
        QueryServerList();
    }

    void Update() {
        // Automatic host/connect logic follows
        switch (hostState) {
            case HostingState.ReadyToListServers:
                QueryServerList();
                hostState = HostingState.WaitingForServers;
                break;

            case HostingState.WaitingForServers:
                if (!readResponse.HasValue && !readResponse.InError)
                    break;

                hostState = HostingState.ReadyToChooseServer;
                break;

            case HostingState.ReadyToChooseServer:
                if (readResponse == null) {
                    hostState = HostingState.ReadyToListServers;
                    return;
                }

                currentServer = readResponse.Value.Servers.OrderBy(x => x.Players).ThenBy(x => Guid.NewGuid()).FirstOrDefault(x => !x.ConnectionFailed && x.Players < MaxPlayers && x.Version == buildVersion); //&& x.BuildVer == BuildVersion
                if (currentServer == null) {
                    Debug.Log("Tried to find server, failed. Returning to interactive state.");
                    readResponse = null;
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
                hostState = localMode ? HostingState.ReadyToHost : HostingState.WaitingForNat;
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
                    hostState = HostingState.Hosting;
                    AddServerToList();
                    lastPlayerCount = 0;
                    lastLevelName = RoundScript.Instance.CurrentLevel;
                    sinceRefreshedPlayers = 0;
                } else {
                    Debug.Log("Couldn't create server, will try joining instead");
                    hostState = HostingState.ReadyToChooseServer;
                }
                break;

            case HostingState.Hosting:
                if (!Network.isServer) {
                    Debug.LogError("Hosting but is not the server...?");
                    break;
                }

                sinceRefreshedPlayers += Time.deltaTime;

                if (thisServerId.HasValue &&
                        (lastPlayerCount != Network.connections.Length ||
                         lastLevelName != RoundScript.Instance.CurrentLevel ||
                         sinceRefreshedPlayers > 25)
                ) {
                    Debug.Log("Refreshing...");
                    RefreshListedServer();
                    sinceRefreshedPlayers = 0;
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
            if (readResponse != null && readResponse.HasValue) {
                var message = "Server activity : " + readResponse.Value.Connections + " players in " + readResponse.Value.Activegames + " games.";
                message = message.ToUpperInvariant();

                GUI.Box(new Rect((Screen.width / 2) - 122, Screen.height - 145, 248, 35), message);
            }

            Screen.showCursor = true;
            GUILayout.Window(0, new Rect((Screen.width / 2) - 122, Screen.height - 110, 77, 35), Login, string.Empty);
        }
    }

    public static string RemoveSpecialCharacters(string str) {
        var sb = new StringBuilder();
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

                    GUI.enabled = true;
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

    void QueryServerList() {
        int[] blackList = null;
        if (readResponse != null && readResponse.HasValue) {
            blackList = readResponse.Value.Servers.Where(x => x.ConnectionFailed).Select(x => x.Id).ToArray();
            if (blackList.Length > 0) {
                Debug.Log("blacklisted servers : " + blackList.Skip(1).Aggregate(blackList[0].ToString(), (s, i) => s + ", " + i.ToString()));
            }
        }

        readResponse = ThreadPool.Instance.Evaluate(() => {
            using (var client = new WebClient()) {
                var response = client.DownloadString(MasterServerUri + "&cmd=read");

                try {
                    ReadResponse data = JsonConvert.DeserializeObject<ReadResponse>(response, jsonSettings);
                    Debug.Log("MOTD : " + data.Message);
                    Debug.Log(data.Servers.Length + " servers : ");
                    foreach (ServerInfo s in data.Servers) {
                        s.ConnectionFailed = blackList.Contains(s.Id);
                        Debug.Log(s + (s.ConnectionFailed ? " (blacklisted)" : ""));
                    }
                    return data;
                } catch (Exception ex) {
                    Debug.Log(ex.ToString());
                    throw ex;
                }
            }
        });
    }

    void AddServerToList() {
        // TODO: Replace with new master server code
        thisServerId = ThreadPool.Instance.Evaluate(() => {
            if (localMode) {
                return 0;
            }

            using (var client = new WebClient()) {
                string result = JsonConvert.SerializeObject(currentServer);
                Debug.Log("server json : " + result);

                // then add new server
                var nvc = new NameValueCollection { { "value", result } };
                var uri = MasterServerUri + "&cmd=add";
                var response = Encoding.ASCII.GetString(client.UploadValues(uri, nvc));
                Debug.Log("Added server, got id = " + response);
                currentServer.Id = int.Parse(response);
                return int.Parse(response);
            }
        });
    }

    void RefreshListedServer() {
        // TODO: Replace with new master server code
        currentServer.Players = Network.connections.Length +1;
        ThreadPool.Instance.Fire(() => {
            if (localMode) {
                return;
            }
            using (WebClient client = new WebClient()) {
                string result = JsonConvert.SerializeObject(currentServer);

                Debug.Log("server json : " + result);

                // update!
                NameValueCollection nvc = new NameValueCollection { { "value", result } };
                string uri = MasterServerUri + "&cmd=update";
                string response = Encoding.ASCII.GetString(client.UploadValues(uri, nvc));
                Debug.Log(uri);
                Debug.Log("Refreshed server with connection count to " + currentServer.Players + " and map " + currentServer.Map + ", server said : " + response);
            }
        });
    }

    void DeleteServer() {
        // TODO: Replace with new master server code
        if (localMode) {
            return;
        }
        using (WebClient client = new WebClient()) {
            Uri uri = new Uri(MasterServerUri + "&cmd=delete&id=" + thisServerId.Value);
            NameValueCollection nvc = new NameValueCollection { { "", "" } };
            string response = Encoding.ASCII.GetString(client.UploadValues(uri, nvc));
            Debug.Log("Deleted server " + thisServerId.Value + ", server said : " + response);
        }
    }

    bool CreateServer() {
        // TODO: Replace with new master server code
        var result = Network.InitializeServer(MaxPlayers, port, true);
        if (result == NetworkConnectionError.NoError) {
            currentServer = new ServerInfo {
                Ip = Network.player.guid,
                Map = RoundScript.Instance.CurrentLevel,
                Players = 1,
                Version = buildVersion
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
        Application.LoadLevel(newLevel);
        ChatScript.Instance.LogChat(Network.player, "Changed level to " + newLevel + ".", true, true);
        RoundScript.Instance.CurrentLevel = newLevel;
        if (currentServer != null) {
            currentServer.Map = RoundScript.Instance.CurrentLevel;
        }
    }

    bool Connect() {
        Debug.Log("Connecting to " + currentServer.Ip);
        var result = Network.Connect(currentServer.Ip);
        if (result != NetworkConnectionError.NoError) {
            return false;
        }
        peerType = NetworkPeerType.Connecting;
        return true;
    }

    void OnConnectedToServer() {
        peerType = NetworkPeerType.Client;
    }

    void OnPlayerConnected(NetworkPlayer player) {
        RoundScript.Instance.networkView.RPC("SyncLevel", player, RoundScript.Instance.CurrentLevel);
    }

    void StartNatDiscovery() {
        natDiscoveryStarted = true;

        if (localMode) return;

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