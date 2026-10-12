"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { AiMagicIcon, ArrowRight02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { ask, type AskedPlan } from "./ask";
import styles from "./ask-box.module.css";

/**
 * A question, in words, about what is on this screen.
 *
 * It does not draw an answer. It works out which screen answers the question
 * and sends the reader there with the filters applied -- so the result is the
 * applicants list the console has always rendered, with the permissions,
 * columns and paging it has always had. There is no second way to display an
 * applicant here to drift from the first.
 *
 * That also means an answer is a URL, which is worth more than it sounds on
 * this screen in particular: filters here are query parameters precisely so
 * somebody who finds a problem in the waitlist can send the link to whoever
 * has to act on it. An answer arrived at by asking is as sendable as one
 * arrived at by clicking, and the back button undoes it.
 *
 * What it understood is shown rather than implied. "Applicants whose status is
 * submitted" sits above the list it just filtered, so a question read wrongly
 * reads as a wrong answer instead of a plausible one -- and the filters it set
 * are the ordinary controls, so correcting it by hand is the same gesture as
 * setting them in the first place.
 */
export function AskBox({ eventId }: { eventId?: string }) {
  const router = useRouter();
  const [question, setQuestion] = useState("");
  const [said, setSaid] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, start] = useTransition();

  function submit() {
    const asked = question.trim();
    if (!asked) return;

    setProblem(null);
    setSaid(null);

    start(async () => {
      const result = await ask(asked);

      // Checked positively rather than by eliminating the other two: the
      // union's absent members are optional, so narrowing by what is missing
      // does not tell the compiler what is present.
      if (result.plan) {
        setSaid(result.plan.explain);
        router.push(destination(result.plan, eventId));
        return;
      }

      // A refusal and a failure read the same way here on purpose -- both are
      // "no list for you yet, and here is why" -- but they are different
      // sentences and neither is invented locally.
      setProblem(result.refusal ?? result.error ?? "That question could not be read.");
    });
  }

  return (
    <div className={styles.ask}>
      <form
        className={styles.row}
        onSubmit={(event) => {
          event.preventDefault();
          submit();
        }}
      >
        <span className={styles.spark} aria-hidden="true">
          <Icon icon={AiMagicIcon} size={16} />
        </span>
        <input
          className={styles.input}
          value={question}
          onChange={(event) => setQuestion(event.target.value)}
          disabled={pending}
          /* COPY: needs sign-off. An example rather than an invitation to type
             anything: the set it can answer is real and narrow, and a
             placeholder promising more buys a refusal. */
          placeholder="Ask: who is submitted but not accepted?"
          aria-label="Ask a question about these applicants"
          maxLength={300}
        />
        <button
          type="submit"
          className={styles.go}
          disabled={pending || !question.trim()}
          aria-label="Ask"
        >
          {pending ? "…" : <Icon icon={ArrowRight02Icon} size={16} />}
        </button>
      </form>

      {said ? (
        <p className={styles.said} role="status">
          {/* COPY: needs sign-off. */}
          Showing {said}.
        </p>
      ) : null}

      {problem ? (
        <p className={styles.problem} role="alert">
          {problem}
        </p>
      ) : null}
    </div>
  );
}

/**
 * The screen a plan names, as a URL.
 *
 * A count lands on the same filtered list as a question about who: the list
 * already carries its own count, so the number is answered without a second
 * place that displays one and can disagree with it.
 */
function destination(plan: AskedPlan, eventId?: string): string {
  if (plan.view === "dashboard") {
    return eventId ? `/dashboard?eventId=${encodeURIComponent(eventId)}` : "/dashboard";
  }

  const params = new URLSearchParams();
  if (eventId) params.set("event", eventId);
  if (plan.search) params.set("q", plan.search);
  plan.statuses.forEach((status) => params.append("status", status));

  return `/applicants${params.size ? `?${params}` : ""}`;
}
