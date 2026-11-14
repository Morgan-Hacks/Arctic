"use client";

import { useId, useRef, useState } from "react";
import { Alert02Icon, File01Icon, Tick02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { Select } from "@/components/ui/select";
import { guessColumn, inspect, parseSheet, toRecipients, type Mapping, type Sheet } from "./csv";
import type { ListRecipient } from "./types";
import styles from "./campaign-draft.module.css";

/**
 * Turning a spreadsheet into recipients.
 *
 * Parsed in the browser rather than uploaded. The file is a few hundred rows
 * of names and addresses, and the whole of the work is choosing which column
 * is which -- which is a conversation, not a request. Sending it to the server
 * to be told the mapping is wrong would be a round trip per guess, and would
 * leave a file of personal data sitting in a request log on the way to being
 * rejected.
 */
export function RecipientImport({
  onImport,
  disabled,
}: {
  onImport: (recipients: ListRecipient[]) => void;
  disabled?: boolean;
}) {
  const field = useId();
  const input = useRef<HTMLInputElement>(null);
  const [name, setName] = useState<string | null>(null);
  const [sheet, setSheet] = useState<Sheet | null>(null);
  const [mapping, setMapping] = useState<Mapping>({ email: "", first: "", last: "" });
  const [unreadable, setUnreadable] = useState<string | null>(null);

  async function read(file: File) {
    setUnreadable(null);
    setName(file.name);

    const parsed = parseSheet(await file.text());
    if (!parsed) {
      setSheet(null);
      setUnreadable("That file has no header row, so there are no columns to match up.");
      return;
    }

    setSheet(parsed);
    setMapping({
      email: guessColumn(parsed.columns, "email"),
      first: guessColumn(parsed.columns, "first"),
      last: guessColumn(parsed.columns, "last"),
    });
  }

  function clear() {
    setSheet(null);
    setName(null);
    setUnreadable(null);
    if (input.current) input.current.value = "";
  }

  const problem = sheet ? inspect(sheet, mapping) : null;
  const ready = sheet !== null && problem === null;

  return (
    <div className={styles.importer}>
      <div className={styles.field}>
        <label htmlFor={field}>Upload a spreadsheet <span>Optional</span></label>
        <input
          ref={input}
          id={field}
          type="file"
          accept=".csv,.tsv,text/csv,text/tab-separated-values,text/plain"
          disabled={disabled}
          onChange={event => {
            const file = event.target.files?.[0];
            if (file) void read(file);
          }}
        />
        <p className={styles.hint}>
          A CSV or TSV with a header row. Nothing is uploaded: the file is read
          here, and only the columns you pick are sent.
        </p>
      </div>

      {unreadable ? <p className={styles.error} role="alert">{unreadable}</p> : null}

      {sheet ? (
        <div className={styles.importMap}>
          <p className={styles.importFile}>
            <Icon icon={File01Icon} size={15} />
            <strong>{name}</strong>
            <span>{sheet.rows.length.toLocaleString()} {sheet.rows.length === 1 ? "row" : "rows"}</span>
            <button type="button" onClick={clear} disabled={disabled}>Remove</button>
          </p>

          <div className={styles.importColumns}>
            {([
              ["email", "Email address", true],
              ["first", "First name", false],
              ["last", "Last name", false],
            ] as const).map(([key, label, required]) => (
              <div className={styles.field} key={key}>
                <label htmlFor={`${field}-${key}`}>
                  {label} {required ? null : <span>Optional</span>}
                </label>
                <Select
                  id={`${field}-${key}`}
                  value={mapping[key]}
                  disabled={disabled}
                  onChange={event => setMapping(current => ({ ...current, [key]: event.target.value }))}
                >
                  <option value="">{required ? "Choose a column" : "Do not use"}</option>
                  {sheet.columns.map((column, i) => (
                    <option key={`${column}-${i}`} value={column}>{column}</option>
                  ))}
                </Select>
              </div>
            ))}
          </div>

          {problem ? (
            <div className={styles.importProblem} role="alert">
              <Icon icon={Alert02Icon} size={16} />
              <div>
                <strong>{problem.heading}</strong>
                {problem.rows.length > 0 ? (
                  <p>
                    {problem.total > problem.rows.length
                      ? `First ${problem.rows.length} of ${problem.total.toLocaleString()}, at `
                      : "At "}
                    {problem.rows.length === 1 ? "row " : "rows "}
                    {problem.rows.join(", ")}
                    {problem.total > problem.rows.length ? "…" : ""}
                    . Fix the file and upload it again, or leave that column
                    unused.
                  </p>
                ) : null}
              </div>
            </div>
          ) : (
            <p className={styles.importReady}>
              <Icon icon={Tick02Icon} size={16} />
              <span>
                {sheet.rows.length.toLocaleString()} {sheet.rows.length === 1 ? "recipient" : "recipients"}
                {mapping.first || mapping.last ? ", every one with a name." : ", addresses only."}
              </span>
            </p>
          )}

          <button
            type="button"
            className={styles.secondary}
            disabled={disabled || !ready}
            onClick={() => {
              if (!sheet || !ready) return;
              onImport(toRecipients(sheet, mapping));
              clear();
            }}
          >
            Use these recipients
          </button>
        </div>
      ) : null}
    </div>
  );
}
