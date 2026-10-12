import type { Metadata } from "next";
import localFont from "next/font/local";
import Script from "next/script";
import { Toaster } from "sonner";
import "./globals.css";

// Same typeface AuditVare ships; bundled locally so the desktop app works offline
const sourceSans = localFont({
  src: [
    { path: "./fonts/SourceSans3-Light.woff2", weight: "300", style: "normal" },
    { path: "./fonts/SourceSans3-Regular.woff2", weight: "400", style: "normal" },
    { path: "./fonts/SourceSans3-Medium.woff2", weight: "500", style: "normal" },
    { path: "./fonts/SourceSans3-SemiBold.woff2", weight: "600", style: "normal" },
    { path: "./fonts/SourceSans3-Bold.woff2", weight: "700", style: "normal" },
  ],
  variable: "--font-source-sans",
  display: "swap",
});

export const metadata: Metadata = {
  title: "DeployVare",
  description: "Client onboarding, server setup and deployments for SQL databases and IIS applications",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" suppressHydrationWarning className={sourceSans.variable}>
      <body className="font-sans">
        {/* Runtime settings written by a server install (API address); empty in development and on Vercel */}
        <Script src="/config.js" strategy="beforeInteractive" />
        {children}
        <Toaster position="top-right" richColors />
      </body>
    </html>
  );
}
