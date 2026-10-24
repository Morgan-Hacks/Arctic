# Low-stock email notifications (#247)

Email active organizers with `inventory.manage` when an inventory movement
crosses from above `low_at` to at or below it. Team permissions and direct grants
both qualify; expired memberships/grants and revoked accounts do not. An organizer
qualifying twice receives one email. Hard-bounced, complained, or manually
suppressed addresses are excluded, using the transactional email suppression rules.

The existing optional threshold is the switch: clearing it disables alerts.
Creating an already-low item or editing its threshold does not send an alert.
Replenishing above the threshold naturally permits the next downward crossing.
Emails identify the event, item, location, remaining count and threshold, and link
to the event's inventory page. Counts describe the crossing, not the later balance.

The organizer dashboard also shows current low-stock items for the latest event,
only for users with inventory access. Show counts, thresholds, locations and links
to the relevant inventory items. Restocking or clearing thresholds removes warnings
on the next dashboard load; inventory writes invalidate the dashboard cache.
An inventory fetch failure is shown explicitly. Display at most five items and link
to the full inventory for the remainder.

## Implementation and delivery

1. Save an alert snapshot in `logistics.low_stock_alerts` within the existing locked
   movement transaction. No HTTP/email call runs in the stock-update path.
2. An Atlas background worker polls every 15 seconds, processes up to 20 alerts,
   and queues transactional messages through Lark's existing MessageQueue.
3. Lock each pending alert with `FOR UPDATE SKIP LOCKED`. Queue all recipients and
   mark processed in one transaction. Failed attempts remain pending for retry;
   multiple replicas cannot queue the same alert twice. Lark delivery remains
   at-least-once, as with its other transactional mail.
4. Resolve recipients at dispatch time. Deleted items or cleared thresholds cancel
   pending alerts. A restock does not erase a crossing that already happened.
   No eligible recipients completes the alert with a warning rather than sending
   stale alerts to future team members.

No new delivery provider, dependencies, permissions, API fields, or recipient
configuration UI. Apply the additive migration before deploying Atlas. Rolling
back the application leaves the new table/template harmlessly in place; retain
queued messages. Existing thresholds enable notifications on future crossings.

## Verification

Follow existing C# record/async/Npgsql parameter conventions. Use xUnit tests in
`src/atlas/MorganHacks.Api.Tests` against real PostgreSQL and production migrations.
Cover boundary and skipped crossings, disabled alerts, replenishment, concurrent
movement/dispatch, rollback/retry, recipient authorization, suppression and HTML
escaping. No production email is sent by tests.

Commands (repository root):

- `dotnet test src/atlas -c Release`
- `dotnet format src/atlas --verify-no-changes`

Always preserve inventory authorization, movement atomicity and the email queue's
suppression policy. Do not bypass CI, commit secrets, or change unrelated workflows.

## Dashboard verification

- `npm run typecheck --prefix src/portaladmin`
- `npm run build --prefix src/portaladmin`
- Browser: low stock renders count/threshold/location; item link opens the matching
  event and item; replenishment clears the alert; users without inventory access
  see no alert; inventory failure renders a useful error. Synthetic local API data
  is used for these checks, with no production messages or data changes.
