"use client";

import { useState } from "react";
import {
  Crosshair,
  Download,
  ExternalLink,
  Menu,
  Radio,
  X,
} from "lucide-react";
import { RELEASES_URL, REPO_URL } from "@/lib/release-types";

interface NavbarProps {
  versionTag: string;
  isLiveRelease: boolean;
}

const NAV_LINKS = [
  { href: "#download", label: "DOWNLOAD" },
  { href: "#architecture", label: "ARCHITECTURE" },
  { href: "#arsenal", label: "ARSENAL" },
  { href: "#map", label: "BLACKSITE-09" },
  { href: "#media", label: "MEDIA" },
  { href: "#specs", label: "SPECS & PATCHES" },
];

export function Navbar({ versionTag, isLiveRelease }: NavbarProps) {
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <header className="sticky top-0 z-50 border-b border-slate-800/80 bg-[#07090d]/90 backdrop-blur-md">
      <div className="mx-auto flex h-16 max-w-7xl items-center justify-between px-4 sm:px-6 lg:px-8">
        {/* Brand Emblem */}
        <a
          href="#"
          className="group flex items-center gap-3 focus:outline-none"
        >
          <div className="flex h-9 w-9 items-center justify-center rounded border border-sky-500/40 bg-sky-500/10 text-sky-400 transition-colors group-hover:border-sky-400 group-hover:bg-sky-500/20">
            <Crosshair className="h-4 w-4" />
          </div>
          <div className="flex flex-col">
            <span className="font-display text-xl font-bold tracking-[0.2em] text-slate-100">
              VANGUARD <span className="text-sky-400">ZERO</span>
            </span>
            <span className="font-mono text-[10px] tracking-[0.16em] text-slate-400">
              UNITY 6 // TACTICAL FPS
            </span>
          </div>
        </a>

        {/* Desktop Nav */}
        <nav className="hidden items-center gap-6 lg:flex">
          {NAV_LINKS.map((item) => (
            <a
              key={item.href}
              href={item.href}
              className="font-display text-sm font-semibold tracking-[0.16em] text-slate-300 transition-colors hover:text-sky-400"
            >
              {item.label}
            </a>
          ))}
        </nav>

        {/* Status Pill & CTA */}
        <div className="hidden items-center gap-3 sm:flex">
          <a
            href={RELEASES_URL}
            target="_blank"
            rel="noopener noreferrer"
            className="flex items-center gap-2 rounded border border-slate-700/80 bg-slate-900/80 px-2.5 py-1 font-mono text-xs text-slate-300 transition-colors hover:border-sky-500/40 hover:text-sky-300"
          >
            <Radio
              className={`h-3 w-3 ${
                isLiveRelease
                  ? "text-emerald-400 animate-pulse-slow"
                  : "text-amber-400"
              }`}
            />
            <span>{versionTag}</span>
          </a>

          <a
            href={REPO_URL}
            target="_blank"
            rel="noopener noreferrer"
            className="hidden items-center gap-1.5 rounded border border-slate-700/80 bg-slate-900/60 px-3 py-1.5 font-mono text-xs text-slate-300 transition-colors hover:border-slate-500 hover:text-white xl:flex"
          >
            <span>GITHUB</span>
            <ExternalLink className="h-3 w-3" />
          </a>

          <a
            href="#download"
            className="flex items-center gap-2 rounded bg-sky-500 px-4 py-2 font-display text-sm font-bold tracking-[0.15em] text-slate-950 transition-all hover:bg-sky-400"
          >
            <Download className="h-4 w-4" />
            <span>DEPLOY NOW</span>
          </a>
        </div>

        {/* Mobile menu button */}
        <button
          type="button"
          onClick={() => setMobileOpen((prev) => !prev)}
          aria-label="Toggle tactical navigation menu"
          className="flex h-10 w-10 items-center justify-center rounded border border-slate-800 bg-slate-900/80 text-slate-300 lg:hidden"
        >
          {mobileOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
        </button>
      </div>

      {/* Mobile Drawer */}
      {mobileOpen ? (
        <div className="border-t border-slate-800/80 bg-[#07090d]/95 px-4 py-4 lg:hidden">
          <div className="flex flex-col gap-3">
            {NAV_LINKS.map((item) => (
              <a
                key={item.href}
                href={item.href}
                onClick={() => setMobileOpen(false)}
                className="rounded px-3 py-2 font-display text-base font-semibold tracking-[0.15em] text-slate-200 hover:bg-slate-900 hover:text-sky-400"
              >
                {item.label}
              </a>
            ))}
            <div className="mt-2 flex items-center gap-2 border-t border-slate-800 pt-3">
              <a
                href="#download"
                onClick={() => setMobileOpen(false)}
                className="flex flex-1 items-center justify-center gap-2 rounded bg-sky-500 px-4 py-2.5 font-display text-sm font-bold tracking-[0.15em] text-slate-950"
              >
                <Download className="h-4 w-4" />
                <span>DOWNLOAD ({versionTag})</span>
              </a>
              <a
                href={REPO_URL}
                target="_blank"
                rel="noopener noreferrer"
                className="flex items-center gap-1.5 rounded border border-slate-700 px-3 py-2.5 font-mono text-xs text-slate-300"
              >
                <span>REPO</span>
                <ExternalLink className="h-3.5 w-3.5" />
              </a>
            </div>
          </div>
        </div>
      ) : null}
    </header>
  );
}
