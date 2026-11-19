export const REPO_OWNER = "tamoghnakabi-wq";
export const REPO_NAME = "vanguard-zero";
export const REPO_URL = `https://github.com/${REPO_OWNER}/${REPO_NAME}`;
export const RELEASES_URL = `${REPO_URL}/releases`;
export const LATEST_RELEASE_API_URL = `https://api.github.com/repos/${REPO_OWNER}/${REPO_NAME}/releases/latest`;

export interface ReleaseAssetInfo {
  id: number;
  name: string;
  size: number;
  sizeFormatted: string;
  downloadUrl: string;
  downloadCount: number;
  contentType: string;
  sha256: string | null;
  platform: "macos" | "windows" | "checksum" | "other";
  packageLabel: string;
}

export interface LatestReleaseData {
  status: "live" | "pending";
  tagName: string;
  name: string;
  publishedAt: string | null;
  publishedFormatted: string;
  htmlUrl: string;
  body: string;
  prerelease: boolean;
  macPrimary: ReleaseAssetInfo | null;
  macSecondary: ReleaseAssetInfo | null;
  winPrimary: ReleaseAssetInfo | null;
  winSecondary: ReleaseAssetInfo | null;
  checksumAsset: ReleaseAssetInfo | null;
  allAssets: ReleaseAssetInfo[];
}
