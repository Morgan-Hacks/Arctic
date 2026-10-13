/**
 * Which screen a question was asked from.
 *
 * The panel sits beside whatever you are looking at, so half of what gets
 * asked is about what is on it -- "what's the title", "how many of these are
 * there". Without this the assistant answers those out of whichever read it
 * happened to make last.
 *
 * A name from a closed list rather than the path itself. The path can carry
 * an id, a search somebody typed, or a filter with a name in it, and all of
 * that would end up in a prompt. The API keeps its own copy of this list and
 * drops anything it does not recognise, so this is a convenience and not the
 * check.
 */
export function screenOf(pathname: string | null): string | undefined {
  if (!pathname) return undefined;

  const [, first, second, third] = pathname.split("/");

  switch (first) {
    case undefined:
    case "":
      return "overview";
    case "applicants":
      return second ? "applicant" : "applicants";
    case "forms":
      // Three screens behind one segment, and they are about different
      // things: the list is about forms, the builder about one form's
      // questions, and the third about the answers to them.
      if (!second) return "forms";
      return third === "responses" ? "responses" : "form";
    case "inventory":
    case "check-in":
    case "mail":
    case "templates":
    case "people":
    case "events":
    case "audit":
      return first;
    case "d-day":
      return "d-day";
    default:
      return undefined;
  }
}

/**
 * Which event the page has selected.
 *
 * Read from the URL because that is where the console keeps it: every picker
 * on every screen navigates rather than holding the choice in React state, so
 * an organizer can send somebody the exact view they are looking at. Two
 * spellings because two screens chose differently -- `/applicants?event=` and
 * `/inventory?eventId=` -- and the panel sits beside both.
 *
 * Undefined when the page has not said, which is most of them. The caller
 * falls back to the same event the applicants screen would have shown.
 */
export function eventOf(params: URLSearchParams | null): string | undefined {
  return params?.get("event") ?? params?.get("eventId") ?? undefined;
}
