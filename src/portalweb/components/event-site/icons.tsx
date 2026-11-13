/**
 * The three icons this site uses, written out.
 *
 * There is no icon package in portalweb's dependencies and three shapes is
 * not a reason to add one. All three are stroked on a 24 unit box at 1.5, use
 * `currentColor`, and are decorative wherever they appear next to the words
 * they would otherwise be repeating -- so each is marked aria-hidden and the
 * meaning stays in the text beside it.
 */

type Props = { className?: string };

/** Leaves the site. Sits at the end of a link to a past edition. */
export function ArrowUpRight({ className }: Props) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M7 17 17 7" />
      <path d="M8 7h9v9" />
    </svg>
  );
}

/** Goes further into the site. */
export function ArrowRight({ className }: Props) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M4 12h15" />
      <path d="m13 6 6 6-6 6" />
    </svg>
  );
}

/** One thing a sponsor gets. */
export function Check({ className }: Props) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="m4.5 12.5 5 5 10-11" />
    </svg>
  );
}
