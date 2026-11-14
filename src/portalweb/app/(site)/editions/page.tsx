import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { Frame, SectionHead } from "@/components/event-site/frame";
import { EVENT_SITE, isOn } from "@/lib/features";
import { EditionGrid } from "../page";

export const metadata: Metadata = {
  title: "Every MorganHacks",
  description:
    "Five editions of MorganHacks at Morgan State University, from the first in April 2023 to the fifth in April 2027.",
};

export default function EditionsPage() {
  if (!isOn(EVENT_SITE)) notFound();

  return (
    <Frame current="editions">
      <section className="es-hero">
        <p className="es-hero__eyebrow es-label">Since 2023</p>
        <h1 className="es-hero__title">Every one.</h1>
        <hr className="es-hero__rule" />
        <p className="es-hero__deck">
          Five Aprils at Morgan State University. Each edition built its own site, and each one is
          still standing.
        </p>
      </section>

      <section className="es-sec">
        <SectionHead
          folio="01"
          kicker="The record"
          heading="One April at a time."
          lede="Nothing here is reconstructed. The links go out to the sites each year's team built."
        />
        <EditionGrid />
      </section>

      <section className="es-sec">
        <SectionHead folio="02" kicker="Why it is here" heading="Five is the argument." />
        <div className="es-prose">
          <p>
            A hackathon in its first year is asking people to take a chance on it. One in its fifth
            is asking them to turn up to something that has happened every April since 2023,
            through a change of organizers each time, which is the harder thing and the more useful
            thing to be able to say.
          </p>
          <p>
            It is also the honest answer to a sponsor asking whether this will still exist next
            year.
          </p>
        </div>
      </section>
    </Frame>
  );
}
