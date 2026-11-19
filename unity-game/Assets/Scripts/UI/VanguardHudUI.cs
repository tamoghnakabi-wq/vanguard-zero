using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using VanguardZero.Core;
using VanguardZero.Gameplay;
using VanguardZero.Networking;
using VanguardZero.Weapons;

namespace VanguardZero.UI
{
    /// <summary>
    /// AAA Tactical In-Game HUD, Dynamic Crosshair, Hitmarkers, Directional Damage,
    /// Kill Feed, Compass, Tactical Radar, Scoreboard, Pause Menu, and Victory/Defeat Screen.
    /// </summary>
    public class VanguardHudUI : MonoBehaviour
    {
        private Canvas _canvas;

        // Crosshair & Hitmarker
        private RectTransform _crosshairTop, _crosshairBottom, _crosshairLeft, _crosshairRight;
        private CanvasGroup _crosshairGroup;
        private CanvasGroup _hitmarkerGroup;
        private UnityEngine.UI.Image[] _hitmarkerBars;
        private TextMeshProUGUI _killBannerText;
        private float _hitmarkerTimer;
        private float _killBannerTimer;

        // Damage vignette & flash
        private UnityEngine.UI.Image _damageVignette;
        private float _damageFlashAlpha;

        // Top match telemetry
        private TextMeshProUGUI _alphaScoreText;
        private TextMeshProUGUI _bravoScoreText;
        private TextMeshProUGUI _timerText;
        private TextMeshProUGUI _compassText;
        private TextMeshProUGUI _joinCodeBadge;

        // Bottom-left vitals
        private UnityEngine.UI.Image _healthFill;
        private UnityEngine.UI.Image _armorFill;
        private UnityEngine.UI.Image _staminaFill;
        private TextMeshProUGUI _healthText;
        private TextMeshProUGUI _armorText;
        private TextMeshProUGUI _stanceText;
        private float _displayHealth = 100f;
        private float _displayArmor = 50f;
        private float _targetHealth = 100f;
        private float _targetArmor = 50f;

        // Bottom-right weapons
        private TextMeshProUGUI _weaponNameText;
        private TextMeshProUGUI _ammoMagText;
        private TextMeshProUGUI _ammoReserveText;
        private UnityEngine.UI.Image _reloadFill;
        private GameObject _reloadBarRoot;
        private TextMeshProUGUI[] _slotLabels = new TextMeshProUGUI[3];

        // Kill feed
        private Transform _killFeedContainer;
        private readonly List<(GameObject go, float expireAt)> _killFeedRows = new List<(GameObject, float)>();

        // Radar
        private RectTransform _radarRoot;
        private readonly List<RectTransform> _radarBlips = new List<RectTransform>();

        // Overlays
        private GameObject _respawnOverlay;
        private TextMeshProUGUI _respawnReasonText;
        private TextMeshProUGUI _respawnCountdownText;
        private float _respawnEndTime;

        private GameObject _scoreboardOverlay;
        private Transform _scoreboardRows;

        private GameObject _pauseOverlay;
        private GameObject _matchEndOverlay;
        private TextMeshProUGUI _matchEndTitle;
        private TextMeshProUGUI _matchEndSubtitle;
        private Transform _matchEndScores;
        private UnityEngine.UI.Button _rematchBtn;

        private TacticalFpsController _localPlayer;
        private bool _isPaused;
        private bool _isMatchEnded;

        private void Awake()
        {
            EnsureEventSystem();
            VanguardAudioManager.EnsureInstance();
            BuildHudCanvas();
        }

        private void Start()
        {
            VanguardAudioManager.Instance?.StopMusic();
            VanguardAudioManager.Instance?.PlayAmbienceLoop("ambience_blacksite_rain_loop");

            TacticalFpsController.OnLocalPlayerReady += BindLocalPlayer;
            TacticalFpsController.OnLocalVitalsUpdated += HandleVitalsUpdated;

            if (TacticalFpsController.LocalInstance != null)
                BindLocalPlayer(TacticalFpsController.LocalInstance);

            BindMatchService();
            VanguardNetServices.OnMatchServiceRegistered += _ => BindMatchService();

            SetCursorForGameplay();
        }

        private void OnDestroy()
        {
            TacticalFpsController.OnLocalPlayerReady -= BindLocalPlayer;
            TacticalFpsController.OnLocalVitalsUpdated -= HandleVitalsUpdated;
            UnbindLocalPlayer();
            UnbindMatchService();
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        private void BindLocalPlayer(TacticalFpsController player)
        {
            UnbindLocalPlayer();
            _localPlayer = player;
            if (_localPlayer == null) return;

            if (_localPlayer.Weapons != null)
            {
                _localPlayer.Weapons.OnWeaponAmmoChanged += HandleAmmoChanged;
                _localPlayer.Weapons.OnLocalHitConfirmed += HandleHitConfirmed;
                HandleAmmoChanged(_localPlayer.Weapons.CurrentWeapon, _localPlayer.Weapons.CurrentMagAmmo, _localPlayer.Weapons.CurrentReserveAmmo);
            }

            if (_localPlayer.Authority != null)
            {
                _targetHealth = _localPlayer.Authority.Health;
                _targetArmor = _localPlayer.Authority.Armor;
                _displayHealth = _targetHealth;
                _displayArmor = _targetArmor;
            }
        }

        private void UnbindLocalPlayer()
        {
            if (_localPlayer == null) return;
            if (_localPlayer.Weapons != null)
            {
                _localPlayer.Weapons.OnWeaponAmmoChanged -= HandleAmmoChanged;
                _localPlayer.Weapons.OnLocalHitConfirmed -= HandleHitConfirmed;
            }
        }

        private void BindMatchService()
        {
            var match = VanguardNetServices.Match;
            if (match == null) return;
            match.OnMatchStateChanged -= HandleMatchStateChanged;
            match.OnMatchStateChanged += HandleMatchStateChanged;
            match.OnScoreboardUpdated -= HandleScoreboardUpdated;
            match.OnScoreboardUpdated += HandleScoreboardUpdated;
            match.OnKillFeedEntryAdded -= HandleKillFeedEntry;
            match.OnKillFeedEntryAdded += HandleKillFeedEntry;
            match.OnLocalPlayerEliminated -= HandleLocalEliminated;
            match.OnLocalPlayerEliminated += HandleLocalEliminated;
            match.OnLocalPlayerRespawned -= HandleLocalRespawned;
            match.OnLocalPlayerRespawned += HandleLocalRespawned;
            match.OnMatchEnded -= HandleMatchEnded;
            match.OnMatchEnded += HandleMatchEnded;

            HandleMatchStateChanged(match.CurrentState);
            HandleScoreboardUpdated(match.Scoreboard);
        }

        private void UnbindMatchService()
        {
            var match = VanguardNetServices.Match;
            if (match == null) return;
            match.OnMatchStateChanged -= HandleMatchStateChanged;
            match.OnScoreboardUpdated -= HandleScoreboardUpdated;
            match.OnKillFeedEntryAdded -= HandleKillFeedEntry;
            match.OnLocalPlayerEliminated -= HandleLocalEliminated;
            match.OnLocalPlayerRespawned -= HandleLocalRespawned;
            match.OnMatchEnded -= HandleMatchEnded;
        }

        private void BuildHudCanvas()
        {
            _canvas = VanguardUiFactory.CreateHudCanvas("VanguardHudCanvas", 5);
            Transform root = _canvas.transform;

            // Damage flash overlay (non-blocking)
            var dmgRt = VanguardUiFactory.CreateFullStretch("DamageVignette", root);
            _damageVignette = dmgRt.gameObject.AddComponent<UnityEngine.UI.Image>();
            _damageVignette.sprite = VanguardUiFactory.SolidSprite;
            _damageVignette.color = VanguardPalette.WithAlpha(VanguardPalette.AccentCrimson, 0f);
            _damageVignette.raycastTarget = false;

            BuildCrosshairAndHitmarker(root);
            BuildTopMatchBar(root);
            BuildRadar(root);
            BuildKillFeed(root);
            BuildVitalsCluster(root);
            BuildWeaponCluster(root);
            BuildRespawnOverlay(root);
            BuildScoreboardOverlay(root);
            BuildPauseOverlay(root);
            BuildMatchEndOverlay(root);
        }

        private void BuildCrosshairAndHitmarker(Transform root)
        {
            var center = VanguardUiFactory.CreateRect("CrosshairRoot", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 120f));
            _crosshairGroup = center.gameObject.AddComponent<CanvasGroup>();
            _crosshairGroup.blocksRaycasts = false;

            _crosshairTop = MakeBar("Top", center, new Vector2(2f, 12f), new Vector2(0f, 14f), VanguardPalette.HudLight);
            _crosshairBottom = MakeBar("Bottom", center, new Vector2(2f, 12f), new Vector2(0f, -14f), VanguardPalette.HudLight);
            _crosshairLeft = MakeBar("Left", center, new Vector2(12f, 2f), new Vector2(-14f, 0f), VanguardPalette.HudLight);
            _crosshairRight = MakeBar("Right", center, new Vector2(12f, 2f), new Vector2(14f, 0f), VanguardPalette.HudLight);
            MakeBar("CenterDot", center, new Vector2(2.5f, 2.5f), Vector2.zero, VanguardPalette.AccentAmber);

            var hmRoot = VanguardUiFactory.CreateRect("Hitmarker", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f));
            _hitmarkerGroup = hmRoot.gameObject.AddComponent<CanvasGroup>();
            _hitmarkerGroup.alpha = 0f;
            _hitmarkerGroup.blocksRaycasts = false;
            _hitmarkerBars = new UnityEngine.UI.Image[4];
            Vector2[] dirs = { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) };
            float[] rots = { 45f, -45f, -45f, 45f };
            for (int i = 0; i < 4; i++)
            {
                var bar = MakeBar($"HM_{i}", hmRoot, new Vector2(2.5f, 11f), dirs[i] * 9f, VanguardPalette.HudLight);
                bar.localRotation = Quaternion.Euler(0f, 0f, rots[i]);
                _hitmarkerBars[i] = bar.GetComponent<UnityEngine.UI.Image>();
            }

            _killBannerText = VanguardUiFactory.CreateLabel("KillBanner", root, "", 22f, VanguardPalette.AccentAmber, TextAlignmentOptions.Center, FontStyles.Bold, 4f);
            _killBannerText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _killBannerText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _killBannerText.rectTransform.anchoredPosition = new Vector2(0f, -84f);
            _killBannerText.rectTransform.sizeDelta = new Vector2(460f, 34f);
        }

        private static RectTransform MakeBar(string name, Transform parent, Vector2 size, Vector2 pos, Color col)
        {
            var rt = VanguardUiFactory.CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.sprite = VanguardUiFactory.SolidSprite;
            img.color = col;
            img.raycastTarget = false;
            return rt;
        }

        private void BuildTopMatchBar(Transform root)
        {
            var top = VanguardUiFactory.CreatePanel("TopMatchBar", root, VanguardPalette.HudPanelBg, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(420f, 56f), true, false);

            _alphaScoreText = VanguardUiFactory.CreateLabel("AlphaScore", top.transform, "ALPHA  0", 20f, VanguardPalette.AccentAmber, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 2f);
            _alphaScoreText.rectTransform.anchorMin = new Vector2(0f, 0f);
            _alphaScoreText.rectTransform.anchorMax = new Vector2(0.38f, 1f);
            _alphaScoreText.rectTransform.offsetMin = new Vector2(18f, 0f);
            _alphaScoreText.rectTransform.offsetMax = Vector2.zero;

            _timerText = VanguardUiFactory.CreateLabel("Timer", top.transform, "05:00", 22f, VanguardPalette.HudLight, TextAlignmentOptions.Center, FontStyles.Bold, 3f);
            _timerText.rectTransform.anchorMin = new Vector2(0.38f, 0f);
            _timerText.rectTransform.anchorMax = new Vector2(0.62f, 1f);
            _timerText.rectTransform.offsetMin = Vector2.zero;
            _timerText.rectTransform.offsetMax = Vector2.zero;

            _bravoScoreText = VanguardUiFactory.CreateLabel("BravoScore", top.transform, "0  BRAVO", 20f, VanguardPalette.AccentCyan, TextAlignmentOptions.MidlineRight, FontStyles.Bold, 2f);
            _bravoScoreText.rectTransform.anchorMin = new Vector2(0.62f, 0f);
            _bravoScoreText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _bravoScoreText.rectTransform.offsetMin = Vector2.zero;
            _bravoScoreText.rectTransform.offsetMax = new Vector2(-18f, 0f);

            // Compass strip below match bar
            _compassText = VanguardUiFactory.CreateLabel("Compass", root, "N  ·  000°  ·  BLACKSITE-09", 16f, VanguardPalette.HudLight, TextAlignmentOptions.Center, FontStyles.Bold, 1.2f);
            _compassText.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            _compassText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            _compassText.rectTransform.pivot = new Vector2(0.5f, 1f);
            _compassText.rectTransform.anchoredPosition = new Vector2(0f, -78f);
            _compassText.rectTransform.sizeDelta = new Vector2(400f, 26f);

            _joinCodeBadge = VanguardUiFactory.CreateLabel("JoinCodeBadge", root, "", 14f, VanguardPalette.AccentCyan, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 1.0f);
            _joinCodeBadge.rectTransform.anchorMin = new Vector2(0f, 1f);
            _joinCodeBadge.rectTransform.anchorMax = new Vector2(0f, 1f);
            _joinCodeBadge.rectTransform.pivot = new Vector2(0f, 1f);
            _joinCodeBadge.rectTransform.anchoredPosition = new Vector2(24f, -184f);
            _joinCodeBadge.rectTransform.sizeDelta = new Vector2(300f, 24f);
        }

        private void BuildRadar(Transform root)
        {
            var panel = VanguardUiFactory.CreatePanel("TacticalRadar", root, VanguardPalette.HudPanelDark, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(156f, 156f), true, false);
            _radarRoot = panel.rectTransform;

            // Crosshair grid lines
            MakeBar("RadarH", _radarRoot, new Vector2(152f, 1f), Vector2.zero, VanguardPalette.HudBorder);
            MakeBar("RadarV", _radarRoot, new Vector2(1f, 152f), Vector2.zero, VanguardPalette.HudBorder);
            MakeBar("SelfBlip", _radarRoot, new Vector2(8f, 8f), Vector2.zero, VanguardPalette.HudLight);

            for (int i = 0; i < 12; i++)
            {
                var blip = MakeBar($"RadarBlip_{i}", _radarRoot, new Vector2(7f, 7f), Vector2.zero, VanguardPalette.AccentCyan);
                blip.gameObject.SetActive(false);
                _radarBlips.Add(blip);
            }
        }

        private void BuildKillFeed(Transform root)
        {
            var kf = VanguardUiFactory.CreateRect("KillFeed", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(400f, 220f));
            var vlg = kf.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childAlignment = TextAnchor.UpperRight;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = false;
            vlg.childForceExpandHeight = false;
            _killFeedContainer = kf;
        }

        private void BuildVitalsCluster(Transform root)
        {
            var panel = VanguardUiFactory.CreatePanel("VitalsCluster", root, VanguardPalette.HudPanelBg, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 28f), new Vector2(340f, 118f), true, false);
            VanguardUiFactory.AddAccentStripe(panel.transform, VanguardPalette.AccentAmber, true, 4f);

            _healthText = VanguardUiFactory.CreateLabel("HealthVal", panel.transform, "100", 38f, VanguardPalette.HudLight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 1f);
            _healthText.rectTransform.anchorMin = new Vector2(0f, 0.48f);
            _healthText.rectTransform.anchorMax = new Vector2(0.45f, 1f);
            _healthText.rectTransform.offsetMin = new Vector2(18f, 0f);
            _healthText.rectTransform.offsetMax = Vector2.zero;

            _armorText = VanguardUiFactory.CreateLabel("ArmorVal", panel.transform, "ARM 50", 16f, VanguardPalette.AccentCyan, TextAlignmentOptions.MidlineRight, FontStyles.Bold, 2f);
            _armorText.rectTransform.anchorMin = new Vector2(0.45f, 0.62f);
            _armorText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _armorText.rectTransform.offsetMin = Vector2.zero;
            _armorText.rectTransform.offsetMax = new Vector2(-16f, 0f);

            _stanceText = VanguardUiFactory.CreateLabel("StanceVal", panel.transform, "STANDING", 12f, VanguardPalette.HudMuted, TextAlignmentOptions.MidlineRight, FontStyles.Bold, 2f);
            _stanceText.rectTransform.anchorMin = new Vector2(0.45f, 0.42f);
            _stanceText.rectTransform.anchorMax = new Vector2(1f, 0.65f);
            _stanceText.rectTransform.offsetMin = Vector2.zero;
            _stanceText.rectTransform.offsetMax = new Vector2(-16f, 0f);

            var hpBar = VanguardUiFactory.CreateFilledBar("HealthBar", panel.transform, VanguardPalette.ConcreteTrim, VanguardPalette.HudHealthFill, new Vector2(304f, 11f), out _healthFill);
            hpBar.rectTransform.anchoredPosition = new Vector2(18f, -14f);

            var armBar = VanguardUiFactory.CreateFilledBar("ArmorBar", panel.transform, VanguardPalette.ConcreteTrim, VanguardPalette.HudArmorFill, new Vector2(304f, 7f), out _armorFill);
            armBar.rectTransform.anchoredPosition = new Vector2(18f, -29f);

            var stamBar = VanguardUiFactory.CreateFilledBar("StaminaBar", panel.transform, VanguardPalette.ConcreteTrim, VanguardPalette.AccentAmber, new Vector2(304f, 5f), out _staminaFill);
            stamBar.rectTransform.anchoredPosition = new Vector2(18f, -41f);
        }

        private void BuildWeaponCluster(Transform root)
        {
            var panel = VanguardUiFactory.CreatePanel("WeaponCluster", root, VanguardPalette.HudPanelBg, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 28f), new Vector2(340f, 118f), true, false);
            VanguardUiFactory.AddAccentStripe(panel.transform, VanguardPalette.AccentCyan, false, 3f);

            _weaponNameText = VanguardUiFactory.CreateLabel("WeaponName", panel.transform, "VX-47 ASSAULT RIFLE", 16f, VanguardPalette.AccentAmber, TextAlignmentOptions.MidlineRight, FontStyles.Bold, 1.0f);
            _weaponNameText.rectTransform.anchorMin = new Vector2(0f, 0.72f);
            _weaponNameText.rectTransform.anchorMax = new Vector2(1f, 1f);
            _weaponNameText.rectTransform.offsetMin = new Vector2(14f, 0f);
            _weaponNameText.rectTransform.offsetMax = new Vector2(-18f, -4f);

            _ammoMagText = VanguardUiFactory.CreateLabel("AmmoMag", panel.transform, "30", 44f, VanguardPalette.HudLight, TextAlignmentOptions.MidlineRight, FontStyles.Bold, 1f);
            _ammoMagText.rectTransform.anchorMin = new Vector2(0f, 0.25f);
            _ammoMagText.rectTransform.anchorMax = new Vector2(0.66f, 0.78f);
            _ammoMagText.rectTransform.offsetMin = Vector2.zero;
            _ammoMagText.rectTransform.offsetMax = Vector2.zero;

            _ammoReserveText = VanguardUiFactory.CreateLabel("AmmoRes", panel.transform, "/ 150", 20f, VanguardPalette.HudMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 1f);
            _ammoReserveText.rectTransform.anchorMin = new Vector2(0.68f, 0.28f);
            _ammoReserveText.rectTransform.anchorMax = new Vector2(1f, 0.72f);
            _ammoReserveText.rectTransform.offsetMin = Vector2.zero;
            _ammoReserveText.rectTransform.offsetMax = new Vector2(-14f, 0f);

            string[] names = { "[1] VX-47", "[2] M890", "[3] M19" };
            for (int i = 0; i < 3; i++)
            {
                var lbl = VanguardUiFactory.CreateLabel($"Slot_{i}", panel.transform, names[i], 14f, i == 0 ? VanguardPalette.AccentCyan : VanguardPalette.HudMuted, TextAlignmentOptions.Center, FontStyles.Bold, 0.6f);
                lbl.rectTransform.anchorMin = new Vector2(i / 3f, 0f);
                lbl.rectTransform.anchorMax = new Vector2((i + 1) / 3f, 0.24f);
                lbl.rectTransform.offsetMin = Vector2.zero;
                lbl.rectTransform.offsetMax = Vector2.zero;
                _slotLabels[i] = lbl;
            }

            // Reload bar right above crosshair
            var relBar = VanguardUiFactory.CreateFilledBar("ReloadBar", root, VanguardPalette.HudPanelDark, VanguardPalette.AccentAmber, new Vector2(160f, 7f), out _reloadFill);
            relBar.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            relBar.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            relBar.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            relBar.rectTransform.anchoredPosition = new Vector2(0f, -46f);
            _reloadBarRoot = relBar.gameObject;
            _reloadBarRoot.SetActive(false);
        }

        private void BuildRespawnOverlay(Transform root)
        {
            var panel = VanguardUiFactory.CreatePanel("RespawnOverlay", root, VanguardPalette.HudScrim, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, false, false);
            _respawnOverlay = panel.gameObject;

            _respawnReasonText = VanguardUiFactory.CreateLabel("KilledBy", panel.transform, "ELIMINATED", 34f, VanguardPalette.AccentCrimson, TextAlignmentOptions.Center, FontStyles.Bold, 4f);
            _respawnReasonText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _respawnReasonText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _respawnReasonText.rectTransform.anchoredPosition = new Vector2(0f, 34f);
            _respawnReasonText.rectTransform.sizeDelta = new Vector2(720f, 52f);

            _respawnCountdownText = VanguardUiFactory.CreateLabel("RespawnTimer", panel.transform, "REDEPLOYING IN 3.5s", 18f, VanguardPalette.HudLight, TextAlignmentOptions.Center, FontStyles.Bold, 3f);
            _respawnCountdownText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _respawnCountdownText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _respawnCountdownText.rectTransform.anchoredPosition = new Vector2(0f, -18f);
            _respawnCountdownText.rectTransform.sizeDelta = new Vector2(420f, 32f);

            _respawnOverlay.SetActive(false);
        }

        private void BuildScoreboardOverlay(Transform root)
        {
            var card = VanguardUiFactory.CreatePanel("ScoreboardOverlay", root, VanguardPalette.HudPanelDark, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 500f), true, false);
            VanguardUiFactory.AddAccentStripe(card.transform, VanguardPalette.AccentCyan, false, 4f);
            _scoreboardOverlay = card.gameObject;

            var content = VanguardUiFactory.CreateFullStretch("Content", card.transform, 24f, 20f, 24f, 20f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var hdr = VanguardUiFactory.CreateLabel("Header", content, "BLACKSITE-09 // TACTICAL SCOREBOARD  [TAB]", 20f, VanguardPalette.AccentAmber, TextAlignmentOptions.Left, FontStyles.Bold, 3f);
            hdr.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;

            var cols = VanguardUiFactory.CreateLabel("Cols", content, "OPERATOR                         TEAM        K / D       SCORE      PING", 13f, VanguardPalette.HudMuted, TextAlignmentOptions.Left, FontStyles.Bold, 1.5f);
            cols.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            var rows = new GameObject("Rows", typeof(RectTransform));
            rows.transform.SetParent(content, false);
            rows.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var rvlg = rows.AddComponent<VerticalLayoutGroup>();
            rvlg.spacing = 6f;
            rvlg.childControlWidth = true;
            rvlg.childControlHeight = true;
            rvlg.childForceExpandWidth = true;
            rvlg.childForceExpandHeight = false;
            _scoreboardRows = rows.transform;

            _scoreboardOverlay.SetActive(false);
        }

        private void BuildPauseOverlay(Transform root)
        {
            var scrim = VanguardUiFactory.CreatePanel("PauseOverlay", root, VanguardPalette.HudScrim, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, false, true);
            _pauseOverlay = scrim.gameObject;

            var card = VanguardUiFactory.CreatePanel("PauseCard", scrim.transform, VanguardPalette.HudPanelBg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460f, 430f), true, true);
            VanguardUiFactory.AddAccentStripe(card.transform, VanguardPalette.AccentAmber, false, 4f);

            var content = VanguardUiFactory.CreateFullStretch("Content", card.transform, 28f, 24f, 28f, 24f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var title = VanguardUiFactory.CreateLabel("Title", content, "OPERATION PAUSED", 26f, VanguardPalette.HudLight, TextAlignmentOptions.Center, FontStyles.Bold, 3f);
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

            VanguardUiFactory.CreateTacticalSlider("PauseSens", content, "MOUSE SENSITIVITY", 0.2f, 4.0f, VanguardSettings.MouseSensitivity, v => VanguardSettings.MouseSensitivity = v, out _);
            VanguardUiFactory.CreateTacticalSlider("PauseVol", content, "MASTER VOLUME", 0f, 1f, VanguardSettings.MasterVolume, v => VanguardSettings.MasterVolume = v, out _);

            VanguardUiFactory.CreateTacticalButton("ResumeBtn", content, "RESUME OPERATION", VanguardPalette.AccentAmber, () => SetPaused(false), 50f, 16f, true);
            VanguardUiFactory.CreateTacticalButton("LeaveMatchBtn", content, "RETURN TO MAIN MENU", VanguardPalette.AccentCrimson, OnLeaveToMenuClicked, 46f, 15f, false);

            _pauseOverlay.SetActive(false);
        }

        private void BuildMatchEndOverlay(Transform root)
        {
            var scrim = VanguardUiFactory.CreatePanel("MatchEndOverlay", root, VanguardPalette.HudScrim, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, false, true);
            _matchEndOverlay = scrim.gameObject;

            var card = VanguardUiFactory.CreatePanel("EndCard", scrim.transform, VanguardPalette.HudPanelBg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720f, 520f), true, true);
            VanguardUiFactory.AddAccentStripe(card.transform, VanguardPalette.AccentAmber, false, 5f);

            var content = VanguardUiFactory.CreateFullStretch("Content", card.transform, 32f, 24f, 32f, 24f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 12f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            _matchEndTitle = VanguardUiFactory.CreateLabel("Title", content, "OPERATION COMPLETE", 42f, VanguardPalette.AccentAmber, TextAlignmentOptions.Center, FontStyles.Bold, 5f);
            _matchEndTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;

            _matchEndSubtitle = VanguardUiFactory.CreateLabel("Sub", content, "ALPHA TEAM VICTORY", 18f, VanguardPalette.HudLight, TextAlignmentOptions.Center, FontStyles.Bold, 3f);
            _matchEndSubtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var scores = new GameObject("Scores", typeof(RectTransform));
            scores.transform.SetParent(content, false);
            scores.AddComponent<LayoutElement>().flexibleHeight = 1f;
            var svlg = scores.AddComponent<VerticalLayoutGroup>();
            svlg.spacing = 6f;
            svlg.childControlWidth = true;
            svlg.childControlHeight = true;
            svlg.childForceExpandWidth = true;
            svlg.childForceExpandHeight = false;
            _matchEndScores = scores.transform;

            var foot = new GameObject("EndButtons", typeof(RectTransform));
            foot.transform.SetParent(content, false);
            foot.AddComponent<LayoutElement>().preferredHeight = 52f;
            var hlg = foot.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;

            _rematchBtn = VanguardUiFactory.CreateTacticalButton("RematchBtn", foot.transform, "DEPLOY REMATCH", VanguardPalette.AccentAmber, () =>
            {
                VanguardNetServices.Match?.RequestRematch();
                _isMatchEnded = false;
                _matchEndOverlay.SetActive(false);
                SetCursorForGameplay();
            }, 50f, 16f, true);

            VanguardUiFactory.CreateTacticalButton("EndMenuBtn", foot.transform, "RETURN TO MAIN MENU", VanguardPalette.AccentCyan, OnLeaveToMenuClicked, 50f, 16f, false);

            _matchEndOverlay.SetActive(false);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // Pause & Scoreboard hotkeys
            var kb = Keyboard.current;
            if (kb != null && !_isMatchEnded)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                    SetPaused(!_isPaused);

                if (_scoreboardOverlay != null)
                    _scoreboardOverlay.SetActive(kb.tabKey.isPressed && !_isPaused);
            }

            // Smooth animated health/armor bars (never snap!)
            _displayHealth = Mathf.Lerp(_displayHealth, _targetHealth, dt * 10f);
            _displayArmor = Mathf.Lerp(_displayArmor, _targetArmor, dt * 10f);
            if (_healthFill != null) _healthFill.fillAmount = Mathf.Clamp01(_displayHealth / 100f);
            if (_armorFill != null) _armorFill.fillAmount = Mathf.Clamp01(_displayArmor / 50f);
            if (_healthText != null) _healthText.text = Mathf.RoundToInt(_displayHealth).ToString();
            if (_armorText != null) _armorText.text = $"ARM {Mathf.RoundToInt(_displayArmor)}";

            // Dynamic crosshair + stamina + compass + reload bar
            if (_localPlayer != null)
            {
                if (_staminaFill != null) _staminaFill.fillAmount = Mathf.Clamp01(_localPlayer.Stamina / _localPlayer.MaxStamina);
                if (_stanceText != null)
                {
                    _stanceText.text = _localPlayer.IsCrouching ? "CROUCHED" : (_localPlayer.IsSprinting ? "SPRINTING" : (_localPlayer.IsAds ? "OPTIC ADS" : "READY"));
                }

                float spread = _localPlayer.Weapons != null ? _localPlayer.Weapons.CurrentSpreadDegrees : 1.2f;
                float gap = Mathf.Clamp(10f + spread * 6.5f, 10f, 44f);
                if (_crosshairTop != null) _crosshairTop.anchoredPosition = new Vector2(0f, gap);
                if (_crosshairBottom != null) _crosshairBottom.anchoredPosition = new Vector2(0f, -gap);
                if (_crosshairLeft != null) _crosshairLeft.anchoredPosition = new Vector2(-gap, 0f);
                if (_crosshairRight != null) _crosshairRight.anchoredPosition = new Vector2(gap, 0f);

                if (_crosshairGroup != null)
                {
                    float targetAlpha = _localPlayer.IsAds ? 0.15f : 0.92f;
                    _crosshairGroup.alpha = Mathf.Lerp(_crosshairGroup.alpha, targetAlpha, dt * 14f);
                }

                if (_reloadBarRoot != null && _localPlayer.Weapons != null)
                {
                    bool rel = _localPlayer.Weapons.IsReloading;
                    _reloadBarRoot.SetActive(rel);
                    if (rel && _reloadFill != null) _reloadFill.fillAmount = _localPlayer.Weapons.ReloadNormalized;
                }

                UpdateCompass(_localPlayer.transform.eulerAngles.y);
                UpdateRadar();
            }

            // Hitmarker fade
            if (_hitmarkerTimer > 0f)
            {
                _hitmarkerTimer -= dt;
                if (_hitmarkerGroup != null) _hitmarkerGroup.alpha = Mathf.Clamp01(_hitmarkerTimer / 0.16f);
            }

            // Kill banner fade
            if (_killBannerTimer > 0f)
            {
                _killBannerTimer -= dt;
                if (_killBannerTimer <= 0f && _killBannerText != null) _killBannerText.text = "";
            }

            // Damage flash + low-health pulse
            if (_damageVignette != null)
            {
                _damageFlashAlpha = Mathf.MoveTowards(_damageFlashAlpha, 0f, dt * 1.4f);
                float lowHpPulse = (_targetHealth > 0f && _targetHealth <= 30f) ? (0.14f + 0.08f * Mathf.Sin(Time.unscaledTime * 6f)) : 0f;
                _damageVignette.color = VanguardPalette.WithAlpha(VanguardPalette.AccentCrimson, Mathf.Max(_damageFlashAlpha, lowHpPulse));
            }

            // Respawn countdown
            if (_respawnOverlay != null && _respawnOverlay.activeSelf)
            {
                float rem = Mathf.Max(0f, _respawnEndTime - Time.time);
                if (_respawnCountdownText != null) _respawnCountdownText.text = $"REDEPLOYING IN {rem:0.0}s";
            }

            // Expire old kill feed entries
            for (int i = _killFeedRows.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime >= _killFeedRows[i].expireAt)
                {
                    if (_killFeedRows[i].go != null) Destroy(_killFeedRows[i].go);
                    _killFeedRows.RemoveAt(i);
                }
            }
        }

        private void UpdateCompass(float yaw)
        {
            if (_compassText == null) return;
            float norm = (yaw % 360f + 360f) % 360f;
            string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int idx = Mathf.RoundToInt(norm / 45f) % 8;
            _compassText.text = $"{dirs[idx]}  ·  {Mathf.RoundToInt(norm):000}°  ·  BLACKSITE-09";
        }

        private void UpdateRadar()
        {
            if (_localPlayer == null || _radarBlips.Count == 0) return;

            int blipIdx = 0;
            Vector3 myPos = _localPlayer.transform.position;
            float myYaw = _localPlayer.transform.eulerAngles.y * Mathf.Deg2Rad;
            float cos = Mathf.Cos(myYaw);
            float sin = Mathf.Sin(myYaw);
            const float radarRange = 45f;
            const float radarRadiusPx = 68f;

            void PlotUnit(Vector3 worldPos, TeamId unitTeam)
            {
                if (blipIdx >= _radarBlips.Count) return;
                Vector3 d = worldPos - myPos;
                float rx = (d.x * cos - d.z * sin) / radarRange;
                float rz = (d.x * sin + d.z * cos) / radarRange;
                Vector2 p = Vector2.ClampMagnitude(new Vector2(rx, rz), 1f) * radarRadiusPx;

                var rt = _radarBlips[blipIdx++];
                rt.anchoredPosition = p;
                var img = rt.GetComponent<UnityEngine.UI.Image>();
                bool isFriendly = _localPlayer.Authority != null && unitTeam != TeamId.None && unitTeam == _localPlayer.Authority.Team;
                img.color = isFriendly ? VanguardPalette.GetTeamColor((int)unitTeam) : VanguardPalette.AccentCrimson;
                rt.gameObject.SetActive(true);
            }

            foreach (var bot in FindObjectsByType<VanguardZero.AI.TacticalBotController>(FindObjectsSortMode.None))
            {
                var bridge = bot.GetComponent<IBotNetworkBridge>();
                if (bridge != null && bridge.IsAlive) PlotUnit(bot.transform.position, bridge.Team);
            }

            foreach (var p in FindObjectsByType<TacticalFpsController>(FindObjectsSortMode.None))
            {
                if (p == _localPlayer || p.Authority == null || !p.Authority.IsAlive) continue;
                PlotUnit(p.transform.position, p.Authority.Team);
            }

            for (int i = blipIdx; i < _radarBlips.Count; i++)
                _radarBlips[i].gameObject.SetActive(false);
        }

        private void HandleAmmoChanged(WeaponId weapon, int mag, int reserve)
        {
            string fullName = weapon switch
            {
                WeaponId.M890_TacticalShotgun => "M890 TACTICAL SHOTGUN",
                WeaponId.M19_TacticalPistol => "M19 TACTICAL SIDEARM",
                _ => "VX-47 ASSAULT RIFLE"
            };
            if (_weaponNameText != null) _weaponNameText.text = fullName;
            if (_ammoMagText != null)
            {
                _ammoMagText.text = mag.ToString();
                _ammoMagText.color = mag <= 4 ? VanguardPalette.AccentCrimson : VanguardPalette.HudLight;
            }
            if (_ammoReserveText != null) _ammoReserveText.text = $"/ {reserve}";

            for (int i = 0; i < _slotLabels.Length; i++)
            {
                if (_slotLabels[i] != null)
                    _slotLabels[i].color = i == (int)weapon ? VanguardPalette.AccentCyan : VanguardPalette.HudMuted;
            }
        }

        private void HandleHitConfirmed(HitConfirmEvent hit)
        {
            _hitmarkerTimer = hit.IsKill ? 0.28f : 0.16f;
            if (_hitmarkerGroup != null) _hitmarkerGroup.alpha = 1f;
            Color c = hit.IsKill || hit.IsHeadshot ? VanguardPalette.AccentCrimson : VanguardPalette.HudLight;
            if (_hitmarkerBars != null)
            {
                foreach (var bar in _hitmarkerBars)
                    if (bar != null) bar.color = c;
            }

            if (hit.IsKill && _killBannerText != null)
            {
                _killBannerText.text = hit.IsHeadshot
                    ? $"HEADSHOT ELIMINATION  //  {hit.VictimCallsign}"
                    : $"ELIMINATED  //  {hit.VictimCallsign}";
                _killBannerText.color = hit.IsHeadshot ? VanguardPalette.AccentCrimson : VanguardPalette.AccentAmber;
                _killBannerTimer = 2.0f;
            }
        }

        private void HandleVitalsUpdated(int hp, int armor, Vector3 attackerOrigin)
        {
            if (hp < _targetHealth) _damageFlashAlpha = 0.34f;
            _targetHealth = hp;
            _targetArmor = armor;
        }

        private void HandleMatchStateChanged(MatchStateSnapshot state)
        {
            if (_alphaScoreText != null) _alphaScoreText.text = $"ALPHA  {state.AlphaScore}";
            if (_bravoScoreText != null) _bravoScoreText.text = $"{state.BravoScore}  BRAVO";
            if (_timerText != null)
            {
                int total = Mathf.Max(0, Mathf.CeilToInt(state.RemainingSeconds));
                _timerText.text = $"{total / 60:00}:{total % 60:00}";
            }
            if (_joinCodeBadge != null)
            {
                _joinCodeBadge.text = string.IsNullOrEmpty(state.JoinCode)
                    ? $"MODE: {state.ConnectionMode.ToString().ToUpper()}"
                    : $"RELAY CODE: {state.JoinCode}";
            }
        }

        private void HandleScoreboardUpdated(IReadOnlyList<PlayerCombatStats> list)
        {
            PopulateScoreRows(_scoreboardRows, list);
        }

        private void PopulateScoreRows(Transform container, IReadOnlyList<PlayerCombatStats> list)
        {
            if (container == null) return;
            for (int i = container.childCount - 1; i >= 0; i--)
                Destroy(container.GetChild(i).gameObject);

            if (list == null) return;
            foreach (var s in list)
            {
                var row = VanguardUiFactory.CreatePanel($"Score_{s.Callsign}", container, VanguardPalette.HudButtonNormal, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, false, false);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
                VanguardUiFactory.AddAccentStripe(row.transform, VanguardPalette.GetTeamColor((int)s.Team), true, 4f);

                string tag = s.IsBot ? "[BOT] " : "";
                string line = $"{(tag + s.Callsign).PadRight(24)}  {s.Team.ToString().ToUpper().PadRight(8)}  {s.Kills,2} / {s.Deaths,-3}     {s.Score,5}     {s.PingMs,3}ms";
                var lbl = VanguardUiFactory.CreateLabel("RowTxt", row.transform, line, 14f, VanguardPalette.HudLight, TextAlignmentOptions.MidlineLeft, FontStyles.Bold, 0.8f);
                lbl.rectTransform.anchorMin = Vector2.zero;
                lbl.rectTransform.anchorMax = Vector2.one;
                lbl.rectTransform.offsetMin = new Vector2(14f, 0f);
                lbl.rectTransform.offsetMax = new Vector2(-10f, 0f);
            }
        }

        private void HandleKillFeedEntry(KillFeedEntry entry)
        {
            if (_killFeedContainer == null) return;
            while (_killFeedRows.Count >= 5)
            {
                if (_killFeedRows[0].go != null) Destroy(_killFeedRows[0].go);
                _killFeedRows.RemoveAt(0);
            }

            var panel = VanguardUiFactory.CreatePanel("KF_Row", _killFeedContainer, VanguardPalette.HudPanelDark, Vector2.zero, Vector2.one, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(360f, 30f), true, false);
            var le = panel.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 30f;
            le.preferredWidth = 370f;
            VanguardUiFactory.AddAccentStripe(panel.transform, VanguardPalette.GetTeamColor((int)entry.KillerTeam), true, 3f);

            string weap = VanguardNetServices.GetWeaponSpec(entry.Weapon).DisplayName;
            string hs = entry.IsHeadshot ? " [HS]" : "";
            var txt = VanguardUiFactory.CreateLabel("Txt", panel.transform, $"{entry.KillerName}  [{weap}{hs}]  {entry.VictimName}", 13f, VanguardPalette.HudLight, TextAlignmentOptions.MidlineRight, FontStyles.Bold, 1f);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.offsetMin = new Vector2(10f, 0f);
            txt.rectTransform.offsetMax = new Vector2(-10f, 0f);

            _killFeedRows.Add((panel.gameObject, Time.unscaledTime + 6.5f));
        }

        private void HandleLocalEliminated(string killer, WeaponId weapon, bool headshot, float respawnDelay)
        {
            if (_respawnOverlay == null) return;
            string weapName = VanguardNetServices.GetWeaponSpec(weapon).DisplayName;
            _respawnReasonText.text = headshot
                ? $"HEADSHOT BY {killer.ToUpper()}  [{weapName}]"
                : $"ELIMINATED BY {killer.ToUpper()}  [{weapName}]";
            _respawnEndTime = Time.time + respawnDelay;
            _respawnOverlay.SetActive(true);
        }

        private void HandleLocalRespawned(Vector3 pos, Quaternion rot)
        {
            if (_respawnOverlay != null) _respawnOverlay.SetActive(false);
            _targetHealth = 100f;
            _targetArmor = 50f;
        }

        private void HandleMatchEnded(MatchStateSnapshot state, IReadOnlyList<PlayerCombatStats> finalScores)
        {
            _isMatchEnded = true;
            _isPaused = false;
            if (_pauseOverlay != null) _pauseOverlay.SetActive(false);
            if (_respawnOverlay != null) _respawnOverlay.SetActive(false);

            bool localWon = false;
            if (_localPlayer != null && _localPlayer.Authority != null)
            {
                localWon = state.Mode == MatchMode.FreeForAll
                    ? string.Equals(state.WinnerCallsign, _localPlayer.Authority.Callsign, StringComparison.OrdinalIgnoreCase)
                    : state.WinningTeam == _localPlayer.Authority.Team;
            }

            _matchEndTitle.text = localWon ? "VICTORY" : "DEFEAT";
            _matchEndTitle.color = localWon ? VanguardPalette.AccentAmber : VanguardPalette.AccentCrimson;
            _matchEndSubtitle.text = state.Mode == MatchMode.FreeForAll
                ? $"TOP OPERATOR: {state.WinnerCallsign.ToUpper()}"
                : $"TEAM {state.WinningTeam.ToString().ToUpper()} WINS  //  {state.AlphaScore} - {state.BravoScore}";

            PopulateScoreRows(_matchEndScores, finalScores);
            if (_rematchBtn != null)
                _rematchBtn.gameObject.SetActive(VanguardNetServices.Session != null && VanguardNetServices.Session.IsHost);

            VanguardAudioManager.Instance?.PlayMusicStinger(localWon ? "music_match_victory" : "music_match_defeat");
            VanguardAudioManager.Instance?.Play2D(localWon ? "vo_victory" : "vo_defeat", AudioBus.Voice, 0.9f);

            _matchEndOverlay.SetActive(true);
            _localPlayer?.SetMenuOrPauseLock(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            if (_pauseOverlay != null) _pauseOverlay.SetActive(paused);
            _localPlayer?.SetMenuOrPauseLock(paused);
            VanguardAudioManager.Instance?.SetPausedDucking(paused);

            // Freeze timeScale ONLY in true offline solo; never freeze network ticks in online play!
            bool isSolo = VanguardNetServices.Session != null && VanguardNetServices.Session.IsOfflineSolo;
            if (isSolo) Time.timeScale = paused ? 0f : 1f;
        }

        private void SetCursorForGameplay()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _localPlayer?.SetMenuOrPauseLock(false);
        }

        private async void OnLeaveToMenuClicked()
        {
            Time.timeScale = 1f;
            VanguardAudioManager.Instance?.SetPausedDucking(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (VanguardNetServices.Session != null)
                await VanguardNetServices.Session.LeaveSessionAsync("Returned to Main Menu");
        }
    }
}
