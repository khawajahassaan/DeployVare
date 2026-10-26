import type { NextConfig } from "next";

// When building for Tauri we need a fully-static export so the app can be
// served from file:// without a Node server. Set TAURI_BUILD=1 before `next
// build` to activate this mode (the Tauri beforeBuildCommand does this).
const isTauriBuild = process.env.TAURI_BUILD === "1";
// Server deploys (deploy/server) set NEXT_OUTPUT=standalone: a self-contained server.js with only the modules it needs.
const isStandaloneBuild = process.env.NEXT_OUTPUT === "standalone";

const nextConfig: NextConfig = {
  output: isTauriBuild ? "export" : isStandaloneBuild ? "standalone" : undefined,
  // Static export can't use Next.js image optimisation (no server)
  images: {
    unoptimized: isTauriBuild,
  },
  // Trailing slashes generate `page/index.html` which works with file:// routing
  trailingSlash: isTauriBuild,
};

export default nextConfig;
