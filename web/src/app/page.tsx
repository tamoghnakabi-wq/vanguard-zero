import {
  Apple,
  Cpu,
  Crosshair,
  ExternalLink,
  FileCode2,
  GitBranch,
  Monitor,
  Radio,
  Server,
  ShieldCheck,
  Sparkles,
  Swords,
  Users,
} from "lucide-react";
import { ArsenalShowcase } from "@/components/ArsenalShowcase";
import { DownloadCenter, HeroDownloadCta } from "@/components/DownloadCenter";
import { MapIntel } from "@/components/MapIntel";
import { MediaGallery } from "@/components/MediaGallery";
import { Navbar } from "@/components/Navbar";
import {
  HERO_MEDIA,
  SYSTEM_REQUIREMENTS,
  TECH_PILLARS,
} from "@/data/game-data";
import {
  getLatestRelease,
  RELEASES_URL,
  REPO_URL,
} from "@/lib/github-release";

export default async function HomePage() {
  const release = await getLatestRelease();

  return (
    <div className="min-h-screen flex flex-col bg-[#07090d] text-slate-100">
      <Navbar
        versionTag={release.tagName}
        isLiveRelease={release.status === "live"}
      />

      <main className="flex-1">
        {/* =============================================================== */}
        {/* HERO BANNER                                                     */}
        {/* =============================================================== */}
        <section className="relative overflow-hidden border-b border-slate-800/90 pt-10 pb-20 sm:pt-16 sm:pb-24">
          {/* Background Screenshot Layer */}
          <div className="pointer-events-none absolute inset-0 z-0">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={HERO_MEDIA.backdrop}
              alt={HERO_MEDIA.alt}
              className="h-full w-full object-cover opacity-40"
            />
            <div className="absolute inset-0 bg-gradient-to-r from-[#07090d] via-[#07090d]/85 to-[#07090d]/55" />
            <div className="absolute inset-0 bg-gradient-to-t from-[#07090d] via-transparent to-[#07090d]/70" />
            <div className="absolute inset-0 scanlines opacity-35" />
          </div>

          <div className="relative z-10 mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
            <div className="grid grid-cols-1 items-center gap-12 lg:grid-cols-12">
              {/* Left Hero Copy & Download CTA */}
              <div className="lg:col-span-7">
                {/* Live Relay Status Badge */}
                <div className="mb-5 inline-flex flex-wrap items-center gap-2.5 rounded border border-sky-500/40 bg-slate-950/85 px-3.5 py-1.5 backdrop-blur-md">
                  <span className="flex h-2 w-2 rounded-full bg-emerald-400 shadow-[0_0_10px_#34d399]" />
                  <span className="font-mono text-xs font-semibold tracking-[0.18em] text-sky-300">
                    UNITY RELAY // CROSS-PLATFORM ONLINE
                  </span>
                  <span className="text-slate-600">|</span>
                  <span className="font-mono text-xs text-amber-400">
                    AO: BLACKSITE-09
                  </span>
                </div>

                <h1 className="font-display text-5xl font-bold tracking-[0.08em] text-white sm:text-6xl lg:text-7xl leading-[0.95]">
                  VANGUARD <span className="text-sky-400">ZERO</span>
                </h1>

                <p className="mt-3 font-display text-xl font-semibold tracking-[0.16em] text-amber-400 sm:text-2xl">
                  RAIN-SWEPT TACTICAL WARFARE. ZERO COMPROMISE.
                </p>

                <p className="mt-5 max-w-2xl text-base leading-relaxed text-slate-300 sm:text-lg">
                  Deploy into <strong className="text-white">BLACKSITE-09</strong> — a
                  brutalist dusk military radar &amp; communications compound built in{" "}
                  <strong className="text-sky-300">Unity 6 (6000.3.22f1 URP)</strong>.
                  Experience cross-platform{" "}
                  <strong className="text-white">macOS &amp; Windows</strong> firefights
                  with Unity Relay matchmaking, 6-character join codes, lag-compensated
                  ballistics, MotionBricks neural locomotion, and offline Solo Skirmish
                  vs Tactical AI Bots.
                </p>

                {/* Smart OS-Aware Download Buttons */}
                <div className="mt-8 max-w-2xl">
                  <HeroDownloadCta release={release} />
                </div>

                {/* Telemetry Pill Bar */}
                <div className="mt-10 grid grid-cols-2 gap-3 sm:grid-cols-4">
                  <div className="rounded border border-slate-800/90 bg-slate-950/75 p-3 backdrop-blur">
                    <div className="font-mono text-[10px] tracking-wider text-slate-400">
                      ENGINE
                    </div>
                    <div className="mt-0.5 font-display text-lg font-bold text-white">
                      UNITY 6 URP
                    </div>
                  </div>
                  <div className="rounded border border-slate-800/90 bg-slate-950/75 p-3 backdrop-blur">
                    <div className="font-mono text-[10px] tracking-wider text-slate-400">
                      NETCODE
                    </div>
                    <div className="mt-0.5 font-display text-lg font-bold text-sky-400">
                      60Hz RELAY NGO
                    </div>
                  </div>
                  <div className="rounded border border-slate-800/90 bg-slate-950/75 p-3 backdrop-blur">
                    <div className="font-mono text-[10px] tracking-wider text-slate-400">
                      BALLISTICS
                    </div>
                    <div className="mt-0.5 font-display text-lg font-bold text-amber-400">
                      LAG-COMPENSATION
                    </div>
                  </div>
                  <div className="rounded border border-slate-800/90 bg-slate-950/75 p-3 backdrop-blur">
                    <div className="font-mono text-[10px] tracking-wider text-slate-400">
                      PLATFORMS
                    </div>
                    <div className="mt-0.5 font-display text-lg font-bold text-emerald-400">
                      macOS &amp; WIN64
                    </div>
                  </div>
                </div>
              </div>

              {/* Right Hero Tactical Preview Frame */}
              <div className="lg:col-span-5">
                <div className="intel-panel hud-frame overflow-hidden rounded-lg p-4 shadow-[0_0_50px_rgba(2,132,199,0.14)]">
                  <div className="mb-3 flex items-center justify-between border-b border-slate-800 pb-2.5 font-mono text-xs">
                    <span className="flex items-center gap-2 text-sky-400">
                      <Radio className="h-3.5 w-3.5 animate-pulse-slow" />
                      <span>LIVE TELEMETRY // BLACKSITE-09</span>
                    </span>
                    <span className="text-slate-400">{release.tagName}</span>
                  </div>

                  <div className="relative aspect-video overflow-hidden rounded border border-slate-800 bg-slate-950">
                    {/* eslint-disable-next-line @next/next/no-img-element */}
                    <img
                      src={HERO_MEDIA.backdrop}
                      alt="BLACKSITE-09 Live Sector Feed"
                      className="h-full w-full object-cover"
                    />
                    <div className="absolute bottom-3 left-3 right-3 flex items-center justify-between rounded border border-slate-800/90 bg-slate-950/85 px-3 py-1.5 font-mono text-[11px] backdrop-blur">
                      <span className="text-slate-200">
                        SECTOR A1 // DUSK RAINFALL
                      </span>
                      <span className="text-sky-400">HDR ACES + SSAO</span>
                    </div>
                  </div>

                  <div className="mt-4 grid grid-cols-3 gap-2.5 font-mono text-xs">
                    <div className="rounded border border-slate-800/80 bg-slate-950/70 p-2.5">
                      <div className="text-[10px] text-slate-400">MATCHMAKING</div>
                      <div className="mt-0.5 font-semibold text-white">
                        Quick Play + Codes
                      </div>
                    </div>
                    <div className="rounded border border-slate-800/80 bg-slate-950/70 p-2.5">
                      <div className="text-[10px] text-slate-400">OFFLINE MODE</div>
                      <div className="mt-0.5 font-semibold text-white">
                        Tactical AI Bots
                      </div>
                    </div>
                    <div className="rounded border border-slate-800/80 bg-slate-950/70 p-2.5">
                      <div className="text-[10px] text-slate-400">AUDIO SUITE</div>
                      <div className="mt-0.5 font-semibold text-white">
                        48kHz Spatial DSP
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </section>

        {/* =============================================================== */}
        {/* LIVE GITHUB RELEASES DOWNLOAD CENTER                            */}
        {/* =============================================================== */}
        <DownloadCenter release={release} />

        {/* =============================================================== */}
        {/* MULTIPLAYER & TECHNICAL ARCHITECTURE                            */}
        {/* =============================================================== */}
        <section
          id="architecture"
          className="relative border-b border-slate-800/80 py-20 sm:py-24"
        >
          <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
            <div className="mb-12 flex flex-col justify-between gap-6 lg:flex-row lg:items-end">
              <div>
                <div className="mb-2 inline-flex items-center gap-2 rounded border border-sky-500/30 bg-sky-500/10 px-3 py-1 font-mono text-xs tracking-[0.18em] text-sky-400">
                  <Server className="h-3.5 w-3.5" />
                  <span>SYSTEMS ENGINEERING // UNITY 6 &amp; MULTIPLAYER SERVICES</span>
                </div>
                <h2 className="font-display text-3xl font-bold tracking-[0.1em] text-white sm:text-4xl">
                  MULTIPLAYER &amp; TECHNICAL ARCHITECTURE
                </h2>
              </div>
              <p className="max-w-xl text-sm text-slate-400">
                Built from the ground up on Unity 6000.3.22f1 URP and Netcode for
                GameObjects with authoritative lag-compensated hit validation and
                seamless offline bot parity.
              </p>
            </div>

            <div className="grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
              {TECH_PILLARS.map((pillar) => (
                <div
                  key={pillar.id}
                  className="intel-panel hud-frame flex flex-col justify-between rounded-lg p-6 transition-colors hover:border-sky-500/40"
                >
                  <div>
                    <div className="mb-3 inline-flex items-center gap-2 rounded border border-slate-700/80 bg-slate-900/90 px-2.5 py-1 font-mono text-[11px] text-sky-400">
                      <Cpu className="h-3.5 w-3.5" />
                      <span>{pillar.badge}</span>
                    </div>
                    <h3 className="font-display text-2xl font-bold tracking-[0.05em] text-white">
                      {pillar.title}
                    </h3>
                    <p className="mt-2.5 text-sm leading-relaxed text-slate-300">
                      {pillar.summary}
                    </p>
                  </div>

                  <div className="mt-6 flex flex-wrap gap-2 border-t border-slate-800/80 pt-4">
                    {pillar.specs.map((spec) => (
                      <span
                        key={spec}
                        className="rounded border border-slate-800 bg-slate-950/80 px-2.5 py-1 font-mono text-[11px] text-slate-300"
                      >
                        {spec}
                      </span>
                    ))}
                  </div>
                </div>
              ))}
            </div>

            {/* Multiplayer Modes Banner */}
            <div className="mt-10 intel-panel rounded-lg p-6">
              <div className="grid grid-cols-1 gap-6 md:grid-cols-4">
                <div className="flex items-start gap-3.5">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded border border-sky-500/30 bg-sky-500/10 text-sky-400">
                    <Sparkles className="h-5 w-5" />
                  </div>
                  <div>
                    <div className="font-display text-lg font-bold text-white">
                      QUICK PLAY RELAY
                    </div>
                    <p className="mt-1 text-xs leading-relaxed text-slate-400">
                      Automatic session query + instant host creation fallback via{" "}
                      <code className="text-sky-300">MatchmakeSessionAsync</code>.
                    </p>
                  </div>
                </div>

                <div className="flex items-start gap-3.5">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded border border-amber-500/30 bg-amber-500/10 text-amber-400">
                    <Users className="h-5 w-5" />
                  </div>
                  <div>
                    <div className="font-display text-lg font-bold text-white">
                      JOIN BY 6-CHAR CODE
                    </div>
                    <p className="mt-1 text-xs leading-relaxed text-slate-400">
                      Host private or public lobbies and share a 6-character Relay code
                      across Windows and macOS.
                    </p>
                  </div>
                </div>

                <div className="flex items-start gap-3.5">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded border border-emerald-500/30 bg-emerald-500/10 text-emerald-400">
                    <ShieldCheck className="h-5 w-5" />
                  </div>
                  <div>
                    <div className="font-display text-lg font-bold text-white">
                      HITBOX REWIND BUFFER
                    </div>
                    <p className="mt-1 text-xs leading-relaxed text-slate-400">
                      Host maintains a rolling timestamped pose ring-buffer to validate
                      shots at the exact client fire timestamp.
                    </p>
                  </div>
                </div>

                <div className="flex items-start gap-3.5">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded border border-sky-500/30 bg-sky-500/10 text-sky-400">
                    <Swords className="h-5 w-5" />
                  </div>
                  <div>
                    <div className="font-display text-lg font-bold text-white">
                      SOLO vs TACTICAL AI
                    </div>
                    <p className="mt-1 text-xs leading-relaxed text-slate-400">
                      Play offline anytime against NavMesh tactical bots with identical
                      weapons, armor, and scoring rules.
                    </p>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </section>

        {/* =============================================================== */}
        {/* INTERACTIVE ARSENAL SHOWCASE                                    */}
        {/* =============================================================== */}
        <ArsenalShowcase />

        {/* =============================================================== */}
        {/* MAP INTEL — BLACKSITE-09                                        */}
        {/* =============================================================== */}
        <MapIntel />

        {/* =============================================================== */}
        {/* SCREENSHOTS & MEDIA GALLERY                                     */}
        {/* =============================================================== */}
        <MediaGallery />

        {/* =============================================================== */}
        {/* SYSTEM REQUIREMENTS & PATCH NOTES                               */}
        {/* =============================================================== */}
        <section id="specs" className="relative py-20 sm:py-24">
          <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
            <div className="mb-12">
              <div className="mb-2 inline-flex items-center gap-2 rounded border border-sky-500/30 bg-sky-500/10 px-3 py-1 font-mono text-xs tracking-[0.18em] text-sky-400">
                <FileCode2 className="h-3.5 w-3.5" />
                <span>HARDWARE MATRIX &amp; RELEASE CHANGELOG</span>
              </div>
              <h2 className="font-display text-3xl font-bold tracking-[0.1em] text-white sm:text-4xl">
                SYSTEM REQUIREMENTS &amp; PATCH NOTES
              </h2>
            </div>

            <div className="grid grid-cols-1 gap-8 lg:grid-cols-12">
              {/* System Requirements Matrix */}
              <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:col-span-7">
                {/* macOS Specs */}
                <div className="intel-panel hud-frame rounded-lg p-6">
                  <div className="mb-4 flex items-center gap-2.5 border-b border-slate-800 pb-3">
                    <Apple className="h-5 w-5 text-sky-400" />
                    <h3 className="font-display text-xl font-bold tracking-wider text-white">
                      {SYSTEM_REQUIREMENTS.macos.platform}
                    </h3>
                  </div>

                  <div className="space-y-4">
                    <div>
                      <div className="mb-2 font-mono text-xs font-semibold text-sky-400">
                        MINIMUM SPECIFICATIONS
                      </div>
                      <dl className="space-y-1.5 text-xs">
                        {SYSTEM_REQUIREMENTS.macos.minimum.map((row) => (
                          <div
                            key={row.label}
                            className="flex justify-between gap-2 border-b border-slate-800/60 pb-1"
                          >
                            <dt className="font-mono text-slate-400">
                              {row.label}
                            </dt>
                            <dd className="text-right text-slate-200">
                              {row.value}
                            </dd>
                          </div>
                        ))}
                      </dl>
                    </div>

                    <div className="pt-2">
                      <div className="mb-2 font-mono text-xs font-semibold text-emerald-400">
                        RECOMMENDED (60+ FPS HIGH URP)
                      </div>
                      <dl className="space-y-1.5 text-xs">
                        {SYSTEM_REQUIREMENTS.macos.recommended.map((row) => (
                          <div
                            key={row.label}
                            className="flex justify-between gap-2 border-b border-slate-800/60 pb-1"
                          >
                            <dt className="font-mono text-slate-400">
                              {row.label}
                            </dt>
                            <dd className="text-right text-slate-200">
                              {row.value}
                            </dd>
                          </div>
                        ))}
                      </dl>
                    </div>
                  </div>
                </div>

                {/* Windows Specs */}
                <div className="intel-panel hud-frame rounded-lg p-6">
                  <div className="mb-4 flex items-center gap-2.5 border-b border-slate-800 pb-3">
                    <Monitor className="h-5 w-5 text-sky-400" />
                    <h3 className="font-display text-xl font-bold tracking-wider text-white">
                      {SYSTEM_REQUIREMENTS.windows.platform}
                    </h3>
                  </div>

                  <div className="space-y-4">
                    <div>
                      <div className="mb-2 font-mono text-xs font-semibold text-sky-400">
                        MINIMUM SPECIFICATIONS
                      </div>
                      <dl className="space-y-1.5 text-xs">
                        {SYSTEM_REQUIREMENTS.windows.minimum.map((row) => (
                          <div
                            key={row.label}
                            className="flex justify-between gap-2 border-b border-slate-800/60 pb-1"
                          >
                            <dt className="font-mono text-slate-400">
                              {row.label}
                            </dt>
                            <dd className="text-right text-slate-200">
                              {row.value}
                            </dd>
                          </div>
                        ))}
                      </dl>
                    </div>

                    <div className="pt-2">
                      <div className="mb-2 font-mono text-xs font-semibold text-emerald-400">
                        RECOMMENDED (60+ FPS HIGH URP)
                      </div>
                      <dl className="space-y-1.5 text-xs">
                        {SYSTEM_REQUIREMENTS.windows.recommended.map((row) => (
                          <div
                            key={row.label}
                            className="flex justify-between gap-2 border-b border-slate-800/60 pb-1"
                          >
                            <dt className="font-mono text-slate-400">
                              {row.label}
                            </dt>
                            <dd className="text-right text-slate-200">
                              {row.value}
                            </dd>
                          </div>
                        ))}
                      </dl>
                    </div>
                  </div>
                </div>
              </div>

              {/* Release Patch Notes & GitHub Repository Card */}
              <div className="intel-panel hud-frame flex flex-col justify-between rounded-lg p-6 lg:col-span-5">
                <div>
                  <div className="mb-4 flex items-center justify-between border-b border-slate-800 pb-3">
                    <div className="flex items-center gap-2 font-mono text-xs text-amber-400">
                      <GitBranch className="h-4 w-4" />
                      <span>PATCH NOTES // {release.tagName}</span>
                    </div>
                    <span className="font-mono text-xs text-slate-400">
                      {release.publishedFormatted}
                    </span>
                  </div>

                  <h3 className="font-display text-2xl font-bold text-white">
                    {release.name}
                  </h3>

                  <div className="mt-4 max-h-72 overflow-y-auto rounded border border-slate-800/90 bg-slate-950/80 p-4 font-mono text-xs leading-relaxed text-slate-300 whitespace-pre-wrap">
                    {release.body}
                  </div>
                </div>

                <div className="mt-6 flex flex-col gap-2.5 border-t border-slate-800 pt-4 sm:flex-row">
                  <a
                    href={release.htmlUrl || RELEASES_URL}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="flex flex-1 items-center justify-center gap-2 rounded bg-sky-500 px-4 py-2.5 font-display text-sm font-bold tracking-wider text-slate-950 hover:bg-sky-400"
                  >
                    <span>VIEW GITHUB RELEASE</span>
                    <ExternalLink className="h-4 w-4" />
                  </a>
                  <a
                    href={REPO_URL}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="flex flex-1 items-center justify-center gap-2 rounded border border-slate-700 bg-slate-900 px-4 py-2.5 font-display text-sm font-bold tracking-wider text-slate-200 hover:border-sky-400 hover:text-white"
                  >
                    <span>SOURCE REPOSITORY</span>
                    <ExternalLink className="h-4 w-4" />
                  </a>
                </div>
              </div>
            </div>
          </div>
        </section>
      </main>

      {/* =============================================================== */}
      {/* FOOTER                                                          */}
      {/* =============================================================== */}
      <footer className="border-t border-slate-800/90 bg-[#05070b] py-12">
        <div className="mx-auto flex max-w-7xl flex-col items-center justify-between gap-6 px-4 sm:flex-row sm:px-6 lg:px-8">
          <div className="flex items-center gap-3">
            <div className="flex h-8 w-8 items-center justify-center rounded border border-sky-500/40 bg-sky-500/10 text-sky-400">
              <Crosshair className="h-4 w-4" />
            </div>
            <div>
              <div className="font-display text-lg font-bold tracking-[0.18em] text-white">
                VANGUARD <span className="text-sky-400">ZERO</span>
              </div>
              <div className="font-mono text-[11px] text-slate-400">
                BUILT WITH UNITY 6000.3.22f1 URP • BLENDER 5.2 • MOTIONBRICKS • UNITY RELAY
              </div>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-6 font-mono text-xs text-slate-400">
            <a
              href="#download"
              className="hover:text-sky-400"
            >
              DOWNLOADS
            </a>
            <a
              href={RELEASES_URL}
              target="_blank"
              rel="noopener noreferrer"
              className="hover:text-sky-400"
            >
              GITHUB RELEASES
            </a>
            <a
              href={`${REPO_URL}/actions`}
              target="_blank"
              rel="noopener noreferrer"
              className="hover:text-sky-400"
            >
              CI/CD PIPELINES
            </a>
            <a
              href={REPO_URL}
              target="_blank"
              rel="noopener noreferrer"
              className="hover:text-sky-400"
            >
              GITHUB REPOSITORY
            </a>
          </div>
        </div>
      </footer>
    </div>
  );
}
