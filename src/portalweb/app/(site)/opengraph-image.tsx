import { ImageResponse } from "next/og";
import { siteConfig } from "@/site.config";
import { HEIST_THEME, isOn } from "@/lib/features";

// Generated at build time rather than shipped as a PNG, so the copy comes from
// site.config.ts like everything else and cannot drift out of step with the
// page -- which is exactly what happened while this card was still announcing
// a drive the page had stopped running.
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";
export const alt = `MorganHacks ${siteConfig.year}`;

/**
 * The Heist card, behind the same flag as the site.
 *
 * Flat colour and the system face, exactly like the holding page's card below:
 * this renders in a constrained runtime at build time, and a webfont or a
 * gradient is one more way for a build to fail on an image nobody is looking
 * at while it breaks.
 *
 * It is a separate function rather than a branch inside one set of markup
 * because the two cards share nothing but their dimensions -- different
 * ground, different accent, different copy, different composition.
 */
function HeistCard() {
  const { anniversary, year } = siteConfig;

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
          background: "#071a2b",
          color: "#e9f2f9",
          fontFamily: "sans-serif",
        }}
      >
        <div style={{ display: "flex", width: 54, height: 4, background: "#ff7a1a" }} />

        <div
          style={{
            display: "flex",
            marginTop: 34,
            fontSize: 24,
            letterSpacing: 6,
            color: "#ff7a1a",
          }}
        >
          MORGANHACKS {year}
        </div>

        <div style={{ display: "flex", marginTop: 18, fontSize: 96, letterSpacing: -3 }}>
          {anniversary.headlineTop}
        </div>
        <div
          style={{
            display: "flex",
            fontSize: 96,
            letterSpacing: -3,
            color: "#ff7a1a",
          }}
        >
          {anniversary.headlineBottom}
        </div>

        <div
          style={{
            display: "flex",
            marginTop: 26,
            maxWidth: 800,
            fontSize: 28,
            color: "#9dbbd2",
          }}
        >
          Morgan State University. April 2027. Free for students.
        </div>
      </div>
    ),
    size,
  );
}

export default function OpengraphImage() {
  if (isOn(HEIST_THEME)) return HeistCard();

  const { announcement } = siteConfig;

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
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
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            textAlign: "center",
          }}
        >
          <div style={{ display: "flex", width: 44, height: 3, background: "#ff6a2b" }} />

          <div
            style={{
              display: "flex",
              marginTop: 38,
              fontSize: 23,
              letterSpacing: 5,
              color: "#8a8681",
            }}
          >
            {announcement.eyebrow.toUpperCase()}
          </div>

          <div style={{ display: "flex", marginTop: 20, fontSize: 94, letterSpacing: -4 }}>
            {announcement.headline}
          </div>

          <div
            style={{
              display: "flex",
              marginTop: 24,
              maxWidth: 740,
              fontSize: 27,
              color: "#8a8681",
            }}
          >
            {announcement.line}
          </div>
        </div>
      </div>
    ),
    size,
  );
}
