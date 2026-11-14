import assert from "node:assert/strict";
import test from "node:test";
import { guessColumn, inspect, looksLikeEmail, MAX_ROWS, parseSheet, toRecipients } from "./csv.ts";

test("a file Excel saved is read despite its byte order mark and CRLF", () => {
  const sheet = parseSheet("﻿Email,First name\r\nada@example.com,Ada\r\n");
  assert.deepEqual(sheet.columns, ["Email", "First name"]);
  assert.deepEqual(sheet.rows, [["ada@example.com", "Ada"]]);
});

test("a quoted field keeps its commas, newlines and doubled quotes", () => {
  const sheet = parseSheet('email,note\n"a@example.com","Smith, Jr.\nsaid ""hi"""\n');
  assert.deepEqual(sheet.rows, [["a@example.com", 'Smith, Jr.\nsaid "hi"']]);
});

test("a tab separated file is read, because pasting from a spreadsheet makes one", () => {
  const sheet = parseSheet("email\tfirst\nada@example.com\tAda\n");
  assert.deepEqual(sheet.columns, ["email", "first"]);
  assert.deepEqual(sheet.rows, [["ada@example.com", "Ada"]]);
});

test("ragged rows are padded and cut so every row matches the header", () => {
  // A trailing comma and a hand-edited short row are both ordinary, and
  // refusing the upload over them would be refusing it for an invisible
  // reason.
  const sheet = parseSheet("email,first,last\na@example.com,Ada\nb@example.com,Bo,Ng,extra\n");
  assert.deepEqual(sheet.rows, [
    ["a@example.com", "Ada", ""],
    ["b@example.com", "Bo", "Ng"],
  ]);
});

test("the thousand empty rows under an export are dropped", () => {
  const sheet = parseSheet("email\na@example.com\n\n,\n\n");
  assert.deepEqual(sheet.rows, [["a@example.com"]]);
});

test("a file with no header is nothing to map", () => {
  assert.equal(parseSheet(""), null);
  assert.equal(parseSheet("\n"), null);
});

test("the common column names are guessed so the usual file needs no mapping", () => {
  const columns = ["Email Address", "Given Name", "Surname", "emailed_at"];
  assert.equal(guessColumn(columns, "email"), "Email Address");
  assert.equal(guessColumn(columns, "first"), "Given Name");
  assert.equal(guessColumn(columns, "last"), "Surname");
});

test("a guess is empty rather than wrong when nothing matches", () => {
  assert.equal(guessColumn(["who", "when"], "email"), "");
});

test("an address check catches the column somebody mapped by mistake", () => {
  assert.ok(looksLikeEmail("ada@example.com"));
  for (const wrong of ["Ada", "ada@example", "ada @example.com", "", "a@@b.com"]) {
    assert.ok(!looksLikeEmail(wrong), `${wrong} should not pass`);
  }
});

// ---------------------------------------------------------------- mapping ---

const sheet = (text) => parseSheet(text);
const map = (email = "email", first = "", last = "") => ({ email, first, last });

test("a clean file under a full mapping has nothing wrong with it", () => {
  const s = sheet("email,first,last\nada@example.com,Ada,Lovelace\nbo@example.com,Bo,Ng\n");
  assert.equal(inspect(s, map("email", "first", "last")), null);
  assert.deepEqual(toRecipients(s, map("email", "first", "last")), [
    { email: "ada@example.com", firstName: "Ada", lastName: "Lovelace" },
    { email: "bo@example.com", firstName: "Bo", lastName: "Ng" },
  ]);
});

test("a mapped name column with a blank in it stops the import", () => {
  // The rule the whole importer exists to enforce: the alternative is a
  // message that opens "Hi ," or a send refused at the last step.
  const s = sheet("email,first\na@example.com,Ada\nb@example.com,\nc@example.com,Cy\n");
  const problem = inspect(s, map("email", "first"));
  assert.ok(problem.heading.includes("empty in 1 row"));
  // Row 3, because the header is row 1 -- the number they see in the file.
  assert.deepEqual(problem.rows, [3]);
});

test("the same file is fine when the name column is simply not used", () => {
  // The way out, and the reason the rule can afford to be strict.
  const s = sheet("email,first\na@example.com,Ada\nb@example.com,\n");
  assert.equal(inspect(s, map("email")), null);
  assert.deepEqual(toRecipients(s, map("email")), [
    { email: "a@example.com", firstName: undefined, lastName: undefined },
    { email: "b@example.com", firstName: undefined, lastName: undefined },
  ]);
});

test("a column mapped to the wrong thing is named with its rows", () => {
  const s = sheet("name,email\nAda,ada@example.com\nBo,bo@example.com\n");
  const problem = inspect(s, map("name"));
  assert.ok(problem.heading.includes('"name"'));
  assert.deepEqual(problem.rows, [2, 3]);
});

test("a long list of bad rows is cut, and says how many there were", () => {
  const rows = Array.from({ length: 30 }, (_, i) => `not-an-email-${i}`).join("\n");
  const problem = inspect(sheet(`email\n${rows}\n`), map());
  assert.equal(problem.rows.length, 8);
  assert.equal(problem.total, 30);
});

test("an unmapped email column is the first thing asked for", () => {
  const problem = inspect(sheet("a,b\n1,2\n"), map(""));
  assert.ok(problem.heading.includes("Choose which column"));
});

test("a header with no rows under it is refused rather than imported as nothing", () => {
  assert.ok(inspect(sheet("email,first\n"), map()).heading.includes("nothing under it"));
});

test("more rows than a campaign takes is refused before anything is sent", () => {
  const rows = Array.from({ length: MAX_ROWS + 1 }, (_, i) => `p${i}@example.com`).join("\n");
  assert.ok(inspect(sheet(`email\n${rows}\n`), map()).heading.includes("more than the"));
});
