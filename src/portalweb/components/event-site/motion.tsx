"use client";

import { useEffect, useRef, useState } from "react";

/**
 * The motion that needs a browser, as small client islands.
 *
 * Most of what moves on this site moves in CSS -- the words of the headline
 * arriving, the ticker, the searchlight over the skyline, the scroll bar
 * under the masthead, the laser rules drawing themselves in as each section
 * is scrolled to. None of that is in here, because none of it needs state.
 *
 * What is in here is the handful of pieces that have to read something only
 * the browser knows: the time (the rotating word), the scroll position (the
 * reveals and the counters), and the pointer (the dial and the torch).
 * Everything else stays a server component, and each of these renders its
 * finished state on the server first -- the rotating word starts on a real
 * word, the counters start on their real number. With JavaScript off, or
 * before it loads, the page reads correctly and simply does not move.
 *
 * Every one checks `prefers-reduced-motion` and does nothing when it is set.
 * Not reduced motion, none: a word swapping itself out is exactly what that
 * setting is asking you not to do.
 */

function usesReducedMotion() {
  return (
    typeof window !== "undefined" &&
    window.matchMedia("(prefers-reduced-motion: reduce)").matches
  );
}

/**
 * Fires once when the element first comes into view.
 *
 * Shared by the reveal and the counters, because they want the same thing and
 * two observers watching the same element would be two answers to one
 * question.
 */
function useInView<T extends HTMLElement>(enabled = true) {
  const ref = useRef<T | null>(null);
  const [seen, setSeen] = useState(!enabled);

  useEffect(() => {
    if (!enabled || seen) return;
    const el = ref.current;
    if (!el) return;

    const io = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          setSeen(true);
          io.disconnect();
        }
      },
      // A little before the edge, so the thing has finished moving by the
      // time it is properly on screen rather than starting then.
      { rootMargin: "0px 0px -12% 0px", threshold: 0.15 },
    );

    io.observe(el);
    return () => io.disconnect();
  }, [enabled, seen]);

  return { ref, seen };
}

/**
 * A word that changes, with a caret after it.
 *
 * The sentence around it is fixed and only the last word moves, which is the
 * one place on the page where motion is carrying meaning rather than
 * decorating: it is saying that what people build here is not one thing.
 */
export function RotatingWord({ words }: { words: readonly string[] }) {
  const [i, setI] = useState(0);
  const [out, setOut] = useState(false);

  useEffect(() => {
    if (usesReducedMotion() || words.length < 2) return;

    // Two timers: one to start the word leaving, one to swap it and bring the
    // next in. Swapping on a single interval makes the exit and the entrance
    // the same frame, which reads as a flicker rather than a change.
    const hold = window.setInterval(() => {
      setOut(true);
      window.setTimeout(() => {
        setI((n) => (n + 1) % words.length);
        setOut(false);
      }, 260);
    }, 2600);

    return () => window.clearInterval(hold);
  }, [words.length]);

  return (
    <span className="es-rot">
      <span className={`es-rot__word${out ? " es-rot__word--out" : ""}`}>{words[i]}</span>
      <span className="es-rot__caret" aria-hidden="true" />
    </span>
  );
}

/**
 * A number that counts up the first time it is seen.
 *
 * Only the digits animate. "$0" and "2023" still have to arrive as "$0" and
 * "2023", so the prefix and suffix are split off and left alone rather than
 * the whole string being treated as a number.
 */
export function CountUp({ value }: { value: string }) {
  const match = value.match(/^(\D*)(\d[\d,]*)(.*)$/);
  const target = match ? Number(match[2].replace(/,/g, "")) : NaN;

  // Group only if the written value was grouped. "2023" is a year and
  // toLocaleString turns it into "2,023", which is wrong and was shipped
  // once; "$18,000" would want its comma back.
  const grouped = match ? match[2].includes(",") : false;
  const animatable = match !== null && Number.isFinite(target) && target > 0;

  const { ref, seen } = useInView<HTMLSpanElement>(animatable);
  const [n, setN] = useState<number | null>(null);

  useEffect(() => {
    if (!animatable || !seen || usesReducedMotion()) return;

    let raf = 0;
    const started = performance.now();
    // Long enough to read as counting, short enough that nobody waits for it.
    const ms = 1100;

    const tick = (now: number) => {
      const t = Math.min(1, (now - started) / ms);
      // Ease out: fast at the start, settling at the end, which is how a
      // counter that is arriving somewhere behaves.
      const eased = 1 - Math.pow(1 - t, 3);
      setN(Math.round(target * eased));
      if (t < 1) raf = requestAnimationFrame(tick);
    };

    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [animatable, seen, target]);

  if (!animatable || n === null) return <span ref={ref}>{value}</span>;

  return (
    <span ref={ref}>
      {match![1]}
      {grouped ? n.toLocaleString("en-US") : String(n)}
      {match![3]}
    </span>
  );
}

/**
 * Lifts its children into place the first time they are scrolled to.
 *
 * One gesture, and it never runs twice. A page where every section animates
 * on every pass is a page that is hard to read.
 *
 * `group` moves the items inside instead of the block itself, one after the
 * next -- the three parts of the weekend, the five editions on the wall. The
 * stagger lives in CSS, keyed off `:nth-child`, so this component still only
 * has to answer the one question it can answer: has this been seen yet. The
 * wrapper itself sits still in that mode, because a block that rises while
 * its children are also rising is two gestures fighting over one object.
 */
export function Reveal({
  children,
  delay = 0,
  group = false,
}: {
  children: React.ReactNode;
  delay?: number;
  group?: boolean;
}) {
  const { ref, seen } = useInView<HTMLDivElement>(true);

  return (
    <div
      ref={ref}
      className={[
        "es-reveal",
        group ? "es-reveal--group" : "",
        seen ? "es-reveal--in" : "",
      ]
        .filter(Boolean)
        .join(" ")}
      style={delay ? { transitionDelay: `${delay}ms` } : undefined}
    >
      {children}
    </div>
  );
}

/**
 * The vault dial.
 *
 * The one piece of real dimension on the page: four rings stacked in 3D,
 * tilted, each turning at its own rate. A heist needs a lock, and a lock that
 * is actually rotating in space says more about the event being built by
 * people who can build things than any stock illustration would.
 *
 * Client-side only because it reads the pointer. It renders the whole dial on
 * the server and simply does not respond to the mouse until hydrated, so it
 * is never missing and never pops in.
 */
export function VaultDial() {
  const ref = useRef<HTMLDivElement | null>(null);
  const [tilt, setTilt] = useState({ x: 0, y: 0 });

  useEffect(() => {
    if (usesReducedMotion()) return;
    const el = ref.current;
    if (!el) return;

    // Parented to the window rather than to the dial, so it leans towards the
    // pointer from anywhere on the page instead of only once you are on top
    // of it, which is when you can no longer see it move.
    const onMove = (e: PointerEvent) => {
      const r = el.getBoundingClientRect();
      const cx = r.left + r.width / 2;
      const cy = r.top + r.height / 2;
      setTilt({
        x: Math.max(-1, Math.min(1, (e.clientY - cy) / (window.innerHeight / 2))),
        y: Math.max(-1, Math.min(1, (e.clientX - cx) / (window.innerWidth / 2))),
      });
    };

    window.addEventListener("pointermove", onMove, { passive: true });
    return () => window.removeEventListener("pointermove", onMove);
  }, []);

  return (
    <div
      ref={ref}
      className="es-dial"
      aria-hidden="true"
      style={
        {
          "--tx": `${-tilt.x * 9}deg`,
          "--ty": `${tilt.y * 9}deg`,
        } as React.CSSProperties
      }
    >
      <div className="es-dial__stage">
        <span className="es-dial__ring es-dial__ring--1" />
        <span className="es-dial__ring es-dial__ring--2" />
        <span className="es-dial__ring es-dial__ring--3" />
        <span className="es-dial__ticks" />
        <span className="es-dial__hub">
          <span className="es-dial__spoke" />
          <span className="es-dial__spoke" />
          <span className="es-dial__spoke" />
        </span>
      </div>
    </div>
  );
}

/**
 * The torch.
 *
 * A warm pool of light that follows the pointer across the whole page, mixed
 * into the room with `screen` so it brightens what it crosses rather than
 * sitting on top of it as a grey disc. Somebody is in here with a flashlight,
 * which is the one thing a heist needs that a static background cannot say.
 *
 * Deliberately cheap. The listener writes two custom properties and nothing
 * else; the gradient is painted by the compositor from those two numbers, so
 * moving the mouse across the page does no layout, no React render and no
 * style recalculation beyond the one element. Coalesced to one write per
 * frame, because a pointer can fire faster than the screen refreshes and the
 * extra writes would be thrown away.
 *
 * It starts invisible and fades in on the first movement. On a touch screen
 * there is no pointer to follow, so that movement never comes and the layer
 * stays exactly where it started, which is gone.
 */
export function Torch() {
  const ref = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (usesReducedMotion()) return;
    const el = ref.current;
    if (!el) return;

    let raf = 0;
    let x = 0;
    let y = 0;

    const paint = () => {
      raf = 0;
      el.style.setProperty("--mx", `${x}px`);
      el.style.setProperty("--my", `${y}px`);
    };

    const onMove = (e: PointerEvent) => {
      x = e.clientX;
      y = e.clientY;
      el.classList.add("es-torch--on");
      if (!raf) raf = requestAnimationFrame(paint);
    };

    window.addEventListener("pointermove", onMove, { passive: true });
    return () => {
      window.removeEventListener("pointermove", onMove);
      if (raf) cancelAnimationFrame(raf);
    };
  }, []);

  return <div ref={ref} className="es-torch" aria-hidden="true" />;
}
