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
    private static GameObject protectionSphere;
    private static List<string> savedPlayers;
    private static List<Player> allowedPlayers;
    private static Texture2D protectionSphereTexture;
    private static List<Area> areas = new List<Area>();
    private static readonly string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    private static readonly string protectionSphereTexurePath = Path.Combine(assemblyFolder, "Images/ProtectionSphere.png");
    private UriBuilder protectionSphereUriBuilder = new UriBuilder(protectionSphereTexurePath);
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

    //! Called by unity engine on start up to initialize variables.
    public IEnumerator Start()
    {
        savedPlayers = new List<string>();
        allowedPlayers = new List<Player>();
        protectionSphereUriBuilder.Scheme = "file";
        protectionSphereTexture = new Texture2D(1024, 1024, TextureFormat.DXT5, false);
        using (WWW www = new WWW(protectionSphereUriBuilder.ToString()))
        {
            yield return www;
            www.LoadImageIntoTexture(protectionSphereTexture);
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

            if (PlayerPrefs.GetInt(NetworkManager.instance.mClientThread.serverIP + "createdSphere") == 1)
            {
                if (protectionSphere == null)
                {
                    ulong userID = NetworkManager.instance.mClientThread.mPlayer.mUserID;
                    System.Net.IPAddress serverIP = NetworkManager.instance.mClientThread.serverIP;

                    float sphereX = PlayerPrefs.GetFloat(userID + ":" + serverIP + "sphereX");
                    float sphereY = PlayerPrefs.GetFloat(userID + ":" + serverIP + "sphereY");
                    float sphereZ = PlayerPrefs.GetFloat(userID + ":" + serverIP + "sphereZ");
                    Vector3 spherePosition = new Vector3(sphereX, sphereY, sphereZ);
                    CreateProtectionSphere(spherePosition);

                    float areaX = PlayerPrefs.GetFloat(userID + ":" + serverIP + "areaX");
                    float areaY = PlayerPrefs.GetFloat(userID + ":" + serverIP + "areaY");
                    float areaZ = PlayerPrefs.GetFloat(userID + ":" + serverIP + "areaZ");
                    clientAreaPosition = new Vector3(areaX, areaY, areaZ);
                }
            }

            Player player = NetworkManager.instance.mClientThread.mPlayer;
            float x = player.mnWorldX - WorldScript.instance.mWorldData.mSpawnX;
            float y = player.mnWorldY - WorldScript.instance.mWorldData.mSpawnY;
            float z = player.mnWorldZ - WorldScript.instance.mWorldData.mSpawnZ;
            playerPosition = new Vector3(x, y, z);
        }
    }

    // Holds information about a single area.
    private struct Area
    {
        public string areaID;
        public Vector3 areaLocation;
    }

    // Networking.
    private static void ClientRead(NetIncomingMessage netIncomingMessage)
    {
        int readInt = netIncomingMessage.ReadInt32();
        if (readInt == 1)
        {
            ableToClaim = false;
        }
        else if (readInt == 3)
        {
            ableToClaim = true;
        }
        else if (readInt == 5)
        {
            CollectStartingItems();
        }
        else
        {
            NetworkManager.instance.mClientThread.mPlayer.mBuildPermission = (eBuildPermission)readInt;
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
        writer.Write((int)data);
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

            if (protectionSphere == null)
            {
                CreateProtectionSphere(Camera.main.transform.position);
            }
            else
            {
                MoveProtectionSphere(Camera.main.transform.position);
            }

            Player player = NetworkManager.instance.mClientThread.mPlayer;
            System.Net.IPAddress serverIP = NetworkManager.instance.mClientThread.serverIP;
            PlayerPrefs.SetInt(player.mUserID + ":" + serverIP + "createdSphere", 1);
            PlayerPrefs.SetFloat(player.mUserID + ":" + serverIP + "sphereX", Camera.main.transform.position.x);
            PlayerPrefs.SetFloat(player.mUserID + ":" + serverIP + "sphereY", Camera.main.transform.position.y);
            PlayerPrefs.SetFloat(player.mUserID + ":" + serverIP + "sphereZ", Camera.main.transform.position.z);
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
                                        int distance = (int)Vector3.Distance(clientPosition, areas[j].areaLocation);

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

                                if (savedPlayers != null)
                                {
                                    if (!savedPlayers.Contains(player.mUserID + player.mUserName))
                                    {
                                        Announce("Giving starting items to " + player.mUserName);
                                        SavePlayer(player.mUserID + player.mUserName);
                                        ModManager.ModSendServerCommToClient("Maverick.AreaProtection", player, 5);
                                        yield return new WaitForSeconds(0.5f);
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
                                        ModManager.ModSendServerCommToClient("Maverick.AreaProtection", player, 1);
                                        yield return new WaitForSeconds(0.5f);
                                    }
                                    else
                                    {
                                        if (!allowedPlayers.Contains(player))
                                        {
                                            allowedPlayers.Add(player);
                                        }
                                        ModManager.ModSendServerCommToClient("Maverick.AreaProtection", player, 3);
                                        yield return new WaitForSeconds(0.5f);
                                    }
                                }

                                if (NetworkManager.instance.mAdminListManager != null)
                                {
                                    if (NetworkManager.instance.mAdminListManager.CheckAdminList(player.mUserID, player.mUserName))
                                    {
                                        player.mBuildPermission = eBuildPermission.Admin;
                                        ModManager.ModSendServerCommToClient("Maverick.AreaProtection", player, 0);
                                    }
                                    else
                                    {
                                        if (cannotBuild == true)
                                        {
                                            player.mBuildPermission = eBuildPermission.Visitor;
                                            ModManager.ModSendServerCommToClient("Maverick.AreaProtection", player, 4);
                                        }
                                        else
                                        {
                                            player.mBuildPermission = eBuildPermission.Builder;
                                            ModManager.ModSendServerCommToClient("Maverick.AreaProtection", player, 2);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        yield return new WaitForSeconds(1.5f);
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

    // Creates the protection sphere.
    private static void CreateProtectionSphere(Vector3 position)
    {
        protectionSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        protectionSphere.transform.position = position;
        protectionSphere.transform.localScale += new Vector3(372, 372, 372);
        protectionSphere.GetComponent<Renderer>().material.mainTexture = protectionSphereTexture;
        protectionSphere.GetComponent<Renderer>().receiveShadows = false;
        protectionSphere.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        protectionSphere.GetComponent<Renderer>().material.shader = Shader.Find("Transparent/Diffuse");
        protectionSphere.GetComponent<Renderer>().material.EnableKeyword("_ALPHATEST_ON");

        GameObject protectionSphereInner = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        protectionSphereInner.transform.position = Camera.main.transform.position;
        protectionSphereInner.transform.localScale += new Vector3(372, 372, 372);
        ConvertNormals(protectionSphereInner);
        protectionSphereInner.GetComponent<Renderer>().material.mainTexture = protectionSphereTexture;
        protectionSphereInner.GetComponent<Renderer>().receiveShadows = false;
        protectionSphereInner.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        protectionSphereInner.GetComponent<Renderer>().material.shader = Shader.Find("Transparent/Diffuse");
        protectionSphereInner.GetComponent<Renderer>().material.EnableKeyword("_ALPHATEST_ON");
        protectionSphereInner.transform.SetParent(protectionSphere.transform);
    }

    // Moves the protection sphere.
    private static void MoveProtectionSphere(Vector3 position)
    {
        protectionSphere.transform.position = position;
    }

    // Recalculates mesh normals so textures are visible inside the sphere.
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
                else if (ableToClaim == true && protectionSphere == null)
                {
                    message = "[Area Protection Mod]" +
                    "\nPress Home key to claim this area." +
                    "\nPress End key to toggle messages.";
                }
                else if (ableToClaim == true && protectionSphere != null)
                {
                    message = "[Area Protection Mod]" +
                    "\nPress Home key to relocate your claimed area." +
                    "\nThe coordinates of your protected area are " + clientAreaPosition +
                    "\nYour current coordinates are " + playerPosition +
                    "\nPress End key to toggle messages.";
                }
                else if (ableToClaim == false && protectionSphere != null)
                {
                    message = "[Area Protection Mod]" +
                    "\nYou cannot claim this area." +
                    "\nYou are too close to another protected area." +
                    "\nThe coordinates of your protected area are " + clientAreaPosition +
                    "\nYour current coordinates are " + playerPosition +
                    "\nPress End key to toggle messages.";
                }
                else if (ableToClaim == false && protectionSphere == null)
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