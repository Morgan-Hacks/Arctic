import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { DOOR_STATUSES, rosterNote } from "./roster-note.ts";

/**
 * The roster's sentences, checked against atlas's.
 *
 * No browser and no API, like the check-in tile's test next door. What is
 * tested here is a lifecycle rule written out in prose, and the failure it
 * exists to catch is a reword -- somebody tidying one of these two files and
 * leaving the desk telling one person two different things -- which is a
 * string comparison and nothing more.
 *
 * Run with `node --test app/check-in/roster-note.test.mjs` from
 * `src/portaladmin`.
 */

/**
 * Where atlas keeps the sentences `roster-note.ts` is a copy of.
 *
 * Read from the monorepo rather than duplicated into a fixture, which is the
 * whole point: a fixture would be a third copy to forget. If this path stops
 * resolving the test fails loudly, and a loud failure is the correct outcome
 * -- the file moving is exactly the event that would otherwise let the two
 * sets of wording drift apart unnoticed.
 */
const DESK_CS = new URL(
  "../../../atlas/MorganHacks.Applications/Domain/CheckInDesk.cs",
  import.meta.url,
);

test("confirmed is the only roster row with a code left to scan", () => {
  assert.equal(rosterNote("confirmed").code, "issued");
  assert.equal(rosterNote("checked_in").code, "used");
  assert.equal(rosterNote("accepted").code, "none");
});

test("an accepted person is told why there is nothing to scan, and who fixes it", () => {
  const note = rosterNote("accepted");

  assert.equal(note.code, "none");
  // The fix, not the state. "Not eligible" is not an answer anybody can act
  // on at seven in the morning, which is the rule CheckInDesk works to.
  assert.match(note.text, /organizer can confirm it for them/);
});

test("a status the roster never asks for still gets the scanner's answer", () => {
  // Rejected, waitlisted, declined, withdrawn, expired: one sentence for all
  // of them, on purpose. A desk screen is held up in front of the person it
  // is about, and which of those it was is not for that screen.
  for (const status of ["rejected", "waitlisted", "declined", "withdrawn", "expired"]) {
    assert.deepEqual(rosterNote(status), {
      code: "none",
      text: "They are not confirmed for this event. Send them to an organizer.",
    });
  }
});

test("the roster asks for the three statuses a door can act on, and no others", () => {
  assert.deepEqual([...DOOR_STATUSES].sort(), ["accepted", "checked_in", "confirmed"]);
});

test("the sentences are still the ones atlas sends", () => {
  // The drift check, and the reason this file is worth having. Both halves of
  // the desk -- a scan and a roster row -- describe the same lifecycle, and
  // only one of them gets its words from the API. If these stop matching the
  // screen contradicts itself about somebody standing in front of it, which
  // is not something a reviewer would catch in a diff.
  //
  // C# splits a long sentence across lines with `" + "`, so the quotes and
  // the plus are closed up before the whitespace is flattened -- otherwise
  // every multi-line sentence in that file would read as a mismatch.
  const atlas = readFileSync(DESK_CS, "utf8")
    .replace(/"\s*\+\s*"/g, "")
    .replace(/\s+/g, " ");

  // Confirmed is not here because atlas has no sentence for it: a confirmed
  // person is not a refusal, so the API has never had to explain them, and
  // the roster's line about where their code lives is the console's own.
  for (const status of ["accepted", "checked_in", "rejected"]) {
    const { text } = rosterNote(status);

    assert.ok(
      atlas.includes(text),
      `CheckInDesk.cs no longer contains: ${text}`,
    );
  }
});
