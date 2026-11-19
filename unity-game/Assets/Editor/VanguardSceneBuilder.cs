using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using VanguardZero.AI;
using VanguardZero.Core;
using VanguardZero.Gameplay;
using VanguardZero.Networking;
using VanguardZero.UI;
using VanguardZero.VFX;
using VanguardZero.Weapons;

namespace VanguardZero.EditorTools
{
    /// <summary>
    /// Authoritative scene, prefab, NavMesh, lighting, and build-settings builder for VANGUARD ZERO.
    /// Produces MainMenu.unity, Blacksite09.unity, Player_Networked.prefab, and Bot_Networked.prefab.
    /// </summary>
    public static class VanguardSceneBuilder
    {
        public const string PlayerPrefabPath = "Assets/Prefabs/Player_Networked.prefab";
        public const string BotPrefabPath = "Assets/Prefabs/Bot_Networked.prefab";
        public const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        public const string BlacksiteScenePath = "Assets/Scenes/Blacksite09.unity";

        public static string BuildAll()
        {
            VanguardTextureGenerator.EnsureAllMaterials();
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Scenes");

            var playerPrefab = BuildPlayerPrefab();
            var botPrefab = BuildBotPrefab();

            BuildBlacksite09Scene(playerPrefab, botPrefab);
            BuildMainMenuScene(playerPrefab, botPrefab);
            ConfigureBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "VANGUARD ZERO Prefabs, Blacksite09.unity (with baked NavMesh), MainMenu.unity, and BuildSettings built cleanly.";
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int idx = path.LastIndexOf('/');
            string parent = path.Substring(0, idx);
            string child = path.Substring(idx + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, child);
        }

        // =========================================================================
        // PREFABS: Player_Networked & Bot_Networked
        // =========================================================================

        public static GameObject BuildPlayerPrefab()
        {
            var root = new GameObject("Player_Networked");
            int hitboxLayer = LayerMask.NameToLayer("PlayerHitbox");
            if (hitboxLayer >= 0) root.layer = hitboxLayer;

            root.AddComponent<NetworkObject>();
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.82f;
            cc.radius = 0.38f;
            cc.center = new Vector3(0f, 0.91f, 0f);
            cc.stepOffset = 0.42f;
            cc.slopeLimit = 48f;

            var surf = root.AddComponent<VanguardSurfaceTag>();
            surf.Surface = VanguardSurfaceTag.SurfaceKind.Armor;

            root.AddComponent<NetworkPlayerBridge>();
            root.AddComponent<TacticalWeaponController>();
            root.AddComponent<TacticalFpsController>();

            // Camera Pivot + First-Person Camera + Weapon Rig
            var camPivot = new GameObject("CameraPivot");
            camPivot.transform.SetParent(root.transform, false);
            camPivot.transform.localPosition = new Vector3(0f, 1.64f, 0f);

            var camGo = new GameObject("FP_Camera");
            camGo.transform.SetParent(camPivot.transform, false);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 78f;
            cam.nearClipPlane = 0.04f;
            cam.farClipPlane = 320f;
            var uac = camGo.AddComponent<UniversalAdditionalCameraData>();
            uac.renderPostProcessing = true;
            uac.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();

            var weaponRigGo = new GameObject("WeaponRig");
            weaponRigGo.transform.SetParent(camGo.transform, false);
            var rig = weaponRigGo.AddComponent<TacticalWeaponRig>();
            BuildProceduralWeaponViewmodels(weaponRigGo.transform);
            rig.AutoDiscoverWeaponParts();

            // 3rd-Person Tactical Operator Rig (visible to remote peers & casts shadows)
            var visual3P = BuildOperatorVisualHierarchy("OperatorVisual3P", root.transform);
            visual3P.AddComponent<TacticalOperatorRig>().AutoBindHierarchy();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        public static GameObject BuildBotPrefab()
        {
            var root = new GameObject("Bot_Networked");
            int botLayer = LayerMask.NameToLayer("BotHitbox");
            if (botLayer >= 0) root.layer = botLayer;

            root.AddComponent<NetworkObject>();
            var col = root.AddComponent<CapsuleCollider>();
            col.height = 1.82f;
            col.radius = 0.38f;
            col.center = new Vector3(0f, 0.91f, 0f);

            var surf = root.AddComponent<VanguardSurfaceTag>();
            surf.Surface = VanguardSurfaceTag.SurfaceKind.Armor;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.42f;
            agent.height = 1.82f;
            agent.baseOffset = 0f;
            agent.speed = 4.6f;
            agent.angularSpeed = 360f;
            agent.acceleration = 14f;

            root.AddComponent<NetworkBotSync>();
            root.AddComponent<TacticalBotController>();

            var visual3P = BuildOperatorVisualHierarchy("OperatorVisual3P", root.transform);
            visual3P.AddComponent<TacticalOperatorRig>().AutoBindHierarchy();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, BotPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Builds detailed multi-part hard-surface weapon viewmodels for VX-47, M890, and M19.
        /// Cleanly replaced by Blender FBXs when VanguardAssetIntegrator runs.
        /// </summary>
        private static void BuildProceduralWeaponViewmodels(Transform rigRoot)
        {
            var steel = VanguardTextureGenerator.LoadMat("Mat_WeaponDarkSteel");
            var poly = VanguardTextureGenerator.LoadMat("Mat_WeaponPolymer");
            var brush = VanguardTextureGenerator.LoadMat("Mat_SteelCatwalk");
            var amberGlow = VanguardTextureGenerator.LoadMat("Mat_EmissiveAmber");
            var greenGlow = VanguardTextureGenerator.LoadMat("Mat_EmissiveOpticGreen");

            // 0: VX-47 Assault Rifle
            var vx = new GameObject("WeaponModel_0").transform;
            vx.SetParent(rigRoot, false);
            MakeNoColPart("Receiver", vx, PrimitiveType.Cube, new Vector3(0f, 0f, 0.08f), new Vector3(0.052f, 0.078f, 0.30f), steel);
            MakeNoColPart("Handguard", vx, PrimitiveType.Cube, new Vector3(0f, 0.008f, 0.34f), new Vector3(0.048f, 0.062f, 0.28f), poly);
            MakeNoColPart("TopRail", vx, PrimitiveType.Cube, new Vector3(0f, 0.044f, 0.22f), new Vector3(0.026f, 0.012f, 0.46f), brush);
            MakeNoColPart("Barrel", vx, PrimitiveType.Cylinder, new Vector3(0f, 0.012f, 0.56f), new Vector3(0.020f, 0.14f, 0.020f), steel, Quaternion.Euler(90f, 0f, 0f));
            MakeNoColPart("Suppressor", vx, PrimitiveType.Cylinder, new Vector3(0f, 0.012f, 0.68f), new Vector3(0.036f, 0.09f, 0.036f), poly, Quaternion.Euler(90f, 0f, 0f));
            MakeNoColPart("Stock", vx, PrimitiveType.Cube, new Vector3(0f, -0.014f, -0.18f), new Vector3(0.042f, 0.095f, 0.24f), poly);
            MakeNoColPart("Grip", vx, PrimitiveType.Cube, new Vector3(0f, -0.082f, -0.02f), new Vector3(0.036f, 0.11f, 0.044f), poly, Quaternion.Euler(-16f, 0f, 0f));
            MakeNoColPart("Magazine", vx, PrimitiveType.Cube, new Vector3(0f, -0.095f, 0.13f), new Vector3(0.030f, 0.16f, 0.064f), poly, Quaternion.Euler(12f, 0f, 0f));
            MakeNoColPart("Bolt", vx, PrimitiveType.Cube, new Vector3(0.026f, 0.016f, 0.12f), new Vector3(0.012f, 0.018f, 0.08f), brush);
            MakeNoColPart("OpticFrame", vx, PrimitiveType.Cube, new Vector3(0f, 0.072f, 0.08f), new Vector3(0.044f, 0.046f, 0.085f), steel);
            MakeNoColPart("OpticGlass", vx, PrimitiveType.Cube, new Vector3(0f, 0.074f, 0.082f), new Vector3(0.010f, 0.010f, 0.005f), amberGlow);
            MakeAnchor("MuzzlePoint", vx, new Vector3(0f, 0.012f, 0.78f));
            MakeAnchor("EjectPort", vx, new Vector3(0.036f, 0.016f, 0.12f));

            // 1: M890 Tactical Shotgun
            var m890 = new GameObject("WeaponModel_1").transform;
            m890.SetParent(rigRoot, false);
            MakeNoColPart("Receiver", m890, PrimitiveType.Cube, new Vector3(0f, 0f, 0.06f), new Vector3(0.056f, 0.082f, 0.26f), steel);
            MakeNoColPart("Barrel", m890, PrimitiveType.Cylinder, new Vector3(0f, 0.026f, 0.42f), new Vector3(0.030f, 0.27f, 0.030f), steel, Quaternion.Euler(90f, 0f, 0f));
            MakeNoColPart("TubeMag", m890, PrimitiveType.Cylinder, new Vector3(0f, -0.006f, 0.38f), new Vector3(0.028f, 0.23f, 0.028f), brush, Quaternion.Euler(90f, 0f, 0f));
            MakeNoColPart("ForendPump", m890, PrimitiveType.Cube, new Vector3(0f, -0.010f, 0.34f), new Vector3(0.058f, 0.056f, 0.19f), poly);
            MakeNoColPart("Stock", m890, PrimitiveType.Cube, new Vector3(0f, -0.024f, -0.19f), new Vector3(0.044f, 0.105f, 0.26f), poly);
            MakeNoColPart("SightPost", m890, PrimitiveType.Cube, new Vector3(0f, 0.052f, 0.65f), new Vector3(0.012f, 0.020f, 0.014f), greenGlow);
            MakeAnchor("MuzzlePoint", m890, new Vector3(0f, 0.026f, 0.72f));
            MakeAnchor("EjectPort", m890, new Vector3(0.038f, 0.014f, 0.10f));

            // 2: M19 Tactical Pistol
            var m19 = new GameObject("WeaponModel_2").transform;
            m19.SetParent(rigRoot, false);
            MakeNoColPart("Frame", m19, PrimitiveType.Cube, new Vector3(0f, 0.002f, 0.06f), new Vector3(0.034f, 0.034f, 0.19f), poly);
            MakeNoColPart("Slide", m19, PrimitiveType.Cube, new Vector3(0f, 0.026f, 0.06f), new Vector3(0.036f, 0.036f, 0.21f), steel);
            MakeNoColPart("Grip", m19, PrimitiveType.Cube, new Vector3(0f, -0.056f, -0.01f), new Vector3(0.034f, 0.11f, 0.048f), poly, Quaternion.Euler(-14f, 0f, 0f));
            MakeNoColPart("Magazine", m19, PrimitiveType.Cube, new Vector3(0f, -0.065f, -0.01f), new Vector3(0.024f, 0.095f, 0.036f), brush, Quaternion.Euler(-14f, 0f, 0f));
            MakeNoColPart("FrontSight", m19, PrimitiveType.Cube, new Vector3(0f, 0.048f, 0.155f), new Vector3(0.008f, 0.010f, 0.008f), greenGlow);
            MakeAnchor("MuzzlePoint", m19, new Vector3(0f, 0.026f, 0.20f));
            MakeAnchor("EjectPort", m19, new Vector3(0.024f, 0.028f, 0.07f));
        }

        /// <summary>
        /// Builds an articulated multi-part armored special-ops operator matching the MotionBricks/Unity Humanoid bone hierarchy.
        /// </summary>
        public static GameObject BuildOperatorVisualHierarchy(string name, Transform parent)
        {
            var armorMat = VanguardTextureGenerator.LoadMat("Mat_OperatorArmor");
            var clothMat = VanguardTextureGenerator.LoadMat("Mat_OperatorCloth");
            var steelMat = VanguardTextureGenerator.LoadMat("Mat_WeaponDarkSteel");
            var visorMat = VanguardTextureGenerator.LoadMat("Mat_EmissiveAmber");

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var hips = new GameObject("Hips").transform;
            hips.SetParent(root.transform, false);
            hips.localPosition = new Vector3(0f, 0.96f, 0f);
            MakeNoColPart("PelvisBelt", hips, PrimitiveType.Cube, Vector3.zero, new Vector3(0.36f, 0.16f, 0.24f), clothMat);

            var spine = new GameObject("Spine").transform;
            spine.SetParent(hips, false);
            spine.localPosition = new Vector3(0f, 0.14f, 0f);

            var chest = new GameObject("Chest").transform;
            chest.SetParent(spine, false);
            chest.localPosition = new Vector3(0f, 0.24f, 0f);
            MakeNoColPart("PlateCarrier", chest, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), new Vector3(0.42f, 0.44f, 0.28f), armorMat);
            MakeNoColPart("MagPouches", chest, PrimitiveType.Cube, new Vector3(0f, -0.06f, 0.15f), new Vector3(0.32f, 0.15f, 0.07f), steelMat);
            MakeNoColPart("Backpack", chest, PrimitiveType.Cube, new Vector3(0f, 0.04f, -0.18f), new Vector3(0.30f, 0.36f, 0.14f), clothMat);

            var head = new GameObject("Head").transform;
            head.SetParent(chest, false);
            head.localPosition = new Vector3(0f, 0.34f, 0f);
            MakeNoColPart("HelmetShell", head, PrimitiveType.Sphere, new Vector3(0f, 0.04f, 0f), new Vector3(0.25f, 0.27f, 0.27f), armorMat);
            MakeNoColPart("VisorStrip", head, PrimitiveType.Cube, new Vector3(0f, 0.04f, 0.125f), new Vector3(0.19f, 0.055f, 0.04f), visorMat);
            MakeNoColPart("NvgMount", head, PrimitiveType.Cube, new Vector3(0f, 0.12f, 0.14f), new Vector3(0.06f, 0.06f, 0.07f), steelMat);
            MakeAnchor("HeadEyePoint", head, new Vector3(0f, 0.05f, 0.12f));

            // Arms
            var lArm = new GameObject("LeftUpperArm").transform;
            lArm.SetParent(chest, false);
            lArm.localPosition = new Vector3(-0.27f, 0.14f, 0f);
            MakeNoColPart("ShoulderPadL", lArm, PrimitiveType.Cube, new Vector3(0f, -0.03f, 0f), new Vector3(0.14f, 0.16f, 0.16f), armorMat);
            MakeNoColPart("ArmMeshL", lArm, PrimitiveType.Capsule, new Vector3(0.05f, -0.18f, 0.18f), new Vector3(0.11f, 0.24f, 0.11f), clothMat, Quaternion.Euler(55f, 18f, 0f));

            var rArm = new GameObject("RightUpperArm").transform;
            rArm.SetParent(chest, false);
            rArm.localPosition = new Vector3(0.27f, 0.14f, 0f);
            MakeNoColPart("ShoulderPadR", rArm, PrimitiveType.Cube, new Vector3(0f, -0.03f, 0f), new Vector3(0.14f, 0.16f, 0.16f), armorMat);
            MakeNoColPart("ArmMeshR", rArm, PrimitiveType.Capsule, new Vector3(-0.04f, -0.18f, 0.20f), new Vector3(0.11f, 0.24f, 0.11f), clothMat, Quaternion.Euler(65f, -12f, 0f));

            // 3P Weapon Mount
            var weapMount = new GameObject("RightHandWeaponMount").transform;
            weapMount.SetParent(chest, false);
            weapMount.localPosition = new Vector3(0.16f, -0.04f, 0.36f);
            MakeNoColPart("Rifle3P_Body", weapMount, PrimitiveType.Cube, new Vector3(0f, 0f, 0.18f), new Vector3(0.06f, 0.09f, 0.68f), steelMat);
            MakeAnchor("MuzzlePoint", weapMount, new Vector3(0f, 0.02f, 0.58f));

            // Legs
            BuildLeg("Left", -0.11f, hips, clothMat, armorMat, steelMat);
            BuildLeg("Right", 0.11f, hips, clothMat, armorMat, steelMat);

            return root;
        }

        private static void BuildLeg(string side, float xOffset, Transform hips, Material cloth, Material armor, Material boot)
        {
            var upper = new GameObject($"{side}UpperLeg").transform;
            upper.SetParent(hips, false);
            upper.localPosition = new Vector3(xOffset, -0.06f, 0f);
            MakeNoColPart($"{side}Thigh", upper, PrimitiveType.Capsule, new Vector3(0f, -0.22f, 0f), new Vector3(0.15f, 0.24f, 0.16f), cloth);

            var lower = new GameObject($"{side}LowerLeg").transform;
            lower.SetParent(upper, false);
            lower.localPosition = new Vector3(0f, -0.44f, 0f);
            MakeNoColPart($"{side}KneePad", lower, PrimitiveType.Cube, new Vector3(0f, 0.0f, 0.07f), new Vector3(0.12f, 0.12f, 0.06f), armor);
            MakeNoColPart($"{side}Shin", lower, PrimitiveType.Capsule, new Vector3(0f, -0.22f, 0f), new Vector3(0.13f, 0.22f, 0.14f), cloth);
            MakeNoColPart($"{side}Boot", lower, PrimitiveType.Cube, new Vector3(0f, -0.42f, 0.05f), new Vector3(0.13f, 0.11f, 0.26f), boot);
        }

        // =========================================================================
        // SCENE 1: BLACKSITE-09 (Multi-Level Tactical FPS Arena + NavMesh + Lights)
        // =========================================================================

        public static void BuildBlacksite09Scene(GameObject playerPrefab, GameObject botPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureDuskAtmosphereAndLighting("Blacksite09");

            var worldRoot = new GameObject("Blacksite09_World");
            int wgLayer = LayerMask.NameToLayer("WorldGeometry");
            if (wgLayer < 0) wgLayer = 0;

            var wetTarmac = VanguardTextureGenerator.LoadMat("Mat_WetTarmac");
            var concreteRaw = VanguardTextureGenerator.LoadMat("Mat_ConcreteRaw");
            var concreteTrim = VanguardTextureGenerator.LoadMat("Mat_ConcreteTrim");
            var concreteLight = VanguardTextureGenerator.LoadMat("Mat_ConcreteLight");
            var steelCatwalk = VanguardTextureGenerator.LoadMat("Mat_SteelCatwalk");
            var steelDark = VanguardTextureGenerator.LoadMat("Mat_SteelGunmetal");
            var contOlive = VanguardTextureGenerator.LoadMat("Mat_ContainerOlive");
            var contSlate = VanguardTextureGenerator.LoadMat("Mat_ContainerSlate");
            var contRust = VanguardTextureGenerator.LoadMat("Mat_ContainerRust");
            var hazard = VanguardTextureGenerator.LoadMat("Mat_HazardStripe");
            var sandbag = VanguardTextureGenerator.LoadMat("Mat_Sandbag");
            var emAmber = VanguardTextureGenerator.LoadMat("Mat_EmissiveAmber");
            var emCyan = VanguardTextureGenerator.LoadMat("Mat_EmissiveCyan");
            var emCrimson = VanguardTextureGenerator.LoadMat("Mat_EmissiveCrimson");
            var floodLens = VanguardTextureGenerator.LoadMat("Mat_FloodlightLens");

            // 1. Main Rain-Slicked Courtyard Tarmac Ground (96 x 84m)
            MakeBox("Ground_WetTarmac", worldRoot.transform, new Vector3(0f, -0.5f, 0f), new Vector3(96f, 1f, 84f), wetTarmac, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);

            // Tactical Helipad / Center Courtyard Apron
            MakeBox("CenterApron_Concrete", worldRoot.transform, new Vector3(0f, 0.03f, 0f), new Vector3(24f, 0.08f, 20f), concreteTrim, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("CenterApron_HazardN", worldRoot.transform, new Vector3(0f, 0.08f, 9.6f), new Vector3(22f, 0.04f, 0.7f), hazard, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("CenterApron_HazardS", worldRoot.transform, new Vector3(0f, 0.08f, -9.6f), new Vector3(22f, 0.04f, 0.7f), hazard, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);

            // 2. Towering Brutalist Perimeter Blast Walls (84m x 72m playable bounds)
            MakeBox("Wall_North", worldRoot.transform, new Vector3(0f, 4.5f, 36f), new Vector3(86f, 9f, 1.5f), concreteRaw, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("Wall_South", worldRoot.transform, new Vector3(0f, 4.5f, -36f), new Vector3(86f, 9f, 1.5f), concreteRaw, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("Wall_East", worldRoot.transform, new Vector3(43f, 4.5f, 0f), new Vector3(1.5f, 9f, 74f), concreteRaw, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("Wall_West", worldRoot.transform, new Vector3(-43f, 4.5f, 0f), new Vector3(1.5f, 9f, 74f), concreteRaw, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);
            // Perimeter dark concrete parapet cap
            MakeBox("Parapet_N", worldRoot.transform, new Vector3(0f, 9.2f, 36f), new Vector3(86.6f, 0.5f, 2.0f), concreteTrim, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("Parapet_S", worldRoot.transform, new Vector3(0f, 9.2f, -36f), new Vector3(86.6f, 0.5f, 2.0f), concreteTrim, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);

            // 3. HERO CENTERPIECE: Blacksite-09 Radar Dish Comms Tower (center courtyard)
            BuildRadarTowerCenterpiece(worldRoot.transform, new Vector3(0f, 0f, 0f), concreteTrim, steelDark, steelCatwalk, emCrimson, emCyan, wgLayer);

            // 4. NORTH ALPHA COMMAND BUNKER (Two-Level with Interior & Rooftop Overlook)
            BuildCommandBunker(worldRoot.transform, new Vector3(0f, 0f, 23f), 1, concreteRaw, concreteTrim, concreteLight, steelCatwalk, emAmber, hazard, wgLayer);

            // 5. SOUTH BRAVO SERVER & COMMS FACILITY (Two-Level with Glowing Cyan Server Racks)
            BuildServerFacility(worldRoot.transform, new Vector3(0f, 0f, -23f), concreteRaw, concreteTrim, concreteLight, steelCatwalk, steelDark, emCyan, emAmber, wgLayer);

            // 6. ELEVATED STEEL CATWALKS & RAMPS (East & West Overpasses @ +4.0m)
            BuildElevatedCatwalk(worldRoot.transform, new Vector3(-22f, 4.0f, 0f), steelCatwalk, steelDark, wgLayer);
            BuildElevatedCatwalk(worldRoot.transform, new Vector3(22f, 4.0f, 0f), steelCatwalk, steelDark, wgLayer);

            // 7. SHIPPING CONTAINERS (Dynamic Cover Lanes & Sightline Breakers)
            BuildShippingContainer(worldRoot.transform, "Container_NW", new Vector3(-13f, 0f, 10f), 18f, contOlive, steelDark, wgLayer);
            BuildShippingContainer(worldRoot.transform, "Container_NE", new Vector3(14f, 0f, 11f), -22f, contSlate, steelDark, wgLayer);
            BuildShippingContainer(worldRoot.transform, "Container_SW", new Vector3(-14f, 0f, -11f), -16f, contRust, steelDark, wgLayer);
            BuildShippingContainer(worldRoot.transform, "Container_SE", new Vector3(13f, 0f, -10f), 20f, contOlive, steelDark, wgLayer);
            BuildShippingContainer(worldRoot.transform, "Container_WestFlank", new Vector3(-32f, 0f, 0f), 90f, contSlate, steelDark, wgLayer);
            BuildShippingContainer(worldRoot.transform, "Container_EastFlank", new Vector3(32f, 0f, 0f), 90f, contRust, steelDark, wgLayer);

            // 8. JERSEY BARRIERS, SANDBAGS & TACTICAL SUPPLY CRATES
            Vector3[] barrierCoords =
            {
                new Vector3(-7f, 0f, 6.5f), new Vector3(7f, 0f, 6.5f),
                new Vector3(-7f, 0f, -6.5f), new Vector3(7f, 0f, -6.5f),
                new Vector3(-27f, 0f, 14f), new Vector3(27f, 0f, 14f),
                new Vector3(-27f, 0f, -14f), new Vector3(27f, 0f, -14f),
                new Vector3(0f, 0f, 13f), new Vector3(0f, 0f, -13f)
            };
            for (int i = 0; i < barrierCoords.Length; i++)
            {
                float yaw = (i % 2 == 0) ? 12f : -12f;
                BuildJerseyBarrier(worldRoot.transform, $"JerseyBarrier_{i:00}", barrierCoords[i], yaw, concreteLight, hazard, wgLayer);
            }

            BuildSandbagEmplacement(worldRoot.transform, "Sandbags_West", new Vector3(-16f, 0f, 0f), 90f, sandbag, wgLayer);
            BuildSandbagEmplacement(worldRoot.transform, "Sandbags_East", new Vector3(16f, 0f, 0f), 90f, sandbag, wgLayer);

            BuildSupplyCrate(worldRoot.transform, "Crate_Alpha", new Vector3(-5f, 0f, 18f), 25f, contOlive, emAmber, wgLayer);
            BuildSupplyCrate(worldRoot.transform, "Crate_Bravo", new Vector3(5f, 0f, -18f), -30f, contOlive, emCyan, wgLayer);
            BuildSupplyCrate(worldRoot.transform, "Crate_MidW", new Vector3(-9f, 0f, 0f), 8f, contOlive, emAmber, wgLayer);
            BuildSupplyCrate(worldRoot.transform, "Crate_MidE", new Vector3(9f, 0f, 0f), -14f, contOlive, emCyan, wgLayer);

            // 9. TACTICAL FLOODLIGHT TOWERS (Warm Amber Pools contrasting Cool Dusk Moonlight!)
            BuildFloodlightTower(worldRoot.transform, "Floodlight_NW", new Vector3(-19f, 0f, 18f), new Vector3(0f, 0f, 0f), steelDark, floodLens, VanguardPalette.FloodlightWarm, wgLayer);
            BuildFloodlightTower(worldRoot.transform, "Floodlight_NE", new Vector3(19f, 0f, 18f), new Vector3(0f, 0f, 0f), steelDark, floodLens, VanguardPalette.FloodlightWarm, wgLayer);
            BuildFloodlightTower(worldRoot.transform, "Floodlight_SW", new Vector3(-19f, 0f, -18f), new Vector3(0f, 0f, 0f), steelDark, floodLens, VanguardPalette.FloodlightWarm, wgLayer);
            BuildFloodlightTower(worldRoot.transform, "Floodlight_SE", new Vector3(19f, 0f, -18f), new Vector3(0f, 0f, 0f), steelDark, floodLens, VanguardPalette.FloodlightWarm, wgLayer);

            // 10. REFLECTION PROBES (Wet Tarmac & Brushed Steel Reflections)
            CreateReflectionProbe(worldRoot.transform, "Probe_Courtyard", new Vector3(0f, 4.5f, 0f), new Vector3(48f, 18f, 44f));
            CreateReflectionProbe(worldRoot.transform, "Probe_NorthBunker", new Vector3(0f, 3.2f, 23f), new Vector3(28f, 12f, 20f));
            CreateReflectionProbe(worldRoot.transform, "Probe_SouthServer", new Vector3(0f, 3.2f, -23f), new Vector3(28f, 12f, 20f));

            // 11. NAVMESH SURFACE BAKE (Unity AI Navigation 2.0.14)
            var navGo = new GameObject("Blacksite_NavMeshSurface");
            var surface = navGo.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.layerMask = (1 << 0) | (1 << wgLayer);
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            // 12. SPAWN POINTS, WAYPOINTS & COVER NODES
            var spawnGo = new GameObject("BlacksiteSpawnProvider");
            var spawnProvider = spawnGo.AddComponent<BlacksiteSpawnProvider>();
            PopulateSpawnsAndWaypoints(spawnGo.transform, spawnProvider, (1 << 0) | (1 << wgLayer));

            // 13. SCENE NETWORK MATCH MANAGER + GAMEPLAY MANAGERS + FALLBACK CAMERA
            var matchMgrGo = new GameObject("NetworkMatchManager");
            matchMgrGo.AddComponent<NetworkObject>();
            var matchMgr = matchMgrGo.AddComponent<NetworkMatchManager>();
            WireMatchManagerPrefabs(matchMgr, playerPrefab, botPrefab);

            new GameObject("VanguardVfxManager").AddComponent<VanguardVfxManager>();
            new GameObject("VanguardWeatherVfx").AddComponent<VanguardWeatherVfx>();
            new GameObject("VanguardHudUI").AddComponent<VanguardHudUI>();

            // Scene fallback camera (automatically disabled when local Player_Networked spawns)
            var fallbackCamGo = new GameObject("FallbackSpectatorCamera");
            fallbackCamGo.tag = "MainCamera";
            fallbackCamGo.transform.position = new Vector3(-26f, 11f, -26f);
            fallbackCamGo.transform.LookAt(new Vector3(0f, 2.5f, 0f));
            var fbCam = fallbackCamGo.AddComponent<Camera>();
            fbCam.fieldOfView = 62f;
            var fbUac = fallbackCamGo.AddComponent<UniversalAdditionalCameraData>();
            fbUac.renderPostProcessing = true;
            fbUac.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            EditorSceneManager.SaveScene(scene, BlacksiteScenePath);
        }

        // =========================================================================
        // SCENE 2: MAIN MENU (Live 3D Dusk Blacksite-09 Backdrop + Network Root)
        // =========================================================================

        public static void BuildMainMenuScene(GameObject playerPrefab, GameObject botPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureDuskAtmosphereAndLighting("MainMenu");

            var backdrop = new GameObject("MainMenu_3DBackdrop");
            int wgLayer = LayerMask.NameToLayer("WorldGeometry");
            if (wgLayer < 0) wgLayer = 0;

            var wetTarmac = VanguardTextureGenerator.LoadMat("Mat_WetTarmac");
            var concreteRaw = VanguardTextureGenerator.LoadMat("Mat_ConcreteRaw");
            var concreteTrim = VanguardTextureGenerator.LoadMat("Mat_ConcreteTrim");
            var concreteLight = VanguardTextureGenerator.LoadMat("Mat_ConcreteLight");
            var steelCatwalk = VanguardTextureGenerator.LoadMat("Mat_SteelCatwalk");
            var steelDark = VanguardTextureGenerator.LoadMat("Mat_SteelGunmetal");
            var contOlive = VanguardTextureGenerator.LoadMat("Mat_ContainerOlive");
            var contSlate = VanguardTextureGenerator.LoadMat("Mat_ContainerSlate");
            var hazard = VanguardTextureGenerator.LoadMat("Mat_HazardStripe");
            var emAmber = VanguardTextureGenerator.LoadMat("Mat_EmissiveAmber");
            var emCyan = VanguardTextureGenerator.LoadMat("Mat_EmissiveCyan");
            var emCrimson = VanguardTextureGenerator.LoadMat("Mat_EmissiveCrimson");
            var floodLens = VanguardTextureGenerator.LoadMat("Mat_FloodlightLens");

            // Atmospheric rain-slicked courtyard showcase
            MakeBox("Ground_Tarmac", backdrop.transform, new Vector3(0f, -0.5f, 0f), new Vector3(110f, 1f, 110f), wetTarmac, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("Helipad_Base", backdrop.transform, new Vector3(0f, 0.03f, 0f), new Vector3(22f, 0.08f, 22f), concreteTrim, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("Helipad_Stripe", backdrop.transform, new Vector3(0f, 0.08f, 9f), new Vector3(18f, 0.04f, 0.7f), hazard, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);

            BuildRadarTowerCenterpiece(backdrop.transform, new Vector3(0f, 0f, 0f), concreteTrim, steelDark, steelCatwalk, emCrimson, emCyan, wgLayer);
            BuildShippingContainer(backdrop.transform, "ShowcaseContainer_1", new Vector3(-11f, 0f, 7f), 24f, contOlive, steelDark, wgLayer);
            BuildShippingContainer(backdrop.transform, "ShowcaseContainer_2", new Vector3(12f, 0f, -6f), -18f, contSlate, steelDark, wgLayer);
            BuildJerseyBarrier(backdrop.transform, "ShowcaseBarrier_1", new Vector3(-6f, 0f, -7f), 16f, concreteLight, hazard, wgLayer);
            BuildSupplyCrate(backdrop.transform, "ShowcaseCrate", new Vector3(5.5f, 0f, 5.5f), -22f, contOlive, emAmber, wgLayer);

            BuildFloodlightTower(backdrop.transform, "MenuFlood_1", new Vector3(-15f, 0f, 13f), Vector3.zero, steelDark, floodLens, VanguardPalette.FloodlightWarm, wgLayer);
            BuildFloodlightTower(backdrop.transform, "MenuFlood_2", new Vector3(15f, 0f, -13f), Vector3.zero, steelDark, floodLens, VanguardPalette.InteriorCyan, wgLayer);

            // Background distant brutalist compound walls for deep skyline silhouette
            MakeBox("BgBunker_N", backdrop.transform, new Vector3(0f, 5f, 34f), new Vector3(56f, 10f, 10f), concreteRaw, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("BgBunker_W", backdrop.transform, new Vector3(-34f, 4f, 0f), new Vector3(10f, 8f, 48f), concreteTrim, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete);

            // Hero Operator standing on guard in the lit courtyard
            var heroOp = BuildOperatorVisualHierarchy("ShowcaseOperator", backdrop.transform);
            heroOp.transform.position = new Vector3(4.2f, 0f, 4.6f);
            heroOp.transform.rotation = Quaternion.Euler(0f, -145f, 0f);
            var heroRig = heroOp.AddComponent<TacticalOperatorRig>();
            heroRig.AutoBindHierarchy();

            CreateReflectionProbe(backdrop.transform, "MenuProbe", new Vector3(0f, 4f, 0f), new Vector3(44f, 16f, 44f));

            // Orbit Camera
            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(22f, 9.5f, 12f);
            camGo.transform.LookAt(new Vector3(0f, 4.2f, 0f));
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 56f;
            var uac = camGo.AddComponent<UniversalAdditionalCameraData>();
            uac.renderPostProcessing = true;
            uac.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();

            // Weather VFX + Audio + MainMenu UI
            new GameObject("VanguardWeatherVfx").AddComponent<VanguardWeatherVfx>();
            new GameObject("VanguardAudioManager").AddComponent<VanguardAudioManager>();
            new GameObject("VanguardMainMenuUI").AddComponent<VanguardMainMenuUI>();

            // Persistent Network Root (NGO 2.14.0 + UTP + SessionManager + HitValidator)
            var netRoot = new GameObject("VanguardNetworkRoot");
            var transport = netRoot.AddComponent<UnityTransport>();
            var netMgr = netRoot.AddComponent<NetworkManager>();
            ConfigureNetworkManager(netMgr, transport, playerPrefab, botPrefab);
            netRoot.AddComponent<VanguardNetworkBootstrap>();
            netRoot.AddComponent<VanguardSessionManager>();
            netRoot.AddComponent<LagCompensatedHitValidator>();

            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
        }

        private static void ConfigureNetworkManager(NetworkManager netMgr, UnityTransport transport, GameObject playerPrefab, GameObject botPrefab)
        {
            netMgr.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                TickRate = 60,
                EnableSceneManagement = true,
                PlayerPrefab = null,
                ConnectionApproval = false
            };

            // Register NetworkPrefabs asset cleanly
            string prefabsListPath = "Assets/Settings/VanguardNetworkPrefabs.asset";
            var prefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(prefabsListPath);
            if (prefabsList == null)
            {
                prefabsList = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(prefabsList, prefabsListPath);
            }

            // Clear & register
            while (prefabsList.PrefabList.Count > 0)
            {
                prefabsList.Remove(prefabsList.PrefabList[0]);
            }
            if (playerPrefab != null) prefabsList.Add(new NetworkPrefab { Prefab = playerPrefab });
            if (botPrefab != null) prefabsList.Add(new NetworkPrefab { Prefab = botPrefab });
            EditorUtility.SetDirty(prefabsList);

            // Bind via SerializedObject so NGO serializes DefaultNetworkPrefabs cleanly
            var so = new SerializedObject(netMgr);
            var cfg = so.FindProperty("NetworkConfig");
            if (cfg != null)
            {
                var transportProp = cfg.FindPropertyRelative("NetworkTransport");
                if (transportProp != null) transportProp.objectReferenceValue = transport;
                var tickProp = cfg.FindPropertyRelative("TickRate");
                if (tickProp != null) tickProp.uintValue = 60;
                var sceneMgmt = cfg.FindPropertyRelative("EnableSceneManagement");
                if (sceneMgmt != null) sceneMgmt.boolValue = true;
                var playerProp = cfg.FindPropertyRelative("PlayerPrefab");
                if (playerProp != null) playerProp.objectReferenceValue = null;

                var prefabsProp = cfg.FindPropertyRelative("Prefabs");
                if (prefabsProp != null)
                {
                    var listsProp = prefabsProp.FindPropertyRelative("NetworkPrefabsLists");
                    if (listsProp != null)
                    {
                        listsProp.ClearArray();
                        listsProp.InsertArrayElementAtIndex(0);
                        listsProp.GetArrayElementAtIndex(0).objectReferenceValue = prefabsList;
                    }
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(netMgr);
        }

        private static void WireMatchManagerPrefabs(NetworkMatchManager matchMgr, GameObject playerPrefab, GameObject botPrefab)
        {
            var so = new SerializedObject(matchMgr);
            var pProp = so.FindProperty("m_PlayerPrefab");
            if (pProp != null) pProp.objectReferenceValue = playerPrefab;
            var bProp = so.FindProperty("m_BotPrefab");
            if (bProp != null) bProp.objectReferenceValue = botPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(matchMgr);
        }

        private static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuScenePath, true),
                new EditorBuildSettingsScene(BlacksiteScenePath, true)
            };
        }

        // =========================================================================
        // ATMOSPHERE, LIGHTING & ARCHITECTURAL MODULE HELPERS
        // =========================================================================

        private static void ConfigureDuskAtmosphereAndLighting(string label)
        {
            RenderSettings.skybox = VanguardTextureGenerator.LoadMat("Mat_BlacksiteDuskSkybox");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = VanguardPalette.AmbientSky;
            RenderSettings.ambientEquatorColor = VanguardPalette.AmbientEquator;
            RenderSettings.ambientGroundColor = VanguardPalette.AmbientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = VanguardPalette.FogDusk;
            RenderSettings.fogDensity = 0.0115f;

            // Directional Dusk Moonlight Key Light
            var sunGo = new GameObject($"MoonKeyLight_{label}");
            sunGo.transform.rotation = Quaternion.Euler(36f, 142f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = VanguardPalette.MoonKeyLight;
            sun.intensity = 1.32f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.35f;
            RenderSettings.sun = sun;

            // Global URP Volume
            var volGo = new GameObject("GlobalVolume_Blacksite09");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/Blacksite09_VolumeProfile.asset");
        }

        private static void BuildRadarTowerCenterpiece(Transform parent, Vector3 pos, Material concrete, Material steelDark, Material steelBright, Material emRed, Material emCyan, int layer)
        {
            var root = new GameObject("Hero_RadarDishTower").transform;
            root.SetParent(parent, false);
            root.position = pos;

            // Octagonal/Stepped concrete plinth (gives great tactical cover around center!)
            MakeBox("Plinth_Base", root, new Vector3(0f, 0.7f, 0f), new Vector3(5.6f, 1.4f, 5.6f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("Plinth_Cap", root, new Vector3(0f, 1.6f, 0f), new Vector3(4.2f, 0.5f, 4.2f), steelDark, layer, VanguardSurfaceTag.SurfaceKind.Metal);

            // 4 Steel Lattice Corner Legs
            Vector3[] legs = { new Vector3(-1.4f, 5.4f, -1.4f), new Vector3(1.4f, 5.4f, -1.4f), new Vector3(-1.4f, 5.4f, 1.4f), new Vector3(1.4f, 5.4f, 1.4f) };
            for (int i = 0; i < 4; i++)
            {
                MakeBox($"Leg_{i}", root, legs[i], new Vector3(0.32f, 7.6f, 0.32f), steelDark, layer, VanguardSurfaceTag.SurfaceKind.Metal);
            }

            // Cross bracing decks
            MakeBox("Deck_Mid", root, new Vector3(0f, 5.2f, 0f), new Vector3(3.4f, 0.25f, 3.4f), steelBright, layer, VanguardSurfaceTag.SurfaceKind.Metal);
            MakeBox("Deck_Top", root, new Vector3(0f, 9.2f, 0f), new Vector3(3.8f, 0.35f, 3.8f), steelBright, layer, VanguardSurfaceTag.SurfaceKind.Metal);

            // Parabolic Radar Dish Assembly (angled toward stormy dusk sky!)
            var dishPivot = new GameObject("RadarDishAssembly").transform;
            dishPivot.SetParent(root, false);
            dishPivot.localPosition = new Vector3(0f, 11.4f, 0f);
            dishPivot.localRotation = Quaternion.Euler(-28f, 32f, 0f);

            MakeNoColPart("DishPlate", dishPivot, PrimitiveType.Cylinder, Vector3.zero, new Vector3(5.2f, 0.24f, 5.2f), steelBright, Quaternion.Euler(90f, 0f, 0f));
            MakeNoColPart("DishRim", dishPivot, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.25f), new Vector3(5.4f, 0.12f, 5.4f), steelDark, Quaternion.Euler(90f, 0f, 0f));
            MakeNoColPart("FeedHornShaft", dishPivot, PrimitiveType.Cylinder, new Vector3(0f, 0f, 1.4f), new Vector3(0.18f, 1.4f, 0.18f), steelDark, Quaternion.Euler(90f, 0f, 0f));
            MakeNoColPart("FeedHornTip", dishPivot, PrimitiveType.Sphere, new Vector3(0f, 0f, 2.75f), new Vector3(0.55f, 0.55f, 0.55f), emCyan);

            // Aviation Warning Beacons
            MakeNoColPart("Beacon_1", root, PrimitiveType.Sphere, new Vector3(1.8f, 9.6f, 1.8f), new Vector3(0.28f, 0.28f, 0.28f), emRed);
            MakeNoColPart("Beacon_2", root, PrimitiveType.Sphere, new Vector3(-1.8f, 9.6f, -1.8f), new Vector3(0.28f, 0.28f, 0.28f), emRed);

            var beaconLight = new GameObject("TowerBeaconLight").AddComponent<Light>();
            beaconLight.transform.SetParent(root, false);
            beaconLight.transform.localPosition = new Vector3(0f, 10.2f, 0f);
            beaconLight.type = LightType.Point;
            beaconLight.color = VanguardPalette.AccentCrimson;
            beaconLight.intensity = 14f;
            beaconLight.range = 16f;
        }

        private static void BuildCommandBunker(Transform parent, Vector3 pos, int sideSign, Material concrete, Material trim, Material lightConc, Material steel, Material accent, Material hazard, int layer)
        {
            var root = new GameObject("North_AlphaCommandBunker").transform;
            root.SetParent(parent, false);
            root.position = pos;

            // Floor slab + Roof deck (+4.0m playable rooftop!)
            MakeBox("Bunker_Floor", root, new Vector3(0f, 0.12f, 0f), new Vector3(24f, 0.24f, 12f), trim, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("Bunker_Roof", root, new Vector3(0f, 4.0f, 0f), new Vector3(24f, 0.45f, 12f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);

            // Front wall with 3 tactical doorways/breaches
            MakeBox("FrontWall_L", root, new Vector3(-8.5f, 2.0f, -5.8f), new Vector3(7f, 4.0f, 0.7f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("FrontWall_C", root, new Vector3(0f, 2.0f, -5.8f), new Vector3(4.5f, 4.0f, 0.7f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("FrontWall_R", root, new Vector3(8.5f, 2.0f, -5.8f), new Vector3(7f, 4.0f, 0.7f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("BackWall", root, new Vector3(0f, 2.0f, 5.8f), new Vector3(24f, 4.0f, 0.7f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);

            // Rooftop chest-high tactical parapet walls
            MakeBox("RoofParapet_L", root, new Vector3(-7.5f, 4.85f, -5.7f), new Vector3(8.5f, 1.25f, 0.45f), lightConc, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("RoofParapet_R", root, new Vector3(7.5f, 4.85f, -5.7f), new Vector3(8.5f, 1.25f, 0.45f), lightConc, layer, VanguardSurfaceTag.SurfaceKind.Concrete);

            // Interior command console & warm amber overhead tactical lights
            MakeBox("CommandTable", root, new Vector3(0f, 0.65f, 0.8f), new Vector3(4.2f, 1.05f, 1.8f), trim, layer, VanguardSurfaceTag.SurfaceKind.Metal);
            MakeNoColPart("HoloMapStrip", root, PrimitiveType.Cube, new Vector3(0f, 1.20f, 0.8f), new Vector3(3.4f, 0.06f, 1.2f), accent);
            MakeBox("ThresholdStripe", root, new Vector3(0f, 0.25f, -5.8f), new Vector3(22f, 0.04f, 0.9f), hazard, layer, VanguardSurfaceTag.SurfaceKind.Concrete);

            // Smooth NavMesh-walkable concrete access ramps on East & West flanks to rooftop
            MakeRamp("RoofRamp_W", root, new Vector3(-14.5f, 2.0f, 0f), new Vector3(3.4f, 0.4f, 12.2f), Quaternion.Euler(19.5f, 0f, 0f), steel, layer);
            MakeRamp("RoofRamp_E", root, new Vector3(14.5f, 2.0f, 0f), new Vector3(3.4f, 0.4f, 12.2f), Quaternion.Euler(19.5f, 0f, 0f), steel, layer);

            AddPointLight(root, "BunkerLight_W", new Vector3(-6f, 3.3f, 0f), VanguardPalette.FloodlightWarm, 18f, 16f);
            AddPointLight(root, "BunkerLight_E", new Vector3(6f, 3.3f, 0f), VanguardPalette.FloodlightWarm, 18f, 16f);
        }

        private static void BuildServerFacility(Transform parent, Vector3 pos, Material concrete, Material trim, Material lightConc, Material steel, Material steelDark, Material emCyan, Material emAmber, int layer)
        {
            var root = new GameObject("South_BravoServerFacility").transform;
            root.SetParent(parent, false);
            root.position = pos;

            MakeBox("Server_Floor", root, new Vector3(0f, 0.12f, 0f), new Vector3(24f, 0.24f, 12f), trim, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("Server_Roof", root, new Vector3(0f, 4.0f, 0f), new Vector3(24f, 0.45f, 12f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);

            // Courtyard-facing wall with 3 wide tactical entries
            MakeBox("FrontWall_L", root, new Vector3(-8.5f, 2.0f, 5.8f), new Vector3(7f, 4.0f, 0.7f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("FrontWall_C", root, new Vector3(0f, 2.0f, 5.8f), new Vector3(4.5f, 4.0f, 0.7f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("FrontWall_R", root, new Vector3(8.5f, 2.0f, 5.8f), new Vector3(7f, 4.0f, 0.7f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("BackWall", root, new Vector3(0f, 2.0f, -5.8f), new Vector3(24f, 4.0f, 0.7f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);

            MakeBox("RoofParapet_L", root, new Vector3(-7.5f, 4.85f, 5.7f), new Vector3(8.5f, 1.25f, 0.45f), lightConc, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("RoofParapet_R", root, new Vector3(7.5f, 4.85f, 5.7f), new Vector3(8.5f, 1.25f, 0.45f), lightConc, layer, VanguardSurfaceTag.SurfaceKind.Concrete);

            // Server Rack Terminals (interior CQB cover + cyan LED strips!)
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 rackPos = new Vector3(i * 5.2f, 1.25f, -0.4f);
                MakeBox($"ServerRack_{i}", root, rackPos, new Vector3(1.2f, 2.3f, 2.8f), steelDark, layer, VanguardSurfaceTag.SurfaceKind.Metal);
                MakeNoColPart($"ServerLed_{i}", root, PrimitiveType.Cube, rackPos + new Vector3(0f, 0.35f, 1.43f), new Vector3(0.85f, 0.14f, 0.05f), emCyan);
                MakeNoColPart($"ServerLed2_{i}", root, PrimitiveType.Cube, rackPos + new Vector3(0f, -0.15f, 1.43f), new Vector3(0.85f, 0.10f, 0.05f), emAmber);
            }

            // Access Ramps to Roof
            MakeRamp("ServerRamp_W", root, new Vector3(-14.5f, 2.0f, 0f), new Vector3(3.4f, 0.4f, 12.2f), Quaternion.Euler(-19.5f, 0f, 0f), steel, layer);
            MakeRamp("ServerRamp_E", root, new Vector3(14.5f, 2.0f, 0f), new Vector3(3.4f, 0.4f, 12.2f), Quaternion.Euler(-19.5f, 0f, 0f), steel, layer);

            AddPointLight(root, "ServerCyanLight_W", new Vector3(-5.5f, 3.3f, 0.5f), VanguardPalette.InteriorCyan, 20f, 16f);
            AddPointLight(root, "ServerCyanLight_E", new Vector3(5.5f, 3.3f, 0.5f), VanguardPalette.InteriorCyan, 20f, 16f);
        }

        private static void BuildElevatedCatwalk(Transform parent, Vector3 centerPos, Material catwalkMat, Material steelDark, int layer)
        {
            var root = new GameObject($"ElevatedCatwalk_{(centerPos.x < 0 ? "West" : "East")}").transform;
            root.SetParent(parent, false);
            root.position = centerPos;

            // Main elevated deck (26m long along Z, 3.4m wide)
            MakeBox("Deck", root, Vector3.zero, new Vector3(3.6f, 0.25f, 26f), catwalkMat, layer, VanguardSurfaceTag.SurfaceKind.Metal);

            // Low tactical cover rails along inner courtyard edge
            float innerSign = centerPos.x < 0 ? 1f : -1f;
            MakeBox("Rail_N", root, new Vector3(innerSign * 1.65f, 0.65f, 6.5f), new Vector3(0.18f, 1.05f, 9f), steelDark, layer, VanguardSurfaceTag.SurfaceKind.Metal);
            MakeBox("Rail_S", root, new Vector3(innerSign * 1.65f, 0.65f, -6.5f), new Vector3(0.18f, 1.05f, 9f), steelDark, layer, VanguardSurfaceTag.SurfaceKind.Metal);

            // Support columns down to tarmac
            for (int z = -10; z <= 10; z += 10)
            {
                MakeBox($"Pillar_{z}", root, new Vector3(0f, -2.0f, z), new Vector3(0.45f, 4.0f, 0.45f), steelDark, layer, VanguardSurfaceTag.SurfaceKind.Metal);
            }

            // Smooth NavMesh-walkable ramps at North & South ends
            MakeRamp("Ramp_N", root, new Vector3(0f, -2.0f, 18.4f), new Vector3(3.4f, 0.3f, 11.8f), Quaternion.Euler(20f, 0f, 0f), catwalkMat, layer);
            MakeRamp("Ramp_S", root, new Vector3(0f, -2.0f, -18.4f), new Vector3(3.4f, 0.3f, 11.8f), Quaternion.Euler(-20f, 0f, 0f), catwalkMat, layer);
        }

        private static void BuildShippingContainer(Transform parent, string name, Vector3 pos, float yaw, Material bodyMat, Material frameMat, int layer)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            MakeBox("Body", root, new Vector3(0f, 1.4f, 0f), new Vector3(2.6f, 2.8f, 6.4f), bodyMat, layer, VanguardSurfaceTag.SurfaceKind.Metal);
            MakeBox("TopFrame", root, new Vector3(0f, 2.82f, 0f), new Vector3(2.72f, 0.14f, 6.52f), frameMat, layer, VanguardSurfaceTag.SurfaceKind.Metal);
            MakeBox("BaseFrame", root, new Vector3(0f, 0.08f, 0f), new Vector3(2.72f, 0.16f, 6.52f), frameMat, layer, VanguardSurfaceTag.SurfaceKind.Metal);
        }

        private static void BuildJerseyBarrier(Transform parent, string name, Vector3 pos, float yaw, Material concrete, Material hazard, int layer)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));

            MakeBox("LowerBase", root, new Vector3(0f, 0.28f, 0f), new Vector3(3.4f, 0.56f, 0.72f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeBox("UpperWall", root, new Vector3(0f, 0.82f, 0f), new Vector3(3.4f, 0.58f, 0.38f), concrete, layer, VanguardSurfaceTag.SurfaceKind.Concrete);
            MakeNoColPart("HazardCap", root, PrimitiveType.Cube, new Vector3(0f, 1.12f, 0f), new Vector3(3.2f, 0.06f, 0.32f), hazard);
        }

        private static void BuildSandbagEmplacement(Transform parent, string name, Vector3 pos, float yaw, Material sandbagMat, int layer)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            MakeBox("SandbagWall", root, new Vector3(0f, 0.62f, 0f), new Vector3(4.2f, 1.24f, 0.95f), sandbagMat, layer, VanguardSurfaceTag.SurfaceKind.Dirt);
        }

        private static void BuildSupplyCrate(Transform parent, string name, Vector3 pos, float yaw, Material bodyMat, Material ledMat, int layer)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            MakeBox("CrateBody", root, new Vector3(0f, 0.45f, 0f), new Vector3(1.5f, 0.9f, 1.0f), bodyMat, layer, VanguardSurfaceTag.SurfaceKind.Metal);
            MakeNoColPart("StatusLed", root, PrimitiveType.Cube, new Vector3(0f, 0.80f, 0.52f), new Vector3(0.32f, 0.08f, 0.04f), ledMat);
        }

        private static void BuildFloodlightTower(Transform parent, string name, Vector3 pos, Vector3 lookAt, Material steel, Material lens, Color lightColor, int layer)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = pos;

            MakeBox("Pole", root, new Vector3(0f, 4.2f, 0f), new Vector3(0.28f, 8.4f, 0.28f), steel, layer, VanguardSurfaceTag.SurfaceKind.Metal);
            MakeBox("Head", root, new Vector3(0f, 8.4f, 0f), new Vector3(1.6f, 0.55f, 0.55f), steel, layer, VanguardSurfaceTag.SurfaceKind.Metal);
            MakeNoColPart("Lens", root, PrimitiveType.Cube, new Vector3(0f, 8.2f, 0f), new Vector3(1.4f, 0.22f, 0.48f), lens);

            var spotGo = new GameObject("FloodSpotLight");
            spotGo.transform.SetParent(root, false);
            spotGo.transform.localPosition = new Vector3(0f, 8.1f, 0f);
            spotGo.transform.LookAt(lookAt);
            var spot = spotGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = lightColor;
            spot.intensity = 165f;
            spot.range = 42f;
            spot.spotAngle = 78f;
            spot.innerSpotAngle = 42f;
            spot.shadows = LightShadows.Soft;
        }

        private static void AddPointLight(Transform parent, string name, Vector3 localPos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        private static void CreateReflectionProbe(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.boxProjection = true;
            probe.size = size;
            probe.resolution = 256;
            probe.intensity = 1.15f;
        }

        private static void PopulateSpawnsAndWaypoints(Transform parent, BlacksiteSpawnProvider provider, LayerMask mask)
        {
            var alpha = new List<Transform>();
            var bravo = new List<Transform>();
            var ffa = new List<Transform>();
            var wps = new List<Transform>();
            var covers = new List<Transform>();

            Vector3[] alphaCoords =
            {
                new Vector3(-10f, 0.35f, 30f), new Vector3(-4f, 0.35f, 30f),
                new Vector3(4f, 0.35f, 30f),   new Vector3(10f, 0.35f, 30f),
                new Vector3(-28f, 0.35f, 26f), new Vector3(28f, 0.35f, 26f),
                new Vector3(-16f, 0.35f, 22f), new Vector3(16f, 0.35f, 22f)
            };
            for (int i = 0; i < alphaCoords.Length; i++)
                alpha.Add(MakeSpawnNode($"Spawn_Alpha_{i:00}", parent, alphaCoords[i], 180f));

            Vector3[] bravoCoords =
            {
                new Vector3(-10f, 0.35f, -30f), new Vector3(-4f, 0.35f, -30f),
                new Vector3(4f, 0.35f, -30f),   new Vector3(10f, 0.35f, -30f),
                new Vector3(-28f, 0.35f, -26f), new Vector3(28f, 0.35f, -26f),
                new Vector3(-16f, 0.35f, -22f), new Vector3(16f, 0.35f, -22f)
            };
            for (int i = 0; i < bravoCoords.Length; i++)
                bravo.Add(MakeSpawnNode($"Spawn_Bravo_{i:00}", parent, bravoCoords[i], 0f));

            ffa.AddRange(alpha);
            ffa.AddRange(bravo);
            ffa.Add(MakeSpawnNode("Spawn_FFA_W", parent, new Vector3(-36f, 0.35f, 8f), 90f));
            ffa.Add(MakeSpawnNode("Spawn_FFA_E", parent, new Vector3(36f, 0.35f, -8f), -90f));

            Vector3[] wpCoords =
            {
                new Vector3(0f, 0.3f, 15f), new Vector3(0f, 0.3f, -15f),
                new Vector3(-11f, 0.3f, 0f), new Vector3(11f, 0.3f, 0f),
                new Vector3(-22f, 4.3f, 0f), new Vector3(22f, 4.3f, 0f),
                new Vector3(-26f, 0.3f, 18f), new Vector3(26f, 0.3f, 18f),
                new Vector3(-26f, 0.3f, -18f), new Vector3(26f, 0.3f, -18f),
                new Vector3(-6f, 0.3f, 22f), new Vector3(6f, 0.3f, -22f),
                new Vector3(-34f, 0.3f, 10f), new Vector3(34f, 0.3f, -10f)
            };
            for (int i = 0; i < wpCoords.Length; i++)
                wps.Add(MakeSpawnNode($"Waypoint_{i:00}", parent, wpCoords[i], 0f));

            Vector3[] coverCoords =
            {
                new Vector3(-7f, 0.3f, 8.2f), new Vector3(7f, 0.3f, 8.2f),
                new Vector3(-7f, 0.3f, -8.2f), new Vector3(7f, 0.3f, -8.2f),
                new Vector3(-17.5f, 0.3f, 0f), new Vector3(17.5f, 0.3f, 0f),
                new Vector3(-5f, 0.3f, 20f), new Vector3(5f, 0.3f, -20f)
            };
            for (int i = 0; i < coverCoords.Length; i++)
                covers.Add(MakeSpawnNode($"Cover_{i:00}", parent, coverCoords[i], 0f));

            provider.ConfigureNodes(mask, alpha, bravo, ffa, wps, covers);
            EditorUtility.SetDirty(provider);
        }

        private static Transform MakeSpawnNode(string name, Transform parent, Vector3 pos, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            return go.transform;
        }

        private static GameObject MakeBox(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, int layer, VanguardSurfaceTag.SurfaceKind surface)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.isStatic = true;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<VanguardSurfaceTag>().Surface = surface;
            return go;
        }

        private static GameObject MakeRamp(string name, Transform parent, Vector3 localPos, Vector3 scale, Quaternion rot, Material mat, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = layer;
            go.isStatic = true;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<VanguardSurfaceTag>().Surface = VanguardSurfaceTag.SurfaceKind.Metal;
            return go;
        }

        private static GameObject MakeNoColPart(string name, Transform parent, PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            if (go.TryGetComponent<Collider>(out var col)) Object.DestroyImmediate(col);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static Transform MakeAnchor(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }
    }
}
