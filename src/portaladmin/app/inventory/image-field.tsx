"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Delete02Icon, ImageAdd02Icon, PackageIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { clearItemImage } from "./actions";
import type { ItemView } from "./api";
import styles from "./inventory.module.css";

/**
 * The photo on an item.
 *
 * Not a server action, and that is the one structural decision here. An
 * action serialises the file into its payload, which carries a 1 MB body
 * limit by default -- a quarter of what the API accepts, and well under what
 * a phone camera produces, so the common case would fail as an opaque
 * framework error rather than as the sentence the API would have given.
 * Posting to /api/admin/inventory/:id/image instead goes through the rewrite
 * in next.config.ts, which streams the body on to harbor rather than
 * buffering it, and keeps the browser talking to one origin so the
 * SameSite=Lax session cookie is still sent. It is what the hacker portal's
 * resume upload already does, for the same reasons.
 *
 * XMLHttpRequest rather than fetch because upload progress is the one thing
 * fetch still cannot report, and four megabytes on campus wifi with no
 * progress bar looks like a page that has frozen.
 *
 * Removing a photo does go through an action -- that request is an id and
 * nothing else, so none of the above applies to it.
 *
 * Nothing here is a control. The size and the format are both checked again
 * on the write, so editing this component out of the page buys exactly the
 * same refusals.
 */

/**
 * Mirrors InventoryImage.MaxBytes.
 *
 * Only to name the limit in the sentence below and to save somebody pushing a
 * file up that cannot land. The API measures the bytes it actually receives,
 * and it is the one that decides.
 */
const MAX_BYTES = 4 * 1024 * 1024;

/** What the picker offers. The API accepts exactly these three. */
const ACCEPTS = "image/png,image/jpeg,image/webp";

export function ImageField({ item }: { item: ItemView }) {
  const router = useRouter();
  const input = useRef<HTMLInputElement>(null);
  const request = useRef<XMLHttpRequest | null>(null);

  const [progress, setProgress] = useState<number | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [removing, startRemoving] = useTransition();

  // An upload still in flight when the dialog closes is a request nobody is
  // waiting for. Left running it finishes into a component that no longer
  // exists.
  useEffect(() => () => request.current?.abort(), []);

  const busy = progress !== null || removing;

  function fail(message: string) {
    request.current = null;
    setProgress(null);
    setProblem(message);
  }

  function send(file: File) {
    // A courtesy, not a limit: it saves somebody pushing four megabytes up
    // campus wifi only to be told no at the end of it.
    if (file.size > MAX_BYTES) {
      // COPY: needs sign-off.
      fail(
        `That photo is ${megabytes(file.size)} MB, and the limit is ` +
          `${megabytes(MAX_BYTES)} MB. Choose a smaller one.`,
      );
      return;
    }

    // The format is deliberately not checked here. The API already explains a
    // HEIC from an iPhone better than a second copy of that sentence would,
    // and two copies of one rule drift the first time somebody changes it on
    // one side. `accept` on the input steers the picker, which is the part
    // that actually helps -- iOS converts as it hands the file over.

    setProblem(null);
    setProgress(0);

    const body = new FormData();
    body.append("file", file);

    const xhr = new XMLHttpRequest();
    request.current = xhr;

    // Same origin, through the rewrite. The session cookie is SameSite=Lax
    // and only travels because of that -- pointed at harbor's own hostname
    // this request would arrive signed out.
    xhr.open("POST", `/api/admin/inventory/${encodeURIComponent(item.id)}/image`);
    xhr.responseType = "json";

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        setProgress(Math.round((event.loaded / event.total) * 100));
      }
    };

    xhr.onload = () => {
      request.current = null;
      setProgress(null);

      // The session went while the dialog was open. Sending them back to sign
      // in is the only thing that helps.
      if (xhr.status === 401) {
        window.location.href = "/sign-in";
        return;
      }

      const answer = xhr.response as { error?: string } | null;

      if (xhr.status < 200 || xhr.status >= 300) {
        // COPY: needs sign-off.
        setProblem(
          answer?.error
            ?? (xhr.status === 403
              ? "You do not have inventory.manage. Ask an admin."
              : "That photo could not be saved."),
        );
        return;
      }

      // The card behind this dialog renders the photo, and its URL is signed
      // on the server. Refreshing is what puts the new one on screen.
      router.refresh();
    };

    // COPY: needs sign-off.
    xhr.onerror = () => fail("The upload did not finish. Try again.");
    xhr.onabort = () => setProgress(null);

    xhr.send(body);
  }

  function remove() {
    setProblem(null);
    startRemoving(async () => {
      const result = await clearItemImage(item.id);
      if (result.error) {
        setProblem(result.error);
      } else {
        router.refresh();
      }
    });
  }

  return (
    <div className={styles.field}>
      {/* A real label for the real input, so clicking the word opens the
          picker and .field label styles it like every other field. */}
      <label htmlFor="edit-item-photo">
        Photo <span className={styles.optional}>(optional)</span>
      </label>

      <div className={styles.imageField}>
        {item.imageUrl ? (
          <img src={item.imageUrl} alt="" className={styles.imagePreview} />
        ) : (
          <span className={styles.imagePreviewEmpty} aria-hidden="true">
            <Icon icon={PackageIcon} size={28} />
          </span>
        )}

        <div className={styles.imageControls}>
          <input
            ref={input}
            type="file"
            className={styles.fileInput}
            id="edit-item-photo"
            accept={ACCEPTS}
            disabled={busy}
            aria-describedby="edit-item-photo-help"
            onChange={(event) => {
              const file = event.target.files?.[0];
              // Cleared so choosing the same file twice still fires a change,
              // which is what somebody retrying after a failure does.
              event.target.value = "";
              if (file) send(file);
            }}
          />

          <button
            type="button"
            className={styles.rowAction}
            disabled={busy}
            onClick={() => input.current?.click()}
          >
            <Icon icon={ImageAdd02Icon} size={14} />
            {progress !== null
              ? `Uploading… ${progress}%`
              : item.imageUrl
                ? "Replace photo"
                : "Choose photo"}
          </button>

          {item.imageUrl ? (
            <button
              type="button"
              className={styles.rowAction}
              disabled={busy}
              onClick={remove}
            >
              <Icon icon={Delete02Icon} size={14} />
              {removing ? "Removing…" : "Remove"}
            </button>
          ) : null}
        </div>
      </div>

      {/* COPY: needs sign-off. Saying when it saves matters: the photo is its
          own write, so Cancel closes the dialog without putting it back. */}
      <p id="edit-item-photo-help" className={styles.hint}>
        PNG, JPEG or WebP, up to {megabytes(MAX_BYTES)} MB. The photo saves as
        soon as you choose it — Cancel will not undo it.
      </p>

      {problem ? (
        <p className={styles.imageProblem} role="alert">
          {problem}
        </p>
      ) : null}
    </div>
  );
}

/** One decimal place, and no trailing ".0" on a whole number. */
function megabytes(bytes: number): string {
  return (Math.round((bytes / 1024 / 1024) * 10) / 10).toString();
}
