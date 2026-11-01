"use client";

import { useEffect, useRef, useState } from "react";
import { DEFAULT_EFFECTS, effects, loadEffects, setEffect, type Effects } from "./store";

const SWITCHES: { key: keyof Effects; label: string; hint: string }[] = [
  { key: "rain", label: "Code rain", hint: "Falling code with hidden messages" },
  { key: "vault", label: "Vault motion", hint: "The turning vault behind the page" },
  { key: "ticker", label: "Ticker", hint: "The scrolling banner up top" },
];

/**
 * Opt in or out of the theme's moving parts. The rain starts off; the ticker
 * and the vault start on. Saved in this browser only.
 */
export function EffectsPanel() {
  const [open, setOpen] = useState(false);
  const [state, setState] = useState<Effects>(DEFAULT_EFFECTS);
  const [reduced, setReduced] = useState(false);
  const box = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setState({ ...loadEffects() });
    setReduced(window.matchMedia("(prefers-reduced-motion: reduce)").matches);
  }, []);

  useEffect(() => {
    if (!open) return;
    const away = (e: MouseEvent) => {
      if (!box.current?.contains(e.target as Node)) setOpen(false);
    };
    const esc = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    document.addEventListener("mousedown", away);
    document.addEventListener("keydown", esc);
    return () => {
      document.removeEventListener("mousedown", away);
      document.removeEventListener("keydown", esc);
    };
  }, [open]);

  function flip(key: keyof Effects) {
    setEffect(key, !effects[key]);
    setState({ ...effects });
  }

  return (
    <div className="heist-effects" ref={box}>
      <button type="button" className="heist-effects__button" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" aria-hidden="true">
          <path d="M4 7h10M18 7h2M4 17h4M12 17h8" />
          <circle cx="16" cy="7" r="2" />
          <circle cx="10" cy="17" r="2" />
        </svg>
        <span className="heist-effects__label">Effects</span>
      </button>
      {open ? (
        <section className="heist-effects__panel" aria-label="Effects">
          {reduced ? <p className="heist-effects__note">Your device asks for reduced motion, so everything stays still.</p> : null}
          {SWITCHES.map(({ key, label, hint }) => (
            <button key={key} type="button" role="switch" aria-checked={state[key]} className="heist-switch" disabled={reduced} onClick={() => flip(key)}>
              <span className="heist-switch__text">
                <b>{label}</b>
                <small>{hint}</small>
              </span>
              <span className="heist-switch__track" aria-hidden="true">
                <i />
              </span>
            </button>
          ))}
          <p className="heist-effects__note">Saved in this browser.</p>
        </section>
      ) : null}
    </div>
  );
}
