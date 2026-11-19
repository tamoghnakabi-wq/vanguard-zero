"use client";

import { useState, useSyncExternalStore } from "react";
import {
  Apple,
  Check,
  Copy,
  Download,
  ExternalLink,
  HardDrive,
  Monitor,
  ShieldCheck,
  Terminal,
} from "lucide-react";
import type { LatestReleaseData, ReleaseAssetInfo } from "@/lib/release-types";
import { RELEASES_URL } from "@/lib/release-types";

type DetectedOS = "macos" | "windows" | "unknown";

const subscribeNoop = () => () => {};
const getServerOS = (): DetectedOS => "unknown";

function detectClientOS(): DetectedOS {
  if (typeof window === "undefined") return "unknown";
  const ua = window.navigator.userAgent.toLowerCase();
  const platform = (window.navigator.platform || "").toLowerCase();
  if (
    platform.includes("mac") ||
    ua.includes("macintosh") ||
    ua.includes("mac os")
  ) {
    return "macos";
  }
  if (platform.includes("win") || ua.includes("windows")) {
    return "windows";
  }
  return "unknown";
}

function useClientOS(): DetectedOS {
  return useSyncExternalStore(subscribeNoop, detectClientOS, getServerOS);
}

interface HeroDownloadCtaProps {
  release: LatestReleaseData;
}

export function HeroDownloadCta({ release }: HeroDownloadCtaProps) {
  const os = useClientOS();

  const macUrl = release.macPrimary?.downloadUrl ?? release.htmlUrl ?? RELEASES_URL;
  const winUrl = release.winPrimary?.downloadUrl ?? release.htmlUrl ?? RELEASES_URL;
  const macSize = release.macPrimary?.sizeFormatted ?? "Universal / ARM64";
  const winSize = release.winPrimary?.sizeFormatted ?? "64-bit Standalone";

  const macFirst = os !== "windows";

  const macButton = (
    <a
      key="mac"
      href={macUrl}
      className={`group relative flex items-center justify-between gap-4 rounded border px-5 py-3.5 transition-all ${
        macFirst
          ? "border-sky-400 bg-sky-500 text-slate-950 shadow-[0_0_32px_rgba(56,189,248,0.28)] hover:bg-sky-400"
          : "border-slate-700/90 bg-slate-900/85 text-slate-100 hover:border-sky-500/60 hover:bg-slate-900"
      }`}
    >
      <div className="flex items-center gap-3">
        <Apple className="h-5 w-5 shrink-0" />
        <div className="text-left">
          <div className="flex items-center gap-2 font-display text-base font-bold tracking-[0.12em]">
            <span>DOWNLOAD FOR MACOS</span>
            {os === "macos" ? (
              <span className="rounded bg-slate-950/20 px-1.5 py-0.5 font-mono text-[10px] tracking-wider">
                DETECTED
              </span>
            ) : null}
          </div>
          <div
            className={`font-mono text-xs ${
              macFirst ? "text-slate-900/80" : "text-slate-400"
            }`}
          >
            Apple Silicon / Universal • {macSize}
          </div>
        </div>
      </div>
      <Download className="h-4 w-4 shrink-0 transition-transform group-hover:translate-y-0.5" />
    </a>
  );

  const winButton = (
    <a
      key="win"
      href={winUrl}
      className={`group relative flex items-center justify-between gap-4 rounded border px-5 py-3.5 transition-all ${
        !macFirst
          ? "border-sky-400 bg-sky-500 text-slate-950 shadow-[0_0_32px_rgba(56,189,248,0.28)] hover:bg-sky-400"
          : "border-slate-700/90 bg-slate-900/85 text-slate-100 hover:border-sky-500/60 hover:bg-slate-900"
      }`}
    >
      <div className="flex items-center gap-3">
        <Monitor className="h-5 w-5 shrink-0" />
        <div className="text-left">
          <div className="flex items-center gap-2 font-display text-base font-bold tracking-[0.12em]">
            <span>DOWNLOAD FOR WINDOWS</span>
            {os === "windows" ? (
              <span className="rounded bg-slate-950/20 px-1.5 py-0.5 font-mono text-[10px] tracking-wider">
                DETECTED
              </span>
            ) : null}
          </div>
          <div
            className={`font-mono text-xs ${
              !macFirst ? "text-slate-900/80" : "text-slate-400"
            }`}
          >
            Windows 10/11 64-bit • {winSize}
          </div>
        </div>
      </div>
      <Download className="h-4 w-4 shrink-0 transition-transform group-hover:translate-y-0.5" />
    </a>
  );

  return (
    <div className="flex flex-col gap-3">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        {macFirst ? [macButton, winButton] : [winButton, macButton]}
      </div>
      <div className="flex flex-wrap items-center gap-x-5 gap-y-2 font-mono text-xs text-slate-400">
        <span>
          RELEASE:{" "}
          <strong className="text-sky-400">{release.tagName}</strong> (
          {release.publishedFormatted})
        </span>
        <span>•</span>
        <a
          href="#download"
          className="text-slate-300 underline decoration-sky-500/50 underline-offset-4 hover:text-sky-400"
        >
          SHA-256 Checksums & First-Launch Guide
        </a>
        <span>•</span>
        <a
          href={release.htmlUrl || RELEASES_URL}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex items-center gap-1 text-slate-300 hover:text-sky-400"
        >
          <span>All GitHub Release Assets</span>
          <ExternalLink className="h-3 w-3" />
        </a>
      </div>
    </div>
  );
}

interface DownloadCenterProps {
  release: LatestReleaseData;
}

export function DownloadCenter({ release }: DownloadCenterProps) {
  const os = useClientOS();
  const [selectedInstallTab, setSelectedInstallTab] = useState<
    "macos" | "windows" | null
  >(null);
  const [copiedKey, setCopiedKey] = useState<string | null>(null);

  const installTab = selectedInstallTab ?? (os === "windows" ? "windows" : "macos");
  const setInstallTab = (tab: "macos" | "windows") => setSelectedInstallTab(tab);

  const copyText = async (key: string, value: string) => {
    try {
      await navigator.clipboard.writeText(value);
      setCopiedKey(key);
      setTimeout(() => {
        setCopiedKey((prev) => (prev === key ? null : prev));
      }, 2000);
    } catch {
      // Clipboard fallback ignored
    }
  };

  const renderPlatformCard = (
    platformKey: "macos" | "windows",
    title: string,
    subtitle: string,
    primary: ReleaseAssetInfo | null,
    secondary: ReleaseAssetInfo | null
  ) => {
    const isRecommended =
      (os === "macos" && platformKey === "macos") ||
      (os === "windows" && platformKey === "windows");

    const primaryUrl = primary?.downloadUrl ?? release.htmlUrl ?? RELEASES_URL;
    const primaryLabel =
      primary?.packageLabel ??
      (platformKey === "macos"
        ? "macOS Universal (.dmg / .zip)"
        : "Windows 64-bit (.zip / .exe)");

    return (
      <div
        key={platformKey}
        className={`intel-panel hud-frame flex flex-col justify-between rounded-lg p-6 transition-all ${
          isRecommended
            ? "ring-1 ring-sky-400/70 shadow-[0_0_40px_rgba(56,189,248,0.12)]"
            : ""
        }`}
      >
        <div>
          <div className="mb-4 flex items-center justify-between gap-2">
            <div className="flex items-center gap-3">
              <div className="flex h-11 w-11 items-center justify-center rounded border border-sky-500/30 bg-sky-500/10 text-sky-400">
                {platformKey === "macos" ? (
                  <Apple className="h-5 w-5" />
                ) : (
                  <Monitor className="h-5 w-5" />
                )}
              </div>
              <div>
                <h3 className="font-display text-xl font-bold tracking-[0.1em] text-white">
                  {title}
                </h3>
                <p className="font-mono text-xs text-slate-400">{subtitle}</p>
              </div>
            </div>

            {isRecommended ? (
              <span className="rounded border border-sky-400/50 bg-sky-500/15 px-2.5 py-1 font-mono text-[11px] font-semibold tracking-wider text-sky-300">
                RECOMMENDED FOR YOUR OS
              </span>
            ) : (
              <span className="rounded border border-slate-700/80 bg-slate-900/80 px-2.5 py-1 font-mono text-[11px] text-slate-400">
                {release.tagName}
              </span>
            )}
          </div>

          {/* Primary Package Metadata */}
          <div className="mb-5 rounded border border-slate-800/90 bg-slate-950/70 p-4">
            <div className="flex items-center justify-between gap-2 font-mono text-xs text-slate-300">
              <span className="truncate font-semibold text-sky-300">
                {primary?.name ??
                  (platformKey === "macos"
                    ? `VanguardZero-${release.tagName}-macOS-Universal.dmg`
                    : `VanguardZero-${release.tagName}-Windows-x64.zip`)}
              </span>
              <span className="shrink-0 rounded bg-slate-900 px-2 py-0.5 text-slate-300">
                {primary?.sizeFormatted ?? "GitHub Release"}
              </span>
            </div>
            <div className="mt-2 flex flex-wrap items-center justify-between gap-2 font-mono text-[11px] text-slate-400">
              <span>{primaryLabel}</span>
              <span>Unity 6000.3.22f1 URP • Relay Enabled</span>
            </div>

            {primary?.sha256 ? (
              <div className="mt-3 flex items-center justify-between gap-2 border-t border-slate-800/80 pt-2.5">
                <div className="truncate font-mono text-[11px] text-slate-400">
                  <span className="text-slate-500">SHA-256: </span>
                  <span className="text-slate-300">{primary.sha256}</span>
                </div>
                <button
                  type="button"
                  onClick={() => copyText(`sha-${primary.id}`, primary.sha256!)}
                  className="inline-flex shrink-0 items-center gap-1 rounded border border-slate-700 bg-slate-900 px-2 py-1 font-mono text-[10px] text-slate-300 hover:border-sky-500/50 hover:text-sky-300"
                >
                  {copiedKey === `sha-${primary.id}` ? (
                    <>
                      <Check className="h-3 w-3 text-emerald-400" />
                      <span>COPIED</span>
                    </>
                  ) : (
                    <>
                      <Copy className="h-3 w-3" />
                      <span>HASH</span>
                    </>
                  )}
                </button>
              </div>
            ) : null}
          </div>
        </div>

        <div className="flex flex-col gap-2.5">
          <a
            href={primaryUrl}
            className="flex items-center justify-center gap-2.5 rounded bg-sky-500 px-5 py-3.5 font-display text-base font-bold tracking-[0.14em] text-slate-950 transition-all hover:bg-sky-400"
          >
            <Download className="h-4 w-4" />
            <span>
              {platformKey === "macos"
                ? "DOWNLOAD FOR MACOS (APPLE SILICON / UNIVERSAL)"
                : "DOWNLOAD FOR WINDOWS (64-BIT)"}
            </span>
          </a>

          {secondary ? (
            <div className="flex items-center justify-between rounded border border-slate-800 bg-slate-900/60 px-3.5 py-2 font-mono text-xs text-slate-300">
              <div className="truncate">
                <span className="text-slate-400">Alternate: </span>
                <a
                  href={secondary.downloadUrl}
                  className="text-sky-400 underline-offset-4 hover:underline"
                >
                  {secondary.name}
                </a>{" "}
                <span className="text-slate-500">
                  ({secondary.sizeFormatted})
                </span>
              </div>
              {secondary.sha256 ? (
                <button
                  type="button"
                  onClick={() =>
                    copyText(`sha-${secondary.id}`, secondary.sha256!)
                  }
                  className="ml-2 shrink-0 text-[11px] text-slate-400 hover:text-sky-300"
                >
                  {copiedKey === `sha-${secondary.id}` ? "Copied!" : "Copy SHA"}
                </button>
              ) : null}
            </div>
          ) : null}
        </div>
      </div>
    );
  };

  const macCard = renderPlatformCard(
    "macos",
    "macOS Standalone Build",
    "Apple Silicon (M1–M4) & Universal • Metal 3 URP",
    release.macPrimary,
    release.macSecondary
  );

  const winCard = renderPlatformCard(
    "windows",
    "Windows Standalone Build",
    "Windows 10 / 11 (64-bit x86_64) • DirectX 11/12 URP",
    release.winPrimary,
    release.winSecondary
  );

  const macCmd =
    "xattr -cr /Applications/VanguardZero.app && open /Applications/VanguardZero.app";
  const winCmd =
    "Unblock-File .\\VanguardZero-v1.0.0-Windows-x64.zip; Expand-Archive .\\VanguardZero-v1.0.0-Windows-x64.zip -DestinationPath .\\VanguardZero";

  return (
    <section
      id="download"
      className="relative border-b border-slate-800/80 py-20 sm:py-24"
    >
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        {/* Section Header */}
        <div className="mb-12 flex flex-col justify-between gap-6 lg:flex-row lg:items-end">
          <div>
            <div className="mb-2 inline-flex items-center gap-2 rounded border border-sky-500/30 bg-sky-500/10 px-3 py-1 font-mono text-xs tracking-[0.18em] text-sky-400">
              <HardDrive className="h-3.5 w-3.5" />
              <span>GITHUB RELEASES // DIRECT STANDALONE BINARIES</span>
            </div>
            <h2 className="font-display text-3xl font-bold tracking-[0.1em] text-white sm:text-4xl">
              DEPLOYMENT & DOWNLOAD CENTER
            </h2>
          </div>

          <div className="flex flex-wrap items-center gap-3 font-mono text-xs text-slate-300">
            <div className="rounded border border-slate-800 bg-slate-900/90 px-3 py-2">
              <span className="text-slate-500">VERSION: </span>
              <span className="font-semibold text-sky-400">
                {release.tagName}
              </span>
            </div>
            <div className="rounded border border-slate-800 bg-slate-900/90 px-3 py-2">
              <span className="text-slate-500">PUBLISHED: </span>
              <span>{release.publishedFormatted}</span>
            </div>
            {release.checksumAsset ? (
              <a
                href={release.checksumAsset.downloadUrl}
                className="inline-flex items-center gap-1.5 rounded border border-emerald-500/40 bg-emerald-500/10 px-3 py-2 text-emerald-300 hover:bg-emerald-500/20"
              >
                <ShieldCheck className="h-3.5 w-3.5" />
                <span>SHA256SUMS.txt</span>
              </a>
            ) : null}
            <a
              href={release.htmlUrl || RELEASES_URL}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1.5 rounded border border-slate-700 bg-slate-900 px-3 py-2 text-slate-200 hover:border-sky-500/50 hover:text-sky-300"
            >
              <span>GitHub Release Page</span>
              <ExternalLink className="h-3.5 w-3.5" />
            </a>
          </div>
        </div>

        {/* Platform Cards (re-ordered if Windows detected) */}
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          {os === "windows" ? [winCard, macCard] : [macCard, winCard]}
        </div>

        {/* Platform Installation & Security Verification Guide */}
        <div className="mt-10 intel-panel rounded-lg p-6 sm:p-8">
          <div className="flex flex-col justify-between gap-4 border-b border-slate-800 pb-5 sm:flex-row sm:items-center">
            <div>
              <div className="flex items-center gap-2 font-mono text-xs tracking-wider text-amber-400">
                <Terminal className="h-4 w-4" />
                <span>FIRST-LAUNCH INSTALLATION & GATEKEEPER / SMARTSCREEN GUIDE</span>
              </div>
              <h3 className="mt-1 font-display text-2xl font-bold tracking-[0.08em] text-white">
                HOW TO INSTALL & LAUNCH UNSIGNED INDIE BUILDS
              </h3>
            </div>

            <div className="flex rounded border border-slate-800 bg-slate-950 p-1">
              <button
                type="button"
                onClick={() => setInstallTab("macos")}
                className={`flex items-center gap-2 rounded px-3.5 py-1.5 font-display text-sm font-bold tracking-wider transition-colors ${
                  installTab === "macos"
                    ? "bg-sky-500 text-slate-950"
                    : "text-slate-400 hover:text-white"
                }`}
              >
                <Apple className="h-4 w-4" />
                <span>MACOS GUIDE</span>
              </button>
              <button
                type="button"
                onClick={() => setInstallTab("windows")}
                className={`flex items-center gap-2 rounded px-3.5 py-1.5 font-display text-sm font-bold tracking-wider transition-colors ${
                  installTab === "windows"
                    ? "bg-sky-500 text-slate-950"
                    : "text-slate-400 hover:text-white"
                }`}
              >
                <Monitor className="h-4 w-4" />
                <span>WINDOWS GUIDE</span>
              </button>
            </div>
          </div>

          {installTab === "macos" ? (
            <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-3">
              <div className="rounded border border-slate-800/90 bg-slate-950/60 p-4">
                <div className="font-mono text-xs text-sky-400">STEP 01 // MOUNT OR EXTRACT</div>
                <h4 className="mt-1 font-display text-lg font-bold text-white">
                  Copy VanguardZero.app to Applications
                </h4>
                <p className="mt-2 text-sm text-slate-300 leading-relaxed">
                  Double-click{" "}
                  <code className="rounded bg-slate-900 px-1.5 py-0.5 font-mono text-xs text-sky-300">
                    VanguardZero-{release.tagName}-macOS-Universal.dmg
                  </code>{" "}
                  and drag <strong>VanguardZero.app</strong> into your{" "}
                  <code className="rounded bg-slate-900 px-1.5 py-0.5 font-mono text-xs text-slate-200">
                    /Applications
                  </code>{" "}
                  folder (or extract the{" "}
                  <code className="rounded bg-slate-900 px-1.5 py-0.5 font-mono text-xs text-slate-200">
                    .zip
                  </code>{" "}
                  archive).
                </p>
              </div>

              <div className="rounded border border-slate-800/90 bg-slate-950/60 p-4">
                <div className="font-mono text-xs text-amber-400">
                  STEP 02 // CLEAR MACOS GATEKEEPER QUARANTINE
                </div>
                <h4 className="mt-1 font-display text-lg font-bold text-white">
                  Right-Click → Open or Run xattr
                </h4>
                <p className="mt-2 text-sm text-slate-300 leading-relaxed">
                  Because standalone indie builds are not Apple-notarized,{" "}
                  <strong>Control-click (Right-click)</strong>{" "}
                  <code className="rounded bg-slate-900 px-1.5 py-0.5 font-mono text-xs text-slate-200">
                    VanguardZero.app
                  </code>{" "}
                  → click <strong>Open</strong>, or run the one-line Terminal command below:
                </p>
                <div className="mt-3 flex items-center justify-between gap-2 rounded border border-slate-800 bg-slate-900 px-3 py-2">
                  <code className="truncate font-mono text-xs text-sky-300">
                    {macCmd}
                  </code>
                  <button
                    type="button"
                    onClick={() => copyText("mac-cmd", macCmd)}
                    className="shrink-0 rounded border border-slate-700 bg-slate-800 px-2 py-1 font-mono text-[10px] text-slate-200 hover:border-sky-400"
                  >
                    {copiedKey === "mac-cmd" ? "COPIED" : "COPY"}
                  </button>
                </div>
              </div>

              <div className="rounded border border-slate-800/90 bg-slate-950/60 p-4">
                <div className="font-mono text-xs text-emerald-400">
                  STEP 03 // DEPLOY TO BLACKSITE-09
                </div>
                <h4 className="mt-1 font-display text-lg font-bold text-white">
                  Quick Play, Join by Code, or Solo vs Bots
                </h4>
                <p className="mt-2 text-sm text-slate-300 leading-relaxed">
                  Allow local network prompt if asked. From the Main Menu, click{" "}
                  <strong>Quick Match</strong> for Unity Relay cross-platform matchmaking, enter a 6-character{" "}
                  <strong>Join Code</strong>, or launch{" "}
                  <strong>Solo Skirmish</strong> offline vs Tactical AI Bots!
                </p>
              </div>
            </div>
          ) : (
            <div className="mt-6 grid grid-cols-1 gap-6 lg:grid-cols-3">
              <div className="rounded border border-slate-800/90 bg-slate-950/60 p-4">
                <div className="font-mono text-xs text-sky-400">
                  STEP 01 // EXTRACT ARCHIVE OR RUN SETUP
                </div>
                <h4 className="mt-1 font-display text-lg font-bold text-white">
                  Unzip Full Folder Together
                </h4>
                <p className="mt-2 text-sm text-slate-300 leading-relaxed">
                  Extract{" "}
                  <code className="rounded bg-slate-900 px-1.5 py-0.5 font-mono text-xs text-sky-300">
                    VanguardZero-{release.tagName}-Windows-x64.zip
                  </code>{" "}
                  keeping{" "}
                  <code className="rounded bg-slate-900 px-1.5 py-0.5 font-mono text-xs text-slate-200">
                    VanguardZero.exe
                  </code>
                  ,{" "}
                  <code className="rounded bg-slate-900 px-1.5 py-0.5 font-mono text-xs text-slate-200">
                    UnityPlayer.dll
                  </code>
                  , and{" "}
                  <code className="rounded bg-slate-900 px-1.5 py-0.5 font-mono text-xs text-slate-200">
                    VanguardZero_Data\
                  </code>{" "}
                  in the same directory.
                </p>
              </div>

              <div className="rounded border border-slate-800/90 bg-slate-950/60 p-4">
                <div className="font-mono text-xs text-amber-400">
                  STEP 02 // WINDOWS DEFENDER SMARTSCREEN
                </div>
                <h4 className="mt-1 font-display text-lg font-bold text-white">
                  More Info → Run Anyway (or PowerShell Unblock)
                </h4>
                <p className="mt-2 text-sm text-slate-300 leading-relaxed">
                  If Windows SmartScreen appears on first launch, click{" "}
                  <strong>More info</strong> → <strong>Run anyway</strong>, or right-click{" "}
                  <code className="rounded bg-slate-900 px-1.5 py-0.5 font-mono text-xs text-slate-200">
                    Install-VanguardZero.ps1
                  </code>{" "}
                  → <strong>Run with PowerShell</strong>:
                </p>
                <div className="mt-3 flex items-center justify-between gap-2 rounded border border-slate-800 bg-slate-900 px-3 py-2">
                  <code className="truncate font-mono text-xs text-sky-300">
                    {winCmd}
                  </code>
                  <button
                    type="button"
                    onClick={() => copyText("win-cmd", winCmd)}
                    className="shrink-0 rounded border border-slate-700 bg-slate-800 px-2 py-1 font-mono text-[10px] text-slate-200 hover:border-sky-400"
                  >
                    {copiedKey === "win-cmd" ? "COPIED" : "COPY"}
                  </button>
                </div>
              </div>

              <div className="rounded border border-slate-800/90 bg-slate-950/60 p-4">
                <div className="font-mono text-xs text-emerald-400">
                  STEP 03 // CROSS-PLAY MACOS ↔ WINDOWS
                </div>
                <h4 className="mt-1 font-display text-lg font-bold text-white">
                  Host or Join via Unity Relay
                </h4>
                <p className="mt-2 text-sm text-slate-300 leading-relaxed">
                  Launch{" "}
                  <code className="rounded bg-slate-900 px-1.5 py-0.5 font-mono text-xs text-slate-200">
                    VanguardZero.exe
                  </code>
                  . Host a public/private match to share your 6-character Relay Join Code with friends on Windows or macOS, or jump straight into Solo Skirmish!
                </p>
              </div>
            </div>
          )}
        </div>
      </div>
    </section>
  );
}
