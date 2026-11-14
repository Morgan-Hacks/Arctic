-- Volunteer shift coverage: who is covering which shift, and where the gaps are.
--
-- The availability question itself is deliberately NOT answered here. A
-- volunteer is a row in identity.teams holding checkin.scan and swag.scan;
-- organizers sign in with Google, while a gated form is answered by somebody
-- with an application who signs in with a magic link. A volunteer may have no
-- application at all, and there is nothing to attach a response to -- so
-- "collect availability with a form" and "volunteers are organizers" do not
-- fit together, and resolving that is a choice about identity rather than a
-- table this migration could add.
--
-- The cheaper half, and the one this file builds, is the part that works
-- whichever way that choice goes: shifts against people, with the gaps
-- obvious, read from rows an organizer enters. Availability collected
-- elsewhere (a shared sheet, or an organizer typing it in) lands in
-- volunteers.assignments; if volunteers ever get applications of their own,
-- only where those rows are written from changes.
--
-- Why free-text names rather than a person_id:
--
-- The person the name belongs to may not exist in identity.people. Volunteers
-- are organizers the day they scan, but the coverage is planned before the
-- accounts exist -- from a sheet of first names and "can do Saturday
-- morning". A foreign key here would refuse the ordinary case. created_by
-- records which organizer typed it, which is the half worth attributing.
--
-- Why needed lives on the shift:
--
-- The question asked at the desk is "how many more", not "who". needed minus
-- the assignment count is the gap, computed on read rather than stored, so a
-- row can never disagree with the assignments under it.

CREATE SCHEMA IF NOT EXISTS volunteers;

CREATE TABLE volunteers.shifts (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Scoped to an event, like everything else that is counted here. Last
    -- year's door shift is not this year's, and a coverage view that mixes
    -- them is one nobody trusts by February.
    event_id    uuid        NOT NULL REFERENCES applications.events (id) ON DELETE CASCADE,

    name        text        NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 120),

    -- When the shift runs. Both nullable: the name is agreed before the
    -- timetable is, and demanding times up front would mean inventing them.
    starts_at   timestamptz,
    ends_at     timestamptz
                    CHECK (ends_at IS NULL OR starts_at IS NULL OR ends_at > starts_at),

    -- How many people this shift needs. The gap is needed minus assigned,
    -- floored at zero on read; storing the gap instead would let the two
    -- disagree the moment an assignment is added.
    needed      integer     NOT NULL DEFAULT 1 CHECK (needed >= 1),

    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),

    created_by  uuid        REFERENCES identity.people (id) ON DELETE SET NULL
);

CREATE INDEX shifts_by_event ON volunteers.shifts (event_id, starts_at NULLS LAST, lower(name));

CREATE TRIGGER shifts_touch_updated_at
    BEFORE UPDATE ON volunteers.shifts
    FOR EACH ROW EXECUTE FUNCTION applications.touch_updated_at();

CREATE TABLE volunteers.assignments (
    id              uuid        PRIMARY KEY DEFAULT gen_random_uuid(),

    shift_id        uuid        NOT NULL REFERENCES volunteers.shifts (id) ON DELETE CASCADE,

    -- Who is covering, as typed by the organizer planning the shift. Free
    -- text on purpose -- see the header -- and bounded at 160 like a sponsor
    -- contact name, which is the closest thing this schema already holds.
    volunteer_name  text        NOT NULL CHECK (length(btrim(volunteer_name)) BETWEEN 1 AND 160),

    -- Where to reach them on the day. Optional: the sheet does not always
    -- have it, and a coverage view that refuses a name without an address is
    -- a view that goes stale while somebody waits for a reply.
    volunteer_email text        CHECK (volunteer_email IS NULL OR length(volunteer_email) <= 254),

    created_at      timestamptz NOT NULL DEFAULT now(),

    -- Who typed it in, and null-on-delete for the reason
    -- applications.decided_by is: removing a person must not remove the
    -- record of what they planned.
    created_by      uuid        REFERENCES identity.people (id) ON DELETE SET NULL
);

CREATE INDEX assignments_by_shift ON volunteers.assignments (shift_id, lower(volunteer_name));

-- Who may plan coverage. Logistics owns the door on the day -- check-in,
-- swag, and the inventory it hands out -- and the volunteer rota is planned
-- by the same people, not by every team that can read the applicant queue.
-- Super admin holds everything, as always.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, 'volunteers.manage' FROM identity.teams
 WHERE slug IN ('logistics', 'super-admin')
ON CONFLICT DO NOTHING;
