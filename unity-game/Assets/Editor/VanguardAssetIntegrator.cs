using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VanguardZero.Core;
using VanguardZero.Gameplay;
using VanguardZero.Weapons;

namespace VanguardZero.EditorTools
{
    /// <summary>
    /// Integrates all production assets from blender-assets/, audio-pipeline/, and motion-pipeline/
    /// into Unity URP materials, configured AudioClips, MotionBricks AnimatorController,
    /// Player/Bot prefabs, and both MainMenu.unity & Blacksite09.unity scenes.
    /// </summary>
    public static class VanguardAssetIntegrator
    {
        private const string BlenderRoot = "Assets/Art/Blender";
        private const string BlenderTexRoot = "Assets/Art/Blender/Textures";
        private const string BlenderMatRoot = "Assets/Art/Blender/Materials";
        private const string AudioRoot = "Assets/Resources/Audio";

        public static string IntegrateEverything()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            int audioCount = ConfigureAudioAssets();
            var materials = BuildAndBindBlenderMaterials();
            int fbxCount = RemapAllBlenderFbxs(materials);

            // Run MotionBricks neural animation importer now that TacticalOperator.fbx is imported
            VanguardZero.Animation.Editor.MotionBricksUnityImporter.ImportAllFromMotionPipeline();
            var animatorCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Animation/MotionBricks/Controllers/VanguardOperator.controller");

            UpgradePlayerAndBotPrefabs(animatorCtrl);
            UpgradeSceneWithBlenderAssets(VanguardSceneBuilder.BlacksiteScenePath, animatorCtrl, true);
            UpgradeSceneWithBlenderAssets(VanguardSceneBuilder.MainMenuScenePath, animatorCtrl, false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return $"Asset Integration Complete: {audioCount} WAVs configured, {materials.Count} URP PBR materials built, {fbxCount} Blender FBXs remapped, MotionBricks Animator={(animatorCtrl != null ? "WIRED" : "MISSING")}, Prefabs & Scenes upgraded.";
        }

        // =========================================================================
        // 1. AUDIO IMPORT CONFIGURATION (62 WAVs from audio-pipeline)
        // =========================================================================

        private static int ConfigureAudioAssets()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot });
            int configured = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) continue;

                string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                bool is2D = name.StartsWith("ui_") || name.StartsWith("music_") || name.StartsWith("ambience_")
                            || name.StartsWith("vo_") || name.StartsWith("hitmarker_") || name.StartsWith("kill_confirm")
                            || name.StartsWith("low_health");
                bool isLongLoop = name.StartsWith("ambience_") || name.StartsWith("music_main_menu");

                importer.forceToMono = !is2D;
                importer.loadInBackground = isLongLoop;

                var settings = importer.defaultSampleSettings;
                settings.loadType = isLongLoop ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = isLongLoop ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
                settings.quality = 0.85f;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                configured++;
            }

            return configured;
        }

        // =========================================================================
        // 2. BLENDER 2K URP PBR MATERIALS (Albedo + Normal + MetSmooth + AO + Emission)
        // =========================================================================

        private static Dictionary<string, Material> BuildAndBindBlenderMaterials()
        {
            EnsureFolder(BlenderMatRoot);
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            var result = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

            string[] pngGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { BlenderTexRoot });

            // Group textures by material stem (everything before _Albedo / _Normal / _MetallicSmoothness / _AO / _Emission)
            var groups = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string guid in pngGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);

                string suffix = null;
                if (file.EndsWith("_Albedo", StringComparison.OrdinalIgnoreCase)) suffix = "albedo";
                else if (file.EndsWith("_Normal", StringComparison.OrdinalIgnoreCase)) suffix = "normal";
                else if (file.EndsWith("_MetallicSmoothness", StringComparison.OrdinalIgnoreCase)) suffix = "metsmooth";
                else if (file.EndsWith("_AO", StringComparison.OrdinalIgnoreCase)) suffix = "ao";
                else if (file.EndsWith("_Emission", StringComparison.OrdinalIgnoreCase)) suffix = "emission";

                if (suffix == null) continue;

                // Configure TextureImporter accurately
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null)
                {
                    bool dirty = false;
                    if (suffix == "normal" && ti.textureType != TextureImporterType.NormalMap)
                    {
                        ti.textureType = TextureImporterType.NormalMap;
                        dirty = true;
                    }
                    else if ((suffix == "metsmooth" || suffix == "ao") && ti.sRGBTexture)
                    {
                        ti.sRGBTexture = false;
                        dirty = true;
                    }
                    if (ti.anisoLevel < 8)
                    {
                        ti.anisoLevel = 8;
                        dirty = true;
                    }
                    if (dirty) ti.SaveAndReimport();
                }

                string stem = file.Substring(0, file.LastIndexOf('_'));
                if (!groups.TryGetValue(stem, out var map))
                {
                    map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    groups[stem] = map;
                }
                map[suffix] = path;
            }

            foreach (var kvp in groups)
            {
                string matName = kvp.Key;
                var map = kvp.Value;
                string matPath = $"{BlenderMatRoot}/{matName}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(urpLit);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else
                {
                    mat.shader = urpLit;
                }

                mat.SetColor("_BaseColor", Color.white);
                if (map.TryGetValue("albedo", out var albPath))
                    mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albPath));

                if (map.TryGetValue("normal", out var nrmPath))
                {
                    mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nrmPath));
                    mat.SetFloat("_BumpScale", 1.0f);
                    mat.EnableKeyword("_NORMALMAP");
                }

                if (map.TryGetValue("metsmooth", out var msPath))
                {
                    mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(msPath));
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetFloat("_Smoothness", 1.0f); // Multiplies alpha channel of MetallicGlossMap
                }

                if (map.TryGetValue("ao", out var aoPath))
                {
                    mat.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath));
                    mat.SetFloat("_OcclusionStrength", 0.85f);
                }

                if (map.TryGetValue("emission", out var emPath))
                {
                    mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emPath));
                    mat.EnableKeyword("_EMISSION");
                    float hdrBoost = matName.Contains("Floodlight") || matName.Contains("Lens") ? 4.2f : 2.6f;
                    mat.SetColor("_EmissionColor", Color.white * hdrBoost);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }

                // Optic glass semi-transparent glow
                if (matName.Contains("OpticReticle"))
                {
                    mat.SetFloat("_Surface", 1f); // Transparent
                    mat.SetFloat("_Blend", 0f);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.renderQueue = 3000;
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetColor("_BaseColor", new Color(0.15f, 0.25f, 0.28f, 0.22f));
                }

                EditorUtility.SetDirty(mat);
                result[matName] = mat;
            }

            // Alias Operator_Visor -> Operator_VisorAmber (dynamically tinted per team via MaterialPropertyBlock!)
            if (result.TryGetValue("Operator_VisorAmber", out var visorAmber))
                result["Operator_Visor"] = visorAmber;

            // Clean dark trim fallback for minor hardware slots that don't have separate texture maps
            result["Container_RubberSeal"] = VanguardTextureGenerator.LoadMat("Mat_WeaponPolymer");
            result["Radar_ConcreteBase"] = VanguardTextureGenerator.LoadMat("Mat_ConcreteTrim");
            result["Radar_BeaconRed"] = VanguardTextureGenerator.LoadMat("Mat_EmissiveCrimson");

            AssetDatabase.SaveAssets();
            return result;
        }

        private static int RemapAllBlenderFbxs(Dictionary<string, Material> materials)
        {
            string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { BlenderRoot });
            int count = 0;

            foreach (string guid in fbxGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;

                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                mi.materialLocation = ModelImporterMaterialLocation.InPrefab;

                if (path.Contains("TacticalOperator.fbx") || path.Contains("FP_TacticalArms.fbx"))
                {
                    mi.animationType = ModelImporterAnimationType.Generic;
                    mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                }

                // Discover embedded material names from sub-assets
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var obj in subAssets)
                {
                    if (obj is Material embeddedMat)
                    {
                        Material target = ResolveMaterial(embeddedMat.name, materials);
                        if (target != null)
                        {
                            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embeddedMat.name), target);
                        }
                    }
                }

                mi.SaveAndReimport();
                count++;
            }

            return count;
        }

        private static Material ResolveMaterial(string name, Dictionary<string, Material> materials)
        {
            if (materials.TryGetValue(name, out var exact)) return exact;
            // Strip Blender duplicate suffixes like ".001"
            int dot = name.IndexOf('.');
            if (dot > 0 && materials.TryGetValue(name.Substring(0, dot), out var trimmed))
                return trimmed;

            foreach (var kvp in materials)
            {
                if (name.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kvp.Value;
            }
            return VanguardTextureGenerator.LoadMat("Mat_SteelGunmetal");
        }

        // =========================================================================
        // 3. UPGRADE PLAYER & BOT PREFABS WITH BLENDER WEAPONS + OPERATOR + MOTIONBRICKS
        // =========================================================================

        private static void UpgradePlayerAndBotPrefabs(RuntimeAnimatorController operatorController)
        {
            var vx47Fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Weapons/VX47_AssaultRifle.fbx");
            var m890Fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Weapons/M890_TacticalShotgun.fbx");
            var m19Fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Weapons/M19_TacticalPistol.fbx");
            var operatorFbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Characters/TacticalOperator.fbx");

            // Upgrade Player_Networked.prefab
            using (var scope = new PrefabUtility.EditPrefabContentsScope(VanguardSceneBuilder.PlayerPrefabPath))
            {
                var root = scope.prefabContentsRoot;
                var rigTf = root.transform.Find("CameraPivot/FP_Camera/WeaponRig");
                if (rigTf != null)
                {
                    SwapWeaponViewmodel(rigTf, 0, vx47Fbx);
                    SwapWeaponViewmodel(rigTf, 1, m890Fbx);
                    SwapWeaponViewmodel(rigTf, 2, m19Fbx);
                    if (rigTf.TryGetComponent<TacticalWeaponRig>(out var rig))
                        rig.AutoDiscoverWeaponParts();
                }

                if (operatorFbx != null)
                {
                    SwapOperatorVisual(root.transform, operatorFbx, vx47Fbx, operatorController);
                }
            }

            // Upgrade Bot_Networked.prefab
            using (var scope = new PrefabUtility.EditPrefabContentsScope(VanguardSceneBuilder.BotPrefabPath))
            {
                var root = scope.prefabContentsRoot;
                if (operatorFbx != null)
                {
                    SwapOperatorVisual(root.transform, operatorFbx, vx47Fbx, operatorController);
                }
            }
        }

        private static void SwapWeaponViewmodel(Transform rigTf, int slotIndex, GameObject fbx)
        {
            if (fbx == null) return;
            var old = rigTf.Find($"WeaponModel_{slotIndex}");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx, rigTf);
            inst.name = $"WeaponModel_{slotIndex}";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            inst.SetActive(slotIndex == 0);

            foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(col);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void SwapOperatorVisual(Transform parentRoot, GameObject operatorFbx, GameObject rifle3PFbx, RuntimeAnimatorController controller)
        {
            var old = parentRoot.Find("OperatorVisual3P");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(operatorFbx, parentRoot);
            inst.name = "OperatorVisual3P";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;

            foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(col);

            var anim = inst.GetComponentInChildren<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            if (controller != null)
            {
                anim.runtimeAnimatorController = controller;
                anim.applyRootMotion = false;
            }

            // Attach MotionBricks runtime neural animation + IK suite
            if (inst.GetComponent<VanguardZero.Animation.TacticalUpperBodyAimIK>() == null)
                inst.AddComponent<VanguardZero.Animation.TacticalUpperBodyAimIK>();
            if (inst.GetComponent<VanguardZero.Animation.MotionBricksFootIK>() == null)
                inst.AddComponent<VanguardZero.Animation.MotionBricksFootIK>();
            if (inst.GetComponent<VanguardZero.Animation.MotionBricksAnimatorDriver>() == null)
                inst.AddComponent<VanguardZero.Animation.MotionBricksAnimatorDriver>();

            // Mount 3P VX-47 on RightHandWeaponMount if present
            if (rifle3PFbx != null)
            {
                Transform mount = FindChildRecursive(inst.transform, "RightHandWeaponMount");
                if (mount != null)
                {
                    var gun = (GameObject)PrefabUtility.InstantiatePrefab(rifle3PFbx, mount);
                    gun.name = "VX47_3P";
                    gun.transform.localPosition = Vector3.zero;
                    gun.transform.localRotation = Quaternion.identity;
                    gun.transform.localScale = Vector3.one;
                    foreach (var col in gun.GetComponentsInChildren<Collider>(true))
                        UnityEngine.Object.DestroyImmediate(col);
                }
            }

            var opRig = inst.AddComponent<TacticalOperatorRig>();
            opRig.AutoBindHierarchy();
        }

        // =========================================================================
        // 4. UPGRADE BLACKSITE-09 & MAIN MENU SCENES WITH BLENDER HERO PROPS
        // =========================================================================

        private static void UpgradeSceneWithBlenderAssets(string scenePath, RuntimeAnimatorController controller, bool rebakeNavMesh)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            int wgLayer = LayerMask.NameToLayer("WorldGeometry");
            if (wgLayer < 0) wgLayer = 0;

            var radarFbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Props/RadarDishTower.fbx");
            var containerFbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Props/ShippingContainer_Military.fbx");
            var barrierFbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Props/ConcreteBarrier_Jersey.fbx");
            var crateFbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Props/TacticalSupplyCrate.fbx");
            var floodFbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Props/FloodlightTower.fbx");
            var serverFbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Props/ServerRack_Terminal.fbx");
            var sandbagFbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Props/SandbagWall.fbx");
            var operatorFbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Characters/TacticalOperator.fbx");
            var vx47Fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{BlenderRoot}/Weapons/VX47_AssaultRifle.fbx");

            var allTransforms = new List<Transform>();
            foreach (var rootObj in scene.GetRootGameObjects())
                allTransforms.AddRange(rootObj.GetComponentsInChildren<Transform>(true));

            foreach (var tf in allTransforms)
            {
                if (tf == null) continue;
                string n = tf.name;

                if (n == "Hero_RadarDishTower" && radarFbx != null)
                {
                    ReplaceChildrenWithBlenderProp(tf, radarFbx, wgLayer, VanguardSurfaceTag.SurfaceKind.Metal, keepLights: true, addBoxCollider: true, new Vector3(0f, 1.5f, 0f), new Vector3(4.2f, 3.0f, 4.2f));
                }
                else if ((n.StartsWith("Container_") || n.StartsWith("ShowcaseContainer_")) && containerFbx != null)
                {
                    ReplaceChildrenWithBlenderProp(tf, containerFbx, wgLayer, VanguardSurfaceTag.SurfaceKind.Metal, false, true, new Vector3(0f, 1.3f, 0f), new Vector3(2.47f, 2.59f, 6.13f));
                }
                else if ((n.StartsWith("JerseyBarrier_") || n.StartsWith("ShowcaseBarrier_")) && barrierFbx != null)
                {
                    ReplaceChildrenWithBlenderProp(tf, barrierFbx, wgLayer, VanguardSurfaceTag.SurfaceKind.Concrete, false, true, new Vector3(0f, 0.46f, 0f), new Vector3(0.64f, 0.93f, 2.4f), Quaternion.Euler(0f, 90f, 0f));
                }
                else if ((n.StartsWith("Crate_") || n.StartsWith("ShowcaseCrate")) && crateFbx != null)
                {
                    ReplaceChildrenWithBlenderProp(tf, crateFbx, wgLayer, VanguardSurfaceTag.SurfaceKind.Metal, false, true, new Vector3(0f, 0.25f, 0f), new Vector3(1.1f, 0.55f, 0.72f), scale: 1.25f);
                }
                else if ((n.StartsWith("Floodlight_") || n.StartsWith("MenuFlood_")) && floodFbx != null)
                {
                    ReplaceChildrenWithBlenderProp(tf, floodFbx, wgLayer, VanguardSurfaceTag.SurfaceKind.Metal, keepLights: true, addBoxCollider: true, new Vector3(0f, 1.8f, 0f), new Vector3(0.9f, 3.6f, 0.9f), scale: 1.8f);
                }
                else if (n.StartsWith("Sandbags_") && sandbagFbx != null)
                {
                    ReplaceChildrenWithBlenderProp(tf, sandbagFbx, wgLayer, VanguardSurfaceTag.SurfaceKind.Dirt, false, true, new Vector3(0f, 0.55f, 0f), new Vector3(3.4f, 1.1f, 1.0f));
                }
                else if (n.StartsWith("ServerRack_") && serverFbx != null)
                {
                    Vector3 basePos = tf.position;
                    basePos.y = 0.24f;
                    tf.position = basePos;
                    var parent = tf.parent;
                    string savedName = tf.name;
                    var holder = new GameObject(savedName + "_Holder");
                    holder.transform.SetParent(parent, false);
                    holder.transform.position = basePos;
                    UnityEngine.Object.DestroyImmediate(tf.gameObject);
                    ReplaceChildrenWithBlenderProp(holder.transform, serverFbx, wgLayer, VanguardSurfaceTag.SurfaceKind.Metal, false, true, new Vector3(0f, 1.1f, 0f), new Vector3(1.0f, 2.2f, 1.0f));
                }
                else if (n == "ShowcaseOperator" && operatorFbx != null)
                {
                    var parent = tf.parent;
                    Vector3 pos = tf.position;
                    Quaternion rot = tf.rotation;
                    UnityEngine.Object.DestroyImmediate(tf.gameObject);

                    var op = (GameObject)PrefabUtility.InstantiatePrefab(operatorFbx, parent);
                    op.name = "ShowcaseOperator";
                    op.transform.SetPositionAndRotation(pos, rot);
                    var anim = op.GetComponentInChildren<Animator>();
                    if (anim == null) anim = op.AddComponent<Animator>();
                    if (controller != null) anim.runtimeAnimatorController = controller;
                    if (vx47Fbx != null)
                    {
                        var mount = FindChildRecursive(op.transform, "RightHandWeaponMount");
                        if (mount != null) PrefabUtility.InstantiatePrefab(vx47Fbx, mount);
                    }
                    op.AddComponent<TacticalOperatorRig>().AutoBindHierarchy();
                }
            }

            if (rebakeNavMesh)
            {
                var navSurf = UnityEngine.Object.FindFirstObjectByType<NavMeshSurface>();
                if (navSurf != null)
                {
                    navSurf.BuildNavMesh();
                    EditorUtility.SetDirty(navSurf);
                }
            }

            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void ReplaceChildrenWithBlenderProp(
            Transform container,
            GameObject fbx,
            int layer,
            VanguardSurfaceTag.SurfaceKind surface,
            bool keepLights,
            bool addBoxCollider,
            Vector3 colCenter,
            Vector3 colSize,
            Quaternion? meshRot = null,
            float scale = 1f)
        {
            for (int i = container.childCount - 1; i >= 0; i--)
            {
                var child = container.GetChild(i);
                if (keepLights && child.GetComponent<Light>() != null) continue;
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx, container);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = meshRot ?? Quaternion.identity;
            inst.transform.localScale = Vector3.one * scale;

            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
                t.gameObject.isStatic = true;
            }
            foreach (var c in inst.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(c);

            if (addBoxCollider)
            {
                if (!container.gameObject.TryGetComponent<BoxCollider>(out var box))
                    box = container.gameObject.AddComponent<BoxCollider>();
                box.center = colCenter;
                box.size = colSize;
                container.gameObject.layer = layer;
                container.gameObject.isStatic = true;
                if (!container.gameObject.TryGetComponent<VanguardSurfaceTag>(out var st))
                    st = container.gameObject.AddComponent<VanguardSurfaceTag>();
                st.Surface = surface;
            }
        }

        private static Transform FindChildRecursive(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string child = path.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
