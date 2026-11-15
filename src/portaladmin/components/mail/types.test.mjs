import assert from "node:assert/strict";
import test from "node:test";
import { describeSegment, listRecipients } from "./types.ts";

/**
 * Both eras of a stored address list.
 *
 * The rename from `emails` to `recipients` did not rewrite the documents
 * already in notify.campaigns, and nothing rewrites one until somebody saves
 * that campaign again. A campaign drafted last term is therefore still a bare
 * array of strings, and it reaches these screens exactly as it was stored --
 * so every one of these is a page that would otherwise have gone down on a
 * campaign nobody has touched.
 */

test("a list stored the old way reads as recipients with no names", () => {
  assert.deepEqual(
    listRecipients({ emails: ["ada@example.com", "grace@example.com"] }),
    [{ email: "ada@example.com" }, { email: "grace@example.com" }],
  );
});

test("a list stored the new way is handed back as it is", () => {
  const recipients = [{ email: "ada@example.com", firstName: "Ada" }];
  assert.equal(listRecipients({ recipients }), recipients);
});

test("a list with neither spelling is empty rather than a thrown error", () => {
  assert.deepEqual(listRecipients({}), []);
});

test("an empty new-style list is not mistaken for an absent one", () => {
  assert.deepEqual(listRecipients({ recipients: [], emails: ["a@example.com"] }), []);
});

test("the old shape is described without counting names it cannot have", () => {
  assert.equal(
    describeSegment({ type: "explicitList", emails: ["ada@example.com"] }, null),
    "Address list · 1",
  );
});

test("the new shape says how many of the list arrived with names", () => {
  assert.equal(
    describeSegment(
      {
        type: "explicitList",
        recipients: [
          { email: "ada@example.com", firstName: "Ada" },
          { email: "grace@example.com" },
        ],
      },
      null,
    ),
    "Address list · 2 (1 with names)",
  );
});
