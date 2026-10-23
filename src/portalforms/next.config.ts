import path from "node:path";
import type { NextConfig } from "next";

/**
 * The API is served from this app's own origin, by rewriting to harbor.
 *
 * portaladmin does this because its session is a `SameSite=Lax` cookie that a
 * browser will not send cross-site. There is no session here — these pages are
 * public and the code in the URL is the whole permission — so the reasons are
 * different, but they point the same way.
 *
 * A cross-origin API would need harbor's hostname in the page, which means an
 * `Access-Control-Allow-Origin` entry per environment and a preflight before
 * every submission. Both are things to get wrong on the one night of the year
 * when several hundred people are trying to apply at once, and getting them
 * wrong looks like a form that silently will not submit.
 *
 * Proxying means the browser only ever talks to `forms.morganhacks.com`. No
 * CORS, no preflight, and harbor is never a hostname anybody has to know.
 */
/*
 * Harbor, not atlas.
 *
 * Every request here is /api/something, and stripping that prefix is harbor's
 * job -- atlas serves /forms, not /api/forms. Pointed straight at atlas every
 * call 404s, which surfaces as a form that says it does not exist and a
 * console that redirects to sign-in forever, with nothing in any log saying
 * why. The old default was atlas, so it could never have worked.
 */
/*
 * API_ORIGIN is set per environment in Vercel, not here.
 *
 * The fallback below is the local one and is only ever right on a laptop. Each
 * Vercel project carries its own value per environment -- production points at
 * production's harbor, preview at staging's -- because the two environments
 * have different hostnames and a shared value would send one of them at the
 * other's database.
 *
 * Read at build time, not at request time: the rewrite is produced when the
 * app is built, so changing the variable in Vercel changes nothing until
 * something rebuilds. Both portals also carry an ignoreCommand that skips the
 * build when nothing in their directory changed, so a redeploy of the same
 * commit is cancelled and keeps serving the previous value. An environment
 * change therefore needs a commit that touches this directory -- which is what
 * this comment is for.
 */
const apiOrigin = process.env.API_ORIGIN ?? "http://localhost:5050";

const nextConfig: NextConfig = {
  /**
   * The bundler's filesystem root is the repository, not this app.
   *
   * All three portals import their palette from libs/ui/tokens.css, which
   * lives above this directory. Without this the bundler refuses to resolve
   * anything outside src/portalforms, and each app ends up with a copy of the
   * palette — which is how a colour comes to mean two different things.
   *
   * Deploying this app therefore needs libs/ present, not just src/portalforms.
   */
  /**
   * Which upgrades `next upgrade` may take on its own.
   *
   * "security" is the narrowest setting: it offers the patch that clears a
   * published advisory and nothing else, so the portals never drift a major
   * behind without somebody deciding to. Set when this app was taken from
   * 16.4.0 to 16.4.3 for the advisory of 2026-10-23.
   */
  experimental: {
    agentUpgrade: "security",
  },

  turbopack: {
    root: path.join(import.meta.dirname, "..", ".."),
  },

  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${apiOrigin}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
