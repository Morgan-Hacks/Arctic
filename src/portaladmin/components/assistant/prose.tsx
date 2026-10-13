/**
 * The small amount of Markdown a model actually writes.
 *
 * Not a Markdown implementation, and deliberately not a dependency. The input
 * here is one specific thing -- a few paragraphs written by a language model
 * answering a question -- and it uses about five constructs. A full parser
 * would bring link, image and raw-HTML handling for text that should never
 * contain any of them.
 *
 * Everything below builds React elements. There is no `dangerouslySetInnerHTML`
 * anywhere in this file, which is what makes the question "could a model emit
 * markup that runs" answerable with "it has nothing to emit it into" rather
 * than with a list of things that get stripped.
 *
 * Links are the deliberate omission. A URL in an answer was written by the
 * model, not read out of the database, and a clickable one in an admin console
 * is a place somebody can be sent -- so they stay as the text they are.
 */

import type { ReactNode } from "react";
import styles from "./chat-panel.module.css";

/** `**bold**`, `` `code` ``, and `*italic*`. In that order, because `**` must win. */
const INLINE = /(\*\*[^*\n]+\*\*|`[^`\n]+`|\*[^*\s][^*\n]*\*)/g;

const BULLET = /^\s*[-*•]\s+/;
const NUMBER = /^\s*\d+[.)]\s+/;
const HEADING = /^\s*#{1,6}\s+/;

export function Prose({ text }: { text: string }) {
  return <>{blocks(text).map((block, at) => render(block, at))}</>;
}

/**
 * One literal per member, rather than two members with a union of kinds each.
 *
 * `{ kind: "p" | "h" }` reads as the same type and does not narrow the same
 * way: ruling out "ul" leaves a member that could still be "ol", so the
 * compiler keeps both shapes in play and no property is safe to read. Four
 * members cost two lines and make the discriminant actually discriminate.
 */
type Block =
  | { kind: "p"; lines: string[] }
  | { kind: "h"; lines: string[] }
  | { kind: "ul"; items: string[] }
  | { kind: "ol"; items: string[] };

/**
 * One pass over the lines, grouping by what each one looks like.
 *
 * A blank line ends a paragraph and does not end a list. That asymmetry is the
 * whole of this function and it is not a nicety: models put a blank line
 * between numbered points, and closing the list on it gives six lists of one
 * item, every one of them numbered "1". It is also what Markdown itself does
 * with a blank line inside a list.
 *
 * A list ends when the next line is not an item of it -- which includes being
 * an item of the other kind, so bullets following numbers start a new list.
 */
function blocks(text: string): Block[] {
  const out: Block[] = [];
  let open: Block | null = null;

  for (const line of text.trim().split("\n")) {
    const trimmed = line.trim();

    if (trimmed.length === 0) {
      // Only prose is closed here. See above.
      if (open?.kind === "p" || open?.kind === "h") open = null;
      continue;
    }

    if (BULLET.test(trimmed)) {
      const item = trimmed.replace(BULLET, "");
      if (open?.kind === "ul") open.items.push(item);
      else out.push((open = { kind: "ul", items: [item] }));
      continue;
    }

    if (NUMBER.test(trimmed)) {
      const item = trimmed.replace(NUMBER, "");
      if (open?.kind === "ol") open.items.push(item);
      else out.push((open = { kind: "ol", items: [item] }));
      continue;
    }

    if (HEADING.test(trimmed)) {
      out.push((open = { kind: "h", lines: [trimmed.replace(HEADING, "")] }));
      continue;
    }

    // A wrapped paragraph is still one paragraph, and a plain line after a
    // list ends the list rather than joining it.
    if (open?.kind === "p") open.lines.push(trimmed);
    else out.push((open = { kind: "p", lines: [trimmed] }));
  }

  return out;
}

function render(block: Block, key: number): ReactNode {
  if (block.kind === "ul") {
    return (
      <ul key={key} className={styles.proseList}>
        {block.items.map((item, at) => (
          <li key={at}>{inline(item)}</li>
        ))}
      </ul>
    );
  }

  if (block.kind === "ol") {
    return (
      <ol key={key} className={styles.proseList}>
        {block.items.map((item, at) => (
          <li key={at}>{inline(item)}</li>
        ))}
      </ol>
    );
  }

  const text = block.lines.join(" ");

  // A heading in a 380px panel is a bold line. Rendering h1 at its usual size
  // beside 13px body text would make a two-word label the largest thing on the
  // screen.
  return block.kind === "h" ? (
    <p key={key} className={styles.proseHeading}>
      {inline(text)}
    </p>
  ) : (
    <p key={key} className={styles.proseParagraph}>
      {inline(text)}
    </p>
  );
}

function inline(text: string): ReactNode[] {
  return text.split(INLINE).map((piece, at) => {
    if (piece.startsWith("**") && piece.endsWith("**") && piece.length > 4) {
      return <strong key={at}>{piece.slice(2, -2)}</strong>;
    }

    if (piece.startsWith("`") && piece.endsWith("`") && piece.length > 2) {
      return <code key={at}>{piece.slice(1, -1)}</code>;
    }

    if (piece.startsWith("*") && piece.endsWith("*") && piece.length > 2) {
      return <em key={at}>{piece.slice(1, -1)}</em>;
    }

    return piece;
  });
}
