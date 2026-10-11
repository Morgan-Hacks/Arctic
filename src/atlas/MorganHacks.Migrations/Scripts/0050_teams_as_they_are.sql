-- The teams as the committee is actually organised, and two that were missing.
--
-- 0002 seeded a guess made before anybody was running this. The committee has
-- since settled into nine groups, two of which had no team at all: the people
-- building and operating the platform, and the people handling money.
--
-- ------------------------------------------------------- names, not slugs ---
-- Two teams are renamed on screen and keep the slug they were seeded with.
--
-- That looks untidy -- a team displayed as "Marketing & Social Media" whose
-- slug is still `comms` -- and it is the right trade. Slugs are the handle
-- everything else holds: team_members and team_permissions key on team_id, so
-- those would survive a rename, but the test suite names `comms`,
-- `registration` and `logistics` as string literals, and a migration that
-- quietly breaks the tests guarding permissions is a bad way to rename a team.
--
-- The display name is what an admin reads on the grant screen. It is the half
-- that was wrong.
UPDATE identity.teams
   SET name = 'Registration and Hacker Experience'
 WHERE slug = 'registration';

UPDATE identity.teams
   SET name = 'Marketing & Social Media'
 WHERE slug = 'comms';

-- ---------------------------------------------------------- the new two ---
-- ON CONFLICT so this is safe to land on an environment that already has
-- them, which is the rule every seeding migration here follows.
INSERT INTO identity.teams (slug, name) VALUES
    ('technology', 'Technology'),
    ('finance',    'Finance')
ON CONFLICT (slug) DO NOTHING;

-- Technology builds and operates the platform, and that is not the same as
-- deciding who gets in. It can read applications, because a bug report is
-- usually about one; it can manage forms and events, because that is the
-- thing it is asked to fix at midnight; and it can read the audit log,
-- because the question it gets asked is "what happened".
--
-- Deliberately not applications.decide, and not people.grant_permissions.
-- Being able to repair the review screen is not a reason to be able to use
-- it, and the team that can grant permissions should be the one accountable
-- for who holds them.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, p FROM identity.teams, unnest(ARRAY[
    'applications.view',
    'forms.manage',
    'events.manage',
    'audit.view'
]) AS p WHERE slug = 'technology'
ON CONFLICT DO NOTHING;

-- Finance gets the two sponsor permissions and nothing else, because nothing
-- else exists yet. There is no budget, no spend and no reimbursement in this
-- schema, so the honest grant is the money-adjacent surface that is real --
-- what sponsors committed, and what they are worth.
--
-- sponsors.view_financials is on Permission.Sensitive, which is the point of
-- a finance team rather than an argument against one: somebody has to hold it,
-- and it should be them rather than everybody with a sponsorship login.
INSERT INTO identity.team_permissions (team_id, permission)
SELECT id, p FROM identity.teams, unnest(ARRAY[
    'sponsors.view',
    'sponsors.view_financials'
]) AS p WHERE slug = 'finance'
ON CONFLICT DO NOTHING;

-- Nothing is removed. logistics, judge and volunteer stay: logistics owns
-- check-in, swag and the inventory it is about to be given, and judging is
-- paused rather than dropped. Deleting a team cascades its memberships and
-- its grants, and this file runs against production the moment it merges --
-- so a team that might be wanted is left alone rather than guessed away.
