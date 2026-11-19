# VANGUARD ZERO

> **AAA Multiplayer Tactical First-Person Shooter** — built with **Unity 6 (6000.3.22f1 URP)**, **Blender 5.2 PBR Assets**, **MotionBricks Tactical Locomotion**, **Python DSP Audio**, and **Unity Relay** cross-platform multiplayer.

[![Web Platform CI](https://github.com/tamoghnakabi-wq/vanguard-zero/actions/workflows/web-ci.yml/badge.svg)](https://github.com/tamoghnakabi-wq/vanguard-zero/actions/workflows/web-ci.yml)
[![Build & Release](https://github.com/tamoghnakabi-wq/vanguard-zero/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/tamoghnakabi-wq/vanguard-zero/actions/workflows/build-and-release.yml)
[![Latest Release](https://img.shields.io/github/v/release/tamoghnakabi-wq/vanguard-zero?style=flat&color=0ea5e9)](https://github.com/tamoghnakabi-wq/vanguard-zero/releases/latest)

---

## Overview

**VANGUARD ZERO** drops tactical operators into **BLACKSITE-09**, a rain-swept dusk brutalist military radar & communications compound. Designed for crisp, high-lethality tactical combat across **macOS** and **Windows**, VANGUARD ZERO pairs Unity 6 URP High-Fidelity lighting (SSAO, HDR, ACES Filmic tonemapping, dynamic rain & reflection probes) with host-authoritative **Unity Relay** multiplayer and server-side **lag-compensated hit registration**.

### Core Capabilities

- **Cross-Platform Online Multiplayer (Unity Relay + NGO)**
  - Zero port-forwarding required via Unity Relay (`WithRelayNetwork()`).
  - **Quick Play** matchmaking, **Host Public / Private Lobby** with shareable Join Codes, **Open Lobby Browser**, and **Team Alpha vs Team Bravo** or **Free-For-All** modes.
  - **Lag-Compensated Hit Validation** with host position-history rewind for responsive hit registration under real-world latency.
  - **Solo Skirmish vs Tactical AI Bots** & offline fallback using the exact same combat systems.
- **Tactical Arsenal**
  - **VX-47 Assault Rifle** — 5.56mm modular carbine with holographic optic, controllable recoil, and mid-range dominance.
  - **M890 Breacher Shotgun** — 12-gauge pump-action close-quarters devastator with tight tactical buckshot spread.
  - **M19 Tactical Sidearm** — 9mm rapid-draw pistol with tritium sights and lightning swap speed.
- **Map — BLACKSITE-09**
  - Multi-tier tactical arena featuring the **Central Courtyard**, **Radar Spire**, **Server Bunker**, and **Elevated Catwalks** with baked NavMesh lanes for tactical AI bots.
- **Custom Asset, Motion & Audio Pipelines**
  - Hard-surface 2K PBR weapon, operator, and prop models authored in Blender 5.2 LTS.
  - Modular tactical locomotion & upper-body aim/IK motion pipeline inspired by `NVlabs/motionbricks`.
  - 48kHz procedural DSP ballistic, foley, rain ambience, and dark cinematic score suite.

---

## Download & Play

Visit the live **[VANGUARD ZERO Launch Portal](https://vanguard-zero.vercel.app)** or grab the latest standalone builds for **macOS** and **Windows** directly from **[GitHub Releases](https://github.com/tamoghnakabi-wq/vanguard-zero/releases/latest)**.

### macOS (Apple Silicon / Universal)
1. Download `VanguardZero-<version>-macOS-Universal.dmg` (or `.zip`) from [GitHub Releases](https://github.com/tamoghnakabi-wq/vanguard-zero/releases/latest).
2. Open the `.dmg` and drag **VanguardZero.app** into `/Applications`.
3. **First-Launch Gatekeeper Note (Unsigned Indie Build):**
   - Right-click `VanguardZero.app` in Applications and choose **Open** → **Open**, **or** run:
     ```bash
     xattr -cr /Applications/VanguardZero.app
     open /Applications/VanguardZero.app
     ```

### Windows (64-bit)
1. Download `VanguardZero-<version>-Windows-x64.zip` (portable + PowerShell installer) or `VanguardZero-<version>-Windows-x64-Setup.exe` from [GitHub Releases](https://github.com/tamoghnakabi-wq/vanguard-zero/releases/latest).
2. If using the portable ZIP, extract the entire folder and either run `VanguardZero.exe` directly or right-click `Install-VanguardZero.ps1` → **Run with PowerShell** (installs to `%LOCALAPPDATA%\VanguardZero`, clears the download Zone.Identifier, and creates Start Menu + Desktop shortcuts).
3. **First-Launch SmartScreen Note (Unsigned Indie Build):**
   - If Microsoft Defender SmartScreen prompts on first launch, click **More info** → **Run anyway**.

### Verify Download Integrity
Every release includes `SHA256SUMS.txt`. Verify your download with:
```bash
# macOS / Linux
shasum -a 256 -c SHA256SUMS.txt --ignore-missing

# Windows (PowerShell)
Get-FileHash .\VanguardZero-v1.0.0-Windows-x64.zip -Algorithm SHA256
```

---

## Repository Structure

```text
vanguard-zero/
├── unity-game/          # Unity 6 (6000.3.22f1 URP) multiplayer FPS project
├── blender-assets/      # Blender 5.2 source .blend scenes, FBX exports, 2K PBR maps & studio renders
├── audio-pipeline/      # Python DSP audio synthesis pipeline, 48kHz WAV exports & manifest
├── motion-pipeline/     # Modular MotionBricks tactical locomotion/animation assets & manifest
├── docs/                # Multiplayer architecture, replication budgets & design records
├── web/                 # Next.js App Router tactical showcase & live GitHub Releases portal (Vercel)
└── .github/
    ├── packaging/       # Windows PowerShell installer & NSIS Setup script
    └── workflows/       # Automated Unity release pipeline & Web CI
```

---

## Local Development

### Unity Project (`unity-game/`)
- **Unity Editor:** `6000.3.22f1` (Universal Render Pipeline 3D) with `WindowsStandaloneSupport` and `MacStandaloneSupport` modules.
- Open `unity-game/` in Unity Hub or via Unity CLI.
- Multiplayer services use `com.unity.services.multiplayer`, `com.unity.services.authentication`, and `com.unity.netcode.gameobjects`.

### Web Platform (`web/`)
```bash
cd web
npm install
npm run dev      # Local dev server at http://localhost:3000
npm run lint     # ESLint check
npx tsc --noEmit # TypeScript check
npm run build    # Production Next.js build
```

---

## CI/CD & Automated GitHub Releases

### 1. Unity Cross-Platform Release Pipeline (`.github/workflows/build-and-release.yml`)
Triggered automatically on version tags (`git tag v1.0.0 && git push origin v1.0.0`) or manually via **Actions → Build & Release VANGUARD ZERO → Run workflow**.

Pipeline stages:
1. **`build`**: Runs `game-ci/unity-builder@v4` (Unity `6000.3.22f1`) across a matrix for `StandaloneWindows64` and `StandaloneOSX` with `Library/` caching.
2. **`package-windows`**: Bundles the portable `VanguardZero-<tag>-Windows-x64.zip` (with `Install-VanguardZero.ps1` + `README-WINDOWS.txt`) and compiles `VanguardZero-<tag>-Windows-x64-Setup.exe` via NSIS.
3. **`package-macos`**: Packages `VanguardZero-<tag>-macOS-Universal.dmg` (with `/Applications` symlink via `hdiutil`) and `VanguardZero-<tag>-macOS-Universal.zip` (via `ditto`) on a macOS runner.
4. **`release`**: Combines `SHA256SUMS.txt` and publishes the GitHub Release with structured platform tables and security launch instructions.

#### Optional Repository Secrets for Cloud Unity CI
To enable automated cloud Unity builds in GitHub Actions (**Settings → Secrets and variables → Actions**), add either:

- **Unity Personal License:**
  - `UNITY_LICENSE` — full XML contents of your activated `Unity_lic.ulf` license file (see [GameCI Activation Docs](https://game.ci/docs/github/activation)).
- **Unity Pro / Plus / Enterprise License:**
  - `UNITY_EMAIL` — Unity account email
  - `UNITY_PASSWORD` — Unity account password
  - `UNITY_SERIAL` — Unity license serial key

*(Local release builds can also be packaged and published directly via `gh release create` without cloud secrets.)*

### 2. Web Platform CI (`.github/workflows/web-ci.yml`)
Runs on every push and pull request modifying `web/`: installs dependencies with `npm ci`, runs `npm run lint`, `npx tsc --noEmit`, and `npm run build`.
