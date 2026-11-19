import { cacheLife } from "next/cache";
import {
  LATEST_RELEASE_API_URL,
  RELEASES_URL,
  type LatestReleaseData,
  type ReleaseAssetInfo,
} from "./release-types";

export {
  LATEST_RELEASE_API_URL,
  RELEASES_URL,
  REPO_NAME,
  REPO_OWNER,
  REPO_URL,
  type LatestReleaseData,
  type ReleaseAssetInfo,
} from "./release-types";

interface GitHubAssetResponse {
  id: number;
  name: string;
  size: number;
  browser_download_url: string;
  download_count: number;
  content_type: string;
}

interface GitHubReleaseResponse {
  tag_name: string;
  name: string;
  published_at: string;
  html_url: string;
  body: string | null;
  prerelease: boolean;
  assets: GitHubAssetResponse[];
}

export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return "—";
  const units = ["B", "KB", "MB", "GB"];
  let value = bytes;
  let unitIdx = 0;
  while (value >= 1024 && unitIdx < units.length - 1) {
    value /= 1024;
    unitIdx += 1;
  }
  return `${value.toFixed(unitIdx >= 2 ? 1 : 0)} ${units[unitIdx]}`;
}

function formatPublishedDate(iso: string | null): string {
  if (!iso) return "Release build staging";
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return new Intl.DateTimeFormat("en-US", {
    year: "numeric",
    month: "short",
    day: "2-digit",
    timeZone: "UTC",
  }).format(parsed);
}

function parseChecksumMap(text: string): Map<string, string> {
  const map = new Map<string, string>();
  const lines = text.split(/\r?\n/);
  for (const rawLine of lines) {
    const line = rawLine.trim();
    const match = /^([a-fA-F0-9]{64})\s+\*?(.+)$/.exec(line);
    if (match) {
      const hash = match[1].toLowerCase();
      const filename = match[2].trim().split("/").pop() ?? match[2].trim();
      map.set(filename, hash);
    }
  }
  return map;
}

function classifyAsset(
  name: string
): Pick<ReleaseAssetInfo, "platform" | "packageLabel"> {
  const lower = name.toLowerCase();
  if (lower.includes("sha256") || lower.includes("checksum")) {
    return { platform: "checksum", packageLabel: "SHA-256 Checksums" };
  }
  if (
    lower.includes("mac") ||
    lower.includes("osx") ||
    lower.includes("darwin") ||
    lower.endsWith(".dmg")
  ) {
    if (lower.endsWith(".dmg")) {
      return { platform: "macos", packageLabel: "macOS Disk Image (.dmg)" };
    }
    return { platform: "macos", packageLabel: "macOS App Archive (.zip)" };
  }
  if (
    lower.includes("win") ||
    lower.endsWith(".exe") ||
    lower.endsWith(".msi")
  ) {
    if (lower.endsWith(".exe") || lower.endsWith(".msi")) {
      return { platform: "windows", packageLabel: "Windows 64-bit Installer (.exe)" };
    }
    return { platform: "windows", packageLabel: "Windows 64-bit Portable (.zip)" };
  }
  return { platform: "other", packageLabel: "Release Package" };
}

function buildFallbackRelease(): LatestReleaseData {
  return {
    status: "pending",
    tagName: "v1.0.0",
    name: "VANGUARD ZERO v1.0.0",
    publishedAt: null,
    publishedFormatted: "Build pipeline staging",
    htmlUrl: RELEASES_URL,
    body: [
      "### Initial Public Launch — BLACKSITE-09",
      "- Full Unity 6 (6000.3.22f1 URP) standalone binaries for macOS (Apple Silicon / Universal) and Windows (64-bit).",
      "- Unity Relay cross-platform matchmaking, custom lobbies, join-by-code, and lag-compensated hit registration.",
      "- Solo Skirmish vs Tactical AI Bots with baked BLACKSITE-09 NavMesh arena.",
      "- Full Arsenal: VX-47 Assault Rifle, M890 Breacher Shotgun, and M19 Tactical Sidearm.",
    ].join("\n"),
    prerelease: false,
    macPrimary: null,
    macSecondary: null,
    winPrimary: null,
    winSecondary: null,
    checksumAsset: null,
    allAssets: [],
  };
}

export async function getLatestRelease(): Promise<LatestReleaseData> {
  "use cache";
  cacheLife({
    stale: 60,
    revalidate: 300,
    expire: 3600,
  });

  const headers: Record<string, string> = {
    Accept: "application/vnd.github+json",
    "User-Agent": "vanguard-zero-web-portal",
    "X-GitHub-Api-Version": "2022-11-28",
  };
  if (process.env.GITHUB_TOKEN) {
    headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
  }

  try {
    const res = await fetch(LATEST_RELEASE_API_URL, { headers });
    if (!res.ok) {
      return buildFallbackRelease();
    }

    const data = (await res.json()) as GitHubReleaseResponse;
    const rawAssets = Array.isArray(data.assets) ? data.assets : [];

    // Extract checksums from release notes first, then augment from SHA256SUMS.txt asset if available
    const checksums = parseChecksumMap(data.body ?? "");
    const rawChecksumAsset = rawAssets.find((a) =>
      a.name.toLowerCase().includes("sha256")
    );

    if (rawChecksumAsset) {
      try {
        const sumRes = await fetch(rawChecksumAsset.browser_download_url, {
          headers: { "User-Agent": "vanguard-zero-web-portal" },
        });
        if (sumRes.ok) {
          const sumText = await sumRes.text();
          for (const [file, hash] of parseChecksumMap(sumText)) {
            checksums.set(file, hash);
          }
        }
      } catch {
        // Checksum file fetch is best-effort; body hashes remain intact
      }
    }

    const mappedAssets: ReleaseAssetInfo[] = rawAssets.map((asset) => {
      const { platform, packageLabel } = classifyAsset(asset.name);
      return {
        id: asset.id,
        name: asset.name,
        size: asset.size,
        sizeFormatted: formatBytes(asset.size),
        downloadUrl: asset.browser_download_url,
        downloadCount: asset.download_count,
        contentType: asset.content_type,
        sha256: checksums.get(asset.name) ?? null,
        platform,
        packageLabel,
      };
    });

    const macAssets = mappedAssets.filter((a) => a.platform === "macos");
    const winAssets = mappedAssets.filter((a) => a.platform === "windows");
    const checksumAsset =
      mappedAssets.find((a) => a.platform === "checksum") ?? null;

    // Prefer .dmg as macOS primary and .zip (or Setup .exe) as Windows primary
    const macPrimary =
      macAssets.find((a) => a.name.toLowerCase().endsWith(".dmg")) ??
      macAssets[0] ??
      null;
    const macSecondary =
      macAssets.find((a) => a.id !== macPrimary?.id) ?? null;

    const winPrimary =
      winAssets.find((a) => a.name.toLowerCase().endsWith(".zip")) ??
      winAssets[0] ??
      null;
    const winSecondary =
      winAssets.find((a) => a.id !== winPrimary?.id) ?? null;

    return {
      status: "live",
      tagName: data.tag_name,
      name: data.name || `VANGUARD ZERO ${data.tag_name}`,
      publishedAt: data.published_at,
      publishedFormatted: formatPublishedDate(data.published_at),
      htmlUrl: data.html_url,
      body: data.body?.trim() || "Production release for macOS and Windows.",
      prerelease: Boolean(data.prerelease),
      macPrimary,
      macSecondary,
      winPrimary,
      winSecondary,
      checksumAsset,
      allAssets: mappedAssets,
    };
  } catch {
    return buildFallbackRelease();
  }
}
