import { ImageResponse } from "next/og";
import { siteConfig } from "@/site.config";

// Generated at build time rather than shipped as a PNG, so the deadline and
// year come from site.config.ts like everything else and cannot drift out of
// step with the page itself.
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";
export const alt = siteConfig.recruiting
  ? `MorganHacks ${siteConfig.year} — organizer applications`
  : `MorganHacks ${siteConfig.year}`;

export default function OpengraphImage() {
  const { year, deadline, recruiting, holding } = siteConfig;

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          padding: "72px 80px",
          // Matches the page's own ground. No gradients or webfonts: this
          // renders in a constrained runtime, and every extra dependency is
          // another way for the build to fail on something nobody looks at.
          background: "#16306e",
          color: "#ffffff",
          fontFamily: "sans-serif",
        }}
      >
        <div style={{ display: "flex", fontSize: 30, letterSpacing: 6, fontWeight: 700 }}>
          MORGANHACKS {year}
        </div>

        <div style={{ display: "flex", flexDirection: "column" }}>
          <div style={{ display: "flex", fontSize: 84, lineHeight: 1.05, fontWeight: 700 }}>
            {recruiting ? "Think you could pull this off?" : holding.card}
          </div>
          {recruiting ? (
            <div style={{ display: "flex", marginTop: 20, fontSize: 34, color: "#c8d3f0" }}>
              Organizer applications are open.
            </div>
          ) : null}
        </div>

        {/* The dated line goes with the drive. A card that still names a
            September deadline is the version that gets shared in November. */}
        <div style={{ display: "flex", alignItems: "center", gap: 18, fontSize: 28 }}>
          <div
            style={{
              display: "flex",
              width: 14,
              height: 14,
              borderRadius: 7,
              background: "#6f9bff",
            }}
          />
          <div style={{ display: "flex", color: "#c8d3f0" }}>
            {recruiting
              ? `Applications close ${deadline.weekday}, ${deadline.date} · ${deadline.time}`
              : "Dates to be announced"}
          </div>
        </div>
      </div>
    ),
    size,
  );
}
