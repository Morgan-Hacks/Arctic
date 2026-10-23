-- Give Registration access to the full campaign workflow.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, p FROM identity.teams, unnest(ARRAY[
    'email.view_stats',
    'email.manage_templates',
    'email.send_broadcast'
]) AS p WHERE slug = 'registration'
ON CONFLICT DO NOTHING;
