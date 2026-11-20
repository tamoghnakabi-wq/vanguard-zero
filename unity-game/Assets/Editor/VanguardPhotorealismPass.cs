using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using VanguardZero.Core;
using VanguardZero.Gameplay;
using VanguardZero.Weapons;

namespace VanguardZero.EditorTools
{
    /// <summary>
    /// VANGUARD ZERO v2.0 Photorealism Pass:
    /// - Crisp alpha-feathered VFX sprites & radial damage vignette
    /// - 4K Poly Haven HDRI Skybox & real IBL reflections (with realtime reflection probes + LightProbeGroup)
    /// - Wet Puddle Asphalt PBR blending & real Poly Haven scanned textures
    /// - 640m Alpine Mountain Ring Unity Terrain + distant red comms towers (zero black void!)
    /// - Soft Fresnel Volumetric Light Cones on floodlights & fixed subtle aviation beacon
    /// - Gloved FP_TacticalArms attached to Player_Networked WeaponRig (zero floating guns!)
    /// - Batch 1 Photogrammetry prop dressing, Batch 2 Modular Architecture, Batch 3 Hero Weapons & Operator.
    /// </summary>
    public static class VanguardPhotorealismPass
    {
        private const string ExternalBlenderRoot = "/Users/tamoghnakabi/.gemini/antigravity/scratch/vanguard-zero/blender-assets";
        private const string HdriUnityDir = "Assets/Art/HDRI";
        private const string ScansUnityDir = "Assets/Art/PBRScans";
        private const string PhotoUnityDir = "Assets/Art/Photogrammetry";
        private const string ArchUnityDir = "Assets/Art/Architecture";
        private const string TerrainDir = "Assets/TerrainData";

        public static string RunPhotorealismPass()
        {
            var log = new System.Text.StringBuilder();

            // 1. Generate alpha-feathered VFX PNGs & radial damage vignette
            VanguardVfxSpriteGenerator.GenerateAllVfxTextures();
            log.AppendLine("VFX sprites generated.");

            // 2. Enable realtime reflection probes & upgrade SSAO + PostFX Volume profiles
            UpgradeQualityAndRenderingSettings();
            log.AppendLine("Quality/SSAO/PostFX upgraded.");

            // 3. Ingest Poly Haven HDRI Skybox (Batch 1) if available
            Material hdriSkybox = ImportHdriSkyboxIfPresent();
            log.AppendLine($"HDRI Skybox: {(hdriSkybox != null ? hdriSkybox.name : "procedural dusk fallback")}");

            // 4. Ingest Poly Haven 2K/4K scanned PBR textures (Batch 1) & build Wet Puddle Asphalt
            int scansCount = ImportPolyHavenScansAndUpgradeMaterials();
            log.AppendLine($"Poly Haven PBR scans integrated: {scansCount}");

            // 5. Ingest Batch 1 Photogrammetry Props (+ Textures) & Batch 2 Modular Architecture & Batch 3 Hero FBXs
            int photoCount = CopyAndImportSubfolder($"{ExternalBlenderRoot}/exports/photogrammetry", PhotoUnityDir);
            int photoTexCount = CopyAndImportSubfolder($"{ExternalBlenderRoot}/textures/photogrammetry", $"{PhotoUnityDir}/Textures");
            var photoMats = BuildPhotogrammetryMaterials();
            int archCount = CopyAndImportSubfolder($"{ExternalBlenderRoot}/exports/architecture", ArchUnityDir);
            int refreshedHero = RefreshUpdatedBlenderHeroAssets();
            log.AppendLine($"FBX synced: photo={photoCount} (tex={photoTexCount}, mats={photoMats.Count}), arch={archCount}, heroRefreshed={refreshedHero}");

            // 6. Attach FP_TacticalArms.fbx cleanly to Player_Networked & set nearClipPlane = 0.03
            AttachFirstPersonArmsToPlayerPrefab();
            log.AppendLine("FP_TacticalArms wired onto Player_Networked.prefab.");

            // 7. Build shared 640m Alpine Mountain Ring Terrain asset
            TerrainData alpineData = BuildOrUpdateAlpineTerrainData();
            log.AppendLine("640m Alpine Mountain Terrain generated.");

            // 8. Overhaul Blacksite09.unity & MainMenu.unity lighting, skybox, terrain, volumetric cones, beacons, probes & set dressing
            OverhaulScene(VanguardSceneBuilder.BlacksiteScenePath, hdriSkybox, alpineData, photoMats, isMainMenu: false);
            OverhaulScene(VanguardSceneBuilder.MainMenuScenePath, hdriSkybox, alpineData, photoMats, isMainMenu: true);
            log.AppendLine("Blacksite09.unity and MainMenu.unity overhauled.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return log.ToString();
        }

        // =========================================================================
        // 1. QUALITY, SSAO & CINEMA POST-PROCESSING PROFILES
        // =========================================================================

        private static void UpgradeQualityAndRenderingSettings()
        {
            QualitySettings.realtimeReflectionProbes = true;
            PlayerSettings.bundleVersion = "2.0.0";

            // Force native 1.0 renderScale + 4x MSAA on all URP Pipeline assets for razor-sharp visuals!
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
            {
                var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (rp != null)
                {
                    rp.renderScale = 1.0f;
                    rp.msaaSampleCount = 4;
                    rp.supportsHDR = true;
                    EditorUtility.SetDirty(rp);
                }
            }

            // Deep crisp SSAO on PC_Renderer
            var rendererAsset = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            if (rendererAsset != null)
            {
                foreach (var feat in rendererAsset.rendererFeatures)
                {
                    if (feat != null && feat.GetType().Name == "ScreenSpaceAmbientOcclusion")
                    {
                        feat.SetActive(true);
                        var fso = new SerializedObject(feat);
                        var settings = fso.FindProperty("m_Settings");
                        if (settings != null)
                        {
                            SetFloatProp(settings, "Intensity", 1.38f);
                            SetFloatProp(settings, "Radius", 0.52f);
                            SetFloatProp(settings, "DirectLightingStrength", 0.45f);
                            var src = settings.FindPropertyRelative("Source");
                            if (src != null) src.intValue = 0; // DepthNormals for crisp crevice AO
                            var samples = settings.FindPropertyRelative("Samples");
                            if (samples != null) samples.intValue = 2; // High sample count
                            fso.ApplyModifiedPropertiesWithoutUndo();
                            EditorUtility.SetDirty(feat);
                        }
                    }
                }
                EditorUtility.SetDirty(rendererAsset);
            }

            // Tune Gameplay & MainMenu Volume Profiles (ACES Filmic, razor-sharp with DoF & MotionBlur disabled!)
            ConfigureVolumeProfile("Assets/Settings/Blacksite09_VolumeProfile.asset", enableDof: false);
            ConfigureVolumeProfile("Assets/Settings/MainMenu_VolumeProfile.asset", enableDof: false);
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/DefaultVolumeProfile.asset") != null)
                ConfigureVolumeProfile("Assets/Settings/DefaultVolumeProfile.asset", enableDof: false);
        }

        private static void ConfigureVolumeProfile(string path, bool enableDof)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            var tm = GetOrAdd<Tonemapping>(profile);
            tm.mode.Override(TonemappingMode.ACES);

            var bloom = GetOrAdd<Bloom>(profile);
            bloom.threshold.Override(1.12f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(true);

            var ca = GetOrAdd<ColorAdjustments>(profile);
            ca.postExposure.Override(enableDof ? 0.18f : 0.16f);
            ca.contrast.Override(20f);
            ca.saturation.Override(-5f);
            ca.colorFilter.Override(new Color(0.97f, 0.99f, 1.0f, 1f));

            var wb = GetOrAdd<WhiteBalance>(profile);
            wb.temperature.Override(-6f);
            wb.tint.Override(1f);

            var st = GetOrAdd<SplitToning>(profile);
            st.shadows.Override(new Color(0.11f, 0.17f, 0.25f, 1f));
            st.highlights.Override(new Color(1.0f, 0.88f, 0.74f, 1f));
            st.balance.Override(-8f);

            var vig = GetOrAdd<Vignette>(profile);
            vig.color.Override(new Color(0.03f, 0.045f, 0.065f, 1f));
            vig.intensity.Override(enableDof ? 0.30f : 0.23f);
            vig.smoothness.Override(0.46f);

            var cab = GetOrAdd<ChromaticAberration>(profile);
            cab.intensity.Override(0.03f);

            var fg = GetOrAdd<FilmGrain>(profile);
            fg.type.Override(FilmGrainLookup.Thin1);
            fg.intensity.Override(0.08f);
            fg.response.Override(0.85f);

            var mb = GetOrAdd<MotionBlur>(profile);
            mb.active = false;
            mb.mode.Override(MotionBlurMode.CameraOnly);
            mb.intensity.Override(0f);
            mb.clamp.Override(0.02f);

            var dof = GetOrAdd<DepthOfField>(profile);
            dof.active = false;
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(32f);
            dof.gaussianEnd.Override(140f);
            dof.gaussianMaxRadius.Override(0.45f);

            EditorUtility.SetDirty(profile);
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var existing)) return existing;
            return profile.Add<T>(true);
        }

        // =========================================================================
        // 2. POLY HAVEN 4K HDRI SKYBOX & SCANNED PBR TEXTURES (BATCH 1)
        // =========================================================================

        private static Material ImportHdriSkyboxIfPresent()
        {
            string extDir = $"{ExternalBlenderRoot}/hdri";
            if (!Directory.Exists(extDir)) return null;

            string[] hdriFiles = Directory.GetFiles(extDir, "*.exr");
            if (hdriFiles.Length == 0) hdriFiles = Directory.GetFiles(extDir, "*.hdr");
            if (hdriFiles.Length == 0) return null;

            EnsureFolder("Assets/Art");
            EnsureFolder(HdriUnityDir);

            // Copy all HDRIs and prefer dramatic stormy dusk sunset ("belfast" or "evening")
            string chosenAsset = null;
            foreach (string src in hdriFiles)
            {
                string fileName = Path.GetFileName(src);
                string dstAsset = $"{HdriUnityDir}/{fileName}";
                string dstFull = Path.Combine(Directory.GetCurrentDirectory(), dstAsset);
                if (!File.Exists(dstFull) || new FileInfo(src).Length != new FileInfo(dstFull).Length)
                    File.Copy(src, dstFull, true);
                AssetDatabase.ImportAsset(dstAsset, ImportAssetOptions.ForceSynchronousImport);

                var ti = AssetImporter.GetAtPath(dstAsset) as TextureImporter;
                if (ti != null)
                {
                    ti.textureShape = TextureImporterShape.Texture2D;
                    ti.maxTextureSize = 4096;
                    ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.mipmapEnabled = true;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.SaveAndReimport();
                }

                if (chosenAsset == null || fileName.IndexOf("evening_road", StringComparison.OrdinalIgnoreCase) >= 0)
                    chosenAsset = dstAsset;
            }

            var hdriTex = AssetDatabase.LoadAssetAtPath<Texture2D>(chosenAsset);
            if (hdriTex == null) return null;

            string skyPath = "Assets/Materials/Mat_BlacksiteHDRISkybox.mat";
            var skyShader = Shader.Find("Skybox/Panoramic");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (mat == null)
            {
                mat = new Material(skyShader);
                AssetDatabase.CreateAsset(mat, skyPath);
            }
            else
            {
                mat.shader = skyShader;
            }

            mat.SetTexture("_MainTex", hdriTex);
            mat.SetColor("_Tint", new Color(0.52f, 0.62f, 0.80f, 1f)); // Rich moody stormy dusk sky (zero white blowout!)
            mat.SetFloat("_Exposure", 0.68f);
            mat.SetFloat("_Rotation", 165f);
            mat.SetFloat("_Mapping", 1f); // Latitude Longitude Layout
            mat.SetFloat("_ImageType", 0f); // 360 Degrees
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static int ImportPolyHavenScansAndUpgradeMaterials()
        {
            string extDir = $"{ExternalBlenderRoot}/pbr_scans";
            if (!Directory.Exists(extDir)) return 0;

            EnsureFolder("Assets/Art");
            EnsureFolder(ScansUnityDir);

            int copied = 0;
            foreach (string file in Directory.GetFiles(extDir, "*.png", SearchOption.AllDirectories))
            {
                string rel = file.Substring(extDir.Length + 1).Replace('\\', '/');
                string dstAsset = $"{ScansUnityDir}/{rel}";
                string dstFull = Path.Combine(Directory.GetCurrentDirectory(), dstAsset);
                Directory.CreateDirectory(Path.GetDirectoryName(dstFull));
                if (!File.Exists(dstFull) || new FileInfo(file).Length != new FileInfo(dstFull).Length)
                {
                    File.Copy(file, dstFull, true);
                    copied++;
                }
            }

            if (copied > 0) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Configure TextureImporters across all scanned PBR maps (only reimport if needed)
            var mapGroups = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ScansUnityDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fname = Path.GetFileNameWithoutExtension(path);
                string suffix = null;
                if (fname.EndsWith("_Albedo", StringComparison.OrdinalIgnoreCase)) suffix = "albedo";
                else if (fname.EndsWith("_Normal", StringComparison.OrdinalIgnoreCase)) suffix = "normal";
                else if (fname.EndsWith("_MetallicSmoothness", StringComparison.OrdinalIgnoreCase)) suffix = "metsmooth";
                else if (fname.EndsWith("_AO", StringComparison.OrdinalIgnoreCase)) suffix = "ao";
                if (suffix == null) continue;

                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null)
                {
                    bool dirty = false;
                    if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; dirty = true; }
                    if (ti.anisoLevel != 8) { ti.anisoLevel = 8; dirty = true; }
                    if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; dirty = true; }
                    if (suffix == "normal" && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                    else if ((suffix == "metsmooth" || suffix == "ao") && ti.sRGBTexture) { ti.sRGBTexture = false; dirty = true; }
                    if (dirty) ti.SaveAndReimport();
                }

                string stem = fname.Substring(0, fname.LastIndexOf('_'));
                if (!mapGroups.TryGetValue(stem, out var dict))
                {
                    dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    mapGroups[stem] = dict;
                }
                dict[suffix] = path;
            }

            // Upgrade core architectural URP materials with exact Poly Haven scanned PBR textures!
            ApplyScanToMaterial(mapGroups, "Asphalt_Weathered", "Assets/Materials/Mat_WetTarmac.mat", new Vector2(18f, 18f), 0.78f, boostWetPuddles: true);
            ApplyScanToMaterial(mapGroups, "Concrete_PouredBrutalist", "Assets/Materials/Mat_ConcreteRaw.mat", new Vector2(4f, 2f), 0.58f, false);
            ApplyScanToMaterial(mapGroups, "Concrete_FloorWorn", "Assets/Materials/Mat_ConcreteLight.mat", new Vector2(3f, 2f), 0.52f, false);
            ApplyScanToMaterial(mapGroups, "Concrete_PouredBrutalist", "Assets/Materials/Mat_ConcreteTrim.mat", new Vector2(4f, 2f), 0.68f, false, new Color(0.55f, 0.58f, 0.64f, 1f));
            ApplyScanToMaterial(mapGroups, "Metal_BrushedGunmetal", "Assets/Materials/Mat_SteelGunmetal.mat", new Vector2(3f, 3f), 0.84f, false);
            ApplyScanToMaterial(mapGroups, "Metal_CorrugatedSteel", "Assets/Materials/Mat_SteelCatwalk.mat", new Vector2(6f, 6f), 0.80f, false, new Color(0.68f, 0.72f, 0.76f, 1f));
            ApplyScanToMaterial(mapGroups, "Metal_CorrugatedSteel", "Assets/Materials/Mat_ContainerOlive.mat", new Vector2(2f, 1f), 0.72f, false, new Color(0.52f, 0.58f, 0.48f, 1f));
            ApplyScanToMaterial(mapGroups, "Metal_RustedStructural", "Assets/Materials/Mat_ContainerRust.mat", new Vector2(2f, 1f), 0.68f, false);
            ApplyScanToMaterial(mapGroups, "Fabric_TacticalRipstop", "Assets/Materials/Mat_SandbagFabric.mat", new Vector2(3f, 3f), 0.28f, false, new Color(0.76f, 0.72f, 0.62f, 1f));

            return mapGroups.Count;
        }

        private static void ApplyScanToMaterial(
            Dictionary<string, Dictionary<string, string>> groups,
            string keyword,
            string matPath,
            Vector2 tiling,
            float smoothnessScale,
            bool boostWetPuddles,
            Color? tintOverride = null)
        {
            Dictionary<string, string> match = null;
            foreach (var kvp in groups)
            {
                if (kvp.Key.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    match = kvp.Value;
                    break;
                }
            }
            if (match == null) return;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) return;

            mat.SetColor("_BaseColor", tintOverride ?? (boostWetPuddles ? new Color(0.72f, 0.76f, 0.82f, 1f) : Color.white));
            if (match.TryGetValue("albedo", out var alb))
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(alb));
            if (match.TryGetValue("normal", out var nrm))
            {
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nrm));
                mat.SetFloat("_BumpScale", boostWetPuddles ? 0.75f : 1.15f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (match.TryGetValue("metsmooth", out var ms))
            {
                mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ms));
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Smoothness", smoothnessScale);
            }
            if (match.TryGetValue("ao", out var ao))
            {
                mat.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ao));
                mat.SetFloat("_OcclusionStrength", 0.9f);
            }

            mat.SetTextureScale("_BaseMap", tiling);
            mat.SetTextureScale("_BumpMap", tiling);
            mat.SetTextureScale("_MetallicGlossMap", tiling);
            EditorUtility.SetDirty(mat);
        }

        private static int CopyAndImportSubfolder(string externalDir, string unityDir)
        {
            if (!Directory.Exists(externalDir)) return 0;
            EnsureFolder("Assets/Art");
            EnsureFolder(unityDir);

            int count = 0;
            foreach (string file in Directory.GetFiles(externalDir, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".fbx" && ext != ".png" && ext != ".jpg" && ext != ".tga") continue;
                string rel = file.Substring(externalDir.Length + 1).Replace('\\', '/');
                string dstAsset = $"{unityDir}/{rel}";
                string dstFull = Path.Combine(Directory.GetCurrentDirectory(), dstAsset);
                Directory.CreateDirectory(Path.GetDirectoryName(dstFull));
                if (!File.Exists(dstFull) || new FileInfo(file).Length != new FileInfo(dstFull).Length)
                {
                    File.Copy(file, dstFull, true);
                    count++;
                }
            }

            if (count > 0) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return count;
        }

        private static Dictionary<string, Material> BuildPhotogrammetryMaterials()
        {
            var result = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            string texDir = $"{PhotoUnityDir}/Textures";
            if (!AssetDatabase.IsValidFolder(texDir)) return result;

            EnsureFolder("Assets/Materials/Photogrammetry");
            var litShader = Shader.Find("Universal Render Pipeline/Lit");

            var groups = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string fn = Path.GetFileNameWithoutExtension(p);
                string suffix = null;
                if (fn.EndsWith("_Albedo", StringComparison.OrdinalIgnoreCase)) suffix = "albedo";
                else if (fn.EndsWith("_Normal", StringComparison.OrdinalIgnoreCase)) suffix = "normal";
                else if (fn.EndsWith("_MetallicSmoothness", StringComparison.OrdinalIgnoreCase)) suffix = "metsmooth";
                else if (fn.EndsWith("_AO", StringComparison.OrdinalIgnoreCase)) suffix = "ao";
                if (suffix == null) continue;

                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti != null)
                {
                    bool dirty = false;
                    if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; dirty = true; }
                    if (ti.anisoLevel != 8) { ti.anisoLevel = 8; dirty = true; }
                    if (suffix == "normal" && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                    else if ((suffix == "metsmooth" || suffix == "ao") && ti.sRGBTexture) { ti.sRGBTexture = false; dirty = true; }
                    if (dirty) ti.SaveAndReimport();
                }

                string stem = fn.Substring(0, fn.LastIndexOf('_'));
                if (!groups.TryGetValue(stem, out var dict))
                {
                    dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    groups[stem] = dict;
                }
                dict[suffix] = p;
            }

            foreach (var kvp in groups)
            {
                string matPath = $"Assets/Materials/Photogrammetry/Mat_{kvp.Key}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(litShader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                else mat.shader = litShader;

                mat.SetColor("_BaseColor", Color.white);
                if (kvp.Value.TryGetValue("albedo", out var alb))
                    mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(alb));
                if (kvp.Value.TryGetValue("normal", out var nrm))
                {
                    mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nrm));
                    mat.SetFloat("_BumpScale", 1.15f);
                    mat.EnableKeyword("_NORMALMAP");
                }
                if (kvp.Value.TryGetValue("metsmooth", out var ms))
                {
                    mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ms));
                    mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                    mat.SetFloat("_Smoothness", 0.85f);
                }
                if (kvp.Value.TryGetValue("ao", out var ao))
                {
                    mat.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ao));
                    mat.SetFloat("_OcclusionStrength", 0.92f);
                }

                // Chainlink fence uses alpha cutout for wire transparency if albedo has alpha
                if (kvp.Key.IndexOf("Chainlink", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.SetFloat("_Cutoff", 0.35f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetFloat("_Cull", 0f); // Double-sided wire
                }

                EditorUtility.SetDirty(mat);
                result[kvp.Key] = mat;
            }

            return result;
        }

        private static int RefreshUpdatedBlenderHeroAssets()
        {
            int count = 0;
            string[] pairs =
            {
                $"{ExternalBlenderRoot}/exports/weapons|Assets/Art/Blender/Weapons",
                $"{ExternalBlenderRoot}/exports/characters|Assets/Art/Blender/Characters",
                $"{ExternalBlenderRoot}/exports/props|Assets/Art/Blender/Props",
                $"{ExternalBlenderRoot}/textures|Assets/Art/Blender/Textures"
            };

            foreach (var pair in pairs)
            {
                var sp = pair.Split('|');
                if (!Directory.Exists(sp[0])) continue;
                foreach (var f in Directory.GetFiles(sp[0], "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".fbx" && ext != ".png") continue;
                    string rel = f.Substring(sp[0].Length + 1).Replace('\\', '/');
                    string dst = Path.Combine(Directory.GetCurrentDirectory(), sp[1], rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    // Copy if newer or size differs
                    if (!File.Exists(dst) || File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(dst) || new FileInfo(f).Length != new FileInfo(dst).Length)
                    {
                        File.Copy(f, dst, true);
                        count++;
                    }
                }
            }

            if (count > 0)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                VanguardAssetIntegrator.IntegrateEverything();
            }
            return count;
        }

        // =========================================================================
        // 3. ATTACH GLOVED FIRST-PERSON ARMS (ZERO FLOATING WEAPONS!)
        // =========================================================================

        private static void AttachFirstPersonArmsToPlayerPrefab()
        {
            var armsFbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Blender/Characters/FP_TacticalArms.fbx");
            if (armsFbx == null) return;

            using (var scope = new PrefabUtility.EditPrefabContentsScope(VanguardSceneBuilder.PlayerPrefabPath))
            {
                var root = scope.prefabContentsRoot;
                var fpCamTf = root.transform.Find("CameraPivot/FP_Camera");
                if (fpCamTf != null && fpCamTf.TryGetComponent<Camera>(out var cam))
                {
                    cam.nearClipPlane = 0.03f;
                    cam.farClipPlane = 950f; // Render full 640m alpine mountain vista!
                    var ucd = cam.GetUniversalAdditionalCameraData();
                    if (ucd != null)
                    {
                        ucd.renderPostProcessing = true;
                        ucd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                        ucd.antialiasingQuality = AntialiasingQuality.High;
                    }
                }

                var rigTf = root.transform.Find("CameraPivot/FP_Camera/WeaponRig");
                if (rigTf != null)
                {
                    var oldArms = rigTf.Find("FP_TacticalArms");
                    if (oldArms != null) UnityEngine.Object.DestroyImmediate(oldArms.gameObject);

                    var arms = (GameObject)PrefabUtility.InstantiatePrefab(armsFbx, rigTf);
                    arms.name = "FP_TacticalArms";
                    // Batch 3 FP_TacticalArms.fbx is authored in Tactical Two-Handed Rifle Grip aligned to weapon origin (0,0,0)!
                    arms.transform.localPosition = Vector3.zero;
                    arms.transform.localRotation = Quaternion.identity;
                    arms.transform.localScale = Vector3.one;

                    foreach (var col in arms.GetComponentsInChildren<Collider>(true))
                        UnityEngine.Object.DestroyImmediate(col);
                    foreach (var r in arms.GetComponentsInChildren<Renderer>(true))
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        private static void PoseFirstPersonArms(Transform armsRoot)
        {
            Transform lUpper = FindChildRecursive(armsRoot, "LeftUpperArm");
            Transform lLower = FindChildRecursive(armsRoot, "LeftLowerArm");
            Transform rUpper = FindChildRecursive(armsRoot, "RightUpperArm");
            Transform rLower = FindChildRecursive(armsRoot, "RightLowerArm");

            // Check if already posed in Blender (Batch 3) vs neutral T-pose (Batch 1)
            if (lUpper != null && lUpper.localRotation == Quaternion.identity && rUpper != null && rUpper.localRotation == Quaternion.identity)
            {
                // Offset UpperChest anchor behind camera so forearms reach cleanly to pistol grip & handguard
                Transform chest = FindChildRecursive(armsRoot, "UpperChest");
                if (chest != null) chest.localPosition = new Vector3(-0.16f, -0.34f, -0.36f);
                lUpper.localRotation = Quaternion.Euler(26f, 44f, -38f);
                if (lLower != null) lLower.localRotation = Quaternion.Euler(14f, 48f, 18f);
                rUpper.localRotation = Quaternion.Euler(22f, -36f, 34f);
                if (rLower != null) rLower.localRotation = Quaternion.Euler(18f, -42f, -15f);
            }
        }

        // =========================================================================
        // 4. 640m ALPINE MOUNTAIN VALLEY RING TERRAIN (ZERO EMPTY BLACK VOID!)
        // =========================================================================

        private static TerrainData BuildOrUpdateAlpineTerrainData()
        {
            EnsureFolder(TerrainDir);
            string tdPath = $"{TerrainDir}/Blacksite09_AlpineTerrain.asset";
            var td = AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath);
            if (td == null)
            {
                td = new TerrainData();
                AssetDatabase.CreateAsset(td, tdPath);
            }

            const int res = 257;
            td.heightmapResolution = res;
            td.size = new Vector3(640f, 68f, 640f);

            var heights = new float[res, res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float worldX = ((float)x / (res - 1) - 0.5f) * 640f;
                    float worldZ = ((float)y / (res - 1) - 0.5f) * 640f;

                    // Wide flat valley apron around Blacksite-09 compound so alpine peaks sit naturally in the distance
                    float dx = Mathf.Max(0f, Mathf.Abs(worldX) - 72f);
                    float dz = Mathf.Max(0f, Mathf.Abs(worldZ) - 62f);
                    float distOutside = Mathf.Sqrt(dx * dx + dz * dz);

                    if (distOutside <= 0.1f)
                    {
                        heights[y, x] = 0.0f;
                        continue;
                    }

                    // Smooth rise into sculpted distant alpine peaks with an open horizon pass to reveal the 4K dusk sky!
                    float ringFactor = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distOutside / 220f));
                    float angle = Mathf.Atan2(worldZ, worldX);
                    // Open sweeping valley notches so golden sunset clouds shine over rugged ridges
                    float passMod = 0.52f + 0.48f * Mathf.Pow(Mathf.Abs(Mathf.Sin(angle * 2.0f + 0.6f)), 1.3f);

                    float ridge1 = 1f - Mathf.Abs(Mathf.PerlinNoise(worldX * 0.0085f + 11.2f, worldZ * 0.0085f + 4.7f) * 2f - 1f);
                    float ridge2 = 1f - Mathf.Abs(Mathf.PerlinNoise(worldX * 0.022f + 33.1f, worldZ * 0.022f + 19.4f) * 2f - 1f);
                    float detail = Mathf.PerlinNoise(worldX * 0.065f + 7.5f, worldZ * 0.065f + 2.3f);

                    float mountain = (Mathf.Pow(ridge1, 1.75f) * 0.65f + Mathf.Pow(ridge2, 1.9f) * 0.25f + detail * 0.10f);
                    heights[y, x] = Mathf.Clamp01(ringFactor * passMod * (0.12f + mountain * 0.85f));
                }
            }
            td.SetHeights(0, 0, heights);

            // Configure Terrain Layers (wet alpine gravel + dark cliff rock, using Poly Haven scans if present!)
            var gravelLayer = CreateOrLoadTerrainLayer("TerrainLayer_WetGravel", "gravel", "Assets/Textures/Procedural/T_WetTarmac_Albedo.png", "Assets/Textures/Procedural/T_WetTarmac_Normal.png", new Vector2(18f, 18f));
            var rockLayer = CreateOrLoadTerrainLayer("TerrainLayer_CliffRock", "rock", "Assets/Textures/Procedural/T_Mat_ConcreteTrim_Albedo.png", "Assets/Textures/Procedural/T_Mat_ConcreteTrim_Normal.png", new Vector2(26f, 26f));
            td.terrainLayers = new[] { gravelLayer, rockLayer };

            // Splatmap: gravel in low valley, dark jagged cliff rock on steep mountain slopes
            td.alphamapResolution = 128;
            var splat = new float[128, 128, 2];
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    float nx = (float)x / 127f;
                    float ny = (float)y / 127f;
                    float steep = td.GetSteepness(nx, ny);
                    float rockWeight = Mathf.SmoothStep(14f, 34f, steep);
                    splat[y, x, 0] = 1f - rockWeight;
                    splat[y, x, 1] = rockWeight;
                }
            }
            td.SetAlphamaps(0, 0, splat);

            EditorUtility.SetDirty(td);
            return td;
        }

        private static TerrainLayer CreateOrLoadTerrainLayer(string name, string scanKeyword, string fallbackAlbedo, string fallbackNormal, Vector2 tileSize)
        {
            string path = $"{TerrainDir}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }

            Texture2D alb = FindScannedTexture(scanKeyword, "_Albedo") ?? AssetDatabase.LoadAssetAtPath<Texture2D>(fallbackAlbedo);
            Texture2D nrm = FindScannedTexture(scanKeyword, "_Normal") ?? AssetDatabase.LoadAssetAtPath<Texture2D>(fallbackNormal);

            layer.diffuseTexture = alb;
            layer.normalMapTexture = nrm;
            layer.tileSize = tileSize;
            layer.metallic = 0.04f;
            layer.smoothness = scanKeyword == "gravel" ? 0.35f : 0.22f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static Texture2D FindScannedTexture(string keyword, string suffix)
        {
            if (!AssetDatabase.IsValidFolder(ScansUnityDir)) return null;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ScansUnityDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string fn = Path.GetFileNameWithoutExtension(p);
                if (fn.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 && fn.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }

        // =========================================================================
        // 5. OVERHAUL SCENES: HDRI SKY, DEEP CONTRAST, VOLUMETRIC CONES, TERRAIN, PROBES
        // =========================================================================

        private static void OverhaulScene(string scenePath, Material hdriSkybox, TerrainData alpineData, Dictionary<string, Material> photoMats, bool isMainMenu)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // 1. Skybox & High-Contrast Balanced Dusk Lighting (rich moody shadows, crisp readable details)
            if (hdriSkybox != null)
            {
                RenderSettings.skybox = hdriSkybox;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientIntensity = 0.62f;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                RenderSettings.reflectionIntensity = 1.05f;
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.14f, 0.19f, 0.28f, 1f);
                RenderSettings.ambientEquatorColor = new Color(0.08f, 0.11f, 0.15f, 1f);
                RenderSettings.ambientGroundColor = new Color(0.04f, 0.05f, 0.07f, 1f);
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.085f, 0.112f, 0.165f, 1f);
            RenderSettings.fogDensity = isMainMenu ? 0.0046f : 0.0042f;

            // Assign tuned Volume Profile
            var vol = UnityEngine.Object.FindFirstObjectByType<Volume>();
            if (vol != null)
            {
                string profPath = isMainMenu
                    ? "Assets/Settings/MainMenu_VolumeProfile.asset"
                    : "Assets/Settings/Blacksite09_VolumeProfile.asset";
                vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
                EditorUtility.SetDirty(vol);
            }

            // Tune Directional Key Light for crisp rim-lit dusk contrast without crushing shadows
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional)
                {
                    l.color = new Color(0.86f, 0.86f, 0.96f, 1f);
                    l.intensity = 1.65f;
                    l.shadowStrength = 0.84f;
                    l.transform.rotation = Quaternion.Euler(18f, 142f, 0f);
                }
                else if (l.name == "TowerBeaconLight")
                {
                    // Fix harsh pink blob on radar dish! Subtle realistic aircraft warning beacon
                    l.color = new Color(0.95f, 0.12f, 0.08f, 1f);
                    l.intensity = 1.8f;
                    l.range = 3.5f;
                }
                else if (l.type == LightType.Spot && l.name.IndexOf("Hero", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    // Aim courtyard floodlights cleanly down toward the central radar apron so volumetric beams look cinematic
                    Vector3 target = new Vector3(l.transform.position.x * 0.25f, 0.5f, l.transform.position.z * 0.25f);
                    l.transform.LookAt(target);
                    l.intensity = Mathf.Max(l.intensity, 62f);
                    l.spotAngle = Mathf.Clamp(l.spotAngle, 46f, 66f);
                }
            }

            // 2. Surround with 640m Alpine Mountain Ring Terrain + Distant Comms Masts
            var existingTerrain = GameObject.Find("Blacksite_AlpineMountainRing");
            if (existingTerrain != null) UnityEngine.Object.DestroyImmediate(existingTerrain);
            var terrainGo = Terrain.CreateTerrainGameObject(alpineData);
            terrainGo.name = "Blacksite_AlpineMountainRing";
            terrainGo.transform.position = new Vector3(-320f, -1.6f, -320f);
            terrainGo.isStatic = true;
            var terrainComp = terrainGo.GetComponent<Terrain>();

            BuildDistantAlpineMasts();

            // 2b. Replace flat primitive backdrops & upgrade architecture with Batch 2 Blender 5.2 Modular Architectural Kit!
            DressSceneWithModularArchitecture(isMainMenu);

            // 2c. Dress compound with real Poly Haven photogrammetry props + Alpine Fir Tree forest on mountain foothills!
            DressSceneWithPhotogrammetryProps(photoMats, terrainComp, isMainMenu);

            // 3. Attach Fresnel Volumetric Light Cones to all Floodlight Spotlights!
            AddVolumetricConesToFloodlights();

            // 3b. Organic mirror-gloss rain puddles across the courtyard tarmac
            BuildWetRainPuddles(isMainMenu);

            // 4. Add LightProbeGroup + high-res HDR Reflection Probes for mirror reflections & operator indirect light
            BuildLightProbeGrid();
            UpgradeReflectionProbes(isMainMenu);

            // 5. Ensure Camera farClipPlane sees the full alpine skyline & uses crisp SMAA High (zero TAA/FXAA blur!)
            foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                cam.farClipPlane = 950f;
                var ucd = cam.GetUniversalAdditionalCameraData();
                if (ucd != null)
                {
                    ucd.renderPostProcessing = true;
                    ucd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    ucd.antialiasingQuality = AntialiasingQuality.High;
                }
            }

            // 6. MainMenu dramatic hero staging (rim-lit ShowcaseOperator by glowing supply crate & floodlit puddles)
            if (isMainMenu)
            {
                StageMainMenuHeroLighting();
            }

            // 7. Re-bake NavMesh in Blacksite09
            if (!isMainMenu)
            {
                var nav = UnityEngine.Object.FindFirstObjectByType<NavMeshSurface>();
                if (nav != null)
                {
                    nav.BuildNavMesh();
                    EditorUtility.SetDirty(nav);
                }
            }

            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void BuildDistantAlpineMasts()
        {
            var old = GameObject.Find("DistantAlpineMasts");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);

            var root = new GameObject("DistantAlpineMasts");
            var steel = VanguardTextureGenerator.LoadMat("Mat_SteelGunmetal");
            var redGlow = VanguardTextureGenerator.LoadMat("Mat_EmissiveCrimson");

            Vector3[] mastCoords =
            {
                new Vector3(-148f, 22f, 138f),
                new Vector3(156f, 26f, 128f),
                new Vector3(132f, 20f, -148f),
                new Vector3(-154f, 24f, -124f)
            };

            for (int i = 0; i < mastCoords.Length; i++)
            {
                var mast = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                mast.name = $"RadioMast_{i}";
                mast.transform.SetParent(root.transform, false);
                mast.transform.position = mastCoords[i];
                mast.transform.localScale = new Vector3(0.28f, 18f, 0.28f);
                if (mast.TryGetComponent<Collider>(out var c)) UnityEngine.Object.DestroyImmediate(c);
                mast.GetComponent<Renderer>().sharedMaterial = steel;

                var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                tip.name = $"MastBeacon_{i}";
                tip.transform.SetParent(root.transform, false);
                tip.transform.position = mastCoords[i] + Vector3.up * 18.4f;
                tip.transform.localScale = Vector3.one * 0.9f;
                if (tip.TryGetComponent<Collider>(out var tc)) UnityEngine.Object.DestroyImmediate(tc);
                tip.GetComponent<Renderer>().sharedMaterial = redGlow;
            }
        }

        private static void AddVolumetricConesToFloodlights()
        {
            var coneShader = Shader.Find("VanguardZero/VolumetricLightCone");
            if (coneShader == null) return;

            var coneMesh = BuildVolumetricConeMesh(0.35f, 6.2f, 18f, 24);
            string meshPath = "Assets/Art/VanguardVolumetricCone.asset";
            EnsureFolder("Assets/Art");
            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) == null)
                AssetDatabase.CreateAsset(coneMesh, meshPath);
            else
                coneMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            var warmConeMat = CreateOrLoadConeMat("Assets/Materials/Mat_VolumetricWarm.mat", coneShader, new Color(1.0f, 0.72f, 0.42f, 0.14f));
            var cyanConeMat = CreateOrLoadConeMat("Assets/Materials/Mat_VolumetricCyan.mat", coneShader, new Color(0.24f, 0.78f, 0.98f, 0.13f));

            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Spot) continue;
                var existing = l.transform.Find("VolumetricBeamCone");
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

                var coneGo = new GameObject("VolumetricBeamCone");
                coneGo.transform.SetParent(l.transform, false);
                coneGo.transform.localPosition = Vector3.zero;
                coneGo.transform.localRotation = Quaternion.identity;

                var mf = coneGo.AddComponent<MeshFilter>();
                mf.sharedMesh = coneMesh;
                var mr = coneGo.AddComponent<MeshRenderer>();
                bool isCyan = l.color.b > l.color.r;
                mr.sharedMaterial = isCyan ? cyanConeMat : warmConeMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        private static Material CreateOrLoadConeMat(string path, Shader shader, Color color)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }
            mat.SetColor("_Color", color);
            mat.SetFloat("_FresnelPower", 2.6f);
            mat.SetFloat("_FadePower", 1.7f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Mesh BuildVolumetricConeMesh(float topRadius, float bottomRadius, float length, int segments)
        {
            var mesh = new Mesh { name = "VolumetricLightConeMesh" };
            var verts = new Vector3[(segments + 1) * 2];
            var norms = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[(segments + 1) * 2];
            var tris = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float u = (float)i / segments;
                float ang = u * Mathf.PI * 2f;
                float cos = Mathf.Cos(ang);
                float sin = Mathf.Sin(ang);

                // Cone extends along +Z (matching Spotlight forward!)
                verts[i * 2] = new Vector3(cos * topRadius, sin * topRadius, 0.1f);
                verts[i * 2 + 1] = new Vector3(cos * bottomRadius, sin * bottomRadius, length);

                Vector3 n = new Vector3(cos, sin, -(bottomRadius - topRadius) / length).normalized;
                norms[i * 2] = n;
                norms[i * 2 + 1] = n;

                uvs[i * 2] = new Vector2(u, 1f);
                uvs[i * 2 + 1] = new Vector2(u, 0f);

                if (i < segments)
                {
                    int b = i * 2;
                    int t = i * 6;
                    tris[t] = b;
                    tris[t + 1] = b + 1;
                    tris[t + 2] = b + 2;
                    tris[t + 3] = b + 2;
                    tris[t + 4] = b + 1;
                    tris[t + 5] = b + 3;
                }
            }

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildLightProbeGrid()
        {
            var existing = GameObject.Find("Blacksite_LightProbeGrid");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var go = new GameObject("Blacksite_LightProbeGrid");
            var lpg = go.AddComponent<LightProbeGroup>();
            var pts = new List<Vector3>();
            for (int x = -32; x <= 32; x += 16)
            {
                for (int z = -28; z <= 28; z += 14)
                {
                    pts.Add(new Vector3(x, 1.4f, z));
                    pts.Add(new Vector3(x, 5.2f, z));
                }
            }
            lpg.probePositions = pts.ToArray();
        }

        private static void BuildWetRainPuddles(bool isMainMenu)
        {
            var existing = GameObject.Find("Blacksite_WetRainPuddles");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var alb = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/VFX/T_RainPuddle_Albedo.png");
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/VFX/T_RainPuddle_Normal.png");
            if (alb == null) return;

            string matPath = "Assets/Materials/Mat_WetRainPuddle.mat";
            var puddleShader = Shader.Find("VanguardZero/WetPuddle");
            if (puddleShader == null) return;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(puddleShader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else mat.shader = puddleShader;

            mat.SetTexture("_BaseMap", alb);
            if (nrm != null) mat.SetTexture("_BumpMap", nrm);
            mat.SetColor("_Tint", new Color(0.03f, 0.045f, 0.065f, 1f));
            mat.SetFloat("_ReflectionIntensity", 1.55f);
            mat.SetFloat("_RippleStrength", 0.09f);
            mat.SetFloat("_RippleSpeed", 0.9f);
            mat.SetFloat("_Opacity", 0.90f);
            EditorUtility.SetDirty(mat);

            var root = new GameObject("Blacksite_WetRainPuddles");
            var specs = new (Vector3 pos, Vector2 scale, float rot)[]
            {
                // Hero foreground puddles around MainMenu ShowcaseOperator & central radar courtyard
                (new Vector3(5.6f, 0.014f, 7.2f), new Vector2(5.2f, 3.8f), 22f),
                (new Vector3(2.2f, 0.014f, 6.4f), new Vector2(4.4f, 3.2f), 118f),
                (new Vector3(-3.8f, 0.014f, 5.2f), new Vector2(6.5f, 4.4f), 45f),
                (new Vector3(0.4f, 0.014f, -4.6f), new Vector2(7.0f, 4.8f), 165f),
                (new Vector3(11.5f, 0.014f, 2.8f), new Vector2(5.6f, 3.9f), 74f),
                (new Vector3(-11.8f, 0.014f, -3.2f), new Vector2(6.2f, 4.1f), 12f),
                (new Vector3(14.0f, 0.014f, -12.0f), new Vector2(5.8f, 4.5f), 88f),
                (new Vector3(-14.5f, 0.014f, 13.5f), new Vector2(6.4f, 4.2f), 210f),
                (new Vector3(19.5f, 0.014f, 11.2f), new Vector2(5.2f, 3.8f), 140f),
                (new Vector3(-20.2f, 0.014f, -14.8f), new Vector2(6.8f, 4.6f), 31f),
                (new Vector3(6.8f, 0.014f, -17.5f), new Vector2(5.5f, 4.0f), 192f),
                (new Vector3(-7.4f, 0.014f, 18.2f), new Vector2(6.0f, 4.3f), 64f),
                (new Vector3(24.0f, 0.014f, -1.5f), new Vector2(5.4f, 3.6f), 105f),
                (new Vector3(-24.0f, 0.014f, 2.2f), new Vector2(5.8f, 3.9f), 255f),
            };

            for (int i = 0; i < specs.Length; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = $"WetPuddle_{i:D2}";
                q.transform.SetParent(root.transform, false);
                q.transform.position = specs[i].pos;
                q.transform.rotation = Quaternion.Euler(90f, specs[i].rot, 0f);
                q.transform.localScale = new Vector3(specs[i].scale.x, specs[i].scale.y, 1f);
                if (q.TryGetComponent<Collider>(out var c)) UnityEngine.Object.DestroyImmediate(c);
                var mr = q.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }
        }

        private static void UpgradeReflectionProbes(bool isMainMenu)
        {
            var existing = GameObject.Find("Blacksite_ReflectionProbes");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var root = new GameObject("Blacksite_ReflectionProbes");
            var configs = new (string name, Vector3 pos, Vector3 size)[]
            {
                ("ReflProbe_CourtyardCenter", new Vector3(0f, 3.2f, 0f), new Vector3(58f, 22f, 52f)),
                ("ReflProbe_HeroForeground", new Vector3(4.5f, 2.2f, 5.5f), new Vector3(28f, 14f, 28f)),
                ("ReflProbe_EastSector", new Vector3(22f, 3.2f, 0f), new Vector3(32f, 18f, 44f)),
                ("ReflProbe_WestSector", new Vector3(-22f, 3.2f, 0f), new Vector3(32f, 18f, 44f)),
            };

            foreach (var cfg in configs)
            {
                var go = new GameObject(cfg.name);
                go.transform.SetParent(root.transform, false);
                go.transform.position = cfg.pos;
                var rp = go.AddComponent<ReflectionProbe>();
                rp.mode = ReflectionProbeMode.Realtime;
                rp.refreshMode = ReflectionProbeRefreshMode.OnAwake;
                rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                rp.resolution = isMainMenu && cfg.name == "ReflProbe_HeroForeground" ? 512 : 256;
                rp.hdr = true;
                rp.boxProjection = true;
                rp.size = cfg.size;
                rp.intensity = 1.15f;
                rp.farClipPlane = 600f;
            }
        }

        private static void DressSceneWithModularArchitecture(bool isMainMenu)
        {
            var existing = GameObject.Find("Blacksite_ModularArchitecture");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var root = new GameObject("Blacksite_ModularArchitecture");

            // Load real Poly Haven PBR materials to bind onto Batch 2 Blender Architecture slots
            var matConcrete = VanguardTextureGenerator.LoadMat("Mat_ConcreteRaw");
            var matRusted = VanguardTextureGenerator.LoadMat("Mat_ContainerRust");
            var matBrushed = VanguardTextureGenerator.LoadMat("Mat_SteelGunmetal");
            var matCorrugated = VanguardTextureGenerator.LoadMat("Mat_SteelCatwalk");
            var matAmber = VanguardTextureGenerator.LoadMat("Mat_EmissiveAmber");
            var matCyan = VanguardTextureGenerator.LoadMat("Mat_EmissiveCyan");

            void BindArchMaterials(GameObject inst)
            {
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string n = mats[i] != null ? mats[i].name : "";
                        if (n.IndexOf("BulkheadAmber", StringComparison.OrdinalIgnoreCase) >= 0) mats[i] = matAmber;
                        else if (n.IndexOf("KeycardCyan", StringComparison.OrdinalIgnoreCase) >= 0) mats[i] = matCyan;
                        else if (n.IndexOf("Rusted", StringComparison.OrdinalIgnoreCase) >= 0) mats[i] = matRusted;
                        else if (n.IndexOf("Brushed", StringComparison.OrdinalIgnoreCase) >= 0) mats[i] = matBrushed;
                        else if (n.IndexOf("Corrugated", StringComparison.OrdinalIgnoreCase) >= 0) mats[i] = matCorrugated;
                        else mats[i] = matConcrete;
                    }
                    r.sharedMaterials = mats;
                }
            }

            GameObject PlaceArch(string name, Vector3 pos, float rotY, bool addCollider = false, bool addBulkheadLight = false)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArchUnityDir}/{name}.fbx");
                if (prefab == null) return null;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                inst.name = $"{name}_{root.transform.childCount:D2}";
                inst.transform.position = pos;
                inst.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
                inst.isStatic = true;
                BindArchMaterials(inst);

                if (addCollider && !isMainMenu)
                {
                    var bounds = ComputeHierarchyBounds(inst);
                    var bc = inst.AddComponent<BoxCollider>();
                    bc.center = inst.transform.InverseTransformPoint(bounds.center);
                    bc.size = bounds.size;
                }

                if (addBulkheadLight)
                {
                    var lightGo = new GameObject("BulkheadGlow");
                    lightGo.transform.SetParent(inst.transform, false);
                    lightGo.transform.localPosition = new Vector3(0f, 3.6f, 1.3f);
                    var pt = lightGo.AddComponent<Light>();
                    pt.type = LightType.Point;
                    pt.color = VanguardPalette.FloodlightWarm;
                    pt.intensity = 18f;
                    pt.range = 14f;
                    pt.shadows = LightShadows.None;
                }
                return inst;
            }

            if (isMainMenu)
            {
                // Replace the flat primitive BgBunker_N and BgBunker_W cubes with authentic brutalist architecture!
                foreach (var oldName in new[] { "BgBunker_N", "BgBunker_W" })
                {
                    var oldBox = GameObject.Find(oldName);
                    if (oldBox != null) oldBox.SetActive(false);
                }

                // Back compound bunker facades with glowing amber bulkheads & cyan keycards + Rooftop HVAC
                PlaceArch("Bunker_ModularFacade", new Vector3(-6.5f, 0f, -15.5f), 0f, false, addBulkheadLight: true);
                PlaceArch("Bunker_ModularFacade", new Vector3(6.5f, 0f, -15.5f), 0f, false, addBulkheadLight: true);
                PlaceArch("Rooftop_HVAC", new Vector3(-6.5f, 4.6f, -15.5f), 0f);
                PlaceArch("Rooftop_HVAC", new Vector3(6.5f, 4.6f, -15.5f), 180f);

                // Flanking brutalist blast walls
                PlaceArch("Wall_BlastSection", new Vector3(-14.2f, 0f, -15.5f), 0f);
                PlaceArch("Wall_BlastSection", new Vector3(14.2f, 0f, -15.5f), 0f);
                PlaceArch("Wall_BlastSection", new Vector3(-20.2f, 0f, -15.5f), 0f);
                PlaceArch("Wall_BlastSection", new Vector3(20.2f, 0f, -15.5f), 0f);

                // Elevated steel catwalk + stairs linking the back bunker roof
                PlaceArch("Catwalk_Section", new Vector3(0f, 3.5f, -13.6f), 90f);
                PlaceArch("Catwalk_Stairs", new Vector3(-11.5f, 0f, -12.8f), 90f);

                // Dramatic twin Guard Watchtowers framing the compound silhouette against the sunset mountain peaks!
                PlaceArch("Watchtower", new Vector3(-18.5f, 0f, -9.5f), 30f, false, addBulkheadLight: true);
                PlaceArch("Watchtower", new Vector3(19.0f, 0f, -8.5f), -35f, false, addBulkheadLight: true);

                // Crisp 3D modeled Security Fence perimeter (concrete curb + pickets + barbed wire outriggers!)
                for (int i = -4; i <= 4; i++)
                {
                    PlaceArch("Fence_SecuritySection", new Vector3(-24.0f, 0f, i * 4.0f), 90f);
                    PlaceArch("Fence_SecuritySection", new Vector3(24.0f, 0f, i * 4.0f), -90f);
                }
            }
            else
            {
                // Full BLACKSITE-09 tactical arena architectural upgrade!
                // 4 Corner Guard Watchtowers with warm amber beacon lights
                PlaceArch("Watchtower", new Vector3(-31.5f, 0f, -26.5f), 45f, true, addBulkheadLight: true);
                PlaceArch("Watchtower", new Vector3(31.5f, 0f, -26.5f), -45f, true, addBulkheadLight: true);
                PlaceArch("Watchtower", new Vector3(-31.5f, 0f, 26.5f), 135f, true, addBulkheadLight: true);
                PlaceArch("Watchtower", new Vector3(31.5f, 0f, 26.5f), -135f, true, addBulkheadLight: true);

                // Bunker Modular Facades clad onto North/South/East/West structural entrances
                PlaceArch("Bunker_ModularFacade", new Vector3(0f, 0f, -25.8f), 0f, false, addBulkheadLight: true);
                PlaceArch("Bunker_ModularFacade", new Vector3(0f, 0f, 25.8f), 180f, false, addBulkheadLight: true);
                PlaceArch("Bunker_ModularFacade", new Vector3(-28.5f, 0f, 0f), 90f, false, addBulkheadLight: true);
                PlaceArch("Bunker_ModularFacade", new Vector3(28.5f, 0f, 0f), -90f, false, addBulkheadLight: true);

                // Rooftop HVAC chiller units crowning the bunkers
                PlaceArch("Rooftop_HVAC", new Vector3(-5.5f, 4.6f, -25.5f), 0f);
                PlaceArch("Rooftop_HVAC", new Vector3(5.5f, 4.6f, 25.5f), 180f);
                PlaceArch("Rooftop_HVAC", new Vector3(-28.2f, 4.6f, 6.0f), 90f);
                PlaceArch("Rooftop_HVAC", new Vector3(28.2f, 4.6f, -6.0f), -90f);

                // Perimeter brutalist Blast Walls & Security Fence ring enclosing the compound cleanly
                for (int i = -4; i <= 4; i++)
                {
                    if (Mathf.Abs(i) <= 1) continue;
                    PlaceArch("Wall_BlastSection", new Vector3(i * 6.0f, 0f, -31.5f), 0f, true);
                    PlaceArch("Wall_BlastSection", new Vector3(i * 6.0f, 0f, 31.5f), 180f, true);
                }
                for (int i = -6; i <= 6; i++)
                {
                    PlaceArch("Fence_SecuritySection", new Vector3(-36.5f, 0f, i * 4.0f), 90f, false);
                    PlaceArch("Fence_SecuritySection", new Vector3(36.5f, 0f, i * 4.0f), -90f, false);
                }
            }
        }

        private static void DressSceneWithPhotogrammetryProps(Dictionary<string, Material> photoMats, Terrain terrainComp, bool isMainMenu)
        {
            var existing = GameObject.Find("Blacksite_PhotogrammetryDressing");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var root = new GameObject("Blacksite_PhotogrammetryDressing");

            void Place(string name, Vector3 pos, float rotY, float scale = 1f, bool addCollider = false, bool addLampLight = false)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PhotoUnityDir}/{name}.fbx");
                if (prefab == null) return;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                inst.name = $"{name}_{root.transform.childCount:D2}";
                inst.transform.position = pos;
                inst.transform.rotation = Quaternion.Euler(0f, rotY, 0f);
                inst.transform.localScale = Vector3.one * scale;
                inst.isStatic = true;

                // Keep PG_AlpineFirTree's own extracted multi-materials if available; bind single PBR material for props
                if (name != "PG_AlpineFirTree" && photoMats != null && photoMats.TryGetValue(name, out var mat) && mat != null)
                {
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                        r.sharedMaterials = mats;
                    }
                }
                else if (name == "PG_AlpineFirTree")
                {
                    // Rich dark alpine evergreen needles & bark material mapping
                    var barkTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PhotoUnityDir}/Textures/PG_AlpineFirTree/fir_tree_01_bark_diff_2k.jpg");
                    var barkMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Photogrammetry/Mat_FirBark.mat");
                    if (barkMat == null)
                    {
                        barkMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        AssetDatabase.CreateAsset(barkMat, "Assets/Materials/Photogrammetry/Mat_FirBark.mat");
                    }
                    if (barkTex != null) barkMat.SetTexture("_BaseMap", barkTex);
                    barkMat.SetColor("_BaseColor", new Color(0.55f, 0.48f, 0.42f, 1f));
                    barkMat.SetFloat("_Smoothness", 0.22f);
                    EditorUtility.SetDirty(barkMat);

                    var twigMat = photoMats != null && photoMats.TryGetValue(name, out var tm) ? tm : null;
                    if (twigMat != null)
                    {
                        twigMat.SetFloat("_AlphaClip", 1f);
                        twigMat.SetFloat("_Cutoff", 0.22f);
                        twigMat.EnableKeyword("_ALPHATEST_ON");
                        twigMat.SetFloat("_Cull", 0f);
                        twigMat.SetColor("_BaseColor", new Color(0.24f, 0.36f, 0.25f, 1f));
                        twigMat.SetFloat("_Smoothness", 0.28f);
                        EditorUtility.SetDirty(twigMat);
                    }

                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            string mn = mats[i] != null ? mats[i].name : "";
                            if (mn.IndexOf("bark", StringComparison.OrdinalIgnoreCase) >= 0 || mn.IndexOf("trunk", StringComparison.OrdinalIgnoreCase) >= 0)
                                mats[i] = barkMat;
                            else if (twigMat != null)
                                mats[i] = twigMat;
                        }
                        r.sharedMaterials = mats;
                    }
                }

                if (addLampLight)
                {
                    var lg = new GameObject("StreetLampLight");
                    lg.transform.SetParent(inst.transform, false);
                    lg.transform.localPosition = new Vector3(0f, 3.65f, 0.25f);
                    var pt = lg.AddComponent<Light>();
                    pt.type = LightType.Point;
                    pt.color = VanguardPalette.FloodlightWarm;
                    pt.intensity = 22f;
                    pt.range = 15f;
                    pt.shadows = LightShadows.None;
                }

                if (addCollider && !isMainMenu)
                {
                    var bounds = ComputeHierarchyBounds(inst);
                    var bc = inst.AddComponent<BoxCollider>();
                    bc.center = inst.transform.InverseTransformPoint(bounds.center);
                    bc.size = bounds.size / Mathf.Max(0.01f, scale);
                }
            }

            if (isMainMenu)
            {
                // Hero foreground staging around ShowcaseOperator (4.2, 0, 4.6) & Radar Tower (0, 0, 0)
                Place("PG_SteelBarrel_01", new Vector3(6.1f, 0f, 3.6f), 25f);
                Place("PG_SteelBarrel_02", new Vector3(6.8f, 0f, 4.1f), 110f);
                Place("PG_SteelBarrel_01", new Vector3(-5.8f, 0f, 5.2f), 62f);
                Place("PG_WoodenCrate", new Vector3(5.4f, 0f, 2.7f), 18f);
                Place("PG_WoodenCrate", new Vector3(5.4f, 0.36f, 2.7f), 26f);
                Place("PG_ConcreteRoadBarrier", new Vector3(1.6f, 0f, 8.2f), 14f);
                Place("PG_ConcreteRoadBarrier", new Vector3(-2.4f, 0f, 8.8f), -12f);
                Place("PG_ConcreteRoadBarrier", new Vector3(8.5f, 0f, -1.8f), 85f);
                Place("PG_IndustrialPowerBox", new Vector3(1.8f, 0f, -1.2f), 180f);
                Place("PG_UtilityBox", new Vector3(-2.2f, 0f, -1.6f), 15f);
                Place("PG_ExteriorHVAC", new Vector3(-10.8f, 0f, -14.2f), 0f, 1.2f);
                Place("PG_ExteriorHVAC", new Vector3(10.8f, 0f, -14.2f), 0f, 1.2f);
                Place("PG_IndustrialLamp", new Vector3(10.8f, 0f, 1.8f), -135f, 1f, false, addLampLight: true);
                Place("PG_IndustrialLamp", new Vector3(-6.2f, 0f, 4.5f), 125f, 1f, false, addLampLight: true);
                Place("PG_SecurityCamera", new Vector3(-6.5f, 4.3f, -14.2f), 15f, 1.35f);

                // Natural alpine boulder clusters anchoring compound edges into mountain valley
                Vector3[] boulders =
                {
                    new Vector3(-28f, -0.4f, -22f), new Vector3(-34f, -0.5f, -12f),
                    new Vector3(29f, -0.4f, -21f), new Vector3(35f, -0.5f, -8f),
                    new Vector3(-16f, -0.4f, -26f), new Vector3(15f, -0.4f, -28f),
                    new Vector3(-26f, -0.3f, 12f), new Vector3(27f, -0.3f, 14f)
                };
                for (int i = 0; i < boulders.Length; i++)
                    Place("PG_AlpineBoulder", boulders[i], i * 47f, 2.2f + (i % 3) * 0.7f);
            }
            else
            {
                // Full tactical arena dressing across BLACKSITE-09!
                Place("PG_SteelBarrel_01", new Vector3(5.6f, 0f, 4.4f), 15f, 1f, true);
                Place("PG_SteelBarrel_02", new Vector3(6.3f, 0f, 4.9f), 95f, 1f, true);
                Place("PG_SteelBarrel_01", new Vector3(-6.4f, 0f, -4.8f), 42f, 1f, true);
                Place("PG_SteelBarrel_02", new Vector3(-7.1f, 0f, -4.2f), 160f, 1f, true);
                Place("PG_SteelBarrel_01", new Vector3(16.2f, 0f, -8.5f), 30f, 1f, true);
                Place("PG_SteelBarrel_02", new Vector3(-16.8f, 0f, 9.2f), 210f, 1f, true);
                Place("PG_SteelBarrel_01", new Vector3(11.4f, 0f, 14.2f), 75f, 1f, true);
                Place("PG_SteelBarrel_02", new Vector3(-11.2f, 0f, -14.5f), 125f, 1f, true);

                Place("PG_WoodenCrate", new Vector3(4.5f, 0f, 5.2f), 22f, 1.15f, true);
                Place("PG_WoodenCrate", new Vector3(4.5f, 0.41f, 5.2f), 34f, 1.15f, false);
                Place("PG_WoodenCrate", new Vector3(-12.5f, 0f, 5.8f), 85f, 1.15f, true);
                Place("PG_WoodenCrate", new Vector3(13.2f, 0f, -6.2f), -40f, 1.15f, true);
                Place("PG_WoodenCrate", new Vector3(-3.5f, 0f, 15.8f), 10f, 1.15f, true);
                Place("PG_WoodenCrate", new Vector3(3.8f, 0f, -15.6f), 170f, 1.15f, true);

                Place("PG_ConcreteRoadBarrier", new Vector3(7.5f, 0f, -1.2f), 88f, 1.05f, true);
                Place("PG_ConcreteRoadBarrier", new Vector3(-7.5f, 0f, 1.5f), 92f, 1.05f, true);
                Place("PG_ConcreteRoadBarrier", new Vector3(0.5f, 0f, 11.2f), 4f, 1.05f, true);
                Place("PG_ConcreteRoadBarrier", new Vector3(-1.2f, 0f, -11.4f), -6f, 1.05f, true);
                Place("PG_ConcreteRoadBarrier", new Vector3(20.5f, 0f, 6.2f), 55f, 1.05f, true);
                Place("PG_ConcreteRoadBarrier", new Vector3(-20.5f, 0f, -6.5f), 125f, 1.05f, true);

                Place("PG_ExteriorHVAC", new Vector3(23.5f, 0f, 12.0f), -90f, 1.2f, true);
                Place("PG_ExteriorHVAC", new Vector3(-23.5f, 0f, -12.0f), 90f, 1.2f, true);
                Place("PG_IndustrialPowerBox", new Vector3(2.2f, 0f, 1.8f), 45f, 1f, true);
                Place("PG_UtilityBox", new Vector3(-2.4f, 0f, -2.0f), -45f, 1f, true);
                Place("PG_UtilityBox", new Vector3(17.5f, 0f, 13.5f), 180f, 1f, true);
                Place("PG_IndustrialPowerBox", new Vector3(-17.8f, 0f, -13.2f), 0f, 1f, true);

                Place("PG_IndustrialLamp", new Vector3(10.5f, 0f, 10.5f), -135f, 1f, false, addLampLight: true);
                Place("PG_IndustrialLamp", new Vector3(-10.5f, 0f, 10.5f), 135f, 1f, false, addLampLight: true);
                Place("PG_IndustrialLamp", new Vector3(10.5f, 0f, -10.5f), -45f, 1f, false, addLampLight: true);
                Place("PG_IndustrialLamp", new Vector3(-10.5f, 0f, -10.5f), 45f, 1f, false, addLampLight: true);

                Place("PG_SecurityCamera", new Vector3(21.5f, 4.2f, 0f), -90f, 1.3f);
                Place("PG_SecurityCamera", new Vector3(-21.5f, 4.2f, 0f), 90f, 1.3f);

                for (int i = 0; i < 20; i++)
                {
                    float ang = i * (Mathf.PI * 2f / 20f);
                    float dist = 46f + (i % 3) * 8f;
                    Vector3 bPos = new Vector3(Mathf.Cos(ang) * dist, -0.5f, Mathf.Sin(ang) * dist);
                    Place("PG_AlpineBoulder", bPos, i * 39f, 2.4f + (i % 4) * 0.65f);
                }
            }

            // Scatter lush Alpine Fir Pine forest ring around the valley foothills (both MainMenu & Blacksite09!)
            int treeCount = 34;
            for (int i = 0; i < treeCount; i++)
            {
                float ang = i * (Mathf.PI * 2f / treeCount) + (i % 3) * 0.06f;
                float radius = (isMainMenu ? 36f : 56f) + (i % 4) * 14f;
                float tx = Mathf.Cos(ang) * radius;
                float tz = Mathf.Sin(ang) * radius;
                float ty = terrainComp != null ? terrainComp.SampleHeight(new Vector3(tx, 0f, tz)) + terrainComp.transform.position.y - 0.35f : -0.5f;
                float sc = 0.85f + (i % 5) * 0.16f;
                Place("PG_AlpineFirTree", new Vector3(tx, ty, tz), i * 67f, sc);
            }
        }

        private static Bounds ComputeHierarchyBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        private static void StageMainMenuHeroLighting()
        {
            // Stage ShowcaseOperator heroically in open courtyard right-center (3.6m from camera!) with clear sightline
            var heroOp = GameObject.Find("ShowcaseOperator");
            if (heroOp != null)
            {
                heroOp.transform.position = new Vector3(4.3f, 0f, 4.8f);
                heroOp.transform.rotation = Quaternion.Euler(0f, 36f, 0f); // Facing camera at (6.88, 1.48, 7.51)!
                // Ensure MotionBricksAnimatorDriver is attached so ShowcaseOperator stands tall in neural CombatIdle_Rifle!
                if (!heroOp.TryGetComponent<VanguardZero.Animation.MotionBricksAnimatorDriver>(out _))
                    heroOp.AddComponent<VanguardZero.Animation.MotionBricksAnimatorDriver>();
            }
            var crate = GameObject.Find("ShowcaseCrate");
            if (crate != null)
            {
                crate.transform.position = new Vector3(5.8f, 0f, 1.8f);
                crate.transform.rotation = Quaternion.Euler(0f, -24f, 0f);
            }

            var existing = GameObject.Find("MenuHeroRimLights");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var root = new GameObject("MenuHeroRimLights");

            // Warm key light raking across ShowcaseOperator at (4.3, 0, 4.8)
            var key = new GameObject("HeroKeySpot").AddComponent<Light>();
            key.transform.SetParent(root.transform, false);
            key.transform.position = new Vector3(7.4f, 3.2f, 7.6f);
            key.transform.LookAt(new Vector3(4.3f, 1.25f, 4.8f));
            key.type = LightType.Spot;
            key.color = VanguardPalette.FloodlightWarm;
            key.intensity = 78f;
            key.range = 16f;
            key.spotAngle = 48f;
            key.shadows = LightShadows.Soft;

            // Cool cyan tactical rim light separating ShowcaseOperator's silhouette from dark storm backdrop
            var rim = new GameObject("HeroCyanRim").AddComponent<Light>();
            rim.transform.SetParent(root.transform, false);
            rim.transform.position = new Vector3(2.0f, 2.5f, 2.4f);
            rim.transform.LookAt(new Vector3(4.3f, 1.3f, 4.8f));
            rim.type = LightType.Spot;
            rim.color = VanguardPalette.InteriorCyan;
            rim.intensity = 48f;
            rim.range = 12f;
            rim.spotAngle = 55f;
            rim.shadows = LightShadows.None;
        }

        private static void SetFloatProp(SerializedProperty parent, string name, float value)
        {
            var p = parent.FindPropertyRelative(name);
            if (p != null) p.floatValue = value;
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
            int idx = path.LastIndexOf('/');
            string parent = path.Substring(0, idx);
            string child = path.Substring(idx + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
