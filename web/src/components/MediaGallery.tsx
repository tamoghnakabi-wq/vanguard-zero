"use client";

import { useEffect, useState } from "react";
import {
  Camera,
  ChevronLeft,
  ChevronRight,
  Maximize2,
  X,
} from "lucide-react";
import { SCREENSHOTS } from "@/data/game-data";

export function MediaGallery() {
  const [activeIndex, setActiveIndex] = useState<number | null>(null);

  useEffect(() => {
    if (activeIndex === null) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setActiveIndex(null);
      } else if (e.key === "ArrowRight") {
        setActiveIndex((prev) =>
          prev === null ? 0 : (prev + 1) % SCREENSHOTS.length
        );
      } else if (e.key === "ArrowLeft") {
        setActiveIndex((prev) =>
          prev === null
            ? 0
            : (prev - 1 + SCREENSHOTS.length) % SCREENSHOTS.length
        );
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [activeIndex]);

  const activeItem =
    activeIndex !== null ? SCREENSHOTS[activeIndex] ?? null : null;

  return (
    <section
      id="media"
      className="relative border-b border-slate-800/80 py-20 sm:py-24"
    >
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="mb-12 flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
          <div>
            <div className="mb-2 inline-flex items-center gap-2 rounded border border-sky-500/30 bg-sky-500/10 px-3 py-1 font-mono text-xs tracking-[0.18em] text-sky-400">
              <Camera className="h-3.5 w-3.5" />
              <span>IN-ENGINE CAPTURES // UNITY 6 URP & BLENDER 5.2</span>
            </div>
            <h2 className="font-display text-3xl font-bold tracking-[0.1em] text-white sm:text-4xl">
              SCREENSHOTS & MEDIA GALLERY
            </h2>
          </div>
          <p className="font-mono text-xs text-slate-400">
            CLICK ANY CAPTURE FOR FULL-RESOLUTION TACTICAL LIGHTBOX
          </p>
        </div>

        <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
          {SCREENSHOTS.map((shot, idx) => (
            <button
              key={shot.id}
              type="button"
              onClick={() => setActiveIndex(idx)}
              className="group intel-panel hud-frame flex flex-col overflow-hidden rounded-lg text-left transition-all hover:border-sky-500/50"
            >
              <div className="relative aspect-video w-full overflow-hidden bg-slate-950">
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img
                  src={shot.src}
                  alt={shot.title}
                  className="h-full w-full object-cover transition-transform duration-500 group-hover:scale-105"
                />
                <div className="absolute inset-0 bg-gradient-to-t from-slate-950/90 via-transparent to-transparent" />
                <div className="absolute top-3 left-3 flex items-center gap-2">
                  <span className="rounded border border-sky-500/40 bg-slate-950/85 px-2.5 py-0.5 font-mono text-[10px] tracking-wider text-sky-300">
                    {shot.tag}
                  </span>
                  <span className="rounded border border-slate-700 bg-slate-950/85 px-2.5 py-0.5 font-mono text-[10px] text-slate-300">
                    {shot.sector}
                  </span>
                </div>
                <div className="absolute top-3 right-3 flex h-8 w-8 items-center justify-center rounded border border-slate-700 bg-slate-950/80 text-slate-300 opacity-80 transition-all group-hover:border-sky-400 group-hover:text-sky-300 group-hover:opacity-100">
                  <Maximize2 className="h-3.5 w-3.5" />
                </div>
              </div>

              <div className="flex flex-1 flex-col justify-between p-5">
                <div>
                  <div className="flex items-center justify-between gap-2">
                    <h3 className="font-display text-xl font-bold tracking-[0.06em] text-white group-hover:text-sky-300">
                      {shot.title}
                    </h3>
                    <span className="font-mono text-[11px] text-slate-400">
                      {shot.resolution}
                    </span>
                  </div>
                  <p className="mt-1.5 text-sm text-slate-300">
                    {shot.caption}
                  </p>
                </div>
              </div>
            </button>
          ))}
        </div>
      </div>

      {/* Lightbox Modal */}
      {activeItem !== null && activeIndex !== null ? (
        <div
          role="dialog"
          aria-modal="true"
          aria-label={activeItem.title}
          className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/90 p-4 backdrop-blur-md"
          onClick={() => setActiveIndex(null)}
        >
          <div
            className="intel-panel hud-frame relative w-full max-w-5xl overflow-hidden rounded-lg p-4 sm:p-6"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="mb-4 flex items-center justify-between gap-4 border-b border-slate-800 pb-3">
              <div>
                <div className="font-mono text-xs text-sky-400">
                  {`${activeItem.sector} // ${activeItem.resolution}`}
                </div>
                <h3 className="font-display text-2xl font-bold text-white">
                  {activeItem.title}
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setActiveIndex(null)}
                aria-label="Close lightbox"
                className="flex h-9 w-9 items-center justify-center rounded border border-slate-700 bg-slate-900 text-slate-300 hover:border-sky-400 hover:text-white"
              >
                <X className="h-4 w-4" />
              </button>
            </div>

            <div className="relative aspect-video w-full overflow-hidden rounded border border-slate-800 bg-slate-950">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src={activeItem.src}
                alt={activeItem.title}
                className="h-full w-full object-contain"
              />
            </div>

            <div className="mt-4 flex flex-col items-start justify-between gap-4 sm:flex-row sm:items-center">
              <p className="text-sm text-slate-300">{activeItem.caption}</p>
              <div className="flex items-center gap-2 shrink-0">
                <button
                  type="button"
                  onClick={() =>
                    setActiveIndex(
                      (activeIndex - 1 + SCREENSHOTS.length) %
                        SCREENSHOTS.length
                    )
                  }
                  className="inline-flex items-center gap-1 rounded border border-slate-700 bg-slate-900 px-3 py-1.5 font-mono text-xs text-slate-200 hover:border-sky-400"
                >
                  <ChevronLeft className="h-4 w-4" />
                  <span>PREV</span>
                </button>
                <span className="px-2 font-mono text-xs text-slate-400">
                  {activeIndex + 1} / {SCREENSHOTS.length}
                </span>
                <button
                  type="button"
                  onClick={() =>
                    setActiveIndex((activeIndex + 1) % SCREENSHOTS.length)
                  }
                  className="inline-flex items-center gap-1 rounded border border-slate-700 bg-slate-900 px-3 py-1.5 font-mono text-xs text-slate-200 hover:border-sky-400"
                >
                  <span>NEXT</span>
                  <ChevronRight className="h-4 w-4" />
                </button>
              </div>
            </div>
          </div>
        </div>
      ) : null}
    </section>
  );
}
