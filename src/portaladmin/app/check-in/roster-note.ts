import type { Status } from "@/components/applicants/types";

/**
 * What the desk may tell a volunteer about somebody on the roster, and why the
 * person in front of them might have no code at all.
 *
 * The scanner answers for a code. The roster answers for a person, which is a
 * different question with a worse failure mode: there is no API call behind a
 * roster row, so nothing hands this screen a sentence and the console has to
 * write its own. That is exactly the drift the scanner's own file refuses to
 * risk -- `actions.ts` shows the API's refusals verbatim rather than rewording
 * them -- so the rule lives here, alone, with a test that pins the wording to
 * the API's.
 *
 * ## Why an accepted person has no code
 *
 * `CheckInCode.Issued` is `Confirmed` and `CheckedIn`, and nothing else. An
 * accepted applicant who never answered their RSVP has a spot and no code, so
 * they arrive at the door with nothing to scan and the volunteer with no
 * reason why. `StatusTransition` is the other half: `Accepted -> Confirmed` is
 * a move somebody has to make, `Confirmed -> CheckedIn` is the only road into
 * the room, and the console does not offer the first one. A confirm-at-door
 * button would need an API path that does not exist, and inventing one in the
 * console is how a screen starts disagreeing with the service it reads from.
 *
 * So the roster's job for that person is to name the state and name who fixes
 * it, which is what `CheckInDesk.WhyNot(Accepted)` already says in atlas. The
 * wording here is that sentence, copied rather than paraphrased, and the test
 * asserts it character for character -- a reword on either side then shows up
 * as a failing test instead of as two desks telling one person two different
 * things.
 */

/**
 * The statuses the roster asks the API for.
 *
 * Three, and the omissions are the decision. Confirmed is everybody who may
 * walk in, checked in is everybody who already has, and accepted is the one
 * refusal with a fix -- the three states a door can act on.
 *
 * Rejected, waitlisted, declined and withdrawn are deliberately absent, and
 * not because the desk could not read them. `CheckInDesk` is vaguer than the
 * codebase on purpose, because a volunteer's screen is held at chest height in
 * front of the person it is about, and the difference between "they were
 * rejected" and "they never answered" is not something to put on it. A roster
 * listing those statuses would put on that screen exactly what the scanner's
 * sentences were written to keep off it. Somebody who is not on this list is
 * somebody an organizer deals with away from the queue, which is also the
 * answer the scanner gives them.
 */
export const DOOR_STATUSES: readonly Status[] = [
  "confirmed",
  "accepted",
  "checked_in",
];

/** Whether there is a code to scan, and whether it has been used already. */
export type CodeState = "issued" | "used" | "none";

export type RosterNote = {
  code: CodeState;

  /**
   * What the volunteer does about this person.
   *
   * The API's sentence wherever the API has one for this state, so the roster
   * and the scanner agree about somebody whose phone is dead.
   */
  text: string;
};

/** Atlas's `CheckInDesk.WhyNot(Accepted)`, verbatim. */
const ACCEPTED =
  "They have a spot but have not confirmed it. An organizer can confirm it for them.";

/** Atlas's `CheckInDesk.NotConfirmed`, verbatim. */
const NOT_CONFIRMED =
  "They are not confirmed for this event. Send them to an organizer.";

/** Atlas's `CheckInDesk.Describe(AlreadyCheckedIn)`, verbatim. */
const ALREADY_CHECKED_IN = "Already checked in. Let them through.";

/**
 * What the roster says about one person.
 *
 * Keyed on the stored status and nothing else, so a row rendered from a page
 * of search results that has been on screen a while cannot promise something
 * the lifecycle no longer allows.
 *
 * The confirmed sentence is the one with no counterpart in atlas, because a
 * confirmed person is not a refusal and the API has never had to explain them.
 * It says where their code is rather than what it is: no read in this console
 * returns a code, so the only thing a volunteer can do with a confirmed row is
 * point at the portal screen that has it.
 */
export function rosterNote(status: Status): RosterNote {
  switch (status) {
    case "confirmed":
      return {
        code: "issued",
        text: "Confirmed. Their code is on their portal check-in screen.",
      };

    case "checked_in":
      return { code: "used", text: ALREADY_CHECKED_IN };

    case "accepted":
      return { code: "none", text: ACCEPTED };

    default:
      // Everything else, in one answer on purpose. The roster does not ask
      // for these statuses, so a row in one of them is somebody whose status
      // moved while the results sat on screen -- and the honest thing to say
      // about them is the thing the scanner would say.
      return { code: "none", text: NOT_CONFIRMED };
  }
}
