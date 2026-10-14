-- Four people total: the profile owner can request up to three teammates.
-- Upgrade existing directory databases as well as fresh installations.
ALTER TABLE directory.profiles
    DROP CONSTRAINT profiles_teammates_needed_check;
ALTER TABLE directory.profiles
    ADD CONSTRAINT profiles_teammates_needed_check
    CHECK (teammates_needed BETWEEN 1 AND 3);
