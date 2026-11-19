import { getLatestRelease } from "@/lib/github-release";

export async function GET() {
  const release = await getLatestRelease();
  return Response.json(release, {
    headers: {
      "Cache-Control": "public, s-maxage=300, stale-while-revalidate=600",
    },
  });
}
