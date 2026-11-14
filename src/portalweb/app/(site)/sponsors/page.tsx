import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Check, Frame, SectionHead } from "@/components/event-site/frame";
import { EVENT_SITE, isOn } from "@/lib/features";
import { siteConfig } from "@/site.config";

const { sponsors, edition } = siteConfig;

export const metadata: Metadata = {
  title: "Sponsor MorganHacks",
  description:
    "What sponsoring the fifth MorganHacks pays for, what each tier includes, and who to write to.",
};

/**
 * One cell of the matrix.
 *
 * Three kinds of answer: yes, no, and a quantity. The icon and the dash both
 * carry a word for a screen reader, because a column of unlabelled ticks is a
 * column of nothing to anybody not looking at it.
 */
function Cell({ value }: { value: boolean | string }) {
  if (value === true) {
    return (
      <>
        <Check />
        <span className="es-sr">Included</span>
      </>
    );
  }

  if (value === false) {
    return (
      <>
        <span className="es-matrix__no" aria-hidden="true" />
        <span className="es-sr">Not included</span>
      </>
    );
  }

  return <>{value}</>;
}

export default function SponsorsPage() {
  if (!isOn(EVENT_SITE)) notFound();

  const { tiers, perkGroups, letter, contact } = sponsors;

  return (
    <Frame current="sponsors">
      <section className="es-hero">
        <p className="es-hero__eyebrow es-label">Sponsorship</p>
        <h1 className="es-hero__title">Sponsor us.</h1>
        <hr className="es-hero__rule" />
        <p className="es-hero__deck">
          The {edition.ordinal} MorganHacks is free for every student who attends, and it is free
          because somebody pays for it.
        </p>
      </section>

      <section className="es-sec">
        <SectionHead folio="01" kicker="The short version" heading="What you would be paying for." />
        <div className="es-prose">
          {letter.map((para) => (
            <p key={para.slice(0, 24)}>{para}</p>
          ))}
        </div>
      </section>

      <section className="es-sec">
        <SectionHead
          folio="02"
          kicker="Tiers"
          heading="What each one includes."
          lede="Four tiers, and everything in a column is included at that amount."
        />

        <div className="es-matrix-wrap" tabIndex={0} role="region" aria-label="Sponsorship tiers">
          <table className="es-matrix">
            <caption>Every tier includes everything marked in its column.</caption>

            <thead>
              <tr>
                <th scope="col" className="es-matrix__perk">
                  <span className="es-sr">What you get</span>
                </th>
                {tiers.map((tier) => (
                  <th
                    key={tier.id}
                    scope="col"
                    className={`es-matrix__tier${tier.highlight ? " es-matrix__tier--on" : ""}`}
                  >
                    <span className="es-matrix__tier-name">{tier.name}</span>
                    <span className="es-matrix__tier-price">{tier.price}</span>
                  </th>
                ))}
              </tr>
            </thead>

            {perkGroups.map((group) => (
              <tbody key={group.group}>
                <tr className="es-matrix__group">
                  <th scope="colgroup" colSpan={tiers.length + 1} className="es-label">
                    {group.group}
                  </th>
                </tr>

                {group.perks.map((perk) => (
                  <tr key={perk.label}>
                    <th scope="row" className="es-matrix__perk">
                      {perk.label}
                      <span className="es-matrix__help">{perk.help}</span>
                    </th>

                    {perk.cells.map((value, i) => (
                      <td
                        key={tiers[i].id}
                        className={`es-matrix__cell${tiers[i].highlight ? " es-matrix__cell--on" : ""}`}
                      >
                        <Cell value={value} />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            ))}
          </table>
        </div>

        <p className="es-matrix__hint">Scroll the table sideways to see every tier.</p>
      </section>

      <section className="es-sec">
        <SectionHead folio="03" kicker="Next" heading="Write to us." />
        <div className="es-closing">
          <p>
            Tell us roughly what you are looking for and we will tell you honestly whether we can
            deliver it. If a tier is close but not right, say so.
          </p>
          <p>
            <a className="es-btn" href={`mailto:${contact}?subject=Sponsoring MorganHacks 2027`}>
              {contact}
            </a>
          </p>
        </div>
      </section>
    </Frame>
  );
}
