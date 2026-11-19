using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace VanguardZero.Core
{
    public enum AudioBus
    {
        Weapons,
        Impacts,
        Foley,
        Ambience,
        Music,
        UI,
        Voice
    }

    /// <summary>
    /// Centralized AAA audio manager with AudioMixer support, static gain fallback,
    /// pooled 3D spatialized SFX sources, seamless rain/music loops, and runtime
    /// synthesized fallback clips if external WAVs are not yet imported.
    /// </summary>
    public class VanguardAudioManager : MonoBehaviour
    {
        public static VanguardAudioManager Instance { get; private set; }

        [Header("Optional AudioMixer Routing")]
        [SerializeField] private AudioMixer mainMixer;
        [SerializeField] private AudioMixerGroup weaponsGroup;
        [SerializeField] private AudioMixerGroup impactsGroup;
        [SerializeField] private AudioMixerGroup foleyGroup;
        [SerializeField] private AudioMixerGroup ambienceGroup;
        [SerializeField] private AudioMixerGroup musicGroup;
        [SerializeField] private AudioMixerGroup uiGroup;
        [SerializeField] private AudioMixerGroup voiceGroup;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private readonly List<AudioSource> _pool3D = new List<AudioSource>(32);
        private int _poolCursor;

        private AudioSource _uiSource;
        private AudioSource _voiceSource;
        private AudioSource _musicSource;
        private AudioSource _ambienceSource;
        private AudioSource _heartbeatSource;
        private bool _pausedDucking;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            SetupSources();
            LoadAllClipsFromResources();
            EnsureFallbackClips();
            ApplyVolumes();
            VanguardSettings.OnSettingsChanged += ApplyVolumes;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                VanguardSettings.OnSettingsChanged -= ApplyVolumes;
                Instance = null;
            }
        }

        public static VanguardAudioManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("VanguardAudioManager");
            return go.AddComponent<VanguardAudioManager>();
        }

        private void SetupSources()
        {
            _uiSource = CreateDedicatedSource("UI_2D", AudioBus.UI, false, false);
            _voiceSource = CreateDedicatedSource("Voice_2D", AudioBus.Voice, false, false);
            _musicSource = CreateDedicatedSource("Music_2D", AudioBus.Music, false, true);
            _ambienceSource = CreateDedicatedSource("Ambience_2D", AudioBus.Ambience, false, true);
            _heartbeatSource = CreateDedicatedSource("Heartbeat_2D", AudioBus.Foley, false, true);

            for (int i = 0; i < 28; i++)
            {
                var src = CreateDedicatedSource($"SFX_3D_{i:00}", AudioBus.Weapons, true, false);
                src.minDistance = 2.5f;
                src.maxDistance = 85f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.dopplerLevel = 0.25f;
                _pool3D.Add(src);
            }
        }

        private AudioSource CreateDedicatedSource(string childName, AudioBus bus, bool spatial, bool loop)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = spatial ? 1f : 0f;
            src.outputAudioMixerGroup = ResolveGroup(bus);
            return src;
        }

        private AudioMixerGroup ResolveGroup(AudioBus bus)
        {
            return bus switch
            {
                AudioBus.Weapons => weaponsGroup,
                AudioBus.Impacts => impactsGroup,
                AudioBus.Foley => foleyGroup,
                AudioBus.Ambience => ambienceGroup,
                AudioBus.Music => musicGroup,
                AudioBus.UI => uiGroup,
                AudioBus.Voice => voiceGroup,
                _ => null
            };
        }

        private void LoadAllClipsFromResources()
        {
            var loaded = Resources.LoadAll<AudioClip>("Audio");
            foreach (var clip in loaded)
            {
                if (clip != null)
                    _clips[clip.name] = clip;
            }
        }

        public void RegisterClip(string key, AudioClip clip)
        {
            if (!string.IsNullOrEmpty(key) && clip != null)
                _clips[key] = clip;
        }

        public AudioClip GetClip(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _clips.TryGetValue(key, out var clip) ? clip : null;
        }

        public void SetPausedDucking(bool paused)
        {
            _pausedDucking = paused;
            ApplyVolumes();
        }

        public void ApplyVolumes()
        {
            float master = VanguardSettings.MasterVolume;
            float music = VanguardSettings.MusicVolume * (_pausedDucking ? 0.5f : 1f);
            float sfx = VanguardSettings.SfxVolume * (_pausedDucking ? 0.45f : 1f);
            float voice = VanguardSettings.VoiceVolume;

            bool hasMixerMusic = mainMixer != null && mainMixer.GetFloat("MusicVol", out _);
            bool hasMixerSfx = mainMixer != null && mainMixer.GetFloat("SFXVol", out _);

            if (hasMixerMusic)
                mainMixer.SetFloat("MusicVol", LinearToDb(music * master));
            if (hasMixerSfx)
                mainMixer.SetFloat("SFXVol", LinearToDb(sfx * master));
            if (mainMixer != null && mainMixer.GetFloat("MasterVol", out _))
                mainMixer.SetFloat("MasterVol", LinearToDb(master));

            if (_musicSource != null) _musicSource.volume = hasMixerMusic ? 0.55f : (0.55f * music * master);
            if (_ambienceSource != null) _ambienceSource.volume = hasMixerSfx ? 0.42f : (0.42f * sfx * master);
            if (_uiSource != null) _uiSource.volume = hasMixerSfx ? 0.85f : (0.85f * sfx * master);
            if (_voiceSource != null) _voiceSource.volume = 0.90f * voice * master;
        }

        private static float LinearToDb(float v)
        {
            return v <= 0.0001f ? -80f : Mathf.Log10(Mathf.Clamp(v, 0.0001f, 1f)) * 20f;
        }

        private float GetEffectiveBusGain(AudioBus bus)
        {
            float master = VanguardSettings.MasterVolume;
            float duck = _pausedDucking ? 0.5f : 1f;
            return bus switch
            {
                AudioBus.Music => VanguardSettings.MusicVolume * master * duck,
                AudioBus.Voice => VanguardSettings.VoiceVolume * master,
                _ => VanguardSettings.SfxVolume * master * duck
            };
        }

        public void Play2D(string clipKey, AudioBus bus = AudioBus.UI, float volume = 1f, float pitchJitter = 0f)
        {
            var clip = GetClip(clipKey);
            if (clip == null) return;
            var src = bus == AudioBus.Voice ? _voiceSource : _uiSource;
            if (src == null) return;
            src.outputAudioMixerGroup = ResolveGroup(bus);
            src.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            src.PlayOneShot(clip, Mathf.Clamp01(volume) * GetEffectiveBusGain(bus));
        }

        public void PlayRandom2D(string prefix, int count, AudioBus bus, float volume = 1f, float pitchJitter = 0.04f)
        {
            int idx = UnityEngine.Random.Range(1, count + 1);
            string key = $"{prefix}_{idx:00}";
            if (!_clips.ContainsKey(key)) key = $"{prefix}_01";
            Play2D(key, bus, volume, pitchJitter);
        }

        public void Play3D(string clipKey, Vector3 position, AudioBus bus = AudioBus.Weapons, float volume = 1f, float pitchJitter = 0.04f, float maxDistance = 85f)
        {
            var clip = GetClip(clipKey);
            if (clip == null || _pool3D.Count == 0) return;

            var src = _pool3D[_poolCursor];
            _poolCursor = (_poolCursor + 1) % _pool3D.Count;

            src.transform.position = position;
            src.outputAudioMixerGroup = ResolveGroup(bus);
            src.spatialBlend = 1f;
            src.maxDistance = maxDistance;
            src.pitch = 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter);
            src.volume = Mathf.Clamp01(volume) * GetEffectiveBusGain(bus);
            src.clip = clip;
            src.Play();
        }

        public void PlayRandom3D(string prefix, int count, Vector3 position, AudioBus bus = AudioBus.Weapons, float volume = 1f, float pitchJitter = 0.05f, float maxDistance = 85f)
        {
            int idx = UnityEngine.Random.Range(1, count + 1);
            string key = $"{prefix}_{idx:00}";
            if (!_clips.ContainsKey(key)) key = $"{prefix}_01";
            Play3D(key, position, bus, volume, pitchJitter, maxDistance);
        }

        public void PlayMusicLoop(string clipKey = "music_main_menu_loop")
        {
            var clip = GetClip(clipKey);
            if (clip == null || _musicSource == null) return;
            if (_musicSource.clip == clip && _musicSource.isPlaying) return;
            _musicSource.clip = clip;
            _musicSource.loop = true;
            _musicSource.Play();
            ApplyVolumes();
        }

        public void StopMusic()
        {
            if (_musicSource != null && _musicSource.isPlaying)
                _musicSource.Stop();
        }

        public void PlayMusicStinger(string clipKey)
        {
            StopMusic();
            Play2D(clipKey, AudioBus.Music, 0.9f, 0f);
        }

        public void PlayAmbienceLoop(string clipKey = "ambience_blacksite_rain_loop")
        {
            var clip = GetClip(clipKey);
            if (clip == null || _ambienceSource == null) return;
            if (_ambienceSource.clip == clip && _ambienceSource.isPlaying) return;
            _ambienceSource.clip = clip;
            _ambienceSource.loop = true;
            _ambienceSource.Play();
            ApplyVolumes();
        }

        public void StopAmbience()
        {
            if (_ambienceSource != null && _ambienceSource.isPlaying)
                _ambienceSource.Stop();
        }

        public void SetLowHealthHeartbeat(bool active)
        {
            if (_heartbeatSource == null) return;
            if (active)
            {
                var clip = GetClip("low_health_heartbeat");
                if (clip != null && !_heartbeatSource.isPlaying)
                {
                    _heartbeatSource.clip = clip;
                    _heartbeatSource.volume = 0.45f * GetEffectiveBusGain(AudioBus.Foley);
                    _heartbeatSource.Play();
                }
            }
            else if (_heartbeatSource.isPlaying)
            {
                _heartbeatSource.Stop();
            }
        }

        private void EnsureFallbackClips()
        {
            // Synthesize clean tactical placeholders ONLY for clips not yet imported from audio-pipeline
            EnsureSynth("ui_click", 0.04f, (t, d) => Mathf.Sin(t * 2400f * Mathf.PI * 2f) * Mathf.Exp(-t * 95f) * 0.35f);
            EnsureSynth("ui_hover", 0.025f, (t, d) => Mathf.Sin(t * 1650f * Mathf.PI * 2f) * Mathf.Exp(-t * 120f) * 0.2f);
            EnsureSynth("ui_confirm", 0.14f, (t, d) => (Mathf.Sin(t * 880f * Mathf.PI * 2f) + 0.5f * Mathf.Sin(t * 1320f * Mathf.PI * 2f)) * Mathf.Exp(-t * 20f) * 0.3f);
            EnsureSynth("ui_cancel", 0.12f, (t, d) => Mathf.Sin(t * (520f - 180f * (t / d)) * Mathf.PI * 2f) * Mathf.Exp(-t * 22f) * 0.3f);
            EnsureSynth("hitmarker_body", 0.055f, (t, d) => Mathf.Sin(t * 1900f * Mathf.PI * 2f) * Mathf.Exp(-t * 70f) * 0.45f);
            EnsureSynth("hitmarker_headshot", 0.11f, (t, d) => (Mathf.Sin(t * 2800f * Mathf.PI * 2f) + Mathf.Sin(t * 1100f * Mathf.PI * 2f)) * Mathf.Exp(-t * 36f) * 0.4f);
            EnsureSynth("kill_confirm", 0.16f, (t, d) => (Mathf.Sin(t * 640f * Mathf.PI * 2f) + Mathf.Sin(t * 960f * Mathf.PI * 2f)) * Mathf.Exp(-t * 18f) * 0.35f);
            EnsureNoiseShot("vx47_fire_01", 0.24f, 180f, 38f);
            EnsureNoiseShot("m890_fire_01", 0.38f, 95f, 18f);
            EnsureNoiseShot("m19_fire_01", 0.17f, 260f, 48f);
            EnsureNoiseShot("impact_concrete_01", 0.12f, 420f, 55f);
            EnsureNoiseShot("impact_metal_01", 0.16f, 1400f, 32f);
            EnsureNoiseShot("footstep_concrete_01", 0.09f, 160f, 45f);
            EnsureNoiseShot("footstep_metal_01", 0.11f, 520f, 38f);
            EnsureRainFallback("ambience_blacksite_rain_loop", 4.0f);
            EnsureMenuDroneFallback("music_main_menu_loop", 8.0f);
        }

        private void EnsureSynth(string key, float duration, Func<float, float, float> sampleFn)
        {
            if (_clips.ContainsKey(key)) return;
            const int sr = 24000;
            int count = Mathf.Max(64, Mathf.RoundToInt(sr * duration));
            var data = new float[count];
            int fade = Mathf.Min(count / 8, (int)(sr * 0.005f));
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float env = 1f;
                if (i < fade) env = (float)i / fade;
                else if (i > count - fade) env = (float)(count - i) / fade;
                data[i] = Mathf.Clamp(sampleFn(t, duration) * env, -0.9f, 0.9f);
            }
            var clip = AudioClip.Create(key, count, 1, sr, false);
            clip.SetData(data, 0);
            _clips[key] = clip;
        }

        private void EnsureNoiseShot(string key, float duration, float bodyHz, float decay)
        {
            if (_clips.ContainsKey(key)) return;
            const int sr = 24000;
            int count = Mathf.RoundToInt(sr * duration);
            var data = new float[count];
            var rng = new System.Random(key.GetHashCode());
            float lp = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = lp * 0.72f + white * 0.28f;
                float punch = Mathf.Sin(t * bodyHz * Mathf.PI * 2f) * Mathf.Exp(-t * decay * 1.4f);
                float crack = lp * Mathf.Exp(-t * decay);
                data[i] = Mathf.Clamp((punch * 0.55f + crack * 0.7f), -0.92f, 0.92f);
            }
            var clip = AudioClip.Create(key, count, 1, sr, false);
            clip.SetData(data, 0);
            _clips[key] = clip;
        }

        private void EnsureRainFallback(string key, float duration)
        {
            if (_clips.ContainsKey(key)) return;
            const int sr = 24000;
            int count = Mathf.RoundToInt(sr * duration);
            var data = new float[count];
            var rng = new System.Random(4209);
            float lp = 0f;
            for (int i = 0; i < count; i++)
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = lp * 0.88f + w * 0.12f;
                data[i] = lp * 0.25f;
            }
            // Crossfade loop seam
            int xf = sr / 4;
            for (int i = 0; i < xf; i++)
            {
                float a = (float)i / xf;
                data[i] = Mathf.Lerp(data[count - xf + i], data[i], a);
            }
            var clip = AudioClip.Create(key, count, 1, sr, false);
            clip.SetData(data, 0);
            _clips[key] = clip;
        }

        private void EnsureMenuDroneFallback(string key, float duration)
        {
            if (_clips.ContainsKey(key)) return;
            const int sr = 24000;
            int count = Mathf.RoundToInt(sr * duration);
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sr;
                float lfo = 0.7f + 0.3f * Mathf.Sin(t * (2f * Mathf.PI / duration));
                float sub = Mathf.Sin(t * 55f * Mathf.PI * 2f) * 0.22f;
                float fifth = Mathf.Sin(t * 82.5f * Mathf.PI * 2f) * 0.11f;
                float pad = Mathf.Sin(t * 165f * Mathf.PI * 2f) * 0.05f * lfo;
                data[i] = (sub + fifth + pad) * lfo;
            }
            var clip = AudioClip.Create(key, count, 1, sr, false);
            clip.SetData(data, 0);
            _clips[key] = clip;
        }
    }
}
