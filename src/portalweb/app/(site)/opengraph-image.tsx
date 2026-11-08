import { ImageResponse } from "next/og";
import { siteConfig } from "@/site.config";

// Generated at build time rather than shipped as a PNG, so the copy comes from
// site.config.ts like everything else and cannot drift out of step with the
// page -- which is exactly what happened while this card was still announcing
// a drive the page had stopped running.
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";
export const alt = `MorganHacks ${siteConfig.year}`;

export default function OpengraphImage() {
  const { year, announcement } = siteConfig;

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          padding: "68px 76px",
          // The page's own ground. No gradients or webfonts: this renders in a
          // constrained runtime, and every extra dependency is another way for
          // the build to fail on something nobody looks at.
          background: "#0c0b0d",
          color: "#f4f1ec",
          fontFamily: "sans-serif",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 16, fontSize: 26, letterSpacing: 5 }}>
          <div
            style={{
              display: "flex",
              width: 12,
              height: 12,
              borderRadius: 6,
              background: "#ff6a2b",
            }}
          />
          <div style={{ display: "flex", color: "#8f8a85" }}>
            MORGANHACKS {year} · IN PLANNING
          </div>
        </div>

        <div style={{ display: "flex", fontSize: 92, lineHeight: 1.02, fontWeight: 800, letterSpacing: -3 }}>
          {announcement.card}
        </div>

        {/* The same board the page shows, minus the rows a 1200px card has no
            room to spell out. */}
        <div style={{ display: "flex", gap: 56, fontSize: 25, color: "#8f8a85" }}>
          {announcement.board.slice(0, 2).map((row) => (
            <div key={row.label} style={{ display: "flex", flexDirection: "column", gap: 6 }}>
              <div style={{ display: "flex", letterSpacing: 2, fontSize: 19 }}>
                {row.label.toUpperCase()}
              </div>
              <div style={{ display: "flex", color: "#f4f1ec" }}>{row.value}</div>
            </div>
          ))}
        </div>
      </div>
    ),
    size,
  );
}
