import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { siteConfig } from "@/site.config";
import { HEIST_THEME, isOn } from "@/lib/features";
import { HeistNav, HeistFoot, HeistTicker, MlhBadge } from "../../../components/heist-site/chrome";
import { HeistRain } from "../../../components/heist-site/rain";

const { sponsors, contactEmail, year } = siteConfig;

export const metadata: Metadata = {
  title: `Sponsors – MorganHacks ${siteConfig.year}`,
  description:
    "Sponsor MorganHacks 2027 at Morgan State University. Tiers, what each one includes, and who to talk to.",
};

export default function SponsorsPage() {
  if (!isOn(HEIST_THEME)) notFound();

  const mailto = `mailto:${contactEmail}?subject=Sponsoring%20MorganHacks%20${year}`;

  return (
    <>
      <MlhBadge />
      <HeistNav current="sponsors" />
      <HeistTicker />
      <HeistRain />

      <main>
        <section className="heist-shell heist-section">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// The backers</span>
            <h1 className="heist-h2">{sponsors.pitch.heading}</h1>
            <p className="heist-lede">{sponsors.pitch.line}</p>
          </div>

          <div className="heist-hero__actions">
            <a className="heist-button heist-button--primary" href={mailto}>
              Talk to us
            </a>
            <a className="heist-button heist-button--ghost" href="/schedule">
              See the weekend
            </a>
          </div>
        </section>

        <section className="heist-shell heist-section">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// What it buys</span>
            <h2 className="heist-h2">Where the money goes.</h2>
            <p className="heist-lede">
              There is no ticket price and no entry fee. Everything a hacker
              touches over the weekend is paid for by the organisations on this
              page.
            </p>
          </div>

          <div className="heist-grid">
            <article className="heist-card">
              <span className="heist-card__index">01</span>
              <h3>The room</h3>
              <p>
                Space on campus for a day and a night, with power, network and
                somewhere to sleep.
              </p>
            </article>
            <article className="heist-card">
              <span className="heist-card__index">02</span>
              <h3>The food</h3>
              <p>
                Five meals across the weekend, plus coffee that does not run out
                at two in the morning.
              </p>
            </article>
            <article className="heist-card">
              <span className="heist-card__index">03</span>
              <h3>The prizes</h3>
              <p>
                Hardware and awards worth entering for, and the kit teams borrow
                to build with.
              </p>
            </article>
          </div>
        </section>

        <section className="heist-shell heist-section">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// The tiers</span>
            <h2 className="heist-h2">Pick a level.</h2>
            <p className="heist-lede">
              Each tier includes everything below it. These are the asking
              prices, not a fixed menu, and the conversation after the email is
              where it gets tailored.
            </p>
          </div>

          <ul className="heist-tiers">
            {sponsors.tiers.map((tier, i) => (
              <li
                className={
                  // The top tier is the one being sold, so it is the one that
                  // is marked. Four highlighted cards would be wallpaper.
                  i === sponsors.tiers.length - 1
                    ? "heist-tier heist-tier--lead"
                    : "heist-tier"
                }
                key={tier.name}
              >
                <span className="heist-tier__name">{tier.name}</span>
                <span className="heist-tier__amount">{tier.amount}</span>
                <ul>
                  {tier.benefits.map((benefit) => (
                    <li key={benefit}>{benefit}</li>
                  ))}
                </ul>
              </li>
            ))}
          </ul>

          <div className="heist-hero__actions" style={{ marginTop: "28px" }}>
            <a className="heist-button heist-button--primary" href={mailto}>
              Start a conversation
            </a>
          </div>
        </section>

        <section className="heist-shell heist-section">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// This year</span>
            <h2 className="heist-h2">Confirmed partners.</h2>
          </div>

          {/*
            An honest empty state rather than last year's logo wall.
            site.config.sponsors.confirmed is deliberately empty: nobody has
            signed for 2027 yet, and a wall filled in from a previous edition
            claims a relationship nobody has agreed to this year. Add names to
            the config once somebody has, and this block becomes the wall.
          */}
          {sponsors.confirmed.length === 0 ? (
            <div className="heist-notice" role="note">
              <span>
                <b>Nobody yet</b>
                Sponsors for {year} are still being confirmed, so this space is
                empty on purpose rather than showing last year&rsquo;s partners.
                If you want your name to be the first one here,{" "}
                <a href={mailto}>write to us</a>.
              </span>
            </div>
          ) : (
            <ul className="heist-grid">
              {sponsors.confirmed.map((partner) => (
                <li className="heist-card" key={partner.name}>
                  <a href={partner.href} target="_blank" rel="noopener noreferrer">
                    {partner.name}
                  </a>
                </li>
              ))}
            </ul>
          )}
        </section>
      </main>

      <HeistFoot />
    </>
  );
}
