using System;
using UnityEngine;

namespace VanguardZero.Core
{
    /// <summary>
    /// Persistent player settings (sensitivity, FOV, audio levels, graphics tier, callsign).
    /// </summary>
    public static class VanguardSettings
    {
        private const string KeyCallsign = "VZ_Callsign";
        private const string KeySensitivity = "VZ_MouseSens";
        private const string KeyAdsMult = "VZ_AdsMult";
        private const string KeyFov = "VZ_Fov";
        private const string KeyMasterVol = "VZ_MasterVol";
        private const string KeyMusicVol = "VZ_MusicVol";
        private const string KeySfxVol = "VZ_SfxVol";
        private const string KeyVoiceVol = "VZ_VoiceVol";
        private const string KeyQuality = "VZ_Quality";
        private const string KeyInvertY = "VZ_InvertY";

        public static event Action OnSettingsChanged;

        public static string Callsign
        {
            get
            {
                string saved = PlayerPrefs.GetString(KeyCallsign, "");
                if (string.IsNullOrWhiteSpace(saved))
                {
                    saved = $"VANGUARD-{UnityEngine.Random.Range(10, 99)}";
                    PlayerPrefs.SetString(KeyCallsign, saved);
                    PlayerPrefs.Save();
                }
                return saved;
            }
            set
            {
                string clean = string.IsNullOrWhiteSpace(value) ? "OPERATOR-01" : value.Trim();
                if (clean.Length > 18) clean = clean.Substring(0, 18);
                PlayerPrefs.SetString(KeyCallsign, clean);
                PlayerPrefs.Save();
                OnSettingsChanged?.Invoke();
            }
        }

        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(KeySensitivity, 1.35f);
            set { PlayerPrefs.SetFloat(KeySensitivity, Mathf.Clamp(value, 0.2f, 5f)); PlayerPrefs.Save(); OnSettingsChanged?.Invoke(); }
        }

        public static float AdsSensitivityMultiplier
        {
            get => PlayerPrefs.GetFloat(KeyAdsMult, 0.58f);
            set { PlayerPrefs.SetFloat(KeyAdsMult, Mathf.Clamp(value, 0.2f, 1.2f)); PlayerPrefs.Save(); OnSettingsChanged?.Invoke(); }
        }

        public static float FieldOfView
        {
            get => PlayerPrefs.GetFloat(KeyFov, 78f);
            set { PlayerPrefs.SetFloat(KeyFov, Mathf.Clamp(value, 60f, 105f)); PlayerPrefs.Save(); OnSettingsChanged?.Invoke(); }
        }

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(KeyMasterVol, 0.90f);
            set { PlayerPrefs.SetFloat(KeyMasterVol, Mathf.Clamp01(value)); PlayerPrefs.Save(); OnSettingsChanged?.Invoke(); }
        }

        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(KeyMusicVol, 0.65f);
            set { PlayerPrefs.SetFloat(KeyMusicVol, Mathf.Clamp01(value)); PlayerPrefs.Save(); OnSettingsChanged?.Invoke(); }
        }

        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(KeySfxVol, 0.95f);
            set { PlayerPrefs.SetFloat(KeySfxVol, Mathf.Clamp01(value)); PlayerPrefs.Save(); OnSettingsChanged?.Invoke(); }
        }

        public static float VoiceVolume
        {
            get => PlayerPrefs.GetFloat(KeyVoiceVol, 0.90f);
            set { PlayerPrefs.SetFloat(KeyVoiceVol, Mathf.Clamp01(value)); PlayerPrefs.Save(); OnSettingsChanged?.Invoke(); }
        }

        public static bool InvertY
        {
            get => PlayerPrefs.GetInt(KeyInvertY, 0) == 1;
            set { PlayerPrefs.SetInt(KeyInvertY, value ? 1 : 0); PlayerPrefs.Save(); OnSettingsChanged?.Invoke(); }
        }

        public static int QualityLevel
        {
            get => PlayerPrefs.GetInt(KeyQuality, Mathf.Max(0, QualitySettings.names.Length - 1));
            set
            {
                int clamped = Mathf.Clamp(value, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
                PlayerPrefs.SetInt(KeyQuality, clamped);
                PlayerPrefs.Save();
                QualitySettings.SetQualityLevel(clamped, true);
                OnSettingsChanged?.Invoke();
            }
        }
    }
}
