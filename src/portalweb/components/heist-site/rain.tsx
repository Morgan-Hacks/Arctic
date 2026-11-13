"use client";

import { useEffect, useRef } from "react";

const GLYPHS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDEF#$%&*+<>/=";
const FONT_SIZE = 15;
const COLUMN = 12;
const TRAIL = 18;
/** How often an idle column starts a new drop. Low: this is a backdrop, not the subject. */
const RESTART = 0.16;

/**
 * Code rain, fixed behind the whole public site.
 *
 * The portal has its own copy of this in components/heist/backdrop.tsx, and
 * that one is deliberately not reused. It also drives the vault video, the
 * transmission messages and the end of the portal's intro, all of which are
 * tied to the portal frame's grid and its `data-heist-intro` attribute. What
 * this page needs is the rain and nothing else, and the honest way to say that
 * is a file that only does it rather than a prop that turns three quarters of
 * the other one off.
 *
 * Painted at roughly 30fps with the drop step on its own 60ms clock, so the
 * glyph churn does not speed up on a 120Hz display.
 */
export function HeistRain() {
  const canvas = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const c = canvas.current;
    const ctx = c?.getContext("2d");
    if (!c || !ctx) return;

    let cols = 0;
    let rows = 0;
    let grid: string[][] = [];
    let drops: { y: number; v: number; on: boolean }[] = [];
    /*
     * Per-column strength.
     *
     * One flat opacity cannot be right for the whole width. Strong enough to
     * read as code rain and it runs through the body copy, which all sits in
     * the left column; weak enough to leave that copy alone and the effect
     * disappears. So the left is held right back and the right -- where the
     * vault is and where no paragraph ever lands -- carries it, with a ramp
     * between the two so there is no visible seam.
     *
     * The portal's backdrop splits the width the same way for the same
     * reason; see components/heist/backdrop.tsx.
     */
    let dims: number[] = [];
    const glyph = () => GLYPHS[Math.floor(Math.random() * GLYPHS.length)];

    // A canvas cannot read a CSS custom property, and next/font hashes the
    // family name, so the resolved family is taken off the element itself.
    const family = getComputedStyle(c).fontFamily || "monospace";

    const size = () => {
      // Device pixels, so the glyphs are not soft on a retina display. The
      // context is scaled once here rather than every coordinate being
      // multiplied at draw time.
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      c.width = Math.floor(c.offsetWidth * dpr);
      c.height = Math.floor(c.offsetHeight * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      cols = Math.ceil(c.offsetWidth / COLUMN);
      rows = Math.ceil(c.offsetHeight / FONT_SIZE);
      grid = Array.from({ length: cols }, () =>
        Array.from({ length: rows }, glyph),
      );
      drops = Array.from({ length: cols }, () => ({
        y: Math.random() * rows,
        v: 0.3 + Math.random() * 0.4,
        on: Math.random() < 0.55,
      }));
      dims = Array.from({ length: cols }, (_, i) => {
        const x = i / Math.max(1, cols);
        // Narrow screens stack the layout, so there is no quiet side to
        // protect and no loud side to reward: one middling value for all.
        if (cols < 50) return 0.3;
        if (x < 0.46) return 0.2;
        if (x < 0.62) return 0.2 + ((x - 0.46) / 0.16) * 0.4;
        return 0.6;
      });
    };

    const draw = () => {
      ctx.fillStyle = "#071A2B";
      ctx.fillRect(0, 0, c.offsetWidth, c.offsetHeight);
      ctx.textBaseline = "top";
      ctx.font = `${FONT_SIZE}px ${family}`;
      for (let i = 0; i < cols; i++) {
        const d = drops[i];
        if (!d.on) continue;
        const head = Math.floor(d.y);
        for (let k = 0; k <= TRAIL; k++) {
          const r = head - k;
          if (r < 0 || r >= rows) continue;
          if (Math.random() < 0.03) grid[i][r] = glyph();
          // The head of each drop keeps more weight than its trail, so the
          // movement reads as falling rather than as flicker.
          ctx.globalAlpha = (k === 0 ? 0.95 : Math.max(0.1, 1 - k / TRAIL)) * dims[i];
          ctx.fillStyle =
            k === 0 ? "#E9F2F9" : k < 3 ? "#9DBBD2" : k < 8 ? "#5F86A4" : "#16405F";
          ctx.fillText(grid[i][r], i * COLUMN, r * FONT_SIZE);
        }
      }
      ctx.globalAlpha = 1;
    };

    size();

    // Reduced motion gets one still frame. Not a blank canvas: the texture is
    // part of the page, it is the movement that was asked to stop.
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      draw();
      return;
    }

    let frame = 0;
    let drawn = 0;
    let stepped = 0;
    const step = (now: number) => {
      frame = requestAnimationFrame(step);
      if (now - drawn < 33) return;
      drawn = now;
      if (now - stepped >= 60) {
        stepped = now;
        for (const d of drops) {
          if (d.on) {
            d.y += d.v;
            if (d.y - TRAIL > rows) d.on = false;
          } else if (Math.random() < RESTART) {
            d.on = true;
            d.y = -Math.random() * 6;
            d.v = 0.3 + Math.random() * 0.4;
          }
        }
      }
      draw();
    };
    frame = requestAnimationFrame(step);

    // Resizing reallocates the grid, so it is debounced: a drag across the
    // screen would otherwise rebuild it on every frame of the drag.
    let resizeTimer: ReturnType<typeof setTimeout>;
    const onResize = () => {
      clearTimeout(resizeTimer);
      resizeTimer = setTimeout(size, 120);
    };
    window.addEventListener("resize", onResize);
    return () => {
      cancelAnimationFrame(frame);
      clearTimeout(resizeTimer);
      window.removeEventListener("resize", onResize);
    };
  }, []);

  return <canvas ref={canvas} className="heist-site__rain" aria-hidden="true" />;
}
