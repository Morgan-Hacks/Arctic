"use client";

import { Suspense, useEffect, useRef, useState, useTransition } from "react";
import { usePathname, useSearchParams } from "next/navigation";
import {
  AiMagicIcon,
  ArrowRight02Icon,
  Cancel01Icon,
  Clock01Icon,
  Delete02Icon,
} from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { Select } from "@/components/ui/select";
import {
  events as readEvents,
  forget,
  say,
  thread as readThread,
  threads as readThreads,
  type EventChoice,
  type ThreadSummary,
  type Turn,
} from "./chat";
import { eventOf, screenOf } from "./screen";
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

      {/* The boundary lives here rather than at the mount sites, because the
          hook that needs one is inside Panel and a boundary two files away is
          one somebody deletes. Nothing to fall back to: the panel is already
          empty until its first read answers. */}
      {open ? (
        <Suspense fallback={null}>
          <Panel onClose={() => setOpen(false)} />
        </Suspense>
      ) : null}
    </>
  );
}

function Panel({ onClose }: { onClose: () => void }) {
  const [turns, setTurns] = useState<Turn[]>([]);
  const [threadId, setThreadId] = useState<string | undefined>();
  const [history, setHistory] = useState<ThreadSummary[]>([]);
  const [keptForDays, setKeptForDays] = useState(0);
  const [showHistory, setShowHistory] = useState(false);
  const [choices, setChoices] = useState<EventChoice[]>([]);
  const [eventId, setEventId] = useState<string | null>(null);
  const [eventName, setEventName] = useState<string | null>(null);
  const [ready, setReady] = useState(false);
  const [question, setQuestion] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [sending, send] = useTransition();

  const foot = useRef<HTMLDivElement>(null);

  // Read per render rather than when the panel opened. Somebody asks a
  // question, navigates, and asks a follow-up without closing the panel, and
  // the second question is about the screen they are on now.
  const screen = screenOf(usePathname());

  // What the page has selected, used only as the opening suggestion. The
  // choice itself is made in the panel and then held for the conversation:
  // following the URL instead would change what a thread was about halfway
  // through it, and "and by school?" answered against a different cohort is a
  // wrong answer with nothing on it to say so.
  const suggested = eventOf(useSearchParams());

  useEffect(() => {
    let live = true;

    (async () => {
      const [known, listed] = await Promise.all([readEvents(), readThreads()]);
      if (!live) return;

      // The page's event when it names one we know, and the console's own
      // default otherwise. Either way it is shown before anything is asked and
      // can be changed, so nothing is decided on somebody's behalf in silence.
      const opening =
        known.events.find((one) => one.id === suggested) ?? known.chosen ?? null;

      setChoices(known.events);
      setEventId(opening?.id ?? null);
      setEventName(opening?.name ?? null);
      setHistory(listed.threads);
      setKeptForDays(listed.keptForDays);
      setReady(true);
    })();

    return () => {
      live = false;
    };
    // Once, when the panel opens. The suggestion is read at that moment and
    // not followed afterwards -- see above.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Whether the event is still open to be changed.
  //
  // Until the first question, because that is when the thread is created and
  // the event written onto it. After that the conversation is about that
  // cohort and a picker would be offering something it cannot do.
  const choosing = turns.length === 0 && !threadId;

  function choose(id: string) {
    setEventId(id);
    setEventName(choices.find((one) => one.id === id)?.name ?? null);
  }

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
      const said = await say(asked, eventId, threadId, screen);

      // Guarded on the fields this uses rather than on the error, so the
      // narrowing is the compiler's rather than a promise made to it.
      if (said.error || !said.threadId || !said.answer) {
        setProblem(said.error ?? "That answer came back empty.");
        return;
      }

      setThreadId(said.threadId);

      // What the API says it answered about, which is the figure's one
      // unstated premise. They agree in every ordinary case; this is here for
      // the case where they do not.
      if (said.eventName) {
        setEventName(said.eventName);
      }

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

    // The thread's event, not whatever was picked last. Reopening Monday's
    // questions about last cycle and asking "and by school?" has to answer
    // about last cycle -- the alternative is a follow-up that silently changes
    // cohort between one turn and the next.
    if (found.eventId && found.eventId !== eventId) {
      setEventId(found.eventId);
      setEventName(
        choices.find((one) => one.id === found.eventId)?.name ?? null,
      );
    }
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
                      <span className={styles.threadTitle}>{one.title}</span>

                      {/* Which cohort it was about. Worth the line now that a
                          conversation is pinned to an event: two threads
                          titled "how many confirmed" are the same question
                          about different years, and the title alone cannot
                          tell them apart. */}
                      {choices.length > 1 ? (
                        <span className={styles.threadEvent}>
                          {choices.find((it) => it.id === one.eventId)?.name ??
                            "Event since removed"}
                        </span>
                      ) : null}
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
        {turns.length === 0 && !sending ? (
          <Opening
            choosing={choosing}
            choices={choices}
            eventId={eventId}
            onChoose={choose}
          />
        ) : null}

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
/**
 * What the panel says before anything has been asked.
 *
 * The event is picked here rather than in the header, because it is the first
 * decision rather than a setting: every figure that follows is about that
 * cohort, and the answer will not say which one unless it was asked about the
 * right one. Chosen once and then held — the header carries it afterwards.
 */
function Opening({
  choosing,
  choices,
  eventId,
  onChoose,
}: {
  choosing: boolean;
  choices: EventChoice[];
  eventId: string | null;
  onChoose: (id: string) => void;
}) {
  return (
    <div className={styles.opening}>
      {/* Only with something to choose between. One event is not a decision,
          and the header already names it. */}
      {choosing && choices.length > 1 ? (
        <label className={styles.pick}>
          <span>Asking about</span>
          <Select
            aria-label="Event"
            value={eventId ?? ""}
            onChange={(e) => onChoose(e.target.value)}
          >
            {choices.map((one) => (
              <option key={one.id} value={one.id}>
                {one.name}
              </option>
            ))}
          </Select>
        </label>
      ) : null}

      <p className={styles.note}>
        Ask about your applicants, the form, the funnel, the mail, or what is on
        the shelf. Follow-ups work — ask “and by school?” after anything.
      </p>
      <ul className={styles.examples}>
        <li>How many offers should we send to fill 150 seats?</li>
        <li>Which form questions do people skip?</li>
        <li>Who confirmed but has not checked in?</li>
        <li>When do the medium shirts run out?</li>
      </ul>
    </div>
  );
}
