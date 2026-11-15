/**
 * Reading a spreadsheet somebody exported, in the browser.
 *
 * No DOM and no React here on purpose, the same reason placeholders.ts gives:
 * splitting a file into rows is a decision with awkward cases -- a quoted
 * comma, a quoted newline, a doubled quote, a file Excel saved with CRLF and a
 * byte order mark -- and they are answerable by reading one short function
 * rather than by dragging files into a browser until it looks right.
 *
 * Deliberately not a CSV library. The file this exists for is one an organizer
 * exported from Sheets or Excel ten seconds earlier, and the format those two
 * write is the one below. A dependency would also be a dependency in a bundle
 * the console ships to every organizer, to read a file most of them never
 * upload.
 */

/** A parsed file: the header row, and every row under it. */
export type Sheet = {
  /** Column names, in file order, trimmed. */
  columns: string[];
  /** One entry per data row, each the same length as `columns`. */
  rows: string[][];
};

/**
 * Splits a delimited file into cells.
 *
 * Handles what the two spreadsheet programs actually emit: quoted fields,
 * commas and newlines inside quotes, `""` as an escaped quote, CRLF, and the
 * byte order mark Excel puts at the front of a UTF-8 CSV. A tab-separated file
 * works too, because pasting from a spreadsheet into a text box produces tabs
 * and somebody will save that as .csv.
 */
function cells(text: string): string[][] {
  // Excel writes a BOM and nothing strips it, so without this the first
  // column is called "﻿email" and never matches anything.
  const input = text.replace(/^﻿/, "");

  // The delimiter is whichever of the two appears first outside quotes on the
  // first line. Counting over the whole file would be fooled by prose in a
  // later cell; the header row is the one place the format is unambiguous.
  const header = input.slice(0, input.search(/\r?\n/) + 1 || input.length);
  const delimiter = header.includes("\t") && !header.includes(",") ? "\t" : ",";

  const rows: string[][] = [];
  let row: string[] = [];
  let field = "";
  let quoted = false;
  let started = false;

  for (let i = 0; i < input.length; i++) {
    const char = input[i];

    if (quoted) {
      if (char !== '"') {
        field += char;
      } else if (input[i + 1] === '"') {
        // A doubled quote inside a quoted field is one literal quote.
        field += '"';
        i++;
      } else {
        quoted = false;
      }
      continue;
    }

    if (char === '"' && field === "") {
      quoted = true;
      started = true;
      continue;
    }

    if (char === delimiter) {
      row.push(field);
      field = "";
      started = true;
      continue;
    }

    if (char === "\n" || char === "\r") {
      // Only end the row on the \n of a CRLF, so the pair is one break.
      if (char === "\r" && input[i + 1] === "\n") continue;
      if (started || field !== "" || row.length > 0) {
        row.push(field);
        rows.push(row);
      }
      row = [];
      field = "";
      started = false;
      continue;
    }

    field += char;
  }

  if (started || field !== "" || row.length > 0) {
    row.push(field);
    rows.push(row);
  }

  return rows;
}

/**
 * The file as a header and its rows, or null when there is nothing to read.
 *
 * Short rows are padded and long ones are cut, so every row matches the header
 * and a caller can index by column without checking length. A ragged file is
 * ordinary -- a trailing comma, a row somebody edited by hand -- and refusing
 * the whole upload over one short row would be refusing the file for a reason
 * the person cannot see.
 *
 * Rows that are entirely empty are dropped, because a spreadsheet exported
 * with a thousand untouched rows below the data is the normal case and each of
 * them would otherwise arrive as a recipient with a blank address.
 */
export function parseSheet(text: string): Sheet | null {
  const rows = cells(text);
  if (rows.length === 0) return null;

  const columns = rows[0].map(column => column.trim());
  if (columns.length === 0 || columns.every(column => column === "")) return null;

  const body = rows
    .slice(1)
    .map(row =>
      Array.from({ length: columns.length }, (_, i) => (row[i] ?? "").trim()))
    .filter(row => row.some(cell => cell !== ""));

  return { columns, rows: body };
}

/**
 * Which column looks like it holds what, for the first guess at a mapping.
 *
 * A guess rather than a rule: it fills the three selects in so the common file
 * needs no mapping at all, and the person can change any of them. Matching is
 * on the whole normalised name, so a column called "emailed_at" does not win
 * over one called "email".
 */
export function guessColumn(columns: string[], wanted: "email" | "first" | "last"): string {
  const names: Record<typeof wanted, string[]> = {
    email: ["email", "emailaddress", "email_address", "e-mail", "mail"],
    first: ["firstname", "first_name", "first", "givenname", "given_name", "fname"],
    last: ["lastname", "last_name", "last", "surname", "familyname", "family_name", "lname"],
  };

  const normalise = (column: string) => column.toLowerCase().replace(/[\s-]+/g, "");
  const candidates = names[wanted].map(normalise);

  return columns.find(column => candidates.includes(normalise(column))) ?? "";
}

/** Good enough to catch a mis-mapped column, which is all this is for. */
export function looksLikeEmail(value: string): boolean {
  return /^[^@\s]+@[^@\s.]+\.[^@\s]+$/.test(value);
}

/** The most rows one campaign takes, matching Segment.MaxRecipients. */
export const MAX_ROWS = 10_000;

/** How many offending rows to name before the list stops being readable. */
const SHOWN = 8;

/** Which column of the sheet holds what. Empty string means "not used". */
export type Mapping = { email: string; first: string; last: string };

/** What is wrong, in words, with the rows it is wrong on. */
export type Problem = { heading: string; rows: number[]; total: number };

/** One person, as the campaign API takes them. */
export type Imported = { email: string; firstName?: string; lastName?: string };

/**
 * What is wrong with this file under this mapping, in the order it matters.
 *
 * Returns the first problem rather than all of them. Somebody who mapped the
 * wrong column has one thing to fix, and a screen listing four consequences of
 * it reads as four problems.
 *
 * The missing-name rule is the strict one, and deliberately so. An organizer
 * importing last year's attendees is importing them in order to greet them by
 * name, and a blank in that column means either a message that opens "Hi ," or
 * a send refused at the last step by a coverage check nobody expected. So a
 * mapped column has to be filled in every row, and the way to send to a list
 * where some names are missing is to not map the column at all.
 *
 * Row numbers count the header as row 1, so they are the numbers somebody sees
 * when they open the file to fix it.
 */
export function inspect(sheet: Sheet, mapping: Mapping): Problem | null {
  const none = (rows: number[] = []) => ({ rows, total: rows.length });
  const at = (row: string[], column: string) =>
    column === "" ? "" : (row[sheet.columns.indexOf(column)] ?? "");

  if (mapping.email === "") {
    return { heading: "Choose which column holds the email address.", ...none() };
  }

  if (!sheet.columns.includes(mapping.email)) {
    return { heading: "That column is not in this file.", ...none() };
  }

  if (sheet.rows.length === 0) {
    return { heading: "This file has a header row and nothing under it.", ...none() };
  }

  if (sheet.rows.length > MAX_ROWS) {
    return {
      heading: `This file has ${sheet.rows.length.toLocaleString()} rows, which is more than the ${MAX_ROWS.toLocaleString()} a single campaign takes.`,
      ...none(),
    };
  }

  const bad = (test: (row: string[]) => boolean) =>
    sheet.rows.reduce<number[]>((found, row, i) => {
      if (test(row)) found.push(i + 2);
      return found;
    }, []);

  const invalid = bad(row => !looksLikeEmail(at(row, mapping.email)));
  if (invalid.length > 0) {
    return {
      heading: `Column "${mapping.email}" does not hold an email address in every row.`,
      rows: invalid.slice(0, SHOWN),
      total: invalid.length,
    };
  }

  for (const [column, label] of [[mapping.first, "first name"], [mapping.last, "last name"]] as const) {
    if (column === "") continue;
    const blank = bad(row => at(row, column) === "");
    if (blank.length > 0) {
      return {
        heading: `Column "${column}" is empty in ${blank.length.toLocaleString()} ${blank.length === 1 ? "row" : "rows"}, so those people have no ${label}.`,
        rows: blank.slice(0, SHOWN),
        total: blank.length,
      };
    }
  }

  return null;
}

/**
 * The rows as recipients.
 *
 * An unmapped name column leaves the field undefined rather than empty. The
 * two mean the same thing to the server and one spelling of nothing is what
 * keeps the stored segment readable.
 */
export function toRecipients(sheet: Sheet, mapping: Mapping): Imported[] {
  const index = (column: string) => (column === "" ? -1 : sheet.columns.indexOf(column));
  const email = index(mapping.email);
  const first = index(mapping.first);
  const last = index(mapping.last);

  return sheet.rows.map(row => ({
    email: row[email],
    firstName: first < 0 ? undefined : row[first],
    lastName: last < 0 ? undefined : row[last],
  }));
}
