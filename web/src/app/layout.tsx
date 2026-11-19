import type { Metadata, Viewport } from "next";
import { Inter, JetBrains_Mono, Rajdhani } from "next/font/google";
import "./globals.css";

const rajdhani = Rajdhani({
  variable: "--font-display",
  subsets: ["latin"],
  weight: ["500", "600", "700"],
  display: "swap",
});

const inter = Inter({
  variable: "--font-sans",
  subsets: ["latin"],
  display: "swap",
});

const jetbrainsMono = JetBrains_Mono({
  variable: "--font-mono",
  subsets: ["latin"],
  display: "swap",
});

export const metadata: Metadata = {
  title: "VANGUARD ZERO — Tactical Multiplayer FPS | Unity 6 & Unity Relay",
  description:
    "Deploy into BLACKSITE-09. VANGUARD ZERO is an AAA-quality hyper-realistic multiplayer tactical FPS built with Unity 6 URP, Blender PBR assets, MotionBricks locomotion, and Unity Relay cross-platform networking for macOS and Windows.",
  keywords: [
    "Vanguard Zero",
    "Tactical FPS",
    "Multiplayer Shooter",
    "Unity 6",
    "Unity Relay",
    "Blacksite-09",
    "macOS FPS",
    "Windows FPS",
  ],
  openGraph: {
    title: "VANGUARD ZERO — Tactical Multiplayer FPS",
    description:
      "Rain-swept dusk tactical warfare in BLACKSITE-09. Cross-platform Unity Relay multiplayer for macOS and Windows.",
    type: "website",
  },
};

export const viewport: Viewport = {
  themeColor: "#07090d",
  colorScheme: "dark",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html
      lang="en"
      className={`${rajdhani.variable} ${inter.variable} ${jetbrainsMono.variable} dark scroll-smooth antialiased`}
    >
      <body className="min-h-screen bg-[#07090d] text-slate-100 font-sans selection:bg-sky-500/30 selection:text-sky-100 overflow-x-hidden">
        {children}
      </body>
    </html>
  );
}
