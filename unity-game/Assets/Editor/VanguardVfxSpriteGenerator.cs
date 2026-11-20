using System.IO;
using UnityEditor;
using UnityEngine;

namespace VanguardZero.EditorTools
{
    /// <summary>
    /// Generates crisp, smooth alpha-feathered PNG sprites for VFX & HUD (muzzle flash flame star,
    /// soft gunsmoke/mist cloud, spark streak, bullet hole crater decal, delicate rain streak,
    /// and radial damage vignette) so ZERO square edges or flat quads ever appear in-game.
    /// </summary>
    public static class VanguardVfxSpriteGenerator
    {
        private const string VfxResDir = "Assets/Resources/VFX";

        public static void GenerateAllVfxTextures()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder(VfxResDir);

            GenerateMuzzleFlashStar($"{VfxResDir}/T_MuzzleFlash_Star.png", 256);
            GenerateSoftSmoke($"{VfxResDir}/T_SoftSmoke.png", 256);
            GenerateSparkStreak($"{VfxResDir}/T_SparkStreak.png", 128);
            GenerateBulletDecal($"{VfxResDir}/T_BulletDecal.png", 256);
            GenerateRainStreak($"{VfxResDir}/T_RainDropStreak.png", 64, 256);
            GenerateDamageVignette($"{VfxResDir}/T_DamageVignette.png", 512);
            GenerateRainPuddleAlbedo($"{VfxResDir}/T_RainPuddle_Albedo.png", 512);
            GenerateRainPuddleNormal($"{VfxResDir}/T_RainPuddle_Normal.png", 512);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void GenerateMuzzleFlashStar(string assetPath, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);

                    // 6-prong jagged asymmetric ballistic muzzle flash + hot incandescent core
                    float spikes6 = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f + 0.2f)), 4.5f);
                    float spikes4 = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 2f - 0.35f)), 6.0f) * 0.72f;
                    float spikeReach = Mathf.Clamp(0.28f + spikes6 * 0.62f + spikes4 * 0.42f, 0.2f, 0.96f);

                    float core = Mathf.Exp(-r * r * 14f);
                    float flame = Mathf.Clamp01(1f - r / spikeReach);
                    flame = Mathf.Pow(flame, 2.2f);

                    float noise = 0.78f + 0.22f * Mathf.PerlinNoise(x * 0.09f + 3.1f, y * 0.09f + 7.4f);
                    float edgeFade = Mathf.SmoothStep(1.0f, 0.78f, r);
                    float alpha = Mathf.Clamp01((core * 1.1f + flame * 0.85f * noise) * edgeFade);

                    // Color ramp: hot white core -> golden amber -> deep fiery orange
                    Color cCore = new Color(1.0f, 0.98f, 0.88f, 1f);
                    Color cMid = new Color(1.0f, 0.68f, 0.22f, 1f);
                    Color cTip = new Color(0.95f, 0.30f, 0.06f, 1f);
                    Color col = Color.Lerp(cTip, cMid, Mathf.Clamp01(flame * 1.4f));
                    col = Color.Lerp(col, cCore, Mathf.Clamp01(core * 1.5f));
                    col.a = alpha;
                    tex.SetPixel(x, y, col);
                }
            }

            SaveTexture(tex, assetPath, asSprite: false);
        }

        private static void GenerateSoftSmoke(string assetPath, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float radial = Mathf.Pow(Mathf.Clamp01(1f - r), 2.1f);

                    float n1 = Mathf.PerlinNoise(x * 0.032f + 1.7f, y * 0.032f + 4.2f);
                    float n2 = Mathf.PerlinNoise(x * 0.075f + 9.3f, y * 0.075f + 2.8f);
                    float cloud = Mathf.Clamp01(n1 * 0.65f + n2 * 0.35f);
                    float alpha = Mathf.Clamp01(radial * (0.45f + 0.75f * cloud));
                    float shade = Mathf.Lerp(0.68f, 0.96f, cloud);

                    tex.SetPixel(x, y, new Color(shade, shade, shade, alpha));
                }
            }

            SaveTexture(tex, assetPath, asSprite: false);
        }

        private static void GenerateSparkStreak(string assetPath, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / (half * 0.32f);
                    float dy = (y - half) / (half * 0.92f);
                    float d2 = dx * dx + dy * dy;
                    float glow = Mathf.Exp(-d2 * 3.4f);
                    float alpha = Mathf.Clamp01(glow * Mathf.SmoothStep(1.0f, 0.75f, Mathf.Abs((y - half) / half)));
                    Color col = Color.Lerp(new Color(1f, 0.45f, 0.12f, 1f), new Color(1f, 0.96f, 0.82f, 1f), Mathf.Clamp01(glow * 1.4f));
                    col.a = alpha;
                    tex.SetPixel(x, y, col);
                }
            }

            SaveTexture(tex, assetPath, asSprite: false);
        }

        private static void GenerateBulletDecal(string assetPath, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);

                    float chipNoise = Mathf.PerlinNoise(x * 0.08f + 5.2f, y * 0.08f + 1.9f);
                    float crack = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 5f + chipNoise * 3.5f)), 10f);

                    float holeRadius = 0.24f + chipNoise * 0.14f;
                    float scorchRadius = 0.62f + chipNoise * 0.22f;

                    float hole = Mathf.SmoothStep(holeRadius + 0.08f, holeRadius - 0.04f, r);
                    float rim = Mathf.SmoothStep(scorchRadius, holeRadius, r) * (0.42f + 0.35f * crack);
                    float alpha = Mathf.Clamp01((hole * 0.95f + rim * 0.65f) * Mathf.SmoothStep(0.95f, 0.65f, r));

                    // Deep black center crater, dusty concrete chipped edge
                    Color cHole = new Color(0.03f, 0.035f, 0.04f, 1f);
                    Color cRim = new Color(0.28f, 0.30f, 0.33f, 1f);
                    Color col = Color.Lerp(cRim, cHole, Mathf.Clamp01(hole * 1.3f));
                    col.a = alpha;
                    tex.SetPixel(x, y, col);
                }
            }

            SaveTexture(tex, assetPath, asSprite: false);
        }

        private static void GenerateRainStreak(string assetPath, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            float hw = width * 0.5f;

            for (int y = 0; y < height; y++)
            {
                float v = (float)y / (height - 1);
                // Tapered raindrop: soft tail at top, brighter droplet head near bottom, 0 at edges
                float vFade = Mathf.Sin(v * Mathf.PI) * Mathf.Lerp(0.35f, 1.0f, 1f - v);
                for (int x = 0; x < width; x++)
                {
                    float u = Mathf.Abs((x - hw) / hw);
                    float hFade = Mathf.Exp(-u * u * 7.5f);
                    float alpha = Mathf.Clamp01(vFade * hFade);
                    tex.SetPixel(x, y, new Color(0.78f, 0.86f, 0.96f, alpha));
                }
            }

            SaveTexture(tex, assetPath, asSprite: false);
        }

        private static void GenerateDamageVignette(string assetPath, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // Completely transparent in center 55% of screen; organic blood-red edge falloff at borders
                    float mask = Mathf.SmoothStep(0.55f, 1.28f, r);
                    float alpha = Mathf.Pow(mask, 1.8f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            SaveTexture(tex, assetPath, asSprite: true);
        }

        private static void GenerateRainPuddleAlbedo(string assetPath, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float warpX = (Mathf.PerlinNoise(x * 0.012f + 2.3f, y * 0.012f + 8.1f) - 0.5f) * 0.42f;
                    float warpY = (Mathf.PerlinNoise(x * 0.012f + 17.4f, y * 0.012f + 5.6f) - 0.5f) * 0.42f;
                    float fine = (Mathf.PerlinNoise(x * 0.036f + 1.1f, y * 0.036f + 9.9f) - 0.5f) * 0.16f;
                    float r = Mathf.Sqrt((dx + warpX) * (dx + warpX) + (dy + warpY) * (dy + warpY)) + fine;

                    // Mirror-dark water core + organic soft wet asphalt rim
                    float alpha = Mathf.SmoothStep(0.92f, 0.52f, r) * 0.72f;
                    float dampRim = Mathf.SmoothStep(0.68f, 0.45f, r);
                    float colVal = Mathf.Lerp(0.055f, 0.025f, dampRim);
                    tex.SetPixel(x, y, new Color(colVal, colVal * 1.08f, colVal * 1.18f, alpha));
                }
            }

            SaveTexture(tex, assetPath, asSprite: false);
        }

        private static void GenerateRainPuddleNormal(string assetPath, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            // Subtle organic water surface ripples + raindrop micro-rings
            Vector2[] drops =
            {
                new Vector2(0.32f, 0.38f),
                new Vector2(0.66f, 0.55f),
                new Vector2(0.48f, 0.72f),
                new Vector2(0.55f, 0.28f),
                new Vector2(0.28f, 0.64f)
            };

            float[,] h = new float[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float val = Mathf.PerlinNoise(u * 14f + 3.1f, v * 14f + 7.2f) * 0.15f;
                    for (int i = 0; i < drops.Length; i++)
                    {
                        float d = Vector2.Distance(new Vector2(u, v), drops[i]);
                        if (d < 0.14f)
                        {
                            float env = Mathf.Exp(-d * 22f);
                            val += Mathf.Sin(d * 190f) * env * 0.22f;
                        }
                    }
                    h[y, x] = val;
                }
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int xm = (x - 1 + size) % size;
                    int xp = (x + 1) % size;
                    int ym = (y - 1 + size) % size;
                    int yp = (y + 1) % size;
                    float gx = (h[y, xp] - h[y, xm]) * 2.2f;
                    float gy = (h[yp, x] - h[ym, x]) * 2.2f;
                    Vector3 n = new Vector3(-gx, -gy, 1f).normalized;
                    tex.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
                }
            }

            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
        }

        private static void SaveTexture(Texture2D tex, string assetPath, bool asSprite)
        {
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = asSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.mipmapEnabled = !asSprite;
                ti.SaveAndReimport();
            }
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
