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

/**
 * A list marker, and the indentation in front of it.
 *
 * The indentation is captured because it is the only thing that says a line
 * belongs under the one above rather than beside it. A model writing out an
 * inventory item and its quantity writes exactly that.
 */
const BULLET = /^(\s*)[-*•]\s+/;
const NUMBER = /^(\s*)\d+[.)]\s+/;
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
type List = { kind: "ul" | "ol"; items: Item[] };
type Item = { text: string; nested: List | null };

type Block = { kind: "p"; lines: string[] } | { kind: "h"; lines: string[] } | List;

/** Where in the nesting the parser currently is. */
type Level = { indent: number; list: List };

/**
 * One pass over the lines, grouping by what each one looks like.
 *
 * A blank line ends a paragraph and does not end a list. That asymmetry is not
 * a nicety: models put a blank line between numbered points, and closing the
 * list on it gives six lists of one item, every one of them numbered "1". It
 * is also what Markdown itself does with a blank line inside a list.
 *
 * Indentation nests. A line indented past the one above it belongs under it,
 * which is how "- Lanyards" with "- Quantity: 70" beneath it reads on the
 * page and how it should read on the screen. Anything that is not a list item
 * closes every open list, because a paragraph is not part of one.
 */
function blocks(text: string): Block[] {
  const out: Block[] = [];
  let open: Block | null = null;

  // Outermost first. Empty whenever no list is open.
  let levels: Level[] = [];

  for (const line of text.trim().split("\n")) {
    const trimmed = line.trim();

    if (trimmed.length === 0) {
      // Only prose is closed here. See above.
      if (open?.kind === "p" || open?.kind === "h") open = null;
      continue;
    }

    const bullet = BULLET.exec(line);
    const numbered = bullet ? null : NUMBER.exec(line);
    const marker = bullet ?? numbered;

    if (marker) {
      const kind = bullet ? "ul" : "ol";
      const indent = marker[1].length;
      const item: Item = { text: line.replace(marker[0], ""), nested: null };

      // Out of any level deeper than this one.
      while (levels.length > 0 && indent < levels[levels.length - 1].indent) {
        levels.pop();
      }

      let here = levels.at(-1);

      // Same depth, other kind: a sibling list rather than more of this one.
      if (here && indent === here.indent && here.list.kind !== kind) {
        levels.pop();
        here = levels.at(-1);
      } else if (here && indent === here.indent) {
        here.list.items.push(item);
        open = here.list;
        continue;
      }

      const list: List = { kind, items: [item] };

      // Deeper than the level above: hangs off that level's last item. With
      // no item to hang off -- a document that opens indented -- it is a list
      // of its own rather than nothing.
      const under = here && indent > here.indent ? here.list.items.at(-1) : undefined;

      if (under) under.nested = list;
      else out.push(list);

      levels.push({ indent, list });
      open = list;
      continue;
    }

    // Not a list item, so every open list has ended.
    levels = [];

    if (HEADING.test(trimmed)) {
      out.push((open = { kind: "h", lines: [trimmed.replace(HEADING, "")] }));
      continue;
    }

    // A wrapped paragraph is still one paragraph.
    if (open?.kind === "p") open.lines.push(trimmed);
    else out.push((open = { kind: "p", lines: [trimmed] }));
  }

  return out;
}

function render(block: Block, key: number): ReactNode {
  // On the property rather than on the kind. `List` carries `"ul" | "ol"` in
  // one member, so ruling out both literals still leaves it in play and no
  // field is safe to read -- the same narrowing trap the four-member split
  // above avoids, in the one place the split cannot go.
  if ("items" in block) {
    return list(block, key);
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

function list(block: List, key: number): ReactNode {
  const Tag = block.kind === "ul" ? "ul" : "ol";

  return (
    <Tag key={key} className={styles.proseList}>
      {block.items.map((item, at) => (
        <li key={at}>
          {inline(item.text)}
          {item.nested ? list(item.nested, -1) : null}
        </li>
      ))}
    </Tag>
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
