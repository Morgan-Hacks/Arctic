import { EmptyState } from "@/components/ui/empty-state";
import { readPageData } from "@/lib/page-data";
import { Shell } from "../shell";
import { EventPicker } from "./event-picker";
import { AddSponsorForm, DeleteSponsorButton, EditSponsorForm } from "./sponsor-forms";
import { money, readReport, readSponsors, tierLabel } from "./api";
import styles from "./sponsors.module.css";

/**
 * Who is backing the event, and the two exports built on top of them.
 *
 * Two audiences on one screen: the sponsorship team manages the rows, finance
 * reads the money, and the report is the file that may go to a sponsor. The
 * money and the applicant numbers sit in separate cards for the same reason
 * the API keeps them on separate routes -- an export that bundled what a
 * sponsor paid together with applicant data would be one screenshot away from
 * being a problem.
 */
export default async function Sponsors({
  searchParams,
}: {
  searchParams: Promise<{ eventId?: string }>;
}) {
  const { eventId } = await searchParams;
  const [{ person, data: sponsorsRead }, { data: reportRead }] = await Promise.all([
    readPageData(() => readSponsors(eventId)),
    readPageData(() => readReport(eventId)),
  ]);

  if (!sponsorsRead.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>Sponsors</h1>
        <div className="empty">{sponsorsRead.error}</div>
      </Shell>
    );
  }

  if (!reportRead.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>Sponsors</h1>
        <div className="empty">{reportRead.error}</div>
      </Shell>
    );
  }

  const { events, chosen, sponsors, canEdit, canViewFinancials } = sponsorsRead.view;
  const report = reportRead.view;

  if (!chosen) {
    return (
      <Shell personId={person.personId}>
        <h1>Sponsors</h1>
        <EmptyState
          variant="data"
          size="page"
          title="No events yet"
          description="Create an event under Events. Sponsors are tracked per event."
        />
      </Shell>
    );
  }

  const query = `?eventId=${encodeURIComponent(chosen.id)}`;

  return (
    <Shell personId={person.personId}>
      <div className={styles.page}>
        <div className="page-head">
          <div>
            <h1>Sponsors</h1>
            <p className="lede">Who is backing this event, and the numbers that may go to them.</p>
          </div>
        </div>

        <EventPicker events={events} chosen={chosen} />

        <div className={styles.grid}>
          <section className={styles.card} aria-labelledby="sponsors-list">
            <h2 id="sponsors-list">Sponsors</h2>
            <p>
              The table the sponsorship team manages. Amounts show only with
              sponsors.view_financials.
            </p>
            {sponsors.length === 0 ? (
              <EmptyState
                variant="data"
                title="No sponsors yet"
                description="Add the first one to start tracking who is backing this event."
              />
            ) : (
              <table className={styles.table}>
                <thead>
                  <tr>
                    <th scope="col">Name</th>
                    <th scope="col">Tier</th>
                    <th scope="col">Status</th>
                    {canViewFinancials ? <th scope="col">Committed</th> : null}
                    {canEdit ? <th scope="col">Actions</th> : null}
                  </tr>
                </thead>
                <tbody>
                  {sponsors.map((sponsor) => (
                    <tr key={sponsor.id}>
                      <td>
                        <div>{sponsor.name}</div>
                        {sponsor.contactEmail ? (
                          <div className={styles.note}>{sponsor.contactEmail}</div>
                        ) : null}
                      </td>
                      <td>
                        <span className={styles.tier}>{tierLabel(sponsor.tier)}</span>
                      </td>
                      <td>{sponsor.status}</td>
                      {canViewFinancials ? (
                        <td>{sponsor.amountCents === null ? "—" : money(sponsor.amountCents)}</td>
                      ) : null}
                      {canEdit ? (
                        <td>
                          <details>
                            <summary>Edit</summary>
                            <EditSponsorForm
                              sponsor={sponsor}
                              canViewFinancials={canViewFinancials}
                            />
                          </details>{" "}
                          <DeleteSponsorButton id={sponsor.id} name={sponsor.name} />
                        </td>
                      ) : null}
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            {canEdit ? (
              <details>
                <summary>Add a sponsor</summary>
                <AddSponsorForm eventId={chosen.id} canViewFinancials={canViewFinancials} />
              </details>
            ) : null}
          </section>

          <div className={styles.grid} style={{ gridTemplateColumns: "1fr" }}>
            <section className={styles.card} aria-labelledby="sponsor-report">
              <h2 id="sponsor-report">Sponsor report</h2>
              <p>
                Attendance, reach and the tier mix. No names, no money, nothing
                anybody wrote -- the file that may go to a sponsor.
              </p>
              {report.report ? (
                <>
                  <div className={styles.stat}>
                    <span>Applicants</span>
                    <strong>{report.report.totalApplicants}</strong>
                  </div>
                  <div className={styles.stat}>
                    <span>Submitted</span>
                    <strong>{report.report.submittedApplications}</strong>
                  </div>
                  <div className={styles.stat}>
                    <span>Confirmed</span>
                    <strong>{report.report.confirmed}</strong>
                  </div>
                  <div className={styles.stat}>
                    <span>Checked in</span>
                    <strong>{report.report.checkedIn}</strong>
                  </div>
                  <div className={styles.stat}>
                    <span>Schools represented</span>
                    <strong>{report.report.schoolCount}</strong>
                  </div>
                  {report.report.sponsorsByTier.map((row) => (
                    <div key={row.tier} className={styles.stat}>
                      <span>{tierLabel(row.tier)} sponsors</span>
                      <strong>{row.count}</strong>
                    </div>
                  ))}
                  <div className={styles.actions}>
                    <a className="button" href={`/api/admin/sponsors/report.csv${query}`}>
                      Download report (.csv)
                    </a>
                  </div>
                  {!report.canViewResponses ? (
                    <p className={styles.note}>
                      Demographics need applications.view_responses and are not shown to you.
                    </p>
                  ) : null}
                </>
              ) : (
                <p className={styles.note}>No report for this event yet.</p>
              )}
            </section>

            {report.canViewFinancials && report.financials ? (
              <section className={styles.card} aria-labelledby="sponsor-financials">
                <h2 id="sponsor-financials">Financials</h2>
                <p>What was committed and what has arrived. No applicant data.</p>
                <div className={styles.stat}>
                  <span>Sponsors</span>
                  <strong>{report.financials.sponsorCount}</strong>
                </div>
                <div className={styles.stat}>
                  <span>Committed</span>
                  <strong>{money(report.financials.committedCents)}</strong>
                </div>
                <div className={styles.stat}>
                  <span>Paid</span>
                  <strong>{money(report.financials.paidCents)}</strong>
                </div>
                <div className={styles.actions}>
                  <a className="button" href={`/api/admin/sponsors/financials.csv${query}`}>
                    Download financials (.csv)
                  </a>
                </div>
              </section>
            ) : null}
          </div>
        </div>
      </div>
    </Shell>
  );
}
