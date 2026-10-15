"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import Image from "next/image";
import { useRouter } from "next/navigation";
import { ArrowRight02Icon, CheckmarkCircle02Icon, HelpCircleIcon, Mail01Icon, RefreshIcon, Task01Icon, Tick02Icon, UserGroupIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { Select } from "@/components/ui/select";
import { PersonalizedField } from "@/components/templates/personalized-field";
import type { Placeholder } from "@/components/templates/types";
import type { PreviewResult, SendResult } from "@/app/mail/actions";
import { StatusPill } from "./status";
import { APPLICANT_STATUSES, describeSegment, type Campaign, type CampaignAnalytics, type CampaignSaveResult, type CampaignUpdate, type EventChoice, type FormChoice, type FormQuestion, type FormQuestionsRead, type Preview, type Render, type Segment } from "./types";
import styles from "./campaign-draft.module.css";

const steps = ["Settings", "Design", "Recipients", "Review and send"];
type Content = NonNullable<CampaignAnalytics["content"]>;

function initialSettings(campaign: Campaign, content: Content): CampaignUpdate {
  return {
    name: campaign.name,
    subject: campaign.settings?.subject ?? content.subject,
    previewText: campaign.settings?.previewText ?? content.previewText ?? "",
    fromName: campaign.settings?.fromName ?? content.fromName ?? "",
    fromEmail: campaign.settings?.fromEmail ?? content.fromEmail,
    replyTo: campaign.settings?.replyTo ?? content.replyTo ?? "",
    trackingEnabled: campaign.trackingEnabled ?? false,
    segment: campaign.segment ?? null,
    revision: campaign.revision,
  };
}

// Scrollbars stay unless a caller asks for them, for the reason spelled out
// over emailDocument in components/templates/email-preview: this frame has a
// fixed height and cannot measure its own content, so the scrollbar is the
// only thing telling a reader the email continues below the fold.
function emailDocument(html: string, { scrollbars = false }: { scrollbars?: boolean } = {}) {
  const hidden = scrollbars ? "" : ";scrollbar-width:none";
  const webkit = scrollbars ? "" : "*::-webkit-scrollbar{display:none;width:0;height:0}";
  return `<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><style>html{color-scheme:light;background:#fff}body{margin:0;color:#17191c;font-family:Arial,sans-serif;overflow-wrap:anywhere}img{max-width:100%;height:auto}*{box-sizing:border-box${hidden}}${webkit}</style></head><body>${html}</body></html>`;
}

function CampaignInbox({ sender, subject, snippet }: { sender: string; subject: string; snippet: string }) {
  return <section className={styles.inboxPreview} aria-labelledby="campaign-inbox-title">
    <div className={styles.inboxTitle}><Icon icon={Mail01Icon} size={18} /><h3 id="campaign-inbox-title">Inbox preview</h3></div>
    <p className={styles.hint}>A first impression, before they open your email.</p>
    <div className={styles.inboxWindow}>
      <div className={styles.inboxWindowHeader}><span>Inbox</span><span>1 new message</span></div>
      <div className={styles.inboxMessage}>
        <Image src="/brands/morganhacks-campaign.png" alt="" width={36} height={36} className={styles.inboxAvatar} />
        <div className={styles.inboxMessageText}><div><strong>{sender || "Sender name"}</strong><span>Now</span></div><p>{subject || "Your email subject"}</p><span>{snippet || "Your preview text will appear here."}</span></div>
      </div>
      <div className={styles.inboxPlaceholder} aria-hidden="true"><span /><div><span /><span /></div></div>
      <div className={styles.inboxPlaceholder} aria-hidden="true"><span /><div><span /><span /></div></div>
    </div>
  </section>;
}

function EmailPreview({ html, subject, sender, to, text, samples, selected = 0, onSelect }: {
  html: string; subject: string; sender: string; to?: string; text?: string;
  samples?: Render[]; selected?: number; onSelect?: (index: number) => void;
}) {
  const [mobile, setMobile] = useState(false);
  const [plain, setPlain] = useState(false);
  return (
    <div className={styles.emailPreview}>
      <div className={styles.previewToolbar}>
        {samples && onSelect ? <div className={styles.samplePicker}>
          <label htmlFor="preview-person">Preview as</label>
          <Select id="preview-person" value={selected} onChange={event => onSelect(Number(event.target.value))}>{samples.map((render, index) => <option key={render.email} value={index}>{render.email}</option>)}</Select>
        </div> : <span className={styles.previewLabel}>Email preview</span>}
        <div className={styles.previewTools}>
          <div className={styles.viewOptions} aria-label="Preview size">
            <button type="button" aria-pressed={!mobile} onClick={() => setMobile(false)}>Desktop</button>
            <button type="button" aria-pressed={mobile} onClick={() => setMobile(true)}>Mobile</button>
          </div>
          {text !== undefined ? <button type="button" className={styles.textButton} aria-pressed={plain} onClick={() => setPlain(value => !value)}>{plain ? "Show email" : "Plain text"}</button> : null}
        </div>
      </div>
      <div className={styles.emailCanvas}>
        <div className={styles.emailPaper} data-mobile={mobile}>
          <div className={styles.envelope}>
            <strong>{subject || "Your email subject"}</strong>
            <span>{sender}{to ? <> <span className={styles.muted}>to</span> {to}</> : null}</span>
          </div>
          {plain ? <pre className={styles.plain}>{text}</pre> : <iframe title={to ? `Email preview for ${to}` : "Selected template preview"} sandbox="" referrerPolicy="no-referrer" srcDoc={emailDocument(html, { scrollbars: true })} className={styles.emailFrame} />}
        </div>
      </div>
    </div>
  );
}

export function CampaignDraft({ campaign: initialCampaign, content, available, forms, events, audienceError, me, canSend, save, preview, send, loadQuestions }: {
  campaign: Campaign;
  content: Content;
  available: Placeholder[] | null;
  forms: FormChoice[];
  events: EventChoice[];
  audienceError: string | null;
  me: string;
  canSend: boolean;
  save: (draft: CampaignUpdate) => Promise<CampaignSaveResult>;
  preview: () => Promise<PreviewResult>;
  send: (seen: number, revision?: number) => Promise<SendResult>;

  /**
   * One form's questions, fetched when the answer picker needs them.
   *
   * Not a prop with every form's questions already in it: which form the
   * picker is looking at changes while somebody is on this screen, and loading
   * all of them up front would be a read per form to fill a dropdown nobody
   * may open.
   */
  loadQuestions: (formId: string) => Promise<FormQuestionsRead>;
}) {
  const router = useRouter();
  const [campaign, setCampaign] = useState(initialCampaign);
  const [draft, setDraft] = useState(() => initialSettings(initialCampaign, content));
  const [saved, setSaved] = useState(() => JSON.stringify(initialSettings(initialCampaign, content)));
  const [step, setStep] = useState(0);
  const [pending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);
  const [resolved, setResolved] = useState<Preview | null>(null);
  const [sample, setSample] = useState(0);
  const [confirming, setConfirming] = useState(false);
  const [domainHintDismissed, setDomainHintDismissed] = useState(false);
  const [editingReplyTo, setEditingReplyTo] = useState(false);
  const [queued, setQueued] = useState(false);
  const settingsForm = useRef<HTMLFormElement>(null);
  const replyToInput = useRef<HTMLInputElement>(null);
  const audienceForm = useRef<HTMLFormElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const scrollArea = useRef<HTMLDivElement>(null);
  const [questions, setQuestions] = useState<FormQuestionsRead | null>(null);
  const changed = saved !== JSON.stringify(draft);
  const ownDraft = campaign.createdBy === me || campaign.updatedBy === me;
  const selectedFormId = draft.segment?.type === "formRespondents" || draft.segment?.type === "formAnswer"
    ? draft.segment.formId : null;
  const formName = forms.find(form => form.id === selectedFormId)?.name;

  // Narrowed once and held, because the three steps of the picker all read it
  // and narrowing it again inside a callback does not survive the closure.
  const answer = draft.segment?.type === "formAnswer" ? draft.segment : null;
  const answerQuestions = questions?.ok ? questions.questions : [];
  const chosenQuestion: FormQuestion | null = answer
    ? answerQuestions.find(question => question.key === answer.question) ?? null
    : null;
  const audience = draft.segment ? describeSegment(draft.segment, formName ?? null) : "Not selected yet";
  const problems = resolved?.problems ?? [];
  const missing = resolved?.placeholderCoverage?.filter(item => item.missing > 0) ?? [];
  const ready = Boolean(resolved && resolved.recipientCount > 0 && !problems.length && !missing.length && !changed && resolved.revision === draft.revision);
  const currentRender = resolved?.renders?.[sample];
  const sender = draft.fromName || draft.fromEmail;
  const senderDomain = content.fromEmail.split("@")[1] || "morganhacks.com";
  const senderLocal = draft.fromEmail.split("@")[0];

  useEffect(() => {
    if (!changed) return;
    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [changed]);

  useEffect(() => {
    if (editingReplyTo) replyToInput.current?.focus();
  }, [editingReplyTo]);

  useEffect(() => {
    scrollArea.current?.scrollTo({ top: 0, behavior: "instant" });
    heading.current?.focus({ preventScroll: true });
  }, [step]);

  // Cleared before the fetch rather than left showing the previous form's
  // questions, which would offer a question key that belongs to another form —
  // and a criterion naming a question the chosen form does not have resolves
  // to nobody with nothing on screen saying why.
  const answerFormId = answer?.formId ?? null;
  useEffect(() => {
    setQuestions(null);
    if (!answerFormId) return;
    let current = true;
    loadQuestions(answerFormId).then(read => { if (current) setQuestions(read); });
    return () => { current = false; };
  }, [answerFormId, loadQuestions]);

  function set<K extends keyof CampaignUpdate>(key: K, value: CampaignUpdate[K]) {
    setDraft(previous => ({ ...previous, [key]: value }));
    setConfirming(false);
    setError(null);
  }

  function valid(requireAudience = false) {
    if (step === 0 && !editingReplyTo && replyToInput.current && !replyToInput.current.validity.valid) {
      setEditingReplyTo(true);
      requestAnimationFrame(() => settingsForm.current?.reportValidity());
      return false;
    }
    if (requireAudience && !draft.segment) {
      setError("Choose an audience to continue.");
      return false;
    }
    if (step === 2 && draft.segment?.type === "applicationStatus" && draft.segment.statuses.length === 0) {
      setError("Choose at least one application status.");
      return false;
    }
    // The dropdown disables these, so reaching here means the criterion was
    // already on the campaign — a question that has changed type, or one
    // chosen before it was promoted to a column of its own. It resolves to
    // nobody either way, and the API's sentence says which.
    if (step === 2 && chosenQuestion?.unmatchable) {
      setError(chosenQuestion.unmatchable);
      return false;
    }
    return step === 0 ? (settingsForm.current?.reportValidity() ?? true)
      : step === 2 ? (audienceForm.current?.reportValidity() ?? true) : true;
  }

  function run(action: () => Promise<void>) {
    startTransition(async () => {
      setError(null);
      try { await action(); }
      catch { setConfirming(false); setError("The connection was interrupted. Reload to check the campaign’s latest status before trying again."); }
    });
  }

  async function persist() {
    if (!changed) return draft.revision;
    const normalized = draft.segment?.type === "explicitList"
      ? { ...draft, segment: { ...draft.segment, emails: [...new Set(draft.segment.emails.map(email => email.trim()).filter(Boolean))] } }
      : draft;
    const result = await save(normalized);
    if (!result.ok) { setError(result.error); return null; }
    if (result.campaign.status !== "draft") { router.refresh(); return null; }
    const next = initialSettings(result.campaign, content);
    setCampaign(result.campaign);
    setDraft(next);
    setSaved(JSON.stringify(next));
    setResolved(null);
    setConfirming(false);
    return next.revision;
  }

  async function resolve(revision: number) {
    const result = await preview();
    if (!result.ok) { setError(result.error); return false; }
    if (result.preview.revision !== revision) {
      setResolved(null);
      setError("This draft changed in another session. Reload to review the latest settings.");
      return false;
    }
    setResolved(result.preview);
    setSample(0);
    setConfirming(false);
    return true;
  }

  function go(next: number) {
    if (pending || queued || next === step || !valid(step === 2 && next > step)) return;
    const target = next === 3 && !draft.segment ? 2 : next;
    run(async () => {
      const revision = await persist();
      if (revision === null) return;
      if (target >= 2 && draft.segment && !await resolve(revision)) return;
      setStep(target);
      setConfirming(false);
    });
  }

  function finishLater() {
    if (!valid()) return;
    run(async () => {
      if (await persist() !== null) router.push("/mail");
    });
  }

  function refreshRecipients() {
    if (!valid(true)) return;
    run(async () => { const revision = await persist(); if (revision !== null) await resolve(revision); });
  }

  function queueCampaign() {
    if (!resolved || !ready || !confirming) return;
    run(async () => {
      const result = await send(resolved.recipientCount, draft.revision);
      if (!result.ok) {
        setError(result.error);
        if (result.preview) { setResolved(result.preview); setSample(0); }
        setConfirming(false);
        return;
      }
      setQueued(true);
      router.refresh();
    });
  }

  function chooseAudience(type: Segment["type"]) {
    set("segment", type === "applicationStatus"
      ? { type, eventId: events[0]?.id ?? "", statuses: ["accepted"] }
      : type === "formRespondents" ? { type, formId: forms[0]?.id ?? "" }
        // Blank, not a guessed first question: the form's questions have not
        // been read yet, and a criterion nobody chose is one somebody could
        // send on.
        : type === "formAnswer" ? { type, formId: forms[0]?.id ?? "", question: "", value: "" }
          : { type, emails: [] });
  }

  const sectionTitle = ["Campaign settings", "Preview your design", "Choose your recipients", "Review your campaign"][step];
  const sectionDescription = ["Set the details people will see in their inbox.", "Your selected template, ready to preview on any screen.", "Choose an audience and check who will receive this email.", "Check the personalized message and final audience before sending."][step];

  return (
    <div className={styles.workspace}>
      <header className={styles.header}>
        <div className={styles.campaignTitle}><h1>{campaign.name}</h1><StatusPill status={queued ? "queued" : "draft"} className={styles.draftStatus} /></div>
        <div className={styles.headerActions}>
          <span className={styles.saveState} aria-live="polite">{pending ? "Working…" : changed ? "Unsaved changes" : "Draft saved"}</span>
          <button type="button" className={styles.textButton} disabled={pending || queued} onClick={finishLater}>Finish later</button>
          {step < 3 ? <button type="button" className={`${styles.primary} ${styles.continueButton}`} disabled={pending || queued} onClick={() => go(step + 1)}>{pending ? "Saving…" : step === 2 ? "Review campaign" : "Save and continue"}</button> : null}
        </div>
      </header>

      <nav className={styles.steps} aria-label="Campaign setup">
        <ol className={styles.stepsList}>
          {steps.map((label, index) => <li key={label} className={styles.stepItem} data-complete={index < step}>
            <button type="button" className={styles.step} aria-current={step === index ? "step" : undefined} disabled={pending || queued} onClick={() => go(index)}>
              <span className={styles.stepNumber}>{index < step ? <Icon icon={Tick02Icon} size={17} strokeWidth={2.2} /> : index + 1}</span><span>{label}</span>
            </button>
          </li>)}
        </ol>
      </nav>

      <div ref={scrollArea} className={styles.scrollArea}>
        <div className={styles.sectionIntro}>
          <div><h2 ref={heading} tabIndex={-1}>{sectionTitle}</h2><p>{sectionDescription}</p></div>
          <span className={styles.stepCount}>Step {step + 1} of 4</span>
        </div>
        {error ? <p className={styles.error} role="alert">{error}</p> : null}
        {queued ? <p className={styles.success} role="status">Campaign queued. Opening your delivery report…</p> : null}

        {step === 0 ? <div className={styles.settingsLayout}>
          <form ref={settingsForm} className={styles.settingsForm} onSubmit={event => { event.preventDefault(); go(1); }}>
            <fieldset disabled={pending || queued} className={styles.fields}>
              <div className={styles.field}><label htmlFor="campaign-name">Campaign name</label><input id="campaign-name" required maxLength={200} value={draft.name} onChange={event => set("name", event.target.value)} autoComplete="off" /><p className={styles.hint}>An internal name for your team.</p></div>
              <div className={styles.field}>
                <label htmlFor="campaign-subject">Email subject</label>
                <PersonalizedField id="campaign-subject" label="Email subject" required value={draft.subject} onChange={value => set("subject", value)} available={available} className={styles.personalizedInput} placeholder="Give people a reason to open your email" />
              </div>
              <div className={styles.field}>
                <label htmlFor="campaign-preview">Preview text <span>Optional</span></label>
                <PersonalizedField id="campaign-preview" label="Preview text" value={draft.previewText} onChange={value => set("previewText", value)} available={available} className={styles.personalizedInput} placeholder="A little more context, next to your subject" describedBy="campaign-preview-help" />
                <p id="campaign-preview-help" className={styles.hint}>Appears after the subject in supported inboxes.</p>
              </div>
              <div className={`${styles.sectionDivider} ${styles.senderHeading}`}>
                <h3>Sender details</h3>
                <button type="button" className={styles.senderEdit} aria-expanded={editingReplyTo} aria-controls="campaign-reply-to-editor" onClick={() => setEditingReplyTo(open => !open)}>{editingReplyTo ? "Hide reply-to address" : "Edit reply-to address"}</button>
              </div>
              <div className={styles.fieldPair}>
                <div className={styles.field}><label htmlFor="campaign-from-name">From name</label><input id="campaign-from-name" maxLength={64} value={draft.fromName} onChange={event => set("fromName", event.target.value)} placeholder="MorganHacks" autoComplete="off" /></div>
                <div className={styles.field}>
                  <label htmlFor="campaign-from-email">Sender email</label>
                  <div className={styles.senderEmail}>
                    <input id="campaign-from-email" type="text" inputMode="email" required maxLength={64} pattern={"[^\\s@]+"} value={senderLocal} onChange={event => set("fromEmail", `${event.target.value.split("@")[0]}@${senderDomain}`)} aria-describedby="campaign-sender-domain campaign-sender-domain-reason" autoComplete="off" autoCapitalize="none" spellCheck={false} />
                    <span className={styles.senderDomain} tabIndex={0} aria-describedby="campaign-sender-domain-reason" data-dismissed={domainHintDismissed} onMouseEnter={() => setDomainHintDismissed(false)} onFocus={() => setDomainHintDismissed(false)} onKeyDown={event => { if (event.key === "Escape") setDomainHintDismissed(true); }}>
                      <span id="campaign-sender-domain">@{senderDomain}</span>
                      <span id="campaign-sender-domain-reason" role="tooltip" className={styles.senderDomainTooltip}>Campaigns use the sending domain set by their email template. You can change only the part before @.</span>
                    </span>
                  </div>
                </div>
              </div>
              <div id="campaign-reply-to-editor" className={styles.field} hidden={!editingReplyTo}><label htmlFor="campaign-reply-to">Reply-to email <span>Optional</span></label><input ref={replyToInput} id="campaign-reply-to" type="email" value={draft.replyTo} onChange={event => set("replyTo", event.target.value)} placeholder={draft.fromEmail} aria-describedby="campaign-reply-to-help" autoComplete="off" spellCheck={false} /><p id="campaign-reply-to-help" className={styles.hint}>Leave empty to receive replies at the sender address.</p></div>
              <label className={styles.tracking}><span><strong>Email tracking</strong><small>Track opens, clicks, devices, and approximate countries.</small></span><input type="checkbox" role="switch" checked={draft.trackingEnabled} onChange={event => set("trackingEnabled", event.target.checked)} /><span className={styles.switch} aria-hidden="true" /></label>
            </fieldset>
            <button type="submit" style={{ display: "none" }} tabIndex={-1} aria-hidden="true">Continue to design</button>
          </form>
          <aside className={styles.settingsAside}>
            <CampaignInbox sender={sender} subject={draft.subject} snippet={draft.previewText} />
            <div className={styles.templateInfo}><Icon icon={Mail01Icon} size={22} /><div><span>Selected template</span><strong>{content.templateName || campaign.templateKey}</strong><p>You’ll preview this design in the next step.</p></div></div>
          </aside>
        </div> : null}

        {step === 1 ? <div className={styles.previewLayout}>
          <EmailPreview html={content.html} subject={draft.subject} sender={sender} />
          <aside className={styles.sidePanel}>
            <h3>Your email</h3>
            <dl className={styles.summary}><dt>Template</dt><dd>{content.templateName || campaign.templateKey}</dd><dt>Subject</dt><dd>{draft.subject}</dd><dt>From</dt><dd>{draft.fromName}<span>{draft.fromEmail}</span></dd>{draft.previewText ? <><dt>Preview text</dt><dd>{draft.previewText}</dd></> : null}<dt>Tracking</dt><dd>{draft.trackingEnabled ? "Enabled" : "Disabled"}</dd></dl>
            <p className={styles.hint}>Personalized fields are filled with recipient details in the final review.</p>
            <button type="button" className={styles.secondary} disabled={pending} onClick={() => go(0)}>Edit settings</button>
          </aside>
        </div> : null}

        {step === 2 ? <div className={styles.recipientsLayout}>
          <form ref={audienceForm} className={styles.audienceForm} onSubmit={event => { event.preventDefault(); refreshRecipients(); }}>
            <fieldset className={styles.fields} disabled={pending || queued}>
              <legend className={styles.groupTitle}>Audience source</legend>
              <div className={styles.audienceOptions}>
                {([
                  { value: "applicationStatus", label: "Applicants", description: "Choose by application status", icon: UserGroupIcon },
                  { value: "formRespondents", label: "Form respondents", description: "People who completed a form", icon: Task01Icon },
                  { value: "formAnswer", label: "Form answers", description: "People who gave a particular answer", icon: HelpCircleIcon },
                  { value: "explicitList", label: "Email list", description: "Add specific email addresses", icon: Mail01Icon },
                ] as const).map(option => <label key={option.value}>
                  <input type="radio" name="audience-type" value={option.value} checked={draft.segment?.type === option.value} onChange={() => chooseAudience(option.value)} />
                  <span className={styles.audienceOptionContent}>
                    <Icon icon={option.icon} size={20} />
                    <span className={styles.audienceOptionText}><strong>{option.label}</strong><small>{option.description}</small></span>
                    <span className={styles.audienceIndicator} aria-hidden="true" />
                  </span>
                </label>)}
              </div>
              {draft.segment?.type !== "explicitList" && audienceError ? <p className={styles.error}>{audienceError}</p> : null}
              {draft.segment?.type === "applicationStatus" ? <>
                <div className={styles.field}><label htmlFor="campaign-event">Event</label><Select id="campaign-event" required value={draft.segment.eventId} onChange={event => set("segment", { type: "applicationStatus", eventId: event.target.value, statuses: draft.segment?.type === "applicationStatus" ? draft.segment.statuses : ["accepted"] })}><option value="" disabled>Choose an event</option>{events.map(event => <option key={event.id} value={event.id}>{event.name}</option>)}</Select></div>
                <fieldset className={styles.statusChoices}><legend>Application status</legend>{APPLICANT_STATUSES.map(status => <label key={status.value}><input type="checkbox" checked={draft.segment?.type === "applicationStatus" && draft.segment.statuses.includes(status.value)} onChange={event => { const segment = draft.segment; if (segment?.type === "applicationStatus") set("segment", { ...segment, statuses: event.target.checked ? [...segment.statuses, status.value] : segment.statuses.filter(value => value !== status.value) }); }} /><span>{status.label}</span></label>)}</fieldset>
              </> : null}
              {draft.segment?.type === "formRespondents" ? <div className={styles.field}><label htmlFor="campaign-form">Form</label><Select id="campaign-form" required value={draft.segment.formId} onChange={event => set("segment", { type: "formRespondents", formId: event.target.value })}><option value="" disabled>Choose a form</option>{forms.map(form => <option key={form.id} value={form.id}>{form.name}</option>)}</Select></div> : null}

              {/* Form, then question, then answer. Each step narrows the next
                  and clears what came after it, because a question key only
                  means something on the form it came from and an answer only
                  means something on its question. */}
              {answer ? <>
                <div className={styles.field}>
                  <label htmlFor="campaign-answer-form">Form</label>
                  <Select id="campaign-answer-form" required value={answer.formId} onChange={event => set("segment", { type: "formAnswer", formId: event.target.value, question: "", value: "" })}>
                    <option value="" disabled>Choose a form</option>
                    {forms.map(form => <option key={form.id} value={form.id}>{form.name}</option>)}
                  </Select>
                </div>
                <div className={styles.field}>
                  <label htmlFor="campaign-answer-question">Question</label>
                  <Select id="campaign-answer-question" required disabled={!answerQuestions.length} value={answer.question} onChange={event => set("segment", { ...answer, question: event.target.value, value: "" })}>
                    <option value="" disabled>{questions === null ? "Loading questions…" : answerQuestions.length ? "Choose a question" : "No questions to choose from"}</option>
                    {/* Listed even when they cannot be matched, with the reason
                        shown once one is selected. A question quietly missing
                        from this list reads as a question that was deleted. */}
                    {answerQuestions.map(question => <option key={question.key} value={question.key} disabled={Boolean(question.unmatchable)}>{question.label}</option>)}
                  </Select>
                  {questions && !questions.ok ? <p className={styles.error}>{questions.error}</p> : null}
                  {questions?.ok && questions.version === null ? <p className={styles.hint}>This form has never been published, so there is nothing to have answered yet.</p> : null}
                  {questions?.ok && questions.version !== null ? <p className={styles.hint}>Version {questions.version}, which is the one being answered now. A question removed since then is not offered, so anybody who answered it is not included.</p> : null}
                  {chosenQuestion?.unmatchable ? <p className={styles.error}>{chosenQuestion.unmatchable}</p> : null}
                </div>
                <div className={styles.field}>
                  <label htmlFor="campaign-answer-value">Answer</label>
                  {chosenQuestion && chosenQuestion.values.length
                    ? <Select id="campaign-answer-value" required value={answer.value} onChange={event => set("segment", { ...answer, value: event.target.value })}>
                        <option value="" disabled>Choose an answer</option>
                        {chosenQuestion.values.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
                      </Select>
                    : <input id="campaign-answer-value" required maxLength={500} value={answer.value} onChange={event => set("segment", { ...answer, value: event.target.value })} placeholder="The answer to match" spellCheck={false} />}
                  <p className={styles.hint}>Matched exactly, ignoring capitals. Nothing about the answer goes into the email — it only decides who receives it.</p>
                </div>
              </> : null}
              {draft.segment?.type === "explicitList" ? <div className={styles.field}><label htmlFor="campaign-addresses">Email addresses</label><textarea id="campaign-addresses" required rows={7} value={draft.segment.emails.join("\n")} onChange={event => set("segment", { type: "explicitList", emails: event.target.value.split(/[\n,;]/) })} placeholder={"name@example.com\nanother@example.com"} spellCheck={false} /><p className={styles.hint}>One per line, or separated by commas. Duplicate addresses are counted once.</p></div> : null}
              <button type="submit" style={{ display: "none" }} tabIndex={-1} aria-hidden="true">Refresh recipients</button>
            </fieldset>
          </form>
          <section className={styles.recipientPreview} aria-labelledby="recipient-preview-title">
            <div className={styles.panelHeading}>
              <div><h3 id="recipient-preview-title">Recipients</h3><p className={styles.hint}>A preview of your final audience.</p></div>
              <button type="button" className={styles.refreshButton} disabled={pending || !draft.segment} onClick={refreshRecipients}><Icon icon={RefreshIcon} size={15} />{pending ? "Checking…" : changed ? "Update preview" : "Refresh"}</button>
            </div>
            {resolved && !changed ? <>
              <div className={styles.recipientMetrics}>
                <div className={styles.readyMetric}><strong>{resolved.recipientCount.toLocaleString()}</strong><span>{resolved.recipientCount === 1 ? "recipient ready" : "recipients ready"}</span></div>
                <div><strong>{(resolved.segmentSize ?? resolved.recipientCount).toLocaleString()}</strong><span>Matched</span></div>
                <div><strong>{(resolved.suppressedCount ?? 0).toLocaleString()}</strong><span>Suppressed</span></div>
              </div>
              {resolved.sample.length ? <>
                <div className={styles.recipientTableWrap}><table className={styles.recipientTable} aria-label="Recipient preview">
                  <thead><tr><th>Email address</th><th>Status</th></tr></thead>
                  <tbody>{resolved.sample.map(email => <tr key={email}>
                    <td><span className={styles.recipientIdentity}><span className={styles.recipientAvatar} aria-hidden="true">{email[0].toUpperCase()}</span><span>{email}</span></span></td>
                    <td><span className={styles.recipientReady}><Icon icon={CheckmarkCircle02Icon} size={15} />Ready</span></td>
                  </tr>)}</tbody>
                </table></div>
                <p className={styles.recipientCount}>{resolved.sample.length} of {resolved.recipientCount.toLocaleString()} recipients shown</p>
              </> : <div className={styles.recipientEmpty}><Icon icon={UserGroupIcon} size={26} /><h3>No recipients yet</h3><p>No one matches this audience. Try a different form or application status.</p></div>}
              {Object.entries(resolved.suppressedByReason ?? {}).map(([reason, count]) => <p key={reason} className={styles.hint}>{count} suppressed: {reason}</p>)}
              {/* Said out loud, because the form screen counts answers and
                  this one counts people. An organizer who reads forty on one
                  and thirty-one here, with nothing in between, has been shown
                  a bug that is not there. */}
              {resolved.unreachableCount ? <p className={styles.hint}>{resolved.unreachableCount.toLocaleString()} matching {resolved.unreachableCount === 1 ? "answer has" : "answers have"} nobody to mail — answered anonymously, or by somebody with no application on this event.</p> : null}
              {problems.map(problem => <p key={problem} className={styles.error}>{problem}</p>)}
            </> : <p className={styles.empty}>Choose an audience, then refresh to preview your recipients.</p>}
          </section>
        </div> : null}

        {step === 3 ? <div className={styles.reviewLayout}>
          <section>
            {currentRender ? <EmailPreview html={currentRender.html} text={currentRender.text} subject={currentRender.subject} sender={sender} to={currentRender.email} samples={resolved?.renders} selected={sample} onSelect={setSample} />
              : <div className={styles.empty}><Icon icon={Mail01Icon} size={28} /><h3>No personalized preview yet</h3><p>Choose an audience with recipients to review the message.</p><button type="button" className={styles.secondary} disabled={pending} onClick={() => go(2)}>Edit recipients</button></div>}
          </section>
          <aside className={styles.reviewSidebar}>
            <div className={styles.reviewHeading}><h3>Campaign summary</h3><button type="button" className={styles.textButton} onClick={() => go(0)} disabled={pending}>Edit</button></div>
            <div className={styles.reviewCount}><Icon icon={UserGroupIcon} size={22} /><strong>{(resolved?.recipientCount ?? 0).toLocaleString()} <span>{resolved?.recipientCount === 1 ? "recipient" : "recipients"}</span></strong><button type="button" className={styles.textButton} onClick={() => go(2)} disabled={pending}>View audience</button></div>
            <dl className={styles.reviewDetails}><dt>Audience</dt><dd>{audience}</dd><dt>From</dt><dd>{draft.fromName}<span>{draft.fromEmail}</span></dd>{draft.replyTo ? <><dt>Reply-to</dt><dd>{draft.replyTo}</dd></> : null}<dt>Tracking</dt><dd>{draft.trackingEnabled ? "Enabled" : "Disabled"}</dd></dl>
            <div className={styles.readiness}>
              {ready ? <p><Icon icon={CheckmarkCircle02Icon} size={18} />All checks passed</p> : <p>Resolve the items below before sending.</p>}
              {resolved && resolved.recipientCount === 0 ? <p className={styles.error}>Your audience has no recipients.</p> : null}
              {problems.map(problem => <p key={problem} className={styles.error}>{problem}</p>)}
              {missing.map(item => <p key={item.placeholder} className={styles.error}>{`{{${item.placeholder}}}`} is missing for {item.missing} recipients.</p>)}
              {!missing.length && resolved?.placeholderCoverage?.length ? <span>All personalized fields are filled.</span> : null}
            </div>
            {ownDraft ? <p className={styles.approval}>Another organizer must approve and send this campaign.</p> : !canSend ? <p className={styles.approval}>An organizer with permission to send must approve this campaign.</p> : null}
            {!ownDraft && canSend ? <div className={styles.sendControls}>
              {confirming ? <><p>Send this email to {resolved?.recipientCount.toLocaleString()} recipients now?</p><span className={styles.hint}>Emails cannot be recalled once sent.</span><button type="button" className={styles.primary} disabled={pending || !ready || queued} onClick={queueCampaign}>{pending ? "Sending…" : "Confirm and send"}</button><button type="button" className={styles.textButton} disabled={pending || queued} onClick={() => setConfirming(false)}>Keep reviewing</button></> : <button type="button" className={styles.primary} disabled={pending || !ready || queued} onClick={() => setConfirming(true)}>Send campaign <Icon icon={ArrowRight02Icon} size={17} /></button>}
            </div> : <button type="button" className={styles.primary} onClick={finishLater} disabled={pending || queued}>Finish review</button>}
          </aside>
        </div> : null}
      </div>
    </div>
  );
}
