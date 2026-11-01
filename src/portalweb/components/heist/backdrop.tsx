"use client";

import { useEffect, useRef } from "react";
import { EFFECTS_EVENT, MESSAGES, effects, loadEffects, transmission } from "./store";

const GLYPHS = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDEF#$%&*+<>/=";
const FONT_SIZE = 15;
const COLUMN = 12;
const TRAIL = 20;
// Frames, at one step every 60ms: fade in, hold, fade out, then a gap.
const FADE_IN = 10;
const HOLD = 44;
const FADE_OUT = 14;
const GAP = 28;
/** How often an idle column starts a new drop. Moderate: the vault must still read. */
const RESTART = 0.2;

/**
 * The live layer behind every Heist screen: code rain on a canvas and the vault
 * loop, plus the end of the intro that plays on each full page load.
 *
 * The vault video is drawn into the rain's canvas with a "screen" blend rather
 * than layered over it in CSS. The loop is rendered on black, and Chrome
 * composites a playing video as its own layer, so mix-blend-mode never reaches
 * the rain behind it and the black shows as a box. The <video> stays in the
 * page, invisible, as the decoder and as the box whose position, intro
 * transform and opacity the canvas copies every frame.
 *
 * The intro starts before this component exists. An inline script in the
 * portal layout sets `data-heist-intro` on the portal frame during parsing, so the first paint
 * already has the page hidden and the vault arriving; this component only takes
 * the class off once the animation is done. Doing
 * it here instead would paint the page, hide it on hydration and fade it back.
 *
 * Each moving part is opt-in or opt-out (see `effects` in the store): the rain
 * is off unless somebody turns it on, and with vault motion off the loop holds
 * still and the assembly intro is skipped.
 */
export function HeistBackdrop() {
  const canvas = useRef<HTMLCanvasElement>(null);
  const video = useRef<HTMLVideoElement>(null);
  const assemble = useRef<HTMLVideoElement>(null);
  /** Whichever clip the canvas is drawing: the assembly on an intro, the loop after. */
  const active = useRef<HTMLVideoElement | null>(null);
  const vault = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const loop = video.current;
    const build = assemble.current;
    if (!loop) return;
    // React does not reflect `muted` to the attribute, and a video that is not
    // muted by the time autoplay is decided does not autoplay.
    loop.muted = true;
    if (build) build.muted = true;

    const root = document.querySelector(".heist");
    loadEffects();
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      active.current = loop;
      loop.pause();
      return;
    }

    // Switching vault motion from the effects panel starts or stops the loop.
    const follow = () => {
      if (active.current !== loop) return;
      if (effects.vault) loop.play().catch(() => {});
      else loop.pause();
    };
    window.addEventListener(EFFECTS_EVENT, follow);

    const finish = () => {
      root?.removeAttribute("data-heist-intro");
      build?.pause();
      active.current = loop;
      follow();
    };
    if (!build || !effects.vault || !root?.hasAttribute("data-heist-intro")) {
      finish();
      return () => window.removeEventListener(EFFECTS_EVENT, follow);
    }

    // The vault builds itself part by part; the page arrives as it locks (the
    // delays are in heist.css). A click or key skips straight to the end.
    active.current = build;
    build.playbackRate = 1.25;
    build.play().catch(finish);
    build.addEventListener("ended", finish, { once: true });
    const timer = setTimeout(finish, 7000);
    window.addEventListener("pointerdown", finish, { once: true });
    window.addEventListener("keydown", finish, { once: true });
    return () => {
      clearTimeout(timer);
      window.removeEventListener("pointerdown", finish);
      window.removeEventListener("keydown", finish);
      window.removeEventListener(EFFECTS_EVENT, follow);
    };
  }, []);

  useEffect(() => {
    const c = canvas.current;
    const ctx = c?.getContext("2d");
    if (!c || !ctx) return;

    let cols = 0;
    let rows = 0;
    let grid: string[][] = [];
    let drops: { y: number; v: number; on: boolean }[] = [];
    // Per column: quieter behind the content on the left, stronger around the vault.
    let dims: number[] = [];
    let message = { text: "", row: 0, col: 0, t: 0 };
    let index = 0;
    const glyph = () => GLYPHS[Math.floor(Math.random() * GLYPHS.length)];
    // A canvas cannot read CSS variables, and next/font hashes the family name,
    // so take the resolved Courier Prime family from the canvas element itself.
    const family = getComputedStyle(c).fontFamily || "monospace";

    const size = () => {
      c.width = c.offsetWidth;
      c.height = c.offsetHeight;
      cols = Math.floor(c.width / COLUMN);
      rows = Math.floor(c.height / FONT_SIZE);
      grid = Array.from({ length: cols }, () => Array.from({ length: rows }, glyph));
      drops = Array.from({ length: cols }, () => ({ y: Math.random() * rows, v: 0.35 + Math.random() * 0.45, on: Math.random() < 0.7 }));
      dims = Array.from({ length: cols }, (_, i) => {
        const x = i / Math.max(1, cols);
        return cols < 50 ? 0.6 : x < 0.46 ? 0.38 : x < 0.64 ? 0.38 + ((x - 0.46) / 0.18) * 0.42 : 0.8;
      });
    };

    const next = () => {
      const text = MESSAGES[index++ % MESSAGES.length];
      // Keep messages in open space: below the content on a phone, otherwise
      // the band under the header or the right-hand side where the vault is.
      if (cols < 50) {
        message = { text, row: rows - 4 - Math.floor(Math.random() * 3), col: Math.max(0, Math.floor((cols - text.length) / 2)), t: 0 };
        return;
      }
      const top = Math.random() < 0.4;
      const row = top ? 9 + Math.floor(Math.random() * 5) : 14 + Math.floor(Math.random() * Math.max(1, rows - 22));
      const min = Math.floor(cols * (top ? 0.3 : 0.58));
      message = { text, row, col: min + Math.floor(Math.random() * Math.max(1, cols - min - text.length - 2)), t: 0 };
    };

    const draw = () => {
      ctx.fillStyle = "#071A2B";
      ctx.fillRect(0, 0, c.width, c.height);
      ctx.textBaseline = "top";
      ctx.font = `${FONT_SIZE}px ${family}`;
      for (let i = 0; effects.rain && i < cols; i++) {
        const d = drops[i];
        if (!d.on) continue;
        const head = Math.floor(d.y);
        for (let k = 0; k <= TRAIL; k++) {
          const r = head - k;
          if (r < 0 || r >= rows) continue;
          if (Math.random() < 0.03) grid[i][r] = glyph();
          ctx.globalAlpha = (k === 0 ? 1 : Math.max(0.12, 1 - k / TRAIL)) * dims[i];
          ctx.fillStyle = k === 0 ? "#E9F2F9" : k < 3 ? "#9DBBD2" : k < 8 ? "#5F86A4" : "#16405F";
          ctx.fillText(grid[i][r], i * COLUMN, r * FONT_SIZE);
        }
      }
      ctx.globalAlpha = 1;

      const v = active.current;
      const box = vault.current;
      if (v && box && v.readyState >= 2) {
        const r = box.getBoundingClientRect();
        ctx.globalCompositeOperation = "screen";
        ctx.globalAlpha = Number(getComputedStyle(box).opacity);
        ctx.drawImage(v, r.left, r.top, r.width, r.height);
        ctx.globalCompositeOperation = "source-over";
        ctx.globalAlpha = 1;
      }

      const { text, row, col, t } = message;
      const visible = effects.rain && t > 0 && t < FADE_IN + HOLD + FADE_OUT;
      transmission.current = visible ? text : "";
      if (!visible) return;
      const alpha = t < FADE_IN ? t / FADE_IN : t < FADE_IN + HOLD ? 1 : 1 - (t - FADE_IN - HOLD) / FADE_OUT;
      ctx.font = `bold ${FONT_SIZE}px ${family}`;
      for (let k = 0; k < text.length; k++) {
        const x = (col + k) * COLUMN;
        const y = row * FONT_SIZE;
        ctx.globalAlpha = alpha * 0.92;
        ctx.fillStyle = "#071A2B";
        ctx.fillRect(x - 1, y - 1, COLUMN + 1, FONT_SIZE + 2);
        if (text[k] === " ") continue;
        ctx.globalAlpha = alpha;
        ctx.fillStyle = text.startsWith("CODE") ? "#FF7A1A" : "#FFFFFF";
        ctx.fillText(text[k], x, y);
      }
      ctx.globalAlpha = 1;
    };

    size();
    next();

    // Reduced motion gets one still frame, vault included, with a message held on it.
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      message.t = FADE_IN;
      draw();
      video.current?.addEventListener("loadeddata", draw, { once: true });
      return;
    }

    let frame = 0;
    let drawn = 0;
    let stepped = 0;
    // With the rain off and the vault still, nothing on the canvas moves, so it
    // is drawn once per change instead of thirty times a second.
    let dirty = true;
    const redraw = () => {
      dirty = true;
    };
    const step = (now: number) => {
      frame = requestAnimationFrame(step);
      // ~30fps for the vault, the rain itself moves every 60ms.
      if (now - drawn < 33) return;
      drawn = now;
      if (!effects.rain && (active.current?.paused ?? true)) {
        if (dirty) draw();
        dirty = false;
        return;
      }
      if (transmission.paused) {
        // Hold whatever was showing, fully visible, so the panel and the rain
        // agree. The vault keeps turning; only the rain stops.
        if (message.t !== FADE_IN) {
          if (message.t === 0 || message.t >= FADE_IN + HOLD + FADE_OUT) message = { ...message, text: transmission.last || message.text };
          message.t = FADE_IN;
        }
        draw();
        return;
      }
      if (now - stepped < 60) {
        draw();
        return;
      }
      stepped = now;
      for (const d of drops) {
        if (d.on) {
          d.y += d.v;
          if (d.y - TRAIL > rows) d.on = false;
        } else if (Math.random() < RESTART) {
          d.on = true;
          d.y = -Math.random() * 6;
          d.v = 0.35 + Math.random() * 0.45;
        }
      }
      message.t++;
      if (message.t > FADE_IN + HOLD + FADE_OUT + GAP) {
        transmission.last = message.text;
        next();
      }
      draw();
    };
    frame = requestAnimationFrame(step);
    const resize = () => {
      size();
      redraw();
    };
    window.addEventListener("resize", resize);
    window.addEventListener(EFFECTS_EVENT, redraw);
    video.current?.addEventListener("loadeddata", redraw);
    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener("resize", resize);
      window.removeEventListener(EFFECTS_EVENT, redraw);
    };
  }, []);

  return (
    <div className="heist-backdrop" aria-hidden="true">
      <canvas ref={canvas} className="heist-rain" />
      <div className="heist-vault" ref={vault}>
        <video ref={assemble} src="/heist/vault-assemble.mp4" muted playsInline preload="auto" />
        <video
          ref={video}
          src="/heist/vault-loop.mp4"
          poster="/heist/vault-poster.jpg"
          muted
          loop
          playsInline
          preload="auto"
        />
      </div>
    </div>
  );
}
