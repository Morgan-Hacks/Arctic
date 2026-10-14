CREATE SCHEMA directory;
CREATE TABLE directory.settings (
 event_id uuid PRIMARY KEY REFERENCES applications.events(id),
 opens_at timestamptz, archived_at timestamptz, archived_by uuid REFERENCES identity.people(id)
);
CREATE TABLE directory.photos (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), person_id uuid NOT NULL REFERENCES identity.people(id),
 event_id uuid NOT NULL REFERENCES applications.events(id), storage_key text NOT NULL UNIQUE,
 width integer NOT NULL, height integer NOT NULL, bytes integer NOT NULL CHECK(bytes > 0),
 status text NOT NULL CHECK(status IN ('pending','attached','detached','removed')),
 removed_by uuid REFERENCES identity.people(id), created_at timestamptz NOT NULL DEFAULT now(), attached_at timestamptz
);
CREATE TABLE directory.profiles (
 id uuid PRIMARY KEY DEFAULT gen_random_uuid(), person_id uuid NOT NULL REFERENCES identity.people(id),
 event_id uuid NOT NULL REFERENCES applications.events(id),
 display_name text CHECK(length(display_name)<=200), school text CHECK(length(school)<=200),
 year_level text CHECK(length(year_level)<=100), pronouns text CHECK(length(pronouns)<=30),
 headline text CHECK(length(headline)<=160), build_idea text CHECK(length(build_idea)<=280),
 looking_for text CHECK(length(looking_for)<=200), first_hackathon boolean,
 pace text CHECK(pace IN ('All-nighter','Steady with breaks','Here to learn')),
 team_status text CHECK(team_status IN ('looking','needMembers','full','solo')),
 teammates_needed integer CHECK(teammates_needed BETWEEN 1 AND 2),
 skills text[] NOT NULL DEFAULT '{}' CHECK(cardinality(skills)<=5),
 interests text[] NOT NULL DEFAULT '{}' CHECK(cardinality(interests)<=5),
 contact_links jsonb NOT NULL DEFAULT '[]' CHECK(jsonb_typeof(contact_links)='array' AND jsonb_array_length(contact_links)<=3),
 photo_id uuid REFERENCES directory.photos(id), opted_in_at timestamptz, is_complete boolean NOT NULL DEFAULT false,
 hidden_at timestamptz, hidden_by uuid REFERENCES identity.people(id), hidden_reason text CHECK(length(hidden_reason)<=200),
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(person_id,event_id),
 CHECK ((team_status='needMembers' AND teammates_needed IS NOT NULL) OR (team_status IS DISTINCT FROM 'needMembers' AND teammates_needed IS NULL))
);
CREATE INDEX directory_skills_idx ON directory.profiles USING gin(skills);
CREATE INDEX directory_event_opted_idx ON directory.profiles(event_id,opted_in_at);
CREATE INDEX directory_pending_photos_idx ON directory.photos(created_at) WHERE status='pending';

CREATE FUNCTION directory.record_moderation() RETURNS trigger AS $$
BEGIN
 IF TG_TABLE_NAME='profiles' THEN
  IF OLD.hidden_at IS DISTINCT FROM NEW.hidden_at THEN
   PERFORM audit.record(CASE WHEN NEW.hidden_at IS NULL THEN 'directory.unhidden' ELSE 'directory.hidden' END, NEW.person_id,NULL,NEW.id::text,NULL);
  END IF;
 ELSIF TG_TABLE_NAME='photos' THEN
  IF NEW.status='removed' AND OLD.status<>'removed' THEN
   PERFORM audit.record('directory.photo_removed',NEW.person_id,NULL,NEW.id::text,NULL);
  END IF;
 ELSE
  IF OLD.opens_at IS DISTINCT FROM NEW.opens_at THEN
   PERFORM audit.record('directory.opening_changed',NULL,'directory',NEW.event_id::text,NULL);
  END IF;
  IF OLD.archived_at IS NULL AND NEW.archived_at IS NOT NULL THEN
   PERFORM audit.record('directory.archived',NULL,'directory',NEW.event_id::text,NULL);
  END IF;
 END IF;
 RETURN NULL;
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER directory_profile_audit AFTER UPDATE ON directory.profiles FOR EACH ROW EXECUTE FUNCTION directory.record_moderation();
CREATE TRIGGER directory_photo_audit AFTER UPDATE ON directory.photos FOR EACH ROW EXECUTE FUNCTION directory.record_moderation();
CREATE TRIGGER directory_settings_audit AFTER UPDATE ON directory.settings FOR EACH ROW EXECUTE FUNCTION directory.record_moderation();
