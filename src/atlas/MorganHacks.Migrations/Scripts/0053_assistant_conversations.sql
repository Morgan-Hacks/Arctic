-- What somebody asked the assistant, and what it said back.
--
-- History is the whole reason this table exists. A question like "and by
-- school?" means nothing on its own, and an assistant that cannot be asked a
-- follow-up is one people stop asking anything, because every question has to
-- be a paragraph.
--
-- Scoped to a person, not an event and not a team. Two reasons, and the
-- second is the one that matters. An answer was computed against the
-- permissions its asker held at the time, so showing that answer to somebody
-- else would hand them a figure their own permissions might refuse -- and
-- re-checking a stored sentence is not possible, because a sentence is not a
-- query. Keeping threads private means the permission that produced an answer
-- is the permission of the only person who can read it.

CREATE SCHEMA IF NOT EXISTS assistant;  -- atlas / Applications.Assistant

-- ---------------------------------------------------------- conversations ---
CREATE TABLE assistant.threads (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),

    person_id   uuid        NOT NULL REFERENCES identity.people (id) ON DELETE CASCADE,

    -- Which event the questions were about. Carried so reopening a thread
    -- resumes against the same cohort: "how many confirmed" asked in one
    -- thread and answered against a different event is a wrong answer that
    -- looks right.
    event_id    uuid        REFERENCES applications.events (id) ON DELETE CASCADE,

    -- The first question, trimmed, so the list of threads reads like a list of
    -- questions rather than a list of timestamps.
    title       text        NOT NULL CHECK (length(btrim(title)) BETWEEN 1 AND 200),

    created_at  timestamptz NOT NULL DEFAULT now(),

    -- Touched on every turn, because the list is ordered by "what was I just
    -- doing" rather than by when a thread started.
    last_at     timestamptz NOT NULL DEFAULT now()
);

-- The list screen's only query: this person's threads, most recent first.
CREATE INDEX assistant_threads_mine_idx
    ON assistant.threads (person_id, last_at DESC);

-- Retention's index, and it is deliberately not scoped to a person.
--
-- Atlas has no scheduler, so the purge runs off the back of ordinary use
-- rather than on a timer. If it only ever deleted the caller's own threads,
-- somebody who asked questions once and never came back would keep theirs
-- forever -- which is the one case a retention window exists for. So the
-- sweep is global and this index is what makes it cheap.
CREATE INDEX assistant_threads_stale_idx
    ON assistant.threads (last_at);

-- -------------------------------------------------------------- the turns ---
CREATE TABLE assistant.turns (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),

    thread_id   uuid        NOT NULL REFERENCES assistant.threads (id) ON DELETE CASCADE,

    -- 'person' or 'assistant'. A text CHECK rather than a boolean, because a
    -- third speaker is plausible -- a note saying a thread was truncated, say
    -- -- and `is_user = false` would be a bad place to put one.
    speaker     text        NOT NULL CHECK (speaker IN ('person', 'assistant')),

    body        text        NOT NULL CHECK (length(body) <= 8000),

    -- Which reads produced this answer, as a list of tool names. Not the
    -- arguments and not the rows: the point is that an organizer can see the
    -- answer came from the funnel read rather than from the model's
    -- imagination, and that is a list of names.
    --
    -- Null for a person's turn, because a question is not computed.
    basis       jsonb       CHECK (basis IS NULL OR jsonb_typeof(basis) = 'array'),

    created_at  timestamptz NOT NULL DEFAULT now()
);

-- Reading a thread: its turns in the order they were said.
CREATE INDEX assistant_turns_thread_idx
    ON assistant.turns (thread_id, created_at);
