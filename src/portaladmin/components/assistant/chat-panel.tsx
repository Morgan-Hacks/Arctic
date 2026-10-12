"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import {
  AiMagicIcon,
  ArrowRight02Icon,
  Cancel01Icon,
  Clock01Icon,
  Delete02Icon,
} from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import {
  forget,
  here,
  say,
  thread as readThread,
  threads as readThreads,
  type ThreadSummary,
  type Turn,
} from "./chat";
import styles from "./chat-panel.module.css";

/**
 * The assistant, on the side of whatever you are looking at.
 *
 * A conversation rather than a box that filters. The box it replaces turned a
 * sentence into query parameters, which duplicated controls two clicks away
 * and could not be asked a follow-up -- and a follow-up is most of what
 * analysis actually is. "What is our acceptance rate" is one question; "and
 * for first-timers" is the one that tells you something.
 *
 * Everything shown here was computed by the API out of reads the asking person
 * was already allowed to make. This component holds no figures of its own and
 * works none out, which is why it can be a client component at all.
 */
export function ChatPanel() {
  const [open, setOpen] = useState(false);

  // Nothing is fetched until the panel is first opened. It sits on every
  // screen in the console, and a panel nobody opened should not cost a request
  // on every page load.
  return (
    <>
      {open ? null : (
        <button
          type="button"
          className={styles.launcher}
          onClick={() => setOpen(true)}
          aria-label="Ask the assistant"
        >
          <Icon icon={AiMagicIcon} size={20} />
        </button>
      )}

      {open ? <Panel onClose={() => setOpen(false)} /> : null}
    </>
  );
}

function Panel({ onClose }: { onClose: () => void }) {
  const [turns, setTurns] = useState<Turn[]>([]);
  const [threadId, setThreadId] = useState<string | undefined>();
  const [history, setHistory] = useState<ThreadSummary[]>([]);
  const [keptForDays, setKeptForDays] = useState(0);
  const [showHistory, setShowHistory] = useState(false);
  const [eventId, setEventId] = useState<string | null>(null);
  const [eventName, setEventName] = useState<string | null>(null);
  const [ready, setReady] = useState(false);
  const [question, setQuestion] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [sending, send] = useTransition();

  const foot = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let live = true;

    (async () => {
      const [where, listed] = await Promise.all([here(), readThreads()]);
      if (!live) return;

      setEventId(where.eventId);
      setEventName(where.name);
      setHistory(listed.threads);
      setKeptForDays(listed.keptForDays);
      setReady(true);
    })();

    return () => {
      live = false;
    };
  }, []);

  // Follows the conversation down as it grows, including while an answer is
  // being waited on, so the "thinking" line is what you are looking at.
  useEffect(() => {
    foot.current?.scrollIntoView({ block: "end", behavior: "smooth" });
  }, [turns.length, sending]);

  function ask() {
    const asked = question.trim();
    if (asked.length === 0 || !eventId) return;

    setProblem(null);
    setQuestion("");

    // Shown immediately, before the answer exists. The round trip can take
    // several reads, and a question that vanishes for ten seconds reads as a
    // dropped one.
    setTurns((before) => [
      ...before,
      { speaker: "person", body: asked, basis: [], at: new Date().toISOString() },
    ]);

    send(async () => {
      const said = await say(asked, eventId, threadId);

      // Guarded on the fields this uses rather than on the error, so the
      // narrowing is the compiler's rather than a promise made to it.
      if (said.error || !said.threadId || !said.answer) {
        setProblem(said.error ?? "That answer came back empty.");
        return;
      }

      setThreadId(said.threadId);
      setTurns((before) => [
        ...before,
        {
          speaker: "assistant",
          body: said.answer,
          basis: said.basis,
          at: new Date().toISOString(),
        },
      ]);
    });
  }

  async function open(id: string) {
    const found = await readThread(id);
    if (!found) return;

    setThreadId(found.id);
    setTurns(found.turns);
    setShowHistory(false);
    setProblem(null);
  }

  async function drop(id: string) {
    setHistory((before) => before.filter((one) => one.id !== id));

    if (id === threadId) {
      setThreadId(undefined);
      setTurns([]);
    }

    await forget(id);
  }

  function fresh() {
    setThreadId(undefined);
    setTurns([]);
    setProblem(null);
    setShowHistory(false);
  }

  return (
    <aside className={styles.panel} aria-label="Assistant">
      <header className={styles.head}>
        <div className={styles.title}>
          <Icon icon={AiMagicIcon} size={17} />
          <span>Ask</span>
          {eventName ? <span className={styles.event}>{eventName}</span> : null}
        </div>

        <div className={styles.headActions}>
          <button
            type="button"
            className={styles.quiet}
            onClick={() => setShowHistory((was) => !was)}
            aria-pressed={showHistory}
            aria-label="Earlier questions"
          >
            <Icon icon={Clock01Icon} size={16} />
          </button>

          {turns.length > 0 ? (
            <button type="button" className={styles.quiet} onClick={fresh}>
              New
            </button>
          ) : null}

          <button
            type="button"
            className={styles.quiet}
            onClick={onClose}
            aria-label="Close assistant"
          >
            <Icon icon={Cancel01Icon} size={16} />
          </button>
        </div>
      </header>

      {showHistory ? (
        <div className={styles.history}>
          {history.length === 0 ? (
            <p className={styles.note}>Nothing asked yet.</p>
          ) : (
            <>
              <ul className={styles.threadList}>
                {history.map((one) => (
                  <li key={one.id}>
                    <button
                      type="button"
                      className={styles.thread}
                      onClick={() => open(one.id)}
                    >
                      {one.title}
                    </button>
                    <button
                      type="button"
                      className={styles.drop}
                      onClick={() => drop(one.id)}
                      aria-label={`Forget "${one.title}"`}
                    >
                      <Icon icon={Delete02Icon} size={14} />
                    </button>
                  </li>
                ))}
              </ul>

              {keptForDays > 0 ? (
                <p className={styles.note}>
                  Kept for {keptForDays} days, then forgotten.
                </p>
              ) : null}
            </>
          )}
        </div>
      ) : null}

      <div className={styles.conversation}>
        {turns.length === 0 && !sending ? <Opening /> : null}

        {turns.map((turn, at) => (
          <div
            key={`${turn.at}-${at}`}
            className={turn.speaker === "person" ? styles.mine : styles.theirs}
          >
            <p className={styles.body}>{turn.body}</p>

            {/* The reads the answer came out of, named. A figure that came
                from the funnel read and one the model recalled look identical
                in a sentence, and only one of them is worth repeating. */}
            {turn.basis.length > 0 ? (
              <p className={styles.basis}>
                {turn.basis.map((one) => one.replace(/_/g, " ")).join(" · ")}
              </p>
            ) : null}
          </div>
        ))}

        {sending ? <p className={styles.thinking}>Reading…</p> : null}
        {problem ? <p className={styles.problem}>{problem}</p> : null}

        <div ref={foot} />
      </div>

      <div className={styles.composer}>
        {ready && !eventId ? (
          <p className={styles.problem}>
            There is no event to ask about yet.
          </p>
        ) : (
          <>
            <textarea
              className={styles.input}
              rows={2}
              value={question}
              placeholder="How many are still waiting to hear back?"
              disabled={!ready || sending}
              onChange={(e) => setQuestion(e.target.value)}
              onKeyDown={(e) => {
                // Enter sends, shift-enter breaks the line. A question is
                // usually one line, and reaching for a button every time is
                // what stops people asking the second one.
                if (e.key === "Enter" && !e.shiftKey) {
                  e.preventDefault();
                  ask();
                }
              }}
            />

            <button
              type="button"
              className={`button primary ${styles.send}`}
              onClick={ask}
              disabled={!ready || sending || question.trim().length === 0}
              aria-label="Ask"
            >
              <Icon icon={ArrowRight02Icon} size={16} />
            </button>
          </>
        )}
      </div>
    </aside>
  );
}

/**
 * What to ask, for somebody who has just opened it.
 *
 * Examples rather than a description, and all four are questions the reads
 * behind this can actually answer. An empty box invites "who should we
 * accept", which is the one thing it refuses.
 */
function Opening() {
  return (
    <div className={styles.opening}>
      <p className={styles.note}>
        Ask about your applicants, the funnel, or what is on the shelf. Follow-ups
        work — ask “and by school?” after anything.
      </p>
      <ul className={styles.examples}>
        <li>What is our acceptance rate?</li>
        <li>How many offers should we send to fill 150 seats?</li>
        <li>What should I tell a sponsor?</li>
        <li>Will the review queue clear before the deadline?</li>
      </ul>
    </div>
  );
}
