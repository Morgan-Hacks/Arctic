"use client";

import { useEffect, useRef } from "react";

/**
 * The vault, drawn into a canvas rather than placed as a video.
 *
 * The loop in public/heist is rendered on black. Dropped into the page as a
 * <video> it is a black square on a navy ground, and the usual fix --
 * `mix-blend-mode: screen` -- does not work: Chrome promotes a playing video
 * to its own compositing layer, so the blend never reaches what is behind it.
 * The portal hit this first; see components/heist/backdrop.tsx, which solves it
 * the same way for the same reason.
 *
 * Drawing each frame into a canvas with `globalCompositeOperation = "screen"`
 * does the blend in the 2D context instead, where there is no layer promotion
 * to get in the way. The <video> stays in the tree, hidden, as the decoder.
 */
export function HeistVault() {
  const canvas = useRef<HTMLCanvasElement>(null);
  const video = useRef<HTMLVideoElement>(null);

  useEffect(() => {
    const c = canvas.current;
    const v = video.current;
    const ctx = c?.getContext("2d");
    if (!c || !v || !ctx) return;

    // React does not reflect `muted` to the attribute, and a video that is not
    // muted by the time autoplay is decided does not autoplay.
    v.muted = true;

    const size = () => {
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      c.width = Math.floor(c.offsetWidth * dpr);
      c.height = Math.floor(c.offsetHeight * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    };

    const draw = () => {
      const w = c.offsetWidth;
      const h = c.offsetHeight;
      // The navy ground goes down first, and it is not optional. `screen`
      // against a transparent canvas is a no-op -- the source is simply kept,
      // black included -- so clearing and screening paints the loop's black
      // ground as an opaque box. Screening needs something opaque underneath
      // to lighten, and this is the same colour the page is painted in, so
      // what survives the blend sits on exactly the page's own ground.
      ctx.globalCompositeOperation = "source-over";
      ctx.fillStyle = "#071A2B";
      ctx.fillRect(0, 0, w, h);
      if (v.readyState < 2) return;
      ctx.globalCompositeOperation = "screen";
      ctx.drawImage(v, 0, 0, w, h);
      ctx.globalCompositeOperation = "source-over";
    };

    size();

    const still = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (still) {
      // One frame, held. The vault is the event's mark; a reader who asked for
      // less motion should still see it, just not turning.
      v.addEventListener("loadeddata", draw, { once: true });
      if (v.readyState >= 2) draw();
      return;
    }

    v.play().catch(() => {
      // Autoplay refused. Paint whatever frame the decoder has so the plate is
      // the vault rather than an empty box.
      v.addEventListener("loadeddata", draw, { once: true });
    });

    let frame = 0;
    let drawn = 0;
    const step = (now: number) => {
      frame = requestAnimationFrame(step);
      if (now - drawn < 33) return;
      drawn = now;
      draw();
    };
    frame = requestAnimationFrame(step);

    window.addEventListener("resize", size);
    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener("resize", size);
    };
  }, []);

  return (
    <div className="heist-vaultplate">
      <canvas ref={canvas} />
      <video
        ref={video}
        src="/heist/vault-loop.mp4"
        poster="/heist/vault-poster.jpg"
        muted
        loop
        playsInline
        preload="auto"
        aria-hidden="true"
        style={{ position: "absolute", width: 1, height: 1, opacity: 0, pointerEvents: "none" }}
      />
    </div>
  );
}
