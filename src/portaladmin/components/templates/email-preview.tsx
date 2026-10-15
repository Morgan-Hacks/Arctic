"use client";

import { ErrorToast } from "@/components/ui/error-toast";
import { EmailPreviewSkeleton } from "@/components/ui/page-skeleton";

import { Mail01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import type { Rendered } from "./types";
import type { PreviewDevice } from "./design-toolbar";
import styles from "./design-workspace.module.css";

export function EmailPreview({ rendered, pending, error, device }: {
  rendered: Rendered | null;
  pending: boolean;
  error: string | null;
  device: PreviewDevice;
}) {
  return <>
    <div className={styles.previewCanvas} data-preview-device={device}>
      <div className={styles.previewDocument} data-preview-device={device} aria-busy={pending}>
        {!rendered && pending ? <EmailPreviewSkeleton /> : !rendered ? <div className={styles.emptyPreview}>
          <Icon icon={Mail01Icon} size={28} strokeWidth={1.25} />
          <p>{pending ? "Rendering your email…" : "Your email will appear here"}</p>
        </div> : <iframe className={styles.previewFrame} title="Email preview" sandbox="" srcDoc={emailDocument(rendered.html, { scrollbars: true })} />}
      </div>
    </div>
    {error ? <ErrorToast message={error} /> : null}
  </>;
}

/**
 * The document a preview iframe renders.
 *
 * Scrollbars are hidden by default because most callers are thumbnails: a
 * 56px card scaled down from a 660px document would otherwise carry a
 * scrollbar wider than the artwork it frames.
 *
 * A preview somebody actually reads needs the opposite. Those frames have a
 * fixed height and no way to grow -- `sandbox=""` blocks the script that
 * would measure the content, which is the point of the sandbox, not an
 * oversight -- so the scrollbar is the only thing telling a reader the email
 * continues below the fold. Hidden there, the email is simply clipped and
 * nothing on screen says so.
 */
export function emailDocument(
  html: string,
  { scrollbars = false }: { scrollbars?: boolean } = {},
): string {
  return [
    "<!doctype html><html><head><meta charset=\"utf-8\">",
    "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">",
    "<style>:root{color-scheme:light}",
    scrollbars ? "" : `
      * { scrollbar-width: none; }
      *::-webkit-scrollbar { display: none; width: 0; height: 0; }
    `,
    "</style></head><body>",
    html,
    "</body></html>",
  ].join("");
}
