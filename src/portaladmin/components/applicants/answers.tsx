import { Tick02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { ApplicantTime } from "./applicant-time";
import styles from "./applicant-detail.module.css";
import type { Answer } from "./types";

/**
 * What this applicant wrote, under the questions they were asked.
 *
 * The questions are joined on by the API, in the order the published form asks
 * them, with the ones the form no longer publishes last and unlabelled. Both
 * halves are ordinary: a form edited mid-cycle leaves earlier applicants with
 * answers under keys nothing asks for any more, and those are still somebody's
 * words.
 *
 * Every unanswered question is shown rather than skipped. An absence is a fact
 * about this applicant — an optional question they chose not to answer — and a
 * list that dropped it would read as a question nobody was ever asked.
 */
export function Answers({ answers }: { answers: Answer[] }) {
  if (answers.length === 0) {
    return (
      <p className="meta">
        No answers yet. The row exists from the moment somebody opens the form.
      </p>
    );
  }

  return (
    <dl className={styles.answers}>
      {answers.map((answer) => (
        <div key={answer.key} className={styles.qa}>
          <dt className={styles.question}>
            {answer.label ?? (
              // The key, because the wording was deleted with the question and
              // this is all that is left of it. Mono so it reads as an
              // identifier rather than as a badly written question.
              <span className={styles.questionKey} title="No longer on this form">
                {answer.key}
              </span>
            )}
          </dt>
          <dd className={styles.value}><Value answer={answer} /></dd>
        </div>
      ))}
    </dl>
  );
}

/**
 * One answer, rendered as whatever it turned out to be.
 *
 * The value is `unknown` on purpose — it is whatever somebody answered,
 * including under a key no question claims — so every shape is narrowed here
 * rather than assumed anywhere else.
 */
function Value({ answer }: { answer: Answer }) {
  const { key, value } = answer;
  if (value === null || value === undefined || value === "") {
    return <p className={styles.unanswered}>Not answered</p>;
  }

  if ((key === "mlh_coc_agreed_at" || key === "mlh_data_sharing_at")
    && typeof value === "string" && !Number.isNaN(Date.parse(value))) {
    return <div className={styles.agreement}>
      <p><Icon icon={Tick02Icon} size={16} />Agreed</p>
      <ApplicantTime value={value} />
    </div>;
  }

  if (key === "directory_contact_links" && Array.isArray(value)) return <ul>{value.map((v,i)=><li key={i}>{String(v).replace("|", ": ")}</li>)}</ul>;

  if (Array.isArray(value)) {
    // A checkboxes answer. A list rather than a comma-separated line, because
    // the options themselves can contain commas.
    return (
      <ul className={styles.ticked}>
        {value.map((one, index) => (
          <li key={`${String(one)}-${index}`}>{String(one)}</li>
        ))}
      </ul>
    );
  }

  if (typeof value === "boolean") {
    return <p className={styles.answer}>{value ? "Yes" : "No"}</p>;
  }

  // Everything else as text, line breaks kept. An essay reflowed into one
  // block is one nobody finishes.
  return <p className={styles.answer}>{String(value)}</p>;
}
