-- What sponsors commit, and what finance is told they paid.
--
-- No sponsor existed in this schema before this migration. The three
-- permissions (sponsors.view, sponsors.edit, sponsors.view_financials) were
-- phase-two placeholders; this is the table they finally have something to
-- guard.

CREATE SCHEMA IF NOT EXISTS sponsors;

CREATE TABLE sponsors.sponsors (
    id                      uuid        PRIMARY KEY DEFAULT gen_random_uuid(),

    event_id                uuid        NOT NULL REFERENCES applications.events (id) ON DELETE CASCADE,

    name                    text        NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 160),

    -- Free text on purpose. Tiers change between years, and a lookup table
    -- would force a migration every time a presenting sponsor asks for a
    -- bespoke name. The report logic still needs to know what a tier buys,
    -- which is decided in code by the tier slug rather than by this row.
    tier                    text        NOT NULL CHECK (length(btrim(tier)) BETWEEN 1 AND 60),

    primary_contact_name    text        CHECK (primary_contact_name IS NULL OR length(primary_contact_name) <= 160),
    primary_contact_email   text        CHECK (primary_contact_email IS NULL OR length(primary_contact_email) <= 254),

    -- Money, finance only. NULL means not yet invoiced or in kind. Cents
    -- rather than a decimal, because every other monetary amount in this
    -- system would have to be parsed back to a number by somebody, and
    -- cents do not parse.
    amount_cents            bigint      CHECK (amount_cents IS NULL OR amount_cents >= 0),
    currency                text        NOT NULL DEFAULT 'USD' CHECK (length(currency) = 3),
    committed_at            timestamptz,
    reference               text        CHECK (reference IS NULL OR length(reference) <= 160),

    created_at              timestamptz NOT NULL DEFAULT now(),
    updated_at              timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX sponsors_by_event ON sponsors.sponsors (event_id, lower(name));

CREATE TRIGGER sponsors_touch_updated_at
    BEFORE UPDATE ON sponsors.sponsors
    FOR EACH ROW EXECUTE FUNCTION applications.touch_updated_at();
