# VANGUARD ZERO — Audio & DSP Synthesis Pipeline

Production audio asset pipeline for **VANGUARD ZERO** (set in *BLACKSITE-09*, a rain-swept dusk brutalist military radar & comms compound).

## Output Specification
- **Format**: Standard RIFF WAV, `48,000 Hz`, `16-bit signed PCM` (TPDF-dithered from 64-bit float internal DSP)
- **Headroom**: All 62 assets strictly `< -0.5 dBFS` peak, zero NaN/Inf, zero DC offset
- **Spatial Convention**:
  - `Mono (1 ch)` for all 3D spatialized SFX (`spatialBlend: 1.0`)
  - `Stereo (2 ch)` for all 2D UI, Music, Ambience, Voice, and HUD Combat Feedback (`spatialBlend: 0.0`)
- **Seamless Loops**: `ambience_blacksite_rain_loop.wav` (20.0s), `music_main_menu_loop.wav` (40.0s / 16 bars at 96 BPM), and `low_health_heartbeat.wav` (3.0s / 80 BPM) use circular synthesis, circular FFT convolution reverb, and periodically pre-rolled IIR mastering filters for mathematically click-free seams.
- **Unity Import Manifest**: [`manifest.json`](./manifest.json) lists every file with recommended `mixerGroup` (`Weapons`, `Impacts`, `Foley`, `Ambience`, `Music`, `UI`, `Voice`), `spatialBlend`, `loadType` (`DecompressOnLoad` vs `Streaming`), `loop`, and measured loudness/peak telemetry.

## Suite Inventory (62 Production WAVs in `exports/`)
- **Weapons (19 Mono 3D)**: `vx47_fire_01..03`, `vx47_reload_{magout,magin,bolt}`, `m890_fire_01..02`, `m890_pump`, `m890_shell_insert`, `m19_fire_01..02`, `m19_reload`, `weapon_{dryfire,equip,ads_in,ads_out}`, `shell_casing_concrete_01..02`
- **Impacts & Combat Feedback (13)**: `impact_concrete_01..02`, `impact_metal_01..02`, `impact_flesh_01`, `impact_armor_01`, `hitmarker_body`, `hitmarker_headshot`, `kill_confirm`, `player_hurt`, `player_death`, `low_health_heartbeat`, `explosion_frag`
- **Movement & Foley (11 Mono 3D)**: `footstep_concrete_01..04`, `footstep_metal_01..04`, `jump_effort`, `land_heavy`, `slide_tactical`
- **Ambience & Music (4 Stereo 2D)**: `ambience_blacksite_rain_loop`, `music_main_menu_loop`, `music_match_victory`, `music_match_defeat`
- **Tactical UI (5 Stereo 2D)**: `ui_hover`, `ui_click`, `ui_confirm`, `ui_cancel`, `ui_match_found`
- **Military VHF Radio Announcer (10 Stereo 2D)**: `vo_match_start`, `vo_elimination`, `vo_headshot`, `vo_double_kill`, `vo_alpha_leads`, `vo_bravo_leads`, `vo_victory`, `vo_defeat`, `vo_reloading`, `vo_enemy_spotted` *(ElevenLabs MCP unavailable in sandbox; synthesized via macOS `Daniel` en_GB 48kHz PCM + custom Python military VHF radio DSP chain)*

## Regenerate & Verify
```bash
.venv/bin/python build_all.py
.venv/bin/python verify_audio.py
```
