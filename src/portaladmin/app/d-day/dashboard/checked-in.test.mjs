import assert from "node:assert/strict";
import test from "node:test";
import { CHECK_IN_POLL_MS, CHECK_IN_QUIET_MS, checkedInTile } from "./checked-in.ts";

/** What the tile writes a time with, so the assertions are about the logic. */
const clock = new Intl.DateTimeFormat("en-US", {
  hour: "numeric",
  minute: "2-digit",
  second: "2-digit",
});

const T0 = Date.parse("2026-10-11T18:04:07Z");

test("says when the figure is from, and says nothing more while the feed is beating", () => {
  const tile = checkedInTile(
    { count: 148, at: T0 },
    { count: 146, at: T0 - 55_000 },
    T0 + 5_000,
  );

  assert.equal(tile.count, 148);
  assert.equal(tile.note.live, true);
  // The figure's own time, not the moment of the read -- "as of" is a claim
  // about the number, and the seconds are what make the claim checkable.
  assert.equal(tile.note.text, `As of ${clock.format(T0)}`);
});

test("one missed beat is ordinary, four is the feed going quiet", () => {
  const feed = { count: 148, at: T0 };

  assert.equal(checkedInTile(feed, null, T0 + CHECK_IN_QUIET_MS - 1).note.live, true);
  assert.equal(checkedInTile(feed, null, T0 + CHECK_IN_QUIET_MS).note.live, false);
  assert.match(
    checkedInTile(feed, null, T0 + 6 * CHECK_IN_POLL_MS).note.text,
    /· not live$/,
  );
});

test("when the fast feed stops, the newer figure wins and the quiet is still said", () => {
  // The feed went quiet at T0; the board's minute-old read caught up twice
  // since. The number on screen is the newer one -- a reader gets what the
  // board can see rather than a frozen count -- and the line still reports
  // that nothing is arriving live.
  const tile = checkedInTile(
    { count: 145, at: T0 },
    { count: 148, at: T0 + 100_000 },
    T0 + 120_000,
  );

  assert.equal(tile.count, 148);
  assert.equal(tile.note.live, false);
  assert.equal(tile.note.text, `As of ${clock.format(T0 + 100_000)} · not live`);
});

test("falls back to the board rather than going blank when the feed never answered", () => {
  const tile = checkedInTile(null, { count: 148, at: T0 }, T0 + 10_000);

  assert.equal(tile.count, 148);
  assert.equal(tile.note.live, false);
  assert.equal(tile.note.text, `As of ${clock.format(T0)} · not live`);
});

test("a board with nothing to show says nothing", () => {
  assert.deepEqual(checkedInTile(null, null, T0), { count: null, note: null });
});
