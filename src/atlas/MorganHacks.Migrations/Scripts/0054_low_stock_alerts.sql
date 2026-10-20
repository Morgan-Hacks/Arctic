-- Durable crossing snapshots. Written in the stock movement transaction;
-- delivered separately so an unavailable mail service cannot lose the alert.
CREATE TABLE logistics.low_stock_alerts (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    item_id uuid NOT NULL REFERENCES logistics.items(id) ON DELETE CASCADE,
    event_id uuid NOT NULL REFERENCES applications.events(id) ON DELETE CASCADE,
    item_name text NOT NULL,
    location text,
    balance integer NOT NULL CHECK (balance >= 0),
    low_at integer NOT NULL CHECK (low_at >= 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    processed_at timestamptz
);
CREATE INDEX low_stock_alerts_pending ON logistics.low_stock_alerts(id)
    WHERE processed_at IS NULL;

INSERT INTO notify.templates
    (key, kind, subject, body_html, body_text, from_local, from_domain, reply_to)
VALUES (
    'inventory_low_stock', 'transactional', 'Low stock: {{item}}',
    '<p>Inventory is running low for <strong>{{event}}</strong>.</p>'
    '<p><strong>{{item}}</strong>: {{count}} remaining (low-stock threshold: {{threshold}}).</p>'
    '<p>Location: {{location}}</p><p><a href="{{inventory}}">Review inventory</a></p>',
    E'Inventory is running low for {{event}}.\n\n{{item}}: {{count}} remaining (low-stock threshold: {{threshold}}).\nLocation: {{location}}\n\nReview inventory: {{inventory}}',
    'notifications', 'auth.morganhacks.com', 'info@morganhacks.com'
);
