import assert from "node:assert/strict";
import { test } from "node:test";
import { environmentName, flagValue, resolveDefaults } from "./flags.ts";

/*
 * docs/feature-flags.md is explicit that a test covering only the off case
 * passes just as well when the route is broken, so both positions are here for
 * every layer: the base file, the overlay on top of it, and the environment
 * variable above both.
 */

// ------------------------------------------------------- which environment ---

test("an explicit APP_ENV names the environment", () => {
  assert.equal(environmentName({ APP_ENV: "staging" }), "staging");
  assert.equal(environmentName({ APP_ENV: "production" }), "production");
});

test("the staging branch names itself, with nothing configured by hand", () => {
  // The point of deriving it: staging IS a branch here, because
  // mirror-staging.yml force-pushes main onto it. Vercel sets this variable by
  // itself, so no dashboard entry is needed for the overlay to apply.
  assert.equal(environmentName({ VERCEL_GIT_COMMIT_REF: "staging" }), "staging");
});

test("an explicit APP_ENV beats the branch it happens to be built from", () => {
  // The escape hatch has to win, or it is not an escape hatch.
  assert.equal(environmentName({ APP_ENV: "production", VERCEL_GIT_COMMIT_REF: "staging" }), "production");
});

test("every other branch is no environment at all", () => {
  // The case that matters most. An overlay is opted into by name, so a feature
  // branch cannot inherit staging's flags because it was cut from staging, and
  // production -- built from main -- gets the base file and nothing else.
  assert.equal(environmentName({ VERCEL_GIT_COMMIT_REF: "main" }), undefined);
  assert.equal(environmentName({ VERCEL_GIT_COMMIT_REF: "flags/something" }), undefined);
  assert.equal(environmentName({ VERCEL_GIT_COMMIT_REF: "staging-2" }), undefined);
  assert.equal(environmentName({ VERCEL_GIT_COMMIT_REF: "Staging" }), undefined);
  assert.equal(environmentName({}), undefined);
});

test("an empty or blank APP_ENV is not a choice", () => {
  // An unset variable in a shell script is an empty string, not an absent key.
  // Reading "" as an environment name would look for features..json.
  assert.equal(environmentName({ APP_ENV: "" }), undefined);
  assert.equal(environmentName({ APP_ENV: "   " }), undefined);
  assert.equal(environmentName({ APP_ENV: "", VERCEL_GIT_COMMIT_REF: "staging" }), "staging");
});

test("the name is lower-cased so one filename works in every service", () => {
  // .NET hands over `Staging`; Vercel hands over `staging`. A repository
  // holding both features.Staging.json and features.staging.json is one where
  // somebody edits the wrong file.
  assert.equal(environmentName({ APP_ENV: "Staging" }), "staging");
  assert.equal(environmentName({ APP_ENV: "PRODUCTION" }), "production");
});

// -------------------------------------------------------------- the overlay ---

test("no overlay leaves the base file exactly as it was", () => {
  const base = { enable_a_feature: true, enable_b_feature: false };
  assert.deepEqual(resolveDefaults(base), base);
  assert.deepEqual(resolveDefaults(base, undefined), base);
});

test("an overlay names only what differs", () => {
  // The whole design is the shallow merge: reading an overlay tells you what is
  // different about that environment rather than restating everything.
  const base = { enable_a_feature: false, enable_b_feature: false };
  const merged = resolveDefaults(base, { enable_b_feature: true });
  assert.equal(merged.enable_a_feature, false);
  assert.equal(merged.enable_b_feature, true);
});

test("a flag absent from the overlay keeps its base value, rather than going off", () => {
  // The failure this prevents: an overlay written as a full replacement would
  // silently turn off every flag it forgot to mention.
  const base = { enable_a_feature: true };
  assert.equal(resolveDefaults(base, { enable_b_feature: true }).enable_a_feature, true);
});

test("an overlay can turn something off as well as on", () => {
  assert.equal(resolveDefaults({ enable_a_feature: true }, { enable_a_feature: false }).enable_a_feature, false);
});

test("merging does not mutate the base file", () => {
  // The base is a module-level import shared by every call in the process.
  const base = { enable_a_feature: false };
  resolveDefaults(base, { enable_a_feature: true });
  assert.equal(base.enable_a_feature, false);
});

// ------------------------------------------------------------- one flag now ---

test("the file decides when no variable is set", () => {
  assert.equal(flagValue(undefined, true), true);
  assert.equal(flagValue(undefined, false), false);
});

test("the variable beats the file in both directions", () => {
  // Both directions, because the emergency lever is mostly used to turn
  // something off that the file says is on.
  assert.equal(flagValue("true", false), true);
  assert.equal(flagValue("false", true), false);
});

test('"false" is off, not a non-empty string', () => {
  // The bug this exists to prevent: treating the value as truthy turns the
  // feature on while the dashboard appears to say otherwise.
  assert.equal(flagValue("false", true), false);
  assert.equal(flagValue("0", true), false);
  assert.equal(flagValue("", true), false);
  assert.equal(flagValue("TRUE", true), false);
  assert.equal(flagValue("yes", true), false);
});

test("an unknown flag is off", () => {
  // A typo should hide a feature rather than expose one.
  assert.equal(flagValue(undefined, undefined), false);
});

// ------------------------------------------------------------ all together ---

test("the three layers stack in the documented order", () => {
  const base = { enable_directory_feature: false };
  const overlay = { enable_directory_feature: true };

  // production: base only
  assert.equal(flagValue(undefined, resolveDefaults(base).enable_directory_feature), false);

  // staging: the overlay lifts it
  assert.equal(flagValue(undefined, resolveDefaults(base, overlay).enable_directory_feature), true);

  // staging, with somebody pulling the lever during an incident
  assert.equal(flagValue("false", resolveDefaults(base, overlay).enable_directory_feature), false);

  // production, with somebody turning it on for an hour
  assert.equal(flagValue("true", resolveDefaults(base).enable_directory_feature), true);
});
