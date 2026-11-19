export interface WeaponSpec {
  id: "vx47" | "m890" | "m19";
  code: string;
  name: string;
  category: string;
  caliber: string;
  role: string;
  description: string;
  image: string;
  telemetry: {
    rpm: string;
    magazine: string;
    velocity: string;
    reload: string;
  };
  stats: {
    damage: number;
    fireRate: number;
    range: number;
    control: number;
    mobility: number;
  };
  attachments: string[];
}

export interface MapCallout {
  id: string;
  code: string;
  name: string;
  elevation: string;
  engagement: string;
  summary: string;
  tacticalNote: string;
  coords: { x: number; y: number };
}

export interface ScreenshotItem {
  id: string;
  title: string;
  sector: string;
  resolution: string;
  caption: string;
  src: string;
  tag: string;
}

export interface TechPillar {
  id: string;
  badge: string;
  title: string;
  summary: string;
  specs: string[];
}

export const HERO_MEDIA = {
  backdrop: "/images/screenshots/hero-blacksite.svg",
  alt: "VANGUARD ZERO — BLACKSITE-09 dusk tactical radar compound",
};

export const WEAPONS: WeaponSpec[] = [
  {
    id: "vx47",
    code: "PRIMARY // AR-01",
    name: "VX-47 Assault Rifle",
    category: "Modular Tactical Carbine",
    caliber: "5.56×45mm NATO",
    role: "All-Range Precision & Sustained Suppression",
    description:
      "Full-length quad-rail carbine equipped with an illuminated holographic optic, angled foregrip, and muzzle brake. Tuned for controlled 3-round bursts across the Central Courtyard and decisive mid-range engagements around the Radar Spire.",
    image: "/images/weapons/vx47.png",
    telemetry: {
      rpm: "720 RPM",
      magazine: "30 + 1",
      velocity: "890 m/s",
      reload: "2.1 s",
    },
    stats: {
      damage: 74,
      fireRate: 78,
      range: 84,
      control: 80,
      mobility: 72,
    },
    attachments: [
      "Holographic Red-Dot Sight",
      "Picatinny Quad-Rail Handguard",
      "Angled Tactical Foregrip",
      "Skeletal Collapsible Stock",
    ],
  },
  {
    id: "m890",
    code: "PRIMARY // SG-02",
    name: "M890 Breacher Shotgun",
    category: "Pump-Action Combat Shotgun",
    caliber: "12-Gauge 00 Buck",
    role: "Close-Quarters Breach & Bunker Clearing",
    description:
      "Reinforced heavy-barrel breacher shotgun with vented thermal shroud, ghost-ring sights, and side-saddle reserve shells. Devastating one-shot stopping power inside the Server Bunker and tight catwalk stairwells.",
    image: "/images/weapons/m890.png",
    telemetry: {
      rpm: "75 RPM",
      magazine: "8 Tubular",
      velocity: "410 m/s",
      reload: "0.55 s / shell",
    },
    stats: {
      damage: 96,
      fireRate: 34,
      range: 36,
      control: 62,
      mobility: 68,
    },
    attachments: [
      "Vented Steel Barrel Shroud",
      "Ghost-Ring Aperture Sights",
      "6-Round Side Saddle Carrier",
      "Textured Breacher Forend Pump",
    ],
  },
  {
    id: "m19",
    code: "SIDEARM // HG-03",
    name: "M19 Tactical Sidearm",
    category: "Striker-Fired Combat Pistol",
    caliber: "9×19mm Parabellum",
    role: "Rapid Draw Backup & High-Mobility Flanks",
    description:
      "Machined slide sidearm featuring glowing tritium iron sights, underbarrel laser module, and threaded barrel. Near-instant draw speed makes the M19 the critical finisher when your primary runs dry mid-gunfight.",
    image: "/images/weapons/m19.png",
    telemetry: {
      rpm: "420 RPM",
      magazine: "15 + 1",
      velocity: "385 m/s",
      reload: "1.4 s",
    },
    stats: {
      damage: 54,
      fireRate: 68,
      range: 48,
      control: 88,
      mobility: 95,
    },
    attachments: [
      "Glow Tritium Iron Sights",
      "Underbarrel Laser Module",
      "Machined Slide Serrations",
      "Threaded Match Barrel",
    ],
  },
];

export const MAP_CALLOUTS: MapCallout[] = [
  {
    id: "courtyard",
    code: "SECTOR A1",
    name: "Central Courtyard",
    elevation: "Ground Level (+0.0m)",
    engagement: "Mid-Range Crossfire",
    summary:
      "Rain-slick concrete killing ground segmented by military shipping containers, jersey barriers, and dual-LED floodlight stanchions.",
    tacticalNote:
      "Control container flanking lanes while watching overhead catwalk windows — sprint-sliding between barriers breaks sniper sightlines.",
    coords: { x: 50, y: 54 },
  },
  {
    id: "radar",
    code: "SECTOR B2",
    name: "Radar Spire",
    elevation: "Mid-High Deck (+4.2m)",
    engagement: "Long Sightlines",
    summary:
      "Hero lattice communications array anchoring the northern compound perimeter. Offers commanding vision across both spawn approaches.",
    tacticalNote:
      "High-reward vantage point for VX-47 bursts, balanced by exposed flanks from the eastern steel stairs and catwalk bridge.",
    coords: { x: 50, y: 24 },
  },
  {
    id: "bunker",
    code: "SECTOR C3",
    name: "Server Bunker",
    elevation: "Interior Vault (-0.5m)",
    engagement: "Tight CQB",
    summary:
      "Reinforced brutalist interior housing humming telemetry racks bathed in cool cyan indicator glow and emergency amber strobes.",
    tacticalNote:
      "Prime M890 Breacher territory. Tight choke doors reward disciplined pre-aiming, sound discipline on wet concrete, and fast reloads.",
    coords: { x: 24, y: 58 },
  },
  {
    id: "catwalks",
    code: "SECTOR D4",
    name: "Elevated Catwalks",
    elevation: "Upper Perch (+5.6m)",
    engagement: "Vertical Flanking",
    summary:
      "Interconnected grated steel catwalks ringing the compound perimeter, connecting Alpha and Bravo overwatch positions.",
    tacticalNote:
      "Metallic boot footsteps telegraph full-sprint rotations — crouch-walk for silent drops behind courtyard defenders.",
    coords: { x: 76, y: 42 },
  },
];

export const TECH_PILLARS: TechPillar[] = [
  {
    id: "urp",
    badge: "RENDERING // UNITY 6",
    title: "URP High-Fidelity Visual Pipeline",
    summary:
      "Engineered on Unity 6000.3.22f1 URP with Deferred+/Forward+ lighting, Screen-Space Ambient Occlusion (SSAO), 4K cascaded soft shadows, HDR ACES Filmic tonemapping, and dusk atmospheric reflection probes.",
    specs: ["SSAO + HDR ACES Filmic", "4K Cascaded Shadows", "2K Blender PBR Maps"],
  },
  {
    id: "relay",
    badge: "TRANSPORT // CROSS-PLATFORM",
    title: "Unity Relay P2P Networking",
    summary:
      "Built on Unity Multiplayer Services & Netcode for GameObjects with WithRelayNetwork() — enabling instant cross-platform macOS ↔ Windows matches with zero router port-forwarding.",
    specs: ["60 Hz Authoritative Tick", "NAT-Traversal via Relay", "Graceful LAN / Solo Fallback"],
  },
  {
    id: "matchmaking",
    badge: "SESSIONS // LIVE OPS",
    title: "Quick Play, Custom Lobbies & Join Codes",
    summary:
      "Jump straight into action via Quick Play auto-matchmaking, browse public matches, or host private tactical scrims with shareable 6-character join codes and Team Alpha vs Bravo / FFA rules.",
    specs: ["Quick Play Matchmaking", "6-Char Join Codes", "Alpha vs Bravo & FFA"],
  },
  {
    id: "hitreg",
    badge: "BALLISTICS // AUTHORITATIVE",
    title: "Lag-Compensated Hit Registration",
    summary:
      "Client-side instant muzzle flash, shell ejection, and tracer feedback paired with host-authoritative hitbox rewind buffers — keeping headshots crisp and fair even under real-world latency.",
    specs: ["Position-History Rewind", "Client Fire Responsiveness", "Headshot & Kill-Feed RPCs"],
  },
  {
    id: "motion",
    badge: "ANIMATION // MOTIONBRICKS",
    title: "Neural Tactical Locomotion & Aim-IK",
    summary:
      "Powered by NVIDIA MotionBricks-SOMA-RP-v1 neural motion synthesis retargeted to Humanoid tactical rigs with layered upper-body Aim-IK, procedural weapon sway, and spring recoil.",
    specs: ["200M-Param Motion Synthesis", "Layered Upper-Body Aim-IK", "Tactical Slide & Lean"],
  },
  {
    id: "ai",
    badge: "SOLO & BACKFILL // NAVMESH",
    title: "Tactical AI Bots & Offline Skirmish",
    summary:
      "Baked BLACKSITE-09 NavMesh fuels intelligent AI operators that patrol lanes, acquire sightlines, strafe under fire, and seek cover — delivering instant offline Solo Skirmish or lobby backfill.",
    specs: ["NavMesh Patrol & Cover", "Sightline Burst Combat", "Zero-Wait Offline Mode"],
  },
];

export const SCREENSHOTS: ScreenshotItem[] = [
  {
    id: "courtyard-dusk",
    title: "Central Courtyard — Dusk Rainfall",
    sector: "SECTOR A1",
    resolution: "1920×1080 URP",
    caption:
      "Wet brutalist concrete, dual-LED floodlights, and atmospheric volumetric dusk fog across the BLACKSITE-09 center lane.",
    src: "/images/screenshots/shot-courtyard.svg",
    tag: "ENVIRONMENT",
  },
  {
    id: "vx47-ads",
    title: "VX-47 Holographic Sightline",
    sector: "SECTOR B2",
    resolution: "1920×1080 URP",
    caption:
      "First-person ADS perspective with illuminated reticle glass, tactical HUD compass, and live team scoreboard telemetry.",
    src: "/images/screenshots/shot-combat.svg",
    tag: "GAMEPLAY",
  },
  {
    id: "server-bunker",
    title: "Server Bunker — Telemetry Terminal",
    sector: "SECTOR C3",
    resolution: "1600×900 PBR",
    caption:
      "Hard-surface blacksite server rack with emissive cyan indicator arrays, recessed keyboard bay, and brushed steel framing.",
    src: "/images/screenshots/server-rack-asset.png",
    tag: "INTERIOR ASSET",
  },
  {
    id: "radar-spire",
    title: "Radar Spire — Comms Array Centerpiece",
    sector: "SECTOR B2",
    resolution: "1600×900 PBR",
    caption:
      "Weathered lattice steel tower and rotating parabolic radar dish with glowing beacon anchoring the BLACKSITE-09 courtyard.",
    src: "/images/screenshots/radar-spire-asset.png",
    tag: "HERO PROP",
  },
  {
    id: "tactical-operator",
    title: "Tactical Operator — Team Alpha & Bravo",
    sector: "ARSENAL STUDIO",
    resolution: "1600×900 PBR",
    caption:
      "Armored special-ops operator with quad-NVG FAST helmet, glowing cyan/amber visor faction signifier, and 22-bone Humanoid rig.",
    src: "/images/screenshots/tactical-operator.png",
    tag: "CHARACTER",
  },
];

export const SYSTEM_REQUIREMENTS = {
  windows: {
    platform: "Windows (64-bit)",
    minimum: [
      { label: "OS", value: "Windows 10 64-bit (Version 21H2+)" },
      { label: "Processor", value: "Intel Core i5-9400F / AMD Ryzen 5 2600" },
      { label: "Memory", value: "8 GB RAM" },
      { label: "Graphics", value: "NVIDIA GTX 1060 6GB / AMD RX 580 (DirectX 11/12)" },
      { label: "Storage", value: "2 GB available SSD/HDD space" },
      { label: "Network", value: "Broadband connection for Unity Relay online matches" },
    ],
    recommended: [
      { label: "OS", value: "Windows 11 64-bit" },
      { label: "Processor", value: "Intel Core i7-12700 / AMD Ryzen 7 5800X" },
      { label: "Memory", value: "16 GB RAM" },
      { label: "Graphics", value: "NVIDIA RTX 3060 / AMD RX 6700 XT" },
      { label: "Storage", value: "2 GB NVMe SSD" },
      { label: "Network", value: "Low-latency Ethernet / Wi-Fi 6 for 60Hz Relay sync" },
    ],
  },
  macos: {
    platform: "macOS (Apple Silicon / Universal)",
    minimum: [
      { label: "OS", value: "macOS 13 Ventura or later" },
      { label: "Processor", value: "Apple M1 / Intel Core i5 (Metal 3 capable)" },
      { label: "Memory", value: "8 GB Unified Memory" },
      { label: "Graphics", value: "Apple M1 7-core GPU or Metal-compatible discrete GPU" },
      { label: "Storage", value: "2 GB available SSD space" },
      { label: "Network", value: "Broadband connection for Unity Relay online matches" },
    ],
    recommended: [
      { label: "OS", value: "macOS 14 Sonoma / macOS 15 Sequoia" },
      { label: "Processor", value: "Apple M2 Pro / M3 / M4 Series" },
      { label: "Memory", value: "16 GB Unified Memory" },
      { label: "Graphics", value: "Apple Silicon 10-core+ GPU (Metal High-Fidelity)" },
      { label: "Storage", value: "2 GB Apple Internal SSD" },
      { label: "Network", value: "Low-latency Ethernet / Wi-Fi 6 for 60Hz Relay sync" },
    ],
  },
};
