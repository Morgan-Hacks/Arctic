"use client";

import { useEffect, useId, useState } from "react";
import { EFFECTS_EVENT, effects, setEffect, transmission } from "./store";

const BADGE_KEY = "mh-heist-badge";
// shortcut: the code and the badge live in the browser, so the badge does not
// follow somebody to another device. Move both to a profile field on the API
// before launch. The code being readable in the bundle is fine: it unlocks a
// cosmetic badge and nothing else.
const CLEARANCE_CODE = "ONEJOB";

/**
 * The signal icon: pauses the code rain, shows the message it was holding, and
 * takes the clearance code that unlocks the Inside the Vault badge.
 *
 * The panel is the only place the rain's text exists outside a canvas, so it is
 * also what a screen reader gets: the message is announced when it opens.
 */
export function SignalPanel() {
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState("");
  const [code, setCode] = useState("");
  const [feedback, setFeedback] = useState<{ text: string; ok: boolean } | null>(null);
  const [badge, setBadge] = useState(false);
  // The messages only exist in the rain, so with it off there is nothing to catch.
  const [rain, setRain] = useState(false);
  const field = useId();

  useEffect(() => {
    try {
      setBadge(localStorage.getItem(BADGE_KEY) === "1");
    } catch {}
    const follow = () => setRain(effects.rain);
    follow();
    window.addEventListener(EFFECTS_EVENT, follow);
    return () => window.removeEventListener(EFFECTS_EVENT, follow);
  }, []);

  function toggle() {
    const next = !open;
    transmission.paused = next;
    if (next) setMessage(transmission.current || transmission.last || "LISTENING…");
    setFeedback(null);
    setOpen(next);
  }

  function submit(event: React.FormEvent) {
    event.preventDefault();
    const value = code.trim().toUpperCase();
    if (!value) {
      setFeedback({ text: "Enter a code first.", ok: false });
      return;
    }
    if (value !== CLEARANCE_CODE) {
      setFeedback({ text: "No match. Keep listening to the rain.", ok: false });
      return;
    }
    try {
      localStorage.setItem(BADGE_KEY, "1");
    } catch {}
    setBadge(true);
    setFeedback({ text: "Clearance granted. Inside the Vault badge added to your profile.", ok: true });
  }

  return (
    <div className="heist-signal">
      {badge ? (
        <span className="heist-badge" title="Inside the Vault">
          Inside the vault
        </span>
      ) : null}
      <button
        type="button"
        className="heist-signal__button"
        onClick={toggle}
        aria-expanded={open}
        aria-label="Intercept transmission"
      >
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M12 13v8M9 21h6M8.5 9.5a5 5 0 0 1 7 0M5.6 6.6a9 9 0 0 1 12.8 0" />
          <circle cx="12" cy="12.5" r="1.2" />
        </svg>
      </button>

      {open ? (
        <section className="heist-signal__panel" aria-label="Intercepted transmission">
          {rain ? (
            <>
              <span className="heist-signal__label">■ Rain paused · intercepted</span>
              <p className="heist-signal__message" aria-live="polite">
                {message}
              </p>
            </>
          ) : (
            <>
              <span className="heist-signal__label">■ No signal</span>
              <p className="heist-signal__quiet">The code rain is off, so there is nothing to intercept. Hidden messages fall with it.</p>
              <button
                type="button"
                className="heist-signal__enable"
                onClick={() => {
                  setEffect("rain", true);
                  // Close so the rain runs; opening the signal again catches a message.
                  transmission.paused = false;
                  setOpen(false);
                }}
              >
                Turn on code rain
              </button>
            </>
          )}
          <form onSubmit={submit} className="heist-signal__form">
            <label htmlFor={field}>Clearance code</label>
            <div>
              <input
                id={field}
                value={code}
                onChange={(e) => {
                  setCode(e.target.value);
                  setFeedback(null);
                }}
                autoComplete="off"
                spellCheck={false}
                maxLength={20}
              />
              <button type="submit">Crack it</button>
            </div>
            <span aria-live="polite" data-ok={feedback?.ok ? "" : undefined}>
              {feedback?.text}
            </span>
          </form>
        </section>
      ) : null}
    </div>
  );
}
