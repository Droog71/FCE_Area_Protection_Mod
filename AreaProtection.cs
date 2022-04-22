using System;
using System.IO;
using UnityEngine;
using Lidgren.Network;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

public class AreaProtection : FortressCraftMod
{
    private bool showGUI = true;
    private Coroutine serverCoroutine;
    private Vector3 playerPosition;
    private Vector3 clientAreaPosition;
    private static bool ableToClaim;
    private static GameObject protectionCylinder;
    private static List<string> savedPlayers;
    private static List<Player> allowedPlayers;
    private static Texture2D protectionCylinderTexture;
    private static List<Area> areas = new List<Area>();
    private static readonly string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    private static readonly string protectionCylinderTexurePath = Path.Combine(assemblyFolder, "Images/ProtectionCylinder.png");
    private UriBuilder protectionCylinderUriBuilder = new UriBuilder(protectionCylinderTexurePath);
    private static readonly string areasFilePath = Path.Combine(assemblyFolder, "areas.txt");
    private static readonly string playersFilePath = Path.Combine(assemblyFolder, "players.txt");

    // Registers the mod.
    public override ModRegistrationData Register()
    {
        ModRegistrationData modRegistrationData = new ModRegistrationData();
        modRegistrationData.RegisterServerComms("Maverick.AreaProtection", ServerWrite, ClientRead);
        modRegistrationData.RegisterClientComms("Maverick.AreaProtection", ClientWrite, ServerRead);
        return modRegistrationData;
    }

    // Called by unity engine on start up to initialize variables.
    public IEnumerator Start()
    {
        savedPlayers = new List<string>();
        allowedPlayers = new List<Player>();
        protectionCylinderUriBuilder.Scheme = "file";
        protectionCylinderTexture = new Texture2D(1024, 1024, TextureFormat.DXT5, false);
        using (WWW www = new WWW(protectionCylinderUriBuilder.ToString()))
        {
            yield return www;
            www.LoadImageIntoTexture(protectionCylinderTexture);
        }

        if (WorldScript.mbIsServer || NetworkManager.mbHostingServer)
        {
            LoadAreas();
            LoadPlayers();
        }
    }

    // Loads protected areas from disk.
    private void LoadAreas()
    {
        string fileContents = File.ReadAllText(areasFilePath);
        string[] allAreas = fileContents.Split('}');
        foreach (string entry in allAreas)
        {
            string[] splitEntry = entry.Split(':');
            if (splitEntry.Length > 1)
            {
                string entryName = splitEntry[0];
                float x = float.Parse(splitEntry[1].Split(',')[0]);
                float y = float.Parse(splitEntry[1].Split(',')[1]);
                float z = float.Parse(splitEntry[1].Split(',')[2]);
                Vector3 entryLocation = new Vector3(x, y, z);
                Area loadArea;
                loadArea.areaID = entryName;
                loadArea.areaLocation = entryLocation;
                areas.Add(loadArea);
            }
        }
    }

    // Save area information to disk.
    private static void SaveAreas()
    {
        string allAreas = "";
        foreach (Area area in areas)
        {
            allAreas += area.areaID + ":" + area.areaLocation.x + "," + area.areaLocation.y + "," + area.areaLocation.z + "}";
        }
        File.WriteAllText(areasFilePath, allAreas);
    }

    // Loads player information from disk.
    private void LoadPlayers()
    {
        string fileContents = File.ReadAllText(playersFilePath);
        string[] allPlayers = fileContents.Split('}');
        foreach (string player in allPlayers)
        {
            savedPlayers.Add(player);
        }
    }

    // Saves player information to disk.
    private static void SavePlayer(string playerName)
    {
        string playerData = "";
        foreach (string player in savedPlayers)
        {
            playerData += player + "}";
        }
        savedPlayers.Add(playerName);
        playerData += playerName + "}";
        File.WriteAllText(playersFilePath, playerData);
    }

    // Returns true when the player is trying to chat in a claimed area.
    private bool ChatOverrideRequired()
    {
        bool b1 = UIManager.HudShown;
        bool b2 = !UIManager.AllowInteracting;
        bool b3 = !UIManager.mbEditingTextField;
        bool b4 = UIManager.instance.mChatPanel.ShouldShow();
        bool b5 = !UIManager.instance.WorkshopDetailPanel.activeSelf;
        return b1 && b2 && b3 && b4 && b5;
    }

    // Called once per frame by unity engine.
    public void Update()
    {
        if (WorldScript.mbIsServer || NetworkManager.mbHostingServer)
        {
            serverCoroutine = StartCoroutine(CheckAreas());
        }
        else if (!WorldScript.mbIsServer && GameState.PlayerSpawned)
        {
            if (Input.GetKeyDown(KeyCode.Home))
            {
                ClientClaimArea();
            }

            if (Input.GetKeyDown(KeyCode.End))
            {
                showGUI = !showGUI;
            }

            if (Input.GetKeyUp(KeyCode.Return))
            {
                if (ChatOverrideRequired())
                {
                    UIManager.instance.mChatPanel.ShowUI();
                }
            }

            UpdateClient();
        }
    }

    // Loads info about client's protected area and updates player position.
    private void UpdateClient()
    {
        if (NetworkManager.instance.mClientThread != null)
        {
            if (NetworkManager.instance.mClientThread.mPlayer != null)
            {
                ulong userID = NetworkManager.instance.mClientThread.mPlayer.mUserID;
                System.Net.IPAddress serverIP = NetworkManager.instance.mClientThread.serverIP;

                if (serverIP != null)
                {
                    if (PlayerPrefs.GetInt(userID + ":" + serverIP + "createdCylinder") == 1)
                    {
                        if (protectionCylinder == null)
                        {
                            float cylinderX = PlayerPrefs.GetFloat(userID + ":" + serverIP + "cylinderX");
                            float cylinderY = PlayerPrefs.GetFloat(userID + ":" + serverIP + "cylinderY");
                            float cylinderZ = PlayerPrefs.GetFloat(userID + ":" + serverIP + "cylinderZ");
                            Vector3 cylinderPosition = new Vector3(cylinderX, cylinderY, cylinderZ);
                            CreateProtectionCylinder(cylinderPosition);

                            float areaX = PlayerPrefs.GetFloat(userID + ":" + serverIP + "areaX");
                            float areaY = PlayerPrefs.GetFloat(userID + ":" + serverIP + "areaY");
                            float areaZ = PlayerPrefs.GetFloat(userID + ":" + serverIP + "areaZ");
                            clientAreaPosition = new Vector3(areaX, areaY, areaZ);
                        }
                    }
                }

                Player player = NetworkManager.instance.mClientThread.mPlayer;
                float x = player.mnWorldX - WorldScript.instance.mWorldData.mSpawnX;
                float y = player.mnWorldY - WorldScript.instance.mWorldData.mSpawnY;
                float z = player.mnWorldZ - WorldScript.instance.mWorldData.mSpawnZ;
                playerPosition = new Vector3(x, y, z);
            }
        }
    }

    // Holds information about a single area.
    private struct Area
    {
        public string areaID;
        public Vector3 areaLocation;
    }

    // Holds information for server to client comms.
    private struct ServerMessage
    {
        public int newPlayer;
        public int allowedToClaim;
        public int permissions;
    }

    // Networking.
    private static void ClientRead(NetIncomingMessage netIncomingMessage)
    {
        int readInt1 = netIncomingMessage.ReadInt32();
        int playerID = (int)NetworkManager.instance.mClientThread.mPlayer.mUserID;

        if (readInt1 == playerID)
        {
            int readInt2 = netIncomingMessage.ReadInt32();

            if (readInt2 == 1)
            {
                CollectStartingItems();
            }

            int readInt3 = netIncomingMessage.ReadInt32();
            ableToClaim = readInt3 == 1;

            int readInt4 = netIncomingMessage.ReadInt32();
            NetworkManager.instance.mClientThread.mPlayer.mBuildPermission = (eBuildPermission)readInt4;

            if ((eBuildPermission)readInt4 == eBuildPermission.Visitor)
            {
                UIManager.AllowInteracting = false;
            }
        }
    }

    // Networking.
    private static void ClientWrite(BinaryWriter writer, object data)
    {
        writer.Write((int)data);
    }

    // Networking.
    private static void ServerRead(NetIncomingMessage message, Player player)
    {
        ServerClaimArea(player);
    }

    // Networking.
    private static void ServerWrite(BinaryWriter writer, Player player, object data)
    {
        ServerMessage message = (ServerMessage)data;
        writer.Write((int)player.mUserID);
        writer.Write(message.newPlayer);
        writer.Write(message.allowedToClaim);
        writer.Write(message.permissions);
    }

    // Sends chat messages from the server.
    private static void Announce(string msg)
    {
        ChatLine chatLine = new ChatLine();
        chatLine.mPlayer = -1;
        chatLine.mPlayerName = "[SERVER]";
        chatLine.mText = msg;
        chatLine.mType = ChatLine.Type.Normal;
        NetworkManager.instance.QueueChatMessage(chatLine);
    }

    // Sends area claim command to server.
    private void ClientClaimArea()
    {
        if (ableToClaim == true)
        {
            ModManager.ModSendClientCommToServer("Maverick.AreaProtection", 0);

            if (protectionCylinder == null)
            {
                CreateProtectionCylinder(Camera.main.transform.position);
            }
            else
            {
                MoveProtectionCylinder(Camera.main.transform.position);
            }

            Player player = NetworkManager.instance.mClientThread.mPlayer;
            System.Net.IPAddress serverIP = NetworkManager.instance.mClientThread.serverIP;
            PlayerPrefs.SetInt(player.mUserID + ":" + serverIP + "createdCylinder", 1);
            PlayerPrefs.SetFloat(player.mUserID + ":" + serverIP + "cylinderX", Camera.main.transform.position.x);
            PlayerPrefs.SetFloat(player.mUserID + ":" + serverIP + "cylinderY", Camera.main.transform.position.y);
            PlayerPrefs.SetFloat(player.mUserID + ":" + serverIP + "cylinderZ", Camera.main.transform.position.z);
            PlayerPrefs.SetFloat(player.mUserID + ":" + serverIP + "areaX", player.mnWorldX);
            PlayerPrefs.SetFloat(player.mUserID + ":" + serverIP + "areaY", player.mnWorldY);
            PlayerPrefs.SetFloat(player.mUserID + ":" + serverIP + "areaZ", player.mnWorldZ);
            PlayerPrefs.Save();
            clientAreaPosition = playerPosition;
        }
    }

    // Attempts to claim an area.
    private static void ServerClaimArea(Player player)
    {
        if (allowedPlayers.Contains(player))
        {
            Area newArea;
            bool foundArea = false;

            foreach (Area area in areas)
            {
                if (area.areaID == player.mUserName + player.mUserID)
                {
                    newArea = area;
                    foundArea = true;
                    break;
                }
            }

            if (foundArea == true)
            {
                newArea.areaLocation.x = player.mnWorldX - WorldScript.instance.mWorldData.mSpawnX;
                newArea.areaLocation.y = player.mnWorldY - WorldScript.instance.mWorldData.mSpawnY;
                newArea.areaLocation.z = player.mnWorldZ - WorldScript.instance.mWorldData.mSpawnZ;
                Announce(player.mUserName + "'s claimed area moved to " + newArea.areaLocation);
            }
            else
            {
                newArea.areaID = player.mUserName + player.mUserID;
                newArea.areaLocation.x = player.mnWorldX - WorldScript.instance.mWorldData.mSpawnX;
                newArea.areaLocation.y = player.mnWorldY - WorldScript.instance.mWorldData.mSpawnY;
                newArea.areaLocation.z = player.mnWorldZ - WorldScript.instance.mWorldData.mSpawnZ;
                areas.Add(newArea);
                Announce(player.mUserName + " claimed area at " + newArea.areaLocation);
            }

            SaveAreas();
        }
        else
        {
            Announce("Unable to claim area for " + player.mUserName + ". Too close to another claim.");
        }
    }

    // Checks player positions relative to protected areas and sets permissions.
    private IEnumerator CheckAreas()
    {
        if (NetworkManager.instance != null)
        {
            if (NetworkManager.instance.mServerThread != null)
            {
                List<NetworkServerConnection> connections = NetworkManager.instance.mServerThread.connections;
                for (int i = 0; i < connections.Count; i++)
                {
                    if (connections[i] != null)
                    {
                        if (connections[i].mState == eNetworkConnectionState.Playing)
                        {
                            Player player = connections[i].mPlayer;
                            if (player != null)
                            {
                                float x = player.mnWorldX - WorldScript.instance.mWorldData.mSpawnX;
                                float y = player.mnWorldY - WorldScript.instance.mWorldData.mSpawnY;
                                float z = player.mnWorldZ - WorldScript.instance.mWorldData.mSpawnZ;
                                Vector3 clientPosition = new Vector3(x, y, z);
                                bool cannotBuild = false;
                                bool cannotClaim = false;

                                for (int j = 0; j < areas.Count; j++)
                                {
                                    if (areas[j].areaID != player.mUserName + player.mUserID)
                                    {
                                        Vector2 clientPos2D = new Vector2(clientPosition.x, clientPosition.z);
                                        Vector2 areaPos2D = new Vector2(areas[j].areaLocation.x, areas[j].areaLocation.z);
                                        int distance = (int)Vector2.Distance(clientPos2D, areaPos2D);

                                        if (distance <= 500)
                                        {
                                            cannotClaim = true;
                                        }

                                        if (distance <= 250)
                                        {
                                            cannotBuild = true;
                                        }
                                    }
                                }

                                ServerMessage message = new ServerMessage();

                                if (savedPlayers != null)
                                {
                                    if (!savedPlayers.Contains(player.mUserID + player.mUserName))
                                    {
                                        Announce("Giving starting items to " + player.mUserName);
                                        SavePlayer(player.mUserID + player.mUserName);
                                        message.newPlayer = 1;
                                    }
                                    else
                                    {
                                        message.newPlayer = 0;
                                    }
                                }

                                if (allowedPlayers != null)
                                {
                                    if (cannotClaim == true)
                                    {
                                        if (allowedPlayers.Contains(player))
                                        {
                                            allowedPlayers.Remove(player);
                                        }
                                        message.allowedToClaim = 0;
                                    }
                                    else
                                    {
                                        if (!allowedPlayers.Contains(player))
                                        {
                                            allowedPlayers.Add(player);
                                        }
                                        message.allowedToClaim = 1;
                                    }
                                }

                                if (NetworkManager.instance.mAdminListManager != null)
                                {
                                    if (NetworkManager.instance.mAdminListManager.CheckAdminList(player.mUserID, player.mUserName))
                                    {
                                        player.mBuildPermission = eBuildPermission.Admin;
                                        message.permissions = 0;
                                    }
                                    else
                                    {
                                        if (cannotBuild == true)
                                        {
                                            player.mBuildPermission = eBuildPermission.Visitor;
                                            message.permissions = 4;
                                        }
                                        else
                                        {
                                            player.mBuildPermission = eBuildPermission.Builder;
                                            message.permissions = 2;
                                        }
                                    }
                                }

                                ModManager.ModSendServerCommToClient("Maverick.AreaProtection", player, message);
                                yield return new WaitForSeconds(0.25f);
                            }
                        }
                    }
                }
            }
        }
    }

    // Gives starting items to a player.
    private static void CollectStartingItems()
    {
        NetworkManager.instance.mClientThread.mPlayer.mInventory.CollectValue(507, 99, 7);
        NetworkManager.instance.mClientThread.mPlayer.mInventory.CollectValue(eCubeTypes.OreSmelter, 1, 4);
        NetworkManager.instance.mClientThread.mPlayer.mInventory.CollectValue(eCubeTypes.Conveyor, 11, 200);
        NetworkManager.instance.mClientThread.mPlayer.mInventory.CollectValue(eCubeTypes.OreExtractor, 0, 3);
        NetworkManager.instance.mClientThread.mPlayer.mInventory.CollectValue(eCubeTypes.StorageHopper, 0, 5);
        NetworkManager.instance.mClientThread.mPlayer.mInventory.CollectValue(eCubeTypes.StorageHopper, 2, 5);
        NetworkManager.instance.mClientThread.mPlayer.mInventory.CollectValue(eCubeTypes.PowerStorageBlock, 0, 6);
        NetworkManager.instance.mClientThread.mPlayer.mInventory.CollectValue(eCubeTypes.ManufacturingPlant, 0, 1);
        NetworkManager.instance.mClientThread.mPlayer.mInventory.CollectValue(eCubeTypes.PyrothermicGenerator, 0, 2);
    }

    // Creates the protection cylinder.
    private static void CreateProtectionCylinder(Vector3 position)
    {
        protectionCylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        protectionCylinder.transform.position = position;
        protectionCylinder.transform.localScale += new Vector3(372, 4000, 372);
        protectionCylinder.GetComponent<Renderer>().material.mainTexture = protectionCylinderTexture;
        protectionCylinder.GetComponent<Renderer>().material.mainTextureScale = new Vector2(1, 10);
        protectionCylinder.GetComponent<Renderer>().receiveShadows = false;
        protectionCylinder.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        protectionCylinder.GetComponent<Renderer>().material.shader = Shader.Find("Transparent/Diffuse");
        protectionCylinder.GetComponent<Renderer>().material.EnableKeyword("_ALPHATEST_ON");

        GameObject protectionCylinderInner = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        protectionCylinderInner.transform.position = Camera.main.transform.position;
        protectionCylinderInner.transform.localScale += new Vector3(372, 4000, 372);
        ConvertNormals(protectionCylinderInner);
        protectionCylinderInner.GetComponent<Renderer>().material.mainTexture = protectionCylinderTexture;
        protectionCylinderInner.GetComponent<Renderer>().material.mainTextureScale = new Vector2(1, 10);
        protectionCylinderInner.GetComponent<Renderer>().receiveShadows = false;
        protectionCylinderInner.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        protectionCylinderInner.GetComponent<Renderer>().material.shader = Shader.Find("Transparent/Diffuse");
        protectionCylinderInner.GetComponent<Renderer>().material.EnableKeyword("_ALPHATEST_ON");
        protectionCylinderInner.transform.SetParent(protectionCylinder.transform);
    }

    // Moves the protection cylinder.
    private static void MoveProtectionCylinder(Vector3 position)
    {
        protectionCylinder.transform.position = position;
    }

    // Recalculates mesh normals so textures are visible inside the cylinder.
    private static void ConvertNormals(GameObject obj)
    {
        MeshFilter filter = obj.GetComponent(typeof(MeshFilter)) as MeshFilter;
        if (filter != null)
        {
            Mesh mesh = filter.mesh;

            Vector3[] normals = mesh.normals;
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = -normals[i];
            }
            mesh.normals = normals;

            for (int m = 0; m < mesh.subMeshCount; m++)
            {
                int[] triangles = mesh.GetTriangles(m);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int temp = triangles[i + 0];
                    triangles[i + 0] = triangles[i + 1];
                    triangles[i + 1] = temp;
                }
                mesh.SetTriangles(triangles, m);
            }
        }
    }

    // Displays build permission info.
    public void OnGUI()
    {
        try
        {
            if (!WorldScript.mbIsServer && GameState.PlayerSpawned && showGUI == true)
            {
                string message = "";
                Rect infoRect = new Rect(Screen.width * 0.6f, Screen.height * 0.4f, 500, 100);

                eBuildPermission permission = NetworkManager.instance.mClientThread.mPlayer.mBuildPermission;
                if (permission == eBuildPermission.Admin)
                {
                    message = "[Area Protection Mod]" +
                    "\nAdministrative rights enabled." +
                    "\nPress End key to toggle messages.";
                }
                else if (permission == eBuildPermission.Visitor)
                {
                    message = "[Area Protection Mod]" +
                    "\nThis area is protected." +
                    "\nYou cannot build here." +
                    "\nPress End key to toggle messages.";
                }
                else if (ableToClaim == true && protectionCylinder == null)
                {
                    message = "[Area Protection Mod]" +
                    "\nPress Home key to claim this area." +
                    "\nPress End key to toggle messages.";
                }
                else if (ableToClaim == true && protectionCylinder != null)
                {
                    message = "[Area Protection Mod]" +
                    "\nPress Home key to relocate your claimed area." +
                    "\nThe coordinates of your protected area are " + clientAreaPosition +
                    "\nYour current coordinates are " + playerPosition +
                    "\nPress End key to toggle messages.";
                }
                else if (ableToClaim == false && protectionCylinder != null)
                {
                    message = "[Area Protection Mod]" +
                    "\nYou cannot claim this area." +
                    "\nYou are too close to another protected area." +
                    "\nThe coordinates of your protected area are " + clientAreaPosition +
                    "\nYour current coordinates are " + playerPosition +
                    "\nPress End key to toggle messages.";
                }
                else if (ableToClaim == false && protectionCylinder == null)
                {
                    message = "[Area Protection Mod]" +
                    "\nYou cannot claim this area." +
                    "\nYou are too close to another protected area." +
                    "\nPress End key to toggle messages.";
                }

                int fontSize = GUI.skin.label.fontSize;
                FontStyle fontStyle = GUI.skin.label.fontStyle;
                GUI.skin.label.fontSize = 12;
                GUI.skin.label.fontStyle = FontStyle.Bold;
                GUI.Label(infoRect, message);
                GUI.skin.label.fontSize = fontSize;
                GUI.skin.label.fontStyle = fontStyle;
            }
        }
        catch (Exception e)
        {
            Debug.Log("Area Protection Mod: GUI Error: " + e.Message);
        }
    }
}