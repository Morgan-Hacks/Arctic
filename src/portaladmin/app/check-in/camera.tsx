"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import styles from "./desk.module.css";

/**
 * The camera, reading the QR symbol on a hacker's portal screen.
 *
 * ## Why there is no QR library in package.json
 *
 * `BarcodeDetector` is in the browser already, on Chrome and on Android, which
 * is what a volunteer at a door is holding. Everything it reads goes to the
 * same server action a typed code goes to, so the camera is an input method
 * and not a second way into the API.
 *
 * It does not exist in Safari or Firefox, and that is not a reason to ship a
 * decoder of our own: a desk that needs a specific browser is a desk that
 * fails on the morning somebody opens it on the wrong laptop. So the camera is
 * an offer and never a requirement -- where the browser has no detector this
 * component renders a sentence and the typed input beside it does the whole
 * job, exactly as it did before any of this existed. A third-party decoder
 * would buy those two browsers at the cost of a dependency on the one screen
 * where a broken bundle means a queue out of the door, and the fallback it
 * would replace is the one the portal already prints the code underneath the
 * symbol for.
 *
 * ## Why it never starts on its own
 *
 * `getUserMedia` needs a secure context and, in practice, a gesture: asked for
 * on page load it is a permission prompt nobody invited, and a declined prompt
 * is sticky -- the volunteer who said no at seven in the morning cannot easily
 * say yes at eight. It also lights a camera in a room full of people who did
 * not ask for one. So: a button to start, a loud indicator while it is live,
 * and a stop button that is the largest thing in the panel while it runs.
 *
 * Stopping matters more than starting. A camera left on is a camera pointed at
 * a queue of faces for the rest of the day, so the tracks are stopped when the
 * volunteer says so and again when this component goes away, which includes
 * navigating off the screen.
 */

/** One thing the detector read out of a frame. */
type DetectedBarcode = { rawValue: string };

type Detector = {
  detect(source: HTMLVideoElement): Promise<DetectedBarcode[]>;
};

type DetectorConstructor = {
  new (options?: { formats?: string[] }): Detector;
  getSupportedFormats?(): Promise<string[]>;
};

declare global {
  interface Window {
    /**
     * Absent on Safari and Firefox, which is the case this component is
     * shaped around rather than the exception to it. Optional on the type
     * for the same reason: the compiler should make it impossible to write
     * code here that assumes a camera.
     */
    BarcodeDetector?: DetectorConstructor;
  }
}

/**
 * How often a frame is looked at.
 *
 * Four times a second. Decoding is work, and a phone held up to a lens stays
 * there for a second or two -- a reader that runs on every animation frame
 * spends sixty decodes to find what four would, and on the laptop at the desk
 * that is a fan spinning up next to a queue.
 */
const LOOK_MS = 250;

/**
 * How long the same symbol is ignored after it has been read.
 *
 * The phone does not leave the lens the moment the code is read, so without
 * this one presentation is a dozen submissions of the same code. Long enough
 * to cover a person stepping away, short enough that somebody re-presenting a
 * code on purpose -- because the first attempt failed on a dead network -- is
 * not told to wait.
 */
const REPEAT_MS = 4_000;

type Live = "off" | "starting" | "on";

export function Camera({
  onCode,
  busy,
}: {
  /** The same path a typed code takes. Never a second call to the API. */
  onCode: (code: string) => void;

  /** A scan is in flight, so frames are not worth looking at yet. */
  busy: boolean;
}) {
  const video = useRef<HTMLVideoElement>(null);
  const stream = useRef<MediaStream | null>(null);
  const detector = useRef<Detector | null>(null);
  const looking = useRef(false);
  const last = useRef<{ code: string; at: number } | null>(null);

  // Settled after mount, never during the render. The server has no
  // `window`, so anything that asks about a camera while rendering produces
  // markup the browser then disagrees with -- which React reports as a
  // hydration error and a volunteer experiences as the panel flickering.
  const [supported, setSupported] = useState<boolean | null>(null);
  const [live, setLive] = useState<Live>("off");
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    setSupported(
      typeof window !== "undefined" &&
        window.isSecureContext &&
        typeof window.BarcodeDetector === "function" &&
        typeof navigator.mediaDevices?.getUserMedia === "function",
    );
  }, []);

  const stop = useCallback(() => {
    looking.current = false;
    detector.current = null;

    for (const track of stream.current?.getTracks() ?? []) {
      track.stop();
    }
    stream.current = null;

    if (video.current) {
      video.current.srcObject = null;
    }

    setLive("off");
  }, []);

  // Also on the way out, which is the case a stop button cannot cover: a
  // volunteer who clicks away to the applicants list has left a camera on.
  useEffect(() => stop, [stop]);

  const start = useCallback(async () => {
    const Build = window.BarcodeDetector;
    if (!Build) return;

    setProblem(null);
    setLive("starting");

    // Asked before the camera, because a browser that has the constructor and
    // not the format would otherwise light a camera to decode nothing.
    try {
      const formats = (await Build.getSupportedFormats?.()) ?? ["qr_code"];
      if (!formats.includes("qr_code")) {
        setProblem("This browser cannot read QR codes. Type the code instead.");
        setLive("off");
        return;
      }
    } catch {
      setProblem("This browser cannot read QR codes. Type the code instead.");
      setLive("off");
      return;
    }

    try {
      // The back camera where there is a choice, because the volunteer is
      // holding the device and the phone is in front of it.
      stream.current = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: "environment" },
      });
    } catch (refusal) {
      setLive("off");
      setProblem(why(refusal));
      return;
    }

    if (!video.current) {
      stop();
      return;
    }

    video.current.srcObject = stream.current;

    try {
      await video.current.play();
    } catch {
      // Autoplay refused the stream. Nothing a volunteer can fix, and the
      // typed input is right there, so this says so rather than retrying.
      stop();
      setProblem("The camera could not be shown. Type the code instead.");
      return;
    }

    detector.current = new Build({ formats: ["qr_code"] });
    setLive("on");
  }, [stop]);

  // The reading loop. Separate from starting the camera so that `busy` can
  // pause it without the stream going down and coming back up -- a camera
  // that restarts after every scan is a camera that spends a second of the
  // queue's time on each person.
  useEffect(() => {
    if (live !== "on" || busy) return;

    const look = async () => {
      if (looking.current || !video.current || !detector.current) return;
      looking.current = true;

      try {
        const found = await detector.current.detect(video.current);
        const read = found[0]?.rawValue?.trim();
        if (!read) return;

        const now = Date.now();
        if (last.current && last.current.code === read && now - last.current.at < REPEAT_MS) {
          return;
        }

        last.current = { code: read, at: now };

        // Whatever the symbol held, unchanged. A code is normalised by
        // atlas -- spaces, dashes, case, Crockford's confusable letters --
        // and a symbol holding something else entirely gets the API's own
        // "that code is not one of ours", which is the true answer and
        // better than a guess made here about what a QR code ought to say.
        onCode(read);
      } catch {
        // A frame that could not be decoded is the normal state of a lens
        // pointed at a room. The next one is 250ms away.
      } finally {
        looking.current = false;
      }
    };

    const id = setInterval(look, LOOK_MS);
    return () => clearInterval(id);
  }, [live, busy, onCode]);

  if (supported === null) {
    // One render, before the effect settles. Deliberately empty: offering a
    // camera button that may be about to disappear is worse than a panel that
    // grows one a moment later.
    return null;
  }

  if (!supported) {
    return (
      <p className={styles.cameraNote}>
        {/* COPY: needs sign-off. Names the browsers rather than the API,
            because "BarcodeDetector is unavailable" is not a sentence anybody
            at a door can act on. */}
        This browser cannot use the camera to read QR codes — Safari and
        Firefox have no reader, and a camera needs an https address. Type the
        code from the hacker&apos;s portal screen instead; it is printed under
        the symbol.
      </p>
    );
  }

  return (
    <div className={styles.camera} data-live={live === "on" ? "" : undefined}>
      <div className={styles.cameraHead}>
        {live === "on" ? (
          <>
            <span className={styles.cameraOn}>Camera on</span>
            {/* The largest control in the panel while the camera runs. A
                camera somebody has to go looking for the switch of is a
                camera that stays on all day. */}
            <button type="button" className="button" onClick={stop}>
              Stop camera
            </button>
          </>
        ) : (
          <button
            type="button"
            className="button"
            onClick={() => void start()}
            disabled={live === "starting"}
          >
            {live === "starting" ? "Starting camera…" : "Scan with the camera"}
          </button>
        )}
      </div>

      {/* Inline, or a phone browser takes the video fullscreen and the
          volunteer loses the screen they were reading the answer off. Muted
          because there is nothing to hear and a video element that takes
          audio focus at a desk is a surprise nobody needs. */}
      <video
        ref={video}
        className={styles.cameraView}
        hidden={live !== "on"}
        muted
        playsInline
        aria-label="Camera, pointed at a check-in code"
      />

      {live === "on" ? (
        <p className={styles.cameraNote}>
          Hold the phone&apos;s code in the frame. Every code the camera reads
          goes through the same check as a typed one, and the answer appears
          above.
        </p>
      ) : null}

      {problem ? (
        <p className={styles.cameraProblem} role="alert">
          {problem}
        </p>
      ) : null}
    </div>
  );
}

/**
 * What to say about a camera that did not open.
 *
 * Each sentence names the next move, which is the rule the API's own refusals
 * follow: a volunteer reading this has a queue behind them, and "NotAllowedError"
 * is not something anybody can do anything with.
 */
function why(refusal: unknown): string {
  const name = refusal instanceof Error ? refusal.name : "";

  if (name === "NotAllowedError" || name === "SecurityError") {
    return "The camera was blocked. Allow it for this site in the browser's address bar, or type the code instead.";
  }

  if (name === "NotFoundError" || name === "OverconstrainedError") {
    return "No camera was found on this device. Type the code instead.";
  }

  if (name === "NotReadableError") {
    return "The camera is already in use by something else. Close that, or type the code instead.";
  }

  return "The camera could not be started. Type the code instead.";
}
