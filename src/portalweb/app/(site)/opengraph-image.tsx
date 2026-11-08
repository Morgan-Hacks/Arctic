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
  const { announcement } = siteConfig;

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "center",
          padding: "0 86px",
          // The page's own ground. No gradients or webfonts: this renders in a
          // constrained runtime, and every extra dependency is another way for
          // the build to fail on something nobody looks at.
          background: "#0b0b0c",
          color: "#f2f0ed",
          fontFamily: "sans-serif",
        }}
      >
        <div style={{ display: "flex", width: 44, height: 3, background: "#ff6a2b" }} />

        <div
          style={{
            display: "flex",
            marginTop: 40,
            fontSize: 24,
            letterSpacing: 4,
            color: "#86827d",
          }}
        >
          {announcement.eyebrow.toUpperCase()}
        </div>

        <div style={{ display: "flex", marginTop: 22, fontSize: 96, letterSpacing: -4 }}>
          {announcement.headline}
        </div>

        <div style={{ display: "flex", marginTop: 26, fontSize: 28, color: "#86827d" }}>
          {announcement.line}
        </div>
      </div>
    ),
    size,
  );
}
