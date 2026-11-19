"use client";

import { useState } from "react";
import { Crosshair, Gauge, Layers, Shield, Zap } from "lucide-react";
import { WEAPONS, type WeaponSpec } from "@/data/game-data";

const STAT_META: Array<{
  key: keyof WeaponSpec["stats"];
  label: string;
  accent: string;
}> = [
  { key: "damage", label: "BALLISTIC DAMAGE", accent: "bg-amber-400" },
  { key: "fireRate", label: "RATE OF FIRE", accent: "bg-sky-400" },
  { key: "range", label: "EFFECTIVE RANGE", accent: "bg-sky-400" },
  { key: "control", label: "RECOIL CONTROL", accent: "bg-emerald-400" },
  { key: "mobility", label: "TACTICAL MOBILITY", accent: "bg-sky-400" },
];

export function ArsenalShowcase() {
  const [selectedId, setSelectedId] = useState<WeaponSpec["id"]>("vx47");

  const active =
    WEAPONS.find((weapon) => weapon.id === selectedId) ?? WEAPONS[0];

  return (
    <section
      id="arsenal"
      className="relative border-b border-slate-800/80 py-20 sm:py-24"
    >
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        {/* Section Header */}
        <div className="mb-10 flex flex-col justify-between gap-6 lg:flex-row lg:items-end">
          <div>
            <div className="mb-2 inline-flex items-center gap-2 rounded border border-amber-500/30 bg-amber-500/10 px-3 py-1 font-mono text-xs tracking-[0.18em] text-amber-400">
              <Crosshair className="h-3.5 w-3.5" />
              <span>ARMORY TELEMETRY // HARD-SURFACE 2K PBR</span>
            </div>
            <h2 className="font-display text-3xl font-bold tracking-[0.1em] text-white sm:text-4xl">
              TACTICAL ARSENAL SHOWCASE
            </h2>
          </div>

          {/* Weapon Selector Tabs */}
          <div className="flex flex-wrap gap-2">
            {WEAPONS.map((weapon) => {
              const isSelected = weapon.id === active.id;
              return (
                <button
                  key={weapon.id}
                  type="button"
                  onClick={() => setSelectedId(weapon.id)}
                  className={`flex flex-col items-start rounded border px-4 py-2.5 text-left transition-all ${
                    isSelected
                      ? "border-sky-400 bg-sky-500/15 text-white shadow-[0_0_24px_rgba(56,189,248,0.18)]"
                      : "border-slate-800 bg-slate-900/70 text-slate-400 hover:border-slate-700 hover:text-slate-200"
                  }`}
                >
                  <span className="font-mono text-[10px] tracking-[0.16em] text-sky-400">
                    {weapon.code}
                  </span>
                  <span className="font-display text-base font-bold tracking-[0.08em]">
                    {weapon.name}
                  </span>
                </button>
              );
            })}
          </div>
        </div>

        {/* Main Weapon Display Grid */}
        <div className="grid grid-cols-1 gap-8 lg:grid-cols-12">
          {/* Studio Preview Viewport */}
          <div className="intel-panel hud-frame flex flex-col justify-between overflow-hidden rounded-lg p-5 lg:col-span-7">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2 border-b border-slate-800/80 pb-3">
              <div className="flex items-center gap-2 font-mono text-xs text-sky-400">
                <span>{active.code}</span>
                <span className="text-slate-600">{"//"}</span>
                <span className="text-slate-300">{active.category}</span>
              </div>
              <span className="rounded border border-amber-500/30 bg-amber-500/10 px-2.5 py-0.5 font-mono text-xs text-amber-300">
                CALIBER: {active.caliber}
              </span>
            </div>

            <div className="relative my-2 overflow-hidden rounded border border-slate-800/80 bg-[#060910]">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src={active.image}
                alt={active.name}
                className="aspect-video w-full object-cover transition-all duration-300"
              />
            </div>

            {/* Firing Telemetry Strip */}
            <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
              <div className="rounded border border-slate-800/80 bg-slate-950/70 p-3">
                <div className="flex items-center gap-1.5 font-mono text-[10px] text-slate-400">
                  <Zap className="h-3 w-3 text-sky-400" />
                  <span>CYCLIC RATE</span>
                </div>
                <div className="mt-1 font-display text-xl font-bold text-white">
                  {active.telemetry.rpm}
                </div>
              </div>
              <div className="rounded border border-slate-800/80 bg-slate-950/70 p-3">
                <div className="flex items-center gap-1.5 font-mono text-[10px] text-slate-400">
                  <Layers className="h-3 w-3 text-sky-400" />
                  <span>CAPACITY</span>
                </div>
                <div className="mt-1 font-display text-xl font-bold text-white">
                  {active.telemetry.magazine}
                </div>
              </div>
              <div className="rounded border border-slate-800/80 bg-slate-950/70 p-3">
                <div className="flex items-center gap-1.5 font-mono text-[10px] text-slate-400">
                  <Gauge className="h-3 w-3 text-amber-400" />
                  <span>MUZZLE VELOCITY</span>
                </div>
                <div className="mt-1 font-display text-xl font-bold text-white">
                  {active.telemetry.velocity}
                </div>
              </div>
              <div className="rounded border border-slate-800/80 bg-slate-950/70 p-3">
                <div className="flex items-center gap-1.5 font-mono text-[10px] text-slate-400">
                  <Shield className="h-3 w-3 text-emerald-400" />
                  <span>TACTICAL RELOAD</span>
                </div>
                <div className="mt-1 font-display text-xl font-bold text-white">
                  {active.telemetry.reload}
                </div>
              </div>
            </div>
          </div>

          {/* Ballistic Stats & Attachments Dossier */}
          <div className="intel-panel hud-frame flex flex-col justify-between rounded-lg p-6 lg:col-span-5">
            <div>
              <div className="font-mono text-xs tracking-wider text-sky-400">
                COMBAT DOCTRINE // {active.role.toUpperCase()}
              </div>
              <h3 className="mt-1 font-display text-3xl font-bold tracking-[0.06em] text-white">
                {active.name}
              </h3>
              <p className="mt-3 text-sm leading-relaxed text-slate-300">
                {active.description}
              </p>

              {/* Animated Ballistic Stat Bars */}
              <div className="mt-6 space-y-4">
                {STAT_META.map((stat) => {
                  const val = active.stats[stat.key];
                  return (
                    <div key={stat.key}>
                      <div className="mb-1.5 flex items-center justify-between font-mono text-xs">
                        <span className="tracking-wider text-slate-300">
                          {stat.label}
                        </span>
                        <span className="font-semibold text-white">
                          {val} / 100
                        </span>
                      </div>
                      <div className="h-2 w-full overflow-hidden rounded-full bg-slate-900 p-0.5">
                        <div
                          className={`h-full rounded-full transition-all duration-500 ${stat.accent}`}
                          style={{ width: `${val}%` }}
                        />
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Factory Issue Attachments */}
            <div className="mt-6 border-t border-slate-800/90 pt-5">
              <div className="mb-3 font-mono text-xs tracking-wider text-slate-400">
                STANDARD-ISSUE MODULAR ATTACHMENTS
              </div>
              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                {active.attachments.map((mod) => (
                  <div
                    key={mod}
                    className="flex items-center gap-2 rounded border border-slate-800/90 bg-slate-950/60 px-3 py-2 font-mono text-xs text-slate-200"
                  >
                    <span className="h-1.5 w-1.5 rounded-full bg-sky-400" />
                    <span>{mod}</span>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
