-- What logistics has, where it is, and how much of it is left.
--
-- Two tables rather than one, and the second is the point. A count on its own
-- answers "how many shirts are there" and nothing else; the question that
-- actually gets asked at one in the morning is "there were two hundred, where
-- did the other hundred and sixty go", and only a ledger answers that.

CREATE SCHEMA IF NOT EXISTS logistics;  -- atlas / Logistics module

-- ------------------------------------------------------------- the things ---
CREATE TABLE logistics.items (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Scoped to an event, like everything else that is counted here. Last
    -- year's shirts are not this year's, and a tracker that mixes them is one
    -- nobody trusts by February.
    event_id    uuid        NOT NULL REFERENCES applications.events (id) ON DELETE CASCADE,

    name        text        NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 120),

    -- All optional, because the thing that stops a tracker being used is being
    -- asked six questions to write down that there are forty pizzas.
    category    text        CHECK (category IS NULL OR length(category) <= 60),
    location    text        CHECK (location IS NULL OR length(location) <= 120),
    note        text        CHECK (note IS NULL OR length(note) <= 500),

    -- Say when to worry. Null means never, which is the right default: a
    -- threshold nobody chose is a warning nobody reads.
    low_at      integer     CHECK (low_at IS NULL OR low_at >= 0),

    -- The blob this item's photo lives under, or null. A key rather than a
    -- URL: the container is not public, so what a browser gets is a signed
    -- link minted per request, and a URL stored here would be one that expired
    -- five minutes after somebody saved the row.
    image_key   text        CHECK (image_key IS NULL OR length(image_key) <= 200),

    -- Denormalised on purpose, and the CHECK is why.
    --
    -- This is the sum of every movement below, and it could be derived instead
    -- of stored. Storing it means a list of forty items is one SELECT rather
    -- than forty sums, and -- more importantly -- it means `count >= 0` is a
    -- constraint the database enforces rather than a rule the application
    -- remembers. Handing out a shirt that is not there should fail on the
    -- write, not on the shelf.
    --
    -- The two stay in step because nothing writes one without the other in the
    -- same transaction. See InventoryStore.
    count       integer     NOT NULL DEFAULT 0 CHECK (count >= 0),

    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);

-- No unique constraint on (event_id, name), deliberately. The same shirt in
-- two rooms is two rows with one name, and that is a thing logistics actually
-- does -- a constraint here would force a naming convention on people who are
-- carrying boxes.
CREATE INDEX items_by_event ON logistics.items (event_id, lower(name));

CREATE TRIGGER items_touch_updated_at
    BEFORE UPDATE ON logistics.items
    FOR EACH ROW EXECUTE FUNCTION applications.touch_updated_at();

-- ------------------------------------------------------------ the ledger ---
CREATE TABLE logistics.movements (
    -- bigint and a sequence, for the reason audit.entries gives: this is a log
    -- and a log has to be orderable, and several movements in one transaction
    -- share occurred_at to the microsecond.
    id          bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    item_id     uuid        NOT NULL REFERENCES logistics.items (id) ON DELETE CASCADE,

    -- Signed. Taking out is negative, putting back is positive, and zero is
    -- not a movement -- a row that changed nothing is noise in the one place
    -- somebody reads to find out what changed.
    delta       integer     NOT NULL CHECK (delta <> 0),

    -- The count after this movement, not before. Two reasons: "what did we
    -- have on Tuesday" is answerable without replaying the whole ledger, and
    -- a balance that disagrees with the sum of the deltas before it is a
    -- corruption that shows itself rather than hiding.
    balance     integer     NOT NULL CHECK (balance >= 0),

    reason      text        CHECK (reason IS NULL OR length(reason) <= 200),

    -- Who, and null-on-delete for the reason applications.decided_by is:
    -- removing a person must not remove the record of what they did.
    actor_id    uuid        REFERENCES identity.people (id) ON DELETE SET NULL,

    -- What moved it. 'console' is somebody typing; the next value will be
    -- 'check-in', when handing somebody a shirt at the desk takes one off the
    -- count by itself. Written now so that when it arrives, the ledger can
    -- already say which movements were people and which were the door.
    source      text        NOT NULL DEFAULT 'console'
                            CHECK (source IN ('console', 'check-in')),

    occurred_at timestamptz NOT NULL DEFAULT now()
);

-- The read this table exists for: one item, newest first.
CREATE INDEX movements_by_item ON logistics.movements (item_id, id DESC);

-- --------------------------------------------------------------- who may ---
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, 'inventory.manage' FROM identity.teams
 WHERE slug IN ('logistics', 'super-admin')
ON CONFLICT DO NOTHING;
