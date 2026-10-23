import path from "node:path";
import type { NextConfig } from "next";

/**
 * The API is served from this app's own origin, by rewriting to harbor.
 *
 * Not a convenience. The session is a cookie with SameSite=Lax, and a browser
 * does not send a Lax cookie on a cross-site fetch — so an admin app on one
 * origin calling an API on another simply cannot authenticate. The usual
 * workaround is SameSite=None, which turns the cookie into something every
 * other site can make the browser send, and gives up the CSRF protection Lax
 * was there for.
 *
 * Proxying instead means the browser only ever talks to one origin. Lax keeps
 * working, the cookie stays host-only, and harbor is never exposed to the page
 * directly.
 *
 * It also keeps the OAuth round trip on one origin: Google redirects back here,
 * not to an Azure hostname a person has never seen.
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
const apiOrigin =
  process.env.API_ORIGIN ?? "http://localhost:5050";

const nextConfig: NextConfig = {
  experimental: {
    optimizePackageImports: ["@hugeicons/core-free-icons"],
    /* Only the patch that clears a published advisory, never a major. Set when
       this app was taken from 16.4.0 to 16.4.3 for the advisory of
       2026-10-23. */
    agentUpgrade: "security",
  },
  /**
   * The bundler's filesystem root is the repository, not this app.
   *
   * Both portals import their palette from libs/ui/tokens.css, which lives
   * above this directory. Without this the bundler refuses to resolve anything
   * outside src/portaladmin, and the two apps end up with a copy of the
   * palette each — which is how a colour comes to mean two different things.
   *
   * Deploying this app therefore needs libs/ present, not just src/portaladmin.
   */
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
