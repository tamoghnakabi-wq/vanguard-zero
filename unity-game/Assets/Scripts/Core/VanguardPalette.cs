using UnityEngine;

namespace VanguardZero.Core
{
    /// <summary>
    /// Single source of truth for VANGUARD ZERO ("BLACKSITE-09") Art Bible colors.
    /// Style: Hyper-realistic rain-swept dusk brutalist military radar compound.
    /// </summary>
    public static class VanguardPalette
    {
        // Environment & Architecture
        public static readonly Color TarmacWet       = FromHex("#1D2229");
        public static readonly Color ConcreteRaw     = FromHex("#4A515B");
        public static readonly Color ConcreteLight   = FromHex("#636C77");
        public static readonly Color ConcreteTrim    = FromHex("#2E343D");
        public static readonly Color SteelGunmetal   = FromHex("#242930");
        public static readonly Color SteelBrushed    = FromHex("#46505C");
        public static readonly Color OliveTactical   = FromHex("#3B4639");
        public static readonly Color RustIndustrial  = FromHex("#5A3A2B");
        public static readonly Color SlateCool       = FromHex("#6D7B8D");
        public static readonly Color HazardYellow    = FromHex("#D49B2A");

        // Atmosphere, Sky & Lighting
        public static readonly Color ShadowVoid      = FromHex("#0D1117");
        public static readonly Color FogDusk         = FromHex("#151E2A");
        public static readonly Color SkyZenith       = FromHex("#0B1320");
        public static readonly Color SkyHorizon      = FromHex("#243447");
        public static readonly Color SkyGround       = FromHex("#11161E");
        public static readonly Color MoonKeyLight    = FromHex("#9EBCE0");
        public static readonly Color FloodlightWarm  = FromHex("#FFB46B");
        public static readonly Color InteriorCyan    = FromHex("#38D4FF");
        public static readonly Color AmbientSky      = FromHex("#2B3B52");
        public static readonly Color AmbientEquator  = FromHex("#1B2330");
        public static readonly Color AmbientGround   = FromHex("#0E1117");

        // Teams & Tactical Accents
        public static readonly Color AccentAmber     = FromHex("#FF8C24"); // Team Alpha / Primary CTA / Muzzle
        public static readonly Color AccentCyan      = FromHex("#29C7E8"); // Team Bravo / Tactical Telemetry
        public static readonly Color AccentCrimson   = FromHex("#E53935"); // Headshot / Damage / Alert
        public static readonly Color AccentEmerald   = FromHex("#2ECC71"); // Ready / Online / Positive
        public static readonly Color TracerGold      = FromHex("#FFD180");
        public static readonly Color SparkHot        = FromHex("#FFAB40");
        public static readonly Color BrassCasing     = FromHex("#C9A24B");
        public static readonly Color DustConcrete    = FromHex("#78808A");

        // UI & HUD Chrome
        public static readonly Color HudPanelBg      = new Color(0.055f, 0.075f, 0.105f, 0.84f);
        public static readonly Color HudPanelDark    = new Color(0.035f, 0.050f, 0.072f, 0.92f);
        public static readonly Color HudScrim        = new Color(0.040f, 0.055f, 0.080f, 0.72f);
        public static readonly Color HudBorder       = new Color(0.320f, 0.400f, 0.490f, 0.45f);
        public static readonly Color HudButtonNormal = new Color(0.110f, 0.145f, 0.195f, 0.90f);
        public static readonly Color HudButtonHover  = new Color(0.180f, 0.235f, 0.305f, 0.96f);
        public static readonly Color HudLight        = FromHex("#E6EDF3");
        public static readonly Color HudMuted        = FromHex("#8B99A8");
        public static readonly Color HudHealthFill   = FromHex("#E6EDF3");
        public static readonly Color HudArmorFill    = FromHex("#29C7E8");

        public static Color FromHex(string hex)
        {
            if (ColorUtility.TryParseHtmlString(hex, out Color c))
                return c;
            return new Color(0.2f, 0.24f, 0.29f, 1f);
        }

        public static Color WithAlpha(Color c, float alpha)
        {
            return new Color(c.r, c.g, c.b, alpha);
        }

        public static Color GetTeamColor(int teamId)
        {
            return teamId switch
            {
                1 => AccentAmber, // Alpha
                2 => AccentCyan,  // Bravo
                _ => HudLight
            };
        }

        public static Color GetTeamArmorTint(int teamId)
        {
            return teamId switch
            {
                1 => FromHex("#3A352E"), // Warm charcoal-khaki for Alpha
                2 => FromHex("#27333D"), // Cold slate-navy for Bravo
                _ => SteelGunmetal
            };
        }
    }
}
