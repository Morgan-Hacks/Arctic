/**
 * A Baltimore skyline, drawn as one line.
 *
 * Every hackathon site worth looking at has a world rather than a centred
 * column, and the cheapest honest one here is the city the event is in.
 * HackUTD does exactly this with Dallas; a line drawing needs no illustrator,
 * no licence and no stock photography, and it is specific to MorganHacks in
 * a way that an abstract gradient never is.
 *
 * Stylised, not surveyed. These are the shapes of a harbour city -- towers, a
 * dome, a clock, a spire, cranes on the water -- rather than named buildings
 * measured against a photograph, because a drawing that claimed to be
 * accurate and was not would be worse than one that never claimed it. If
 * somebody on the team can draw, this is the file to replace.
 *
 * One path per silhouette so the stroke weight stays even, `vector-effect`
 * so it stays hairline at any width, and no fill: the paper shows through,
 * which is what makes it sit on the page rather than on top of it.
 */
export function Skyline() {
  return (
    <svg
      className="es-skyline"
      viewBox="0 0 1200 240"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.25}
      strokeLinejoin="round"
      strokeLinecap="round"
      vectorEffect="non-scaling-stroke"
      aria-hidden="true"
      preserveAspectRatio="xMidYMax meet"
    >
      {/* The waterline. Everything stands on it. */}
      <path d="M0 232h1200" opacity={0.5} />

      {/* Low row, left: warehouses and rowhouses. */}
      <path d="M0 232v-44h46v-14h38v58" />
      <path d="M84 232v-34h30v-10h26v44" />
      <path d="M140 232v-52h34v52" />

      {/* A church spire, because the skyline has several. */}
      <path d="M174 232v-62h22v-20l11-26 11 26v20h22v62" />

      {/* The tall cluster. */}
      <path d="M240 232v-96h54v96" />
      <path d="M294 232v-128h46v128" />
      <path d="M340 232v-78h38v78" />

      {/* A stepped tower with a crown. */}
      <path d="M378 232v-112h52v-18h26v18h52v112" />
      <path d="M430 102v-24h26v24" />

      {/* The clock tower. A campus has one and so does this. */}
      <path d="M508 232v-146h40v-22h26v22h40v146" />
      <circle cx={581} cy={118} r={17} />
      <path d="M581 108v10l7 5" />

      {/* A dome. */}
      <path d="M614 232v-84h92v84" />
      <path d="M614 148a46 40 0 0 1 92 0" />
      <path d="M660 108v-16" />

      {/* Centre-right slabs. */}
      <path d="M706 232v-118h48v118" />
      <path d="M754 232v-152h44v152" />
      <path d="M798 232v-92h36v92" />

      {/* Windows, as a screened grid rather than drawn one by one. */}
      <path
        d="M766 110h20M766 126h20M766 142h20M766 158h20M766 174h20"
        opacity={0.45}
      />
      <path d="M306 128h22M306 146h22M306 164h22M306 182h22" opacity={0.45} />

      {/* Harbour cranes, which is the part that makes it this city and not a
          generic one. */}
      <path d="M864 232v-104h4v104" />
      <path d="M842 128h70l-14-22h-42z" />
      <path d="M866 128l44-34" />
      <path d="M910 94v26" />

      <path d="M962 232v-86h4v86" />
      <path d="M944 146h58l-12-18h-34z" />
      <path d="M964 146l36-28" />
      <path d="M1000 118v22" />

      {/* Low right: the far shore trailing off. */}
      <path d="M1026 232v-56h40v-12h30v68" />
      <path d="M1096 232v-40h34v-12h28v52" />
      <path d="M1158 232v-30h42v30" />

      {/* Water. Three broken lines, lightest at the front. */}
      <path d="M40 244h120M200 244h86M330 244h150M520 244h96" opacity={0.3} />
      <path d="M110 254h70M250 254h130M430 254h80M560 254h120" opacity={0.2} />
    </svg>
  );
}
