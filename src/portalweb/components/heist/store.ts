/**
 * What the code rain is showing, shared between the rain and the signal button.
 *
 * A plain module object rather than React state: the rain draws on a canvas
 * twenty times a second and never re-renders, and the signal panel only needs
 * to read the message at the moment somebody opens it.
 */
export const transmission = {
  paused: false,
  /** The message on screen right now, or "" between messages. */
  current: "",
  /** The last message that finished, so pausing between two still has one. */
  last: "",
};

/**
 * Which moving parts somebody has opted into. Rain is opt-in because the
 * ticker, the vault and the rain together read as clutter; the ticker and the
 * vault carry the theme on their own. Kept per browser: it is a viewing
 * preference, not part of anybody's profile.
 */
export type Effects = { rain: boolean; vault: boolean; ticker: boolean };
export const DEFAULT_EFFECTS: Effects = { rain: false, vault: true, ticker: true };
export const effects: Effects = { ...DEFAULT_EFFECTS };
const EFFECTS_KEY = "mh-heist-effects";
export const EFFECTS_EVENT = "heist-effects";

/** Reads the saved choice, keeping only the three known switches as booleans. */
export function loadEffects(): Effects {
  try {
    const saved = JSON.parse(localStorage.getItem(EFFECTS_KEY) ?? "{}");
    for (const key of Object.keys(DEFAULT_EFFECTS) as (keyof Effects)[]) {
      if (typeof saved?.[key] === "boolean") effects[key] = saved[key];
    }
  } catch {}
  applyEffects();
  return effects;
}

export function setEffect(key: keyof Effects, on: boolean) {
  effects[key] = on;
  try {
    localStorage.setItem(EFFECTS_KEY, JSON.stringify(effects));
  } catch {}
  applyEffects();
  window.dispatchEvent(new Event(EFFECTS_EVENT));
}

/** Mirrors the switches onto the frame so CSS can stop what it animates (the ticker). */
function applyEffects() {
  const frame = document.querySelector(".heist");
  for (const key of Object.keys(effects) as (keyof Effects)[]) {
    frame?.setAttribute(`data-${key}`, effects[key] ? "on" : "off");
  }
}

/**
 * The lines the rain spells out. Role words nod at finding teammates; the
 * phrases are the "intercepted" ones. One cycle in eight carries the
 * clearance code.
 */
export const MESSAGES = [
  "THE CREW IS THE KEY",
  "DESIGNER",
  "TRUST YOUR TEAM",
  "BACKEND",
  "WHO'S ON YOUR SIDE?",
  "PITCH",
  "FRONTEND",
  "CODE: ONEJOB",
];
