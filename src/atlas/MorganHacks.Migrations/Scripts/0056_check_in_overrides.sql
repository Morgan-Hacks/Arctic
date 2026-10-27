-- Manual overrides at the check-in desk: somebody let in without a code, or
-- a name corrected when the applicant's own form has locked them out.
--
-- On the day, somebody WILL be standing at the desk with a broken QR code and
-- a queue behind them. The alternative to this is an organizer with database
-- access typing an UPDATE at the desk, which is the actual risk this removes:
-- not that a rule gets bypassed, but that it gets bypassed invisibly.
--
-- Why a table rather than just audit.entries:
--
-- audit.entries is deliberately narrow — no PII ever, and a row for every
-- access change in the system. An override is not an access change, and the
-- things worth reading back in March are the reason somebody gave and what
-- the field said before it was rewritten. The reason goes in both places (the
-- audit detail carries it so the trail alone answers "why"), but the old and
-- new values of a corrected name are a person's name: they belong on an
-- applications.* row, which is where the name already lives, and not copied
-- into the access log the 0010 header promises is safe if it leaks.
--
-- Why the write is a table insert and not a statement in C#:
--
-- Same argument as 0010 and 0006. The trigger below fires inside the caller's
-- transaction, so the override and its audit row commit together or not at
-- all. An HTTP call to something that records audits could fail while the
-- override it was recording succeeds, and a trail with holes is worse than no
-- trail because the holes are invisible.
--
-- The subject of the audit row is the applicant, when they have an identity
-- row. An application begun before sign-in has none, and audit.entries forces
-- exactly one subject (num_nonnulls check) — so those get subject_team
-- 'applications', a text slug naming the collection the row belongs to,
-- against target = the application id. That is the column being text rather
-- than a foreign key earning its keep: the row exists, points at the right
-- object, and refuses to pretend the subject was a person it could not find.
--
-- What is deliberately NOT here: any way to move an application between
-- statuses other than confirmed -> checked_in. The endpoint behind this
-- table has no status field; the lifecycle table (StatusTransition) still
-- decides the one move this makes, and 'accepted' stays refused at the desk
-- exactly as it was before this file. Letting somebody in without a code is
-- a door action; marking an application accepted is a decision, and the two
-- do not sit behind the same button.

CREATE TABLE applications.overrides (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    application_id uuid NOT NULL
                         REFERENCES applications.applications (id) ON DELETE CASCADE,

    -- 'check_in' — admitted without a code, status confirmed -> checked_in.
    -- 'name_corrected' — first/last name rewritten at the desk; no status
    -- change, which is why this table exists at all: status history has
    -- nothing to say about a name.
    kind           text NOT NULL CHECK (kind IN ('check_in', 'name_corrected')),

    -- The sentence somebody typed while the person in front of them waited.
    -- NOT NULL on purpose: an override with no reason attached is
    -- indistinguishable from a mistake when somebody reads it back in March,
    -- and the place to refuse one is before it happens, not in a report.
    -- Bounded at 500 to match what the endpoints accept on a status change.
    reason         text NOT NULL CHECK (length(btrim(reason)) BETWEEN 1 AND 500),

    -- What was rewritten, as {"firstName": {"from": ..., "to": ...}}.
    -- Empty for 'check_in'. This is where the values live rather than in
    -- audit.detail — see the header.
    changes        jsonb NOT NULL DEFAULT '{}'::jsonb,

    -- Who did it, from app.actor_id like every other trail row. Nullable
    -- with the same honest NULL audit.entries takes: a fix typed into psql
    -- during an incident has no person behind it, and putting a name against
    -- a decision nobody made is worse than admitting there isn't one.
    actor_id       uuid DEFAULT (NULLIF(current_setting('app.actor_id', true), '')::uuid)
                         REFERENCES identity.people (id),

    created_at     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX overrides_application_idx
    ON applications.overrides (application_id, created_at DESC);

-- ----------------------------------------------------------------- audit ---
-- One trigger, both kinds. The action names split the same way the issue
-- that asked for them did: a door decision and a data correction are
-- different facts about the same application.
CREATE FUNCTION applications.record_override() RETURNS trigger AS $$
DECLARE
    subject  uuid;
    detail   jsonb := jsonb_build_object('reason', NEW.reason);
BEGIN
    SELECT person_id INTO subject
      FROM applications.applications
     WHERE id = NEW.application_id;

    -- The field names of a correction, never the values: jsonb_object_keys
    -- gives 'firstName', and the before/after stays on the row above. A
    -- reason is a human sentence the way status_history.reason already is;
    -- what must not become structured PII in the access log is the data
    -- itself.
    IF NEW.changes <> '{}'::jsonb THEN
        detail := detail || jsonb_build_object('fields', (
            SELECT coalesce(jsonb_agg(key), '[]'::jsonb)
              FROM jsonb_object_keys(NEW.changes) AS t(key)));
    END IF;

    PERFORM audit.record(
        CASE NEW.kind
            WHEN 'check_in' THEN 'checkin.overridden'
            ELSE 'application.corrected'
        END,
        subject,
        CASE WHEN subject IS NULL THEN 'applications' END,
        NEW.application_id::text,
        NULL,
        detail);

    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER overrides_record_audit
    AFTER INSERT ON applications.overrides
    FOR EACH ROW EXECUTE FUNCTION applications.record_override();

-- The permission, and who holds it.
--
-- Super admin and logistics, and NOT the volunteer team — which is the whole
-- argument for the permission existing. Volunteers hold checkin.scan because
-- the desk is staffed by whoever is free; an override is the sentence
-- somebody reads when they ask "who is allowed to let a person in without a
-- code, or rewrite a name?" and the answer should not be "everybody holding
-- a scanner". Logistics runs the day and is rostered for exactly the case
-- where the normal path refuses; super admin holds everything that is a
-- judgement call rather than a task.
--
-- 'checkin.override' not 'applications.decide': this deliberately does not
-- come with the decisions, and seeding it onto registration or judge would
-- put the desk's bypass in the hands of teams that are never at the door.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, p FROM identity.teams, unnest(ARRAY[
    'checkin.override'
]) AS p WHERE slug IN ('super-admin', 'logistics')
ON CONFLICT DO NOTHING;
