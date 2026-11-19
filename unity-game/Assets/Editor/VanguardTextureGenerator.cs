using System.IO;
using UnityEditor;
using UnityEngine;
using VanguardZero.Core;

namespace VanguardZero.EditorTools
{
    /// <summary>
    /// Generates tileable procedural 512x512 PBR textures (Albedo, Normal, MetallicSmoothness)
    /// and URP Lit materials for BLACKSITE-09 so every architectural surface has crisp
    /// rain-slicked tactical micro-detail.
    /// </summary>
    public static class VanguardTextureGenerator
    {
        private const string TexDir = "Assets/Textures/Procedural";
        private const string MatDir = "Assets/Materials";

        public static void EnsureAllMaterials()
        {
            EnsureFolder("Assets/Textures");
            EnsureFolder(TexDir);
            EnsureFolder(MatDir);

            CreateWetTarmacMaterial();
            CreateConcreteMaterial("Mat_ConcreteRaw", VanguardPalette.ConcreteRaw, 0.24f, false);
            CreateConcreteMaterial("Mat_ConcreteTrim", VanguardPalette.ConcreteTrim, 0.34f, false);
            CreateConcreteMaterial("Mat_ConcreteLight", VanguardPalette.ConcreteLight, 0.22f, true);
            CreateMetalMaterial("Mat_SteelGunmetal", VanguardPalette.SteelGunmetal, 0.82f, 0.56f, false);
            CreateMetalMaterial("Mat_SteelCatwalk", VanguardPalette.SteelBrushed, 0.86f, 0.62f, true);
            CreateCorrugatedContainerMaterial("Mat_ContainerOlive", VanguardPalette.OliveTactical);
            CreateCorrugatedContainerMaterial("Mat_ContainerSlate", VanguardPalette.SlateCool * 0.52f);
            CreateCorrugatedContainerMaterial("Mat_ContainerRust", VanguardPalette.RustIndustrial);
            CreateHazardStripeMaterial();
            CreateSimpleLitMaterial("Mat_WeaponDarkSteel", VanguardPalette.SteelGunmetal * 0.85f, 0.88f, 0.64f);
            CreateSimpleLitMaterial("Mat_WeaponPolymer", VanguardPalette.ConcreteTrim * 0.85f, 0.12f, 0.38f);
            CreateSimpleLitMaterial("Mat_OperatorArmor", VanguardPalette.GetTeamArmorTint(1), 0.28f, 0.42f);
            CreateSimpleLitMaterial("Mat_OperatorCloth", VanguardPalette.ConcreteTrim, 0.05f, 0.22f);
            CreateSimpleLitMaterial("Mat_Sandbag", VanguardPalette.FromHex("#6B6252"), 0.02f, 0.18f);

            CreateEmissiveMaterial("Mat_EmissiveAmber", VanguardPalette.AccentAmber, 3.2f);
            CreateEmissiveMaterial("Mat_EmissiveCyan", VanguardPalette.AccentCyan, 3.0f);
            CreateEmissiveMaterial("Mat_EmissiveCrimson", VanguardPalette.AccentCrimson, 3.4f);
            CreateEmissiveMaterial("Mat_EmissiveOpticGreen", VanguardPalette.AccentEmerald, 2.8f);
            CreateEmissiveMaterial("Mat_FloodlightLens", VanguardPalette.FloodlightWarm, 4.5f);
            CreateDuskSkyboxMaterial();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static Material LoadMat(string name)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");
            if (mat == null)
            {
                EnsureAllMaterials();
                mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");
            }
            return mat;
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

        private static void CreateWetTarmacMaterial()
        {
            const int size = 512;
            var albedo = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var height = new float[size * size];
            var metallic = new Texture2D(size, size, TextureFormat.RGBA32, true);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float grain = TileNoise(u * 48f, v * 48f, 48) * 0.55f + TileNoise(u * 16f, v * 16f, 16) * 0.45f;
                    float puddleMask = Mathf.SmoothStep(0.42f, 0.68f, TileNoise(u * 4f, v * 4f, 4) * 0.7f + TileNoise(u * 8f, v * 8f, 8) * 0.3f);

                    Color baseC = VanguardPalette.TarmacWet * Mathf.Lerp(0.82f, 1.18f, grain);
                    baseC = Color.Lerp(baseC, baseC * 0.68f, puddleMask); // Wet puddles darken albedo
                    albedo.SetPixel(x, y, new Color(baseC.r, baseC.g, baseC.b, 1f));

                    height[y * size + x] = Mathf.Lerp(grain, 0.5f, puddleMask * 0.85f);
                    float smoothness = Mathf.Lerp(0.42f, 0.90f, puddleMask);
                    metallic.SetPixel(x, y, new Color(0.08f, 0.08f, 0.08f, smoothness));
                }
            }

            var albedoAsset = SavePng(albedo, $"{TexDir}/T_WetTarmac_Albedo.png", false);
            var normalAsset = SavePng(BuildNormalMap(height, size, 2.2f), $"{TexDir}/T_WetTarmac_Normal.png", true);
            var metAsset = SavePng(metallic, $"{TexDir}/T_WetTarmac_MetSmooth.png", false, true);

            var mat = CreateOrLoadLitMat($"{MatDir}/Mat_WetTarmac.mat");
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", albedoAsset);
            mat.SetTexture("_BumpMap", normalAsset);
            mat.SetFloat("_BumpScale", 0.9f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", metAsset);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 0.92f);
            mat.SetTextureScale("_BaseMap", new Vector2(18f, 18f));
            mat.SetTextureScale("_BumpMap", new Vector2(18f, 18f));
            mat.SetTextureScale("_MetallicGlossMap", new Vector2(18f, 18f));
            EditorUtility.SetDirty(mat);
        }

        private static void CreateConcreteMaterial(string matName, Color tint, float smoothness, bool withPanelSeams)
        {
            const int size = 512;
            var albedo = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var height = new float[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float n = TileNoise(u * 24f, v * 24f, 24) * 0.5f + TileNoise(u * 8f, v * 8f, 8) * 0.35f + TileNoise(u * 64f, v * 64f, 64) * 0.15f;
                    float streak = TileNoise(u * 32f, v * 3f, 32) * 0.14f; // Vertical rain weathering streaks

                    float seam = 1f;
                    if (withPanelSeams || (x % 256 < 3) || (y % 128 < 3))
                    {
                        if ((x % 256 < 3) || (y % 128 < 3)) seam = 0.72f;
                    }

                    // Formwork tie holes
                    int gx = (x % 128) - 64;
                    int gy = (y % 128) - 64;
                    if (gx * gx + gy * gy < 20) seam *= 0.65f;

                    float shade = Mathf.Clamp(0.84f + (n - 0.5f) * 0.28f - streak, 0.55f, 1.15f) * seam;
                    Color c = tint * shade;
                    albedo.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
                    height[y * size + x] = n * seam;
                }
            }

            var alb = SavePng(albedo, $"{TexDir}/T_{matName}_Albedo.png", false);
            var nrm = SavePng(BuildNormalMap(height, size, 2.8f), $"{TexDir}/T_{matName}_Normal.png", true);

            var mat = CreateOrLoadLitMat($"{MatDir}/{matName}.mat");
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", alb);
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 1.1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_Metallic", 0.04f);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetTextureScale("_BaseMap", new Vector2(3f, 2f));
            mat.SetTextureScale("_BumpMap", new Vector2(3f, 2f));
            EditorUtility.SetDirty(mat);
        }

        private static void CreateMetalMaterial(string matName, Color tint, float metallicVal, float smoothness, bool diamondPlate)
        {
            const int size = 256;
            var albedo = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var height = new float[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float brush = TileNoise(u * 6f, v * 48f, 48) * 0.22f + TileNoise(u * 12f, v * 12f, 12) * 0.2f;
                    float h = 0.5f + brush * 0.3f;

                    if (diamondPlate)
                    {
                        int cellX = x % 32;
                        int cellY = y % 32;
                        float dx = Mathf.Abs(cellX - 16) / 16f;
                        float dy = Mathf.Abs(cellY - 16) / 16f;
                        if (dx + dy < 0.55f) h += 0.35f;
                    }

                    Color c = tint * Mathf.Lerp(0.86f, 1.16f, h);
                    albedo.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
                    height[y * size + x] = h;
                }
            }

            var alb = SavePng(albedo, $"{TexDir}/T_{matName}_Albedo.png", false);
            var nrm = SavePng(BuildNormalMap(height, size, diamondPlate ? 3.2f : 1.5f), $"{TexDir}/T_{matName}_Normal.png", true);

            var mat = CreateOrLoadLitMat($"{MatDir}/{matName}.mat");
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", alb);
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 1.0f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_Metallic", metallicVal);
            mat.SetFloat("_Smoothness", smoothness);
            if (diamondPlate)
            {
                mat.SetTextureScale("_BaseMap", new Vector2(4f, 4f));
                mat.SetTextureScale("_BumpMap", new Vector2(4f, 4f));
            }
            EditorUtility.SetDirty(mat);
        }

        private static void CreateCorrugatedContainerMaterial(string matName, Color tint)
        {
            const int size = 256;
            var albedo = new Texture2D(size, size, TextureFormat.RGBA32, true);
            var height = new float[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float rib = 0.5f + 0.5f * Mathf.Sin(u * 12f * Mathf.PI * 2f);
                    float wear = TileNoise(u * 10f, v * 10f, 10);
                    float grime = Mathf.Clamp01(1f - v * 2.2f) * 0.25f;

                    Color c = tint * Mathf.Lerp(0.76f, 1.12f, rib * 0.65f + wear * 0.35f) * (1f - grime);
                    albedo.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
                    height[y * size + x] = rib * 0.8f + wear * 0.2f;
                }
            }

            var alb = SavePng(albedo, $"{TexDir}/T_{matName}_Albedo.png", false);
            var nrm = SavePng(BuildNormalMap(height, size, 3.6f), $"{TexDir}/T_{matName}_Normal.png", true);

            var mat = CreateOrLoadLitMat($"{MatDir}/{matName}.mat");
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", alb);
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 1.25f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_Metallic", 0.55f);
            mat.SetFloat("_Smoothness", 0.44f);
            EditorUtility.SetDirty(mat);
        }

        private static void CreateHazardStripeMaterial()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool stripe = ((x + y) / 32) % 2 == 0;
                    float grit = 0.85f + 0.2f * TileNoise((float)x / size * 20f, (float)y / size * 20f, 20);
                    Color c = (stripe ? VanguardPalette.HazardYellow : VanguardPalette.ConcreteTrim) * grit;
                    tex.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
                }
            }
            var alb = SavePng(tex, $"{TexDir}/T_HazardStripe.png", false);
            var mat = CreateOrLoadLitMat($"{MatDir}/Mat_HazardStripe.mat");
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", alb);
            mat.SetFloat("_Metallic", 0.15f);
            mat.SetFloat("_Smoothness", 0.38f);
            EditorUtility.SetDirty(mat);
        }

        private static void CreateSimpleLitMaterial(string matName, Color color, float metallic, float smoothness)
        {
            var mat = CreateOrLoadLitMat($"{MatDir}/{matName}.mat");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
        }

        private static void CreateEmissiveMaterial(string matName, Color color, float hdrIntensity)
        {
            var mat = CreateOrLoadLitMat($"{MatDir}/{matName}.mat");
            mat.SetColor("_BaseColor", color);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * hdrIntensity);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetFloat("_Metallic", 0.1f);
            mat.SetFloat("_Smoothness", 0.75f);
            EditorUtility.SetDirty(mat);
        }

        private static void CreateDuskSkyboxMaterial()
        {
            string path = $"{MatDir}/Mat_BlacksiteDuskSkybox.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) return;
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetFloat("_SunSize", 0.025f);
            mat.SetFloat("_SunSizeConvergence", 8f);
            mat.SetFloat("_AtmosphereThickness", 0.65f);
            mat.SetColor("_SkyTint", VanguardPalette.SkyHorizon);
            mat.SetColor("_GroundColor", VanguardPalette.SkyGround);
            mat.SetFloat("_Exposure", 0.72f);
            EditorUtility.SetDirty(mat);
        }

        private static Material CreateOrLoadLitMat(string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new System.Exception("URP Lit shader not found!");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }
            return mat;
        }

        private static Texture2D BuildNormalMap(float[] height, int size, float strength)
        {
            var nrm = new Texture2D(size, size, TextureFormat.RGBA32, true);
            for (int y = 0; y < size; y++)
            {
                int ym = (y - 1 + size) % size;
                int yp = (y + 1) % size;
                for (int x = 0; x < size; x++)
                {
                    int xm = (x - 1 + size) % size;
                    int xp = (x + 1) % size;
                    float dx = (height[y * size + xp] - height[y * size + xm]) * strength;
                    float dy = (height[yp * size + x] - height[ym * size + x]) * strength;
                    Vector3 n = new Vector3(-dx, -dy, 1f).normalized;
                    nrm.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
                }
            }
            return nrm;
        }

        private static Texture2D SavePng(Texture2D tex, string assetPath, bool isNormalMap, bool linear = false)
        {
            tex.Apply();
            byte[] bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, bytes);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                if (isNormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                }
                else if (linear)
                {
                    importer.sRGBTexture = false;
                }
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static float TileNoise(float x, float y, int period)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;
            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);

            int x0 = ((xi % period) + period) % period;
            int y0 = ((yi % period) + period) % period;
            int x1 = (x0 + 1) % period;
            int y1 = (y0 + 1) % period;

            float a = Hash(x0, y0);
            float b = Hash(x1, y0);
            float c = Hash(x0, y1);
            float d = Hash(x1, y1);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private static float Hash(int x, int y)
        {
            int n = x * 374761393 + y * 668265263;
            n = (n ^ (n >> 13)) * 1274126177;
            return ((n ^ (n >> 16)) & 0x7fffffff) / (float)0x7fffffff;
        }
    }
}
