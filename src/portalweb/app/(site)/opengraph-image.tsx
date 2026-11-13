import { ImageResponse } from "next/og";
import { siteConfig } from "@/site.config";
import { EVENT_SITE, isOn } from "@/lib/features";

// Generated at build time rather than shipped as a PNG, so the copy comes from
// site.config.ts like everything else and cannot drift out of step with the
// page -- which is exactly what happened while this card was still announcing
// a drive the page had stopped running.
export const size = { width: 1200, height: 630 };
export const contentType = "image/png";
export const alt = `MorganHacks ${siteConfig.year}`;

/**
 * The share card for the event site.
 *
 * Its own card rather than a tweaked version of the holding page's, because
 * the two pages have different grounds, different accents and different
 * things to say -- and this is the image that renders in the preview of every
 * link anybody pastes, so it is the page's first impression more often than
 * the page is.
 *
 * Left-aligned on a rule, which is the composition the site itself uses. No
 * webfonts and no gradients: this renders in a constrained runtime at build
 * time, and every dependency is another way for the build to fail on
 * something nobody is looking at.
 */
function EventCard() {
  const { edition } = siteConfig;

  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "center",
          padding: "0 90px",
          background: "#0d0f15",
          color: "#ecebe6",
          fontFamily: "sans-serif",
        }}
      >
        <div style={{ display: "flex", width: 56, height: 4, background: "#c9f24d" }} />

        <div
          style={{
            display: "flex",
            marginTop: 40,
            fontSize: 24,
            letterSpacing: 5,
            color: "#8d94a6",
          }}
        >
          {edition.eyebrow.toUpperCase()}
        </div>

        <div
          style={{
            display: "flex",
            flexDirection: "column",
            marginTop: 26,
            fontSize: 78,
            lineHeight: 1.06,
            letterSpacing: -3,
          }}
        >
          <div style={{ display: "flex" }}>{edition.headlineTop}</div>
          <div style={{ display: "flex" }}>{edition.headlineBottom}</div>
        </div>

        <div
          style={{
            display: "flex",
            marginTop: 38,
            fontSize: 26,
            color: "#8d94a6",
          }}
        >
          Morgan State University, Baltimore
        </div>
      </div>
    ),
    size,
  );
}

export default function OpengraphImage() {
  // The flag decides which page `/` is, so it has to decide which card that
  // page shares. While this was missing, the whole event site was being pasted
  // into group chats with a card announcing that the next hackathon was to be
  // announced.
  if (isOn(EVENT_SITE)) return EventCard();

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
