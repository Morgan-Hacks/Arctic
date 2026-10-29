-- Sponsors: who is backing the event, at what tier, and what they committed.
--
-- Until now `sponsors.view`, `sponsors.edit` and `sponsors.view_financials`
-- were phase-two placeholders: granted to the sponsorship and finance teams
-- but checked by no endpoint, with no table behind them. This is the table.
--
-- The shape follows the issue that asked for it. A sponsor report is shaped
-- by the sponsorship tier, not by what is easy to query -- so the tier is a
-- real column with a fixed set of values, and the report below groups by it.
-- Money and applicant numbers are different audiences -- the sponsor wants
-- the numbers, finance wants the money -- so the financial column lives on
-- this row but never leaves it except for a holder of
-- `sponsors.view_financials`, and never in the same export as applicant data.
--
-- Resume sharing is deliberately absent. `mlh_data_sharing_at` records
-- agreement to share with MLH specifically, not with sponsors generally, so
-- there is no consent column here and no resume key: this table cannot become
-- a second way to reach a resume.

CREATE SCHEMA IF NOT EXISTS sponsors;

CREATE TABLE sponsors.sponsors (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Scoped to an event like everything else that is counted here. Last
    -- year's sponsors are not this year's, and a tracker that mixes them is
    -- one nobody trusts by February.
    event_id    uuid NOT NULL REFERENCES applications.events (id) ON DELETE CASCADE,

    -- What the sponsor is called on the report and on the site. Btrimmed and
    -- bounded: a name that is only whitespace is a row nobody can find, and
    -- an unbounded name is a layout break on the page it ends up on.
    name        text NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 200),

    -- The tier the export is shaped by. Fixed values rather than free text:
    -- a report that groups by tier cannot group "Gold", "gold" and "GOLD" as
    -- three tiers, and a typo at entry time would survive into the file that
    -- goes to the sponsor.
    tier        text NOT NULL CHECK (tier IN (
                    'platinum', 'gold', 'silver', 'bronze', 'community', 'in_kind')),

    -- Where this relationship stands. Operational rather than financial, so
    -- it reads with `sponsors.view` like the name and tier do: somebody
    -- working the sponsor table needs to know what is still prospective
    -- without holding the money permission.
    status      text NOT NULL DEFAULT 'prospective' CHECK (status IN (
                    'prospective', 'committed', 'paid', 'declined')),

    -- How to reach them and where they live on the web. Contact details for
    -- organizers, never for the report: the export a sponsor receives must
    -- not contain addresses, and these columns are the reason it has to be
    -- built rather than dumped.
    website_url   text CHECK (website_url IS NULL OR length(website_url) <= 500),
    contact_name  text CHECK (contact_name IS NULL OR length(contact_name) <= 200),
    contact_email text CHECK (contact_email IS NULL OR length(contact_email) <= 320),

    -- What they committed, in cents. Nullable: a prospective sponsor has
    -- committed nothing yet, and zero is a real commitment of nothing rather
    -- than the absence of one. Never negative, enforced here rather than in
    -- C#: a negative commitment that reached the row would read as the event
    -- owing the sponsor.
    --
    -- Financial, so only a holder of `sponsors.view_financials` ever reads
    -- it. The API strips it for anybody else rather than refusing the row,
    -- because the sponsorship team manages these rows without holding the
    -- money permission and still needs the rest of them.
    amount_cents integer CHECK (amount_cents IS NULL OR amount_cents >= 0),

    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);

-- One event's sponsors, by tier then name: the order the report reads in.
CREATE INDEX sponsors_by_event ON sponsors.sponsors (event_id, tier, lower(name));

-- A sponsor once per event. Case-insensitive: "Acme" and "acme" are the same
-- company and would otherwise get two rows, two report lines, and two
-- different totals depending on which one somebody updated.
CREATE UNIQUE INDEX sponsors_event_name_key
    ON sponsors.sponsors (event_id, lower(btrim(name)));

CREATE TRIGGER sponsors_touch_updated_at
    BEFORE UPDATE ON sponsors.sponsors
    FOR EACH ROW EXECUTE FUNCTION applications.touch_updated_at();

-- The permissions already exist on the teams that need them: sponsorship
-- holds view and edit, finance holds view and view_financials, super-admin
-- holds all three. Nothing to grant here, which is worth saying out loud --
-- a migration that re-grants them would hide a baseline that drifted.
