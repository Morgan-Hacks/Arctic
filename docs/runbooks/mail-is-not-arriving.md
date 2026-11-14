# Mail is not arriving

**The failure this page exists for looks exactly like success.** Queue drains,
messages go to `sent`, no errors anywhere, and nothing reaches an inbox. Three
unrelated causes produce that same picture, and **testing with an address you
have verified hides all three.**

Check these in order. The first two take a minute each.

---

## 1. Which AWS account is the deploy actually using

The identities, the DKIM records and the production access all live in
**`602773792443`**. A key from any other account authenticates perfectly and
then fails every send, because the domains it is trying to send from do not
exist there.

This is easy to get wrong because a laptop usually has two profiles:

```bash
aws sts get-caller-identity                        # whatever your shell defaults to
aws sts get-caller-identity --profile morganhacks  # should be 602773792443
```

For the deployed worker, the key is the `AWS_ACCESS_KEY_ID` secret on the
GitHub environment. **GitHub will not show a secret back**, so there is no way
to read it — the ways to find out are:

- CloudTrail in the suspect account: a `SendEmail` denial there names it
- `SesEmailProvider` logs `ErrorCode` and `StatusCode` on a refusal, so a
  `MessageRejected` reading "Email address is not verified" is the signature
- Or send a real link, below, which settles it

## 2. Sandbox, or production

```bash
aws sesv2 get-account --profile morganhacks --region us-east-2 \
  --query "{Production:ProductionAccessEnabled, Sending:SendingEnabled, Quota:SendQuota.Max24HourSend}"
```

**`ProductionAccessEnabled: true` is the pre-launch check.** In the sandbox,
SES accepts a send to a verified recipient and refuses everyone else — and
`SesEmailProvider` turns that refusal into an ordinary `SendOutcome`, so the
queue records it and the worker carries on. Nothing in the system shouts.

Checked 2026-10-01: production access on, sending on, 50,000/day, in
`us-east-2`.

## 3. Region

```bash
gh api repos/Morgan-Hacks/Arctic/environments/Staging/variables \
  --jq '.variables[] | select(.name=="AWS_REGION") | .value'
```

Should be `us-east-2` on Staging and Production. `apps.bicep` passes it to lark
as `AWS_REGION`.

A missing region does **not** silently default: `lark/Program.cs` reads it and
registers `UnconfiguredEmailProvider` when it is absent, so mail queues at
`pending` and never sends. That is a different symptom — nothing moves out of
`pending` at all.

## 4. The identities themselves

```bash
aws sesv2 get-email-identity --email-identity auth.morganhacks.com \
  --profile morganhacks --region us-east-2 \
  --query "{verified:VerifiedForSendingStatus, dkim:DkimAttributes.Status, mailFrom:MailFromAttributes.MailFromDomainStatus}"
```

Each sending domain has its **own** bounce subdomain, and that is correct
rather than a mismatch:

| Identity | MAIL FROM |
|---|---|
| `auth.morganhacks.com` — sign-in links | `bounce.auth.morganhacks.com` |
| `morganhacks.com` — broadcast | `bounce.morganhacks.com` |
| `mail.morganhacks.com` | `bounce.mail.morganhacks.com` |

All three verified with DKIM `SUCCESS` and MAIL FROM `SUCCESS` as of
2026-10-01. Note that no migration names a bounce domain — MAIL FROM is set in
SES, not in the schema, so there is nothing in the repository to keep in step
with it.

## 5. Is SES reporting anything back

```bash
aws sesv2 get-configuration-set-event-destinations \
  --configuration-set-name arctic-staging --profile morganhacks --region us-east-2
# production's is arctic-production, same account and region
```

A send only produces events when it names a configuration set that has an event
destination on it. Without that, SES accepts the message, the row reaches
`sent`, and the bounce that happens two seconds later is never reported —
`notify.suppressions` stays empty of real bounces and nothing ever reaches
`delivered`.

Three things have to line up, and all three are easy to have half of:

| | Where |
|---|---|
| `SES_CONFIGURATION_SET` set | the GitHub environment, passed through `deploy-azure.yml` and `apps.bicep` |
| The set has an event destination | SES, publishing BOUNCE/COMPLAINT/DELIVERY to an SNS topic |
| The topic's subscription is **confirmed** | SNS — a subscription left `PendingConfirmation` delivers nothing |

```bash
aws sns list-subscriptions-by-topic \
  --topic-arn arn:aws:sns:us-east-2:602773792443:arctic-ses-events-staging \
  --profile morganhacks --region us-east-2 --query "Subscriptions[].SubscriptionArn"
```

Production's topic is `arctic-ses-events-production`, same account and
region — same command with the topic name swapped.

An ARN means confirmed. The literal string `PendingConfirmation` means the
endpoint never answered — which is itself a useful signal, because confirming
requires the webhook to verify the SNS signature and then fetch the
`SubscribeURL`, so a confirmed subscription proves that path works.

As of 2026-10-01, both environments have this fully wired: configuration sets
`arctic-staging` and `arctic-production`, each with an `sns-webhook`
destination publishing BOUNCE/COMPLAINT/DELIVERY/REJECT/RENDERING_FAILURE to
its own topic, each subscription confirmed and pointed at
`https://admin-stg.morganhacks.com/api/webhooks/ses` (staging) or
`https://admin.morganhacks.com/api/webhooks/ses` (production) — the console's
host, not the public site's, because that is where the webhook endpoint
lives.

---

## The test that actually proves it

**Send one real sign-in link to an address that is not on the verified list.**

That single send exercises the credentials, the account, the region, the
identity and the MAIL FROM together. Every other check tests one of them.

Using a verified recipient proves almost nothing: a sandboxed account and a
wrong-account key both deliver successfully to a verified address and fail for
everybody else.

Then read what actually happened, which the queue records whatever the outcome:

```sql
SELECT status, attempts, last_error, sent_at
  FROM notify.messages
 WHERE to_email = '<the address>'
 ORDER BY created_at DESC LIMIT 1;
```

| What you see | What it means |
|---|---|
| `sent`, and it arrives | the whole path works |
| `sent`, and it does not arrive | delivery, not configuration — check the bounce domain and the recipient's spam folder |
| `failed_perm`, "Email address is not verified" | **sandbox, or the wrong account** |
| stuck at `pending`, no attempts | no region configured, so `UnconfiguredEmailProvider` is registered |
| `failed_temp` with attempts climbing | throttling or a transient refusal; `RetrySchedule` is backing off |

---

## While you are here: the sender name

If mail arrives from **"mail"** rather than the name you set, that is not SES.
A campaign's `email_settings` holds `""` for a field nobody filled in, and
`->>` returns that empty string as a value — so a `COALESCE` against the
template's name preferred the empty override. Fixed by wrapping the campaign
side in `NULLIF` before the `COALESCE`; if it reappears, that is the line.

---

**Escalate to:** the tech lead. Before changing anything in SES, check the
account first — most of what looks like a broken identity is a key pointed at
the wrong one.

---

## It arrives, but in Gmail's Promotions tab

This is not the failure the rest of this page is about. The mail is being
delivered, authenticated and accepted. Gmail has sorted it, and **there is no
header, flag or setting that moves a message to Primary.** Anybody who tells
you otherwise is selling something.

What is already right, so you can stop re-checking it:

- SPF passes and `morganhacks.com` includes `amazonses.com`.
- A custom MAIL FROM is configured at `bounce.morganhacks.com`, with the SES
  feedback MX and its own SPF record. This is the part most setups skip, and it
  is what gives SPF alignment for DMARC.
- DMARC exists, at `p=none`. Monitoring only, which is the right place to be
  until the reports are boring.
- DKIM is signed by SES. The selectors are random strings, so you cannot check
  it with `dig` unless you know them; read it off **SES → Verified identities →
  morganhacks.com → DKIM** instead.

So authentication is not the problem, and changing DNS will not fix this.

**What actually decides the tab** is the shape of the message and how the
people receiving it behave, judged per recipient. In rough order of how much
they matter:

1. **Tracking.** Open tracking puts a 1×1 image in the body and click tracking
   rewrites every link through our own domain. Both are plain bulk markers. New
   campaigns start untracked; leave them that way for anything going to a large
   list, and turn it on only when somebody actually needs the numbers.
2. **How the mail looks.** Tables, buttons, multiple images and multiple
   columns read as marketing. A short message that looks like a person typed it
   reads as mail.
3. **Replies.** A real `Reply-to` that somebody reads is one of the strongest
   signals there is, and the cheapest to arrange.
4. **Who it goes to.** Mail to people with an existing relationship to
   MorganHacks gets opened; mail to a list that has never heard of us gets
   ignored, and being ignored is itself the signal.
5. **How much goes at once.** A few thousand from a domain that normally sends
   hundreds is a reputation event on its own. Split a large outreach send
   across days.

**`List-Unsubscribe` does not help with this and may make it worse**, because
the header is also one of the ways Gmail recognises bulk mail. It is set on
every broadcast anyway, and should stay set: Google and Yahoo both require a
one-click unsubscribe from bulk senders, and the alternative to an easy
unsubscribe is a spam complaint. Complaints land in `notify.suppressions` and
damage the reputation of a domain that also carries **sign-in links and
decision emails**, which is a far more expensive thing to lose than a tab.

If what you actually need is for one specific group to see the mail, the honest
answer is to ask them to drag it to Primary once, or to reply. Both teach
Gmail, and neither is something the sender can do.
