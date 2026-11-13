import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { siteConfig } from "@/site.config";
import { HEIST_THEME, isOn } from "@/lib/features";
import { HeistNav, HeistFoot, HeistTicker, MlhBadge } from "../../../components/heist-site/chrome";
import { HeistRain } from "../../../components/heist-site/rain";

const { archive, anniversary, year, contactEmail } = siteConfig;

export const metadata: Metadata = {
  title: `Five years of MorganHacks`,
  description:
    "Every MorganHacks since 2023, and the site each one was run on. 2027 is the fifth edition.",
};

export default function ArchivePage() {
  if (!isOn(HEIST_THEME)) notFound();

  return (
    <>
      <MlhBadge />
      <HeistNav current="archive" />
      <HeistTicker />
      <HeistRain />

      <main>
        <section className="heist-shell heist-section">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// The record</span>
            <h1 className="heist-h2">{anniversary.heading}</h1>
            <p className="heist-lede">
              Four hackathons have run at Morgan State since 2023 and the fifth
              is in April. Each one was built on its own site, and every one of
              them is still up, so this is the archive rather than a
              retrospective somebody wrote afterwards.
            </p>
          </div>

          <ul className="heist-years">
            {archive.map((edition) => (
              <li key={edition.year}>
                <a
                  className="heist-year"
                  href={edition.href}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <span className="heist-year__num">{edition.year}</span>
                  <span className="heist-year__body">
                    <strong>{edition.dates ?? "Dates not recorded"}</strong>
                    <span>{edition.note}</span>
                  </span>
                  <span className="heist-year__go">
                    {edition.year}.morganhacks.com →
                  </span>
                </a>
              </li>
            ))}
            <li>
              <div className="heist-year heist-year--current">
                <span className="heist-year__num">{year}</span>
                <span className="heist-year__body">
                  <strong>April 2027, dates to come</strong>
                  <span>The fifth. You are looking at it.</span>
                </span>
                <span className="heist-year__go">This one</span>
              </div>
            </li>
          </ul>
        </section>

        <section className="heist-shell heist-section">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// Why they are still up</span>
            <h2 className="heist-h2">Nothing gets taken down.</h2>
            <p className="heist-lede">
              Each edition keeps its own subdomain once the weekend is over.
              Somebody who built something at MorganHacks 2024 can still point
              at the site that ran it, and the team that takes this over in 2028
              gets four worked examples instead of a folder of screenshots.
            </p>
          </div>

          <div className="heist-grid">
            <article className="heist-card">
              <span className="heist-card__index">01</span>
              <h3>One subdomain each</h3>
              <p>
                2023 through 2026 each answer on their own address. This site
                takes the apex, and hands it on next year.
              </p>
            </article>
            <article className="heist-card">
              <span className="heist-card__index">02</span>
              <h3>Frozen, not maintained</h3>
              <p>
                Past editions are left exactly as they were on the day. Dates,
                sponsors and links on them are historical.
              </p>
            </article>
            <article className="heist-card">
              <span className="heist-card__index">03</span>
              <h3>Missing a year?</h3>
              <p>
                2025&rsquo;s site never published its dates, so this page does
                not claim any. If you have them,{" "}
                <a href={`mailto:${contactEmail}`}>tell us</a> and they go in.
              </p>
            </article>
          </div>
        </section>
      </main>

      <HeistFoot />
    </>
  );
}
