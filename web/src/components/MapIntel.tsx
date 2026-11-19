"use client";

import { useState } from "react";
import { Compass, Eye, MapPin, Navigation, Radio } from "lucide-react";
import { MAP_CALLOUTS, type MapCallout } from "@/data/game-data";

export function MapIntel() {
  const [activeId, setActiveId] = useState<string>(MAP_CALLOUTS[0].id);

  const active: MapCallout =
    MAP_CALLOUTS.find((c) => c.id === activeId) ?? MAP_CALLOUTS[0];

  return (
    <section
      id="map"
      className="relative border-b border-slate-800/80 py-20 sm:py-24"
    >
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        {/* Header */}
        <div className="mb-12 flex flex-col justify-between gap-6 lg:flex-row lg:items-end">
          <div>
            <div className="mb-2 inline-flex items-center gap-2 rounded border border-sky-500/30 bg-sky-500/10 px-3 py-1 font-mono text-xs tracking-[0.18em] text-sky-400">
              <Compass className="h-3.5 w-3.5" />
              <span>AO RECONNAISSANCE // 54°41&apos;N 20°30&apos;E</span>
            </div>
            <h2 className="font-display text-3xl font-bold tracking-[0.1em] text-white sm:text-4xl">
              MAP INTEL — BLACKSITE-09
            </h2>
          </div>

          <div className="flex flex-wrap items-center gap-3 font-mono text-xs text-slate-300">
            <span className="rounded border border-slate-800 bg-slate-900/90 px-3 py-1.5">
              TIME: <strong className="text-amber-400">21:40 ZULU (DUSK)</strong>
            </span>
            <span className="rounded border border-slate-800 bg-slate-900/90 px-3 py-1.5">
              WEATHER: <strong className="text-sky-400">HEAVY RAINFALL</strong>
            </span>
            <span className="rounded border border-slate-800 bg-slate-900/90 px-3 py-1.5">
              MODES: <strong className="text-white">ALPHA vs BRAVO / FFA / BOTS</strong>
            </span>
          </div>
        </div>

        <div className="grid grid-cols-1 gap-8 lg:grid-cols-12">
          {/* Interactive Tactical Blueprint */}
          <div className="intel-panel hud-frame relative overflow-hidden rounded-lg p-5 lg:col-span-7">
            <div className="mb-3 flex items-center justify-between font-mono text-xs text-slate-400">
              <span>TACTICAL OVERHEAD SCHEMATIC // NAVMESH BAKED</span>
              <span className="text-sky-400">{active.code} LOCKED</span>
            </div>

            <div className="relative aspect-[16/10] w-full overflow-hidden rounded border border-slate-800 bg-[#060a12] tactical-grid">
              {/* Schematic SVG Compound Blueprint */}
              <svg
                viewBox="0 0 800 500"
                className="h-full w-full"
                fill="none"
                xmlns="http://www.w3.org/2000/svg"
              >
                {/* Compound Outer Perimeter Wall */}
                <rect
                  x="60"
                  y="45"
                  width="680"
                  height="410"
                  rx="8"
                  stroke="#334155"
                  strokeWidth="3"
                  strokeDasharray="10 6"
                  fill="#0b111d"
                  fillOpacity="0.65"
                />
                {/* Spawn Zones */}
                <rect
                  x="85"
                  y="365"
                  width="120"
                  height="65"
                  rx="4"
                  fill="#0ea5e9"
                  fillOpacity="0.1"
                  stroke="#38bdf8"
                  strokeWidth="1.5"
                />
                <text
                  x="145"
                  y="402"
                  textAnchor="middle"
                  fill="#38bdf8"
                  fontFamily="monospace"
                  fontSize="11"
                >
                  ALPHA SPAWN
                </text>

                <rect
                  x="595"
                  y="70"
                  width="120"
                  height="65"
                  rx="4"
                  fill="#f59e0b"
                  fillOpacity="0.1"
                  stroke="#f59e0b"
                  strokeWidth="1.5"
                />
                <text
                  x="655"
                  y="107"
                  textAnchor="middle"
                  fill="#f59e0b"
                  fontFamily="monospace"
                  fontSize="11"
                >
                  BRAVO SPAWN
                </text>

                {/* Server Bunker (West) */}
                <rect
                  x="105"
                  y="210"
                  width="175"
                  height="130"
                  rx="4"
                  fill="#111a2c"
                  stroke="#38bdf8"
                  strokeOpacity="0.5"
                  strokeWidth="2"
                />
                <text
                  x="192"
                  y="322"
                  textAnchor="middle"
                  fill="#94a3b8"
                  fontFamily="monospace"
                  fontSize="11"
                >
                  SERVER BUNKER
                </text>

                {/* Radar Spire (North Center) */}
                <circle
                  cx="400"
                  cy="120"
                  r="48"
                  fill="#131d30"
                  stroke="#64748b"
                  strokeWidth="2"
                />
                <circle
                  cx="400"
                  cy="120"
                  r="24"
                  stroke="#38bdf8"
                  strokeDasharray="4 4"
                  strokeWidth="1.5"
                />

                {/* Central Courtyard Shipping Containers */}
                <rect
                  x="335"
                  y="225"
                  width="130"
                  height="52"
                  fill="#172236"
                  stroke="#475569"
                  strokeWidth="2"
                />
                <rect
                  x="365"
                  y="305"
                  width="115"
                  height="42"
                  fill="#172236"
                  stroke="#475569"
                  strokeWidth="2"
                />

                {/* Elevated Catwalks (East Ring) */}
                <path
                  d="M540 155H685V360H525V295H610V215H540V155Z"
                  fill="#151e2e"
                  stroke="#f59e0b"
                  strokeOpacity="0.45"
                  strokeWidth="2"
                />
              </svg>

              {/* Interactive Callout Pins */}
              {MAP_CALLOUTS.map((callout) => {
                const isSelected = callout.id === active.id;
                return (
                  <button
                    key={callout.id}
                    type="button"
                    onClick={() => setActiveId(callout.id)}
                    style={{
                      left: `${callout.coords.x}%`,
                      top: `${callout.coords.y}%`,
                    }}
                    className={`group absolute -translate-x-1/2 -translate-y-1/2 flex items-center gap-2 rounded-full border px-3 py-1.5 font-mono text-xs transition-all ${
                      isSelected
                        ? "border-amber-400 bg-amber-500 text-slate-950 font-bold shadow-[0_0_24px_rgba(245,158,11,0.5)] scale-105"
                        : "border-sky-500/50 bg-slate-950/90 text-sky-300 hover:border-sky-400 hover:bg-slate-900"
                    }`}
                  >
                    <MapPin className="h-3.5 w-3.5 shrink-0" />
                    <span>{callout.code}</span>
                  </button>
                );
              })}
            </div>

            <div className="mt-4 flex flex-wrap items-center justify-between gap-2 font-mono text-xs text-slate-400">
              <span>CLICK ANY SECTOR PIN TO INSPECT TACTICAL CALLOUT</span>
              <span>LIGHTING: WARM FLOODLIGHTS vs CYAN BUNKER ACCENTS</span>
            </div>
          </div>

          {/* Sector Dossier & Callout Selector */}
          <div className="flex flex-col justify-between gap-4 lg:col-span-5">
            <div className="grid grid-cols-2 gap-2.5">
              {MAP_CALLOUTS.map((callout) => {
                const isSelected = callout.id === active.id;
                return (
                  <button
                    key={callout.id}
                    type="button"
                    onClick={() => setActiveId(callout.id)}
                    className={`rounded border p-3 text-left transition-all ${
                      isSelected
                        ? "border-amber-400/80 bg-amber-500/15 text-white"
                        : "border-slate-800 bg-slate-900/70 text-slate-400 hover:border-slate-700 hover:text-slate-200"
                    }`}
                  >
                    <div className="font-mono text-[10px] tracking-wider text-amber-400">
                      {callout.code}
                    </div>
                    <div className="mt-0.5 font-display text-base font-bold">
                      {callout.name}
                    </div>
                  </button>
                );
              })}
            </div>

            {/* Active Sector Detail Card */}
            <div className="intel-panel hud-frame flex-1 rounded-lg p-6">
              <div className="flex items-center justify-between border-b border-slate-800 pb-3">
                <span className="font-mono text-xs text-amber-400">
                  {`${active.code} // TACTICAL CALLOUT`}
                </span>
                <span className="rounded bg-slate-900 px-2.5 py-0.5 font-mono text-xs text-sky-300">
                  {active.elevation}
                </span>
              </div>

              <h3 className="mt-4 font-display text-3xl font-bold tracking-[0.06em] text-white">
                {active.name}
              </h3>

              <div className="mt-2 inline-flex items-center gap-2 rounded border border-sky-500/30 bg-sky-500/10 px-2.5 py-1 font-mono text-xs text-sky-300">
                <Eye className="h-3.5 w-3.5" />
                <span>COMBAT PROFILE: {active.engagement}</span>
              </div>

              <p className="mt-4 text-sm leading-relaxed text-slate-300">
                {active.summary}
              </p>

              <div className="mt-5 rounded border border-amber-500/30 bg-amber-500/10 p-4">
                <div className="flex items-center gap-2 font-mono text-xs font-semibold text-amber-300">
                  <Navigation className="h-3.5 w-3.5" />
                  <span>OPERATOR TACTICAL ADVISORY</span>
                </div>
                <p className="mt-1.5 text-xs leading-relaxed text-slate-200">
                  {active.tacticalNote}
                </p>
              </div>

              <div className="mt-5 flex items-center justify-between border-t border-slate-800/80 pt-4 font-mono text-xs text-slate-400">
                <span className="flex items-center gap-1.5">
                  <Radio className="h-3.5 w-3.5 text-emerald-400" />
                  <span>AI BOT NAVMESH COVERAGE: 100%</span>
                </span>
                <span>SPAWN PROTECTION: ACTIVE</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
