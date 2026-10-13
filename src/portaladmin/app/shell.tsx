import { currentPerson } from "@/lib/api";
import { currentEvent } from "@/lib/current-event";
import { dDayLock, when } from "./d-day/lock";
import { CHECK_IN_DESK, isOn } from "@/lib/features";
import { sectionsFor } from "./sections";
import { SidebarLayout, SidebarPage } from "./sidebar";
import { ChatPanel } from "@/components/assistant/chat-panel";
import { displayName } from "@/lib/person-profile";

type SidebarOptions = {
  fullName?: string | null;
  email?: string | null;
  templateCount?: number;
};

async function sidebarProps({ fullName, email, templateCount }: SidebarOptions = {}) {
  const [person, event] = await Promise.all([currentPerson(), currentEvent()]);
  const address = email ?? person?.email ?? null;

  // Shut until the day before, and the bar says so rather than only the
  // screen -- somebody who can see the tab should be able to tell why it is
  // quiet without opening it.
  const lock = dDayLock(event?.startsAt);

  return {
    sections: sectionsFor(
      person?.permissions ?? new Set<string>(),
      templateCount === undefined ? {} : { "/templates": templateCount },
      lock.locked
        ? { "/d-day": { why: `The board opens ${when(lock)}, the day before the event.` } }
        : {},
      // A tab that only ever refuses teaches its reader that the console
      // is broken, so the desk stays out of the nav until its flag is on.
    ).filter((section) => section.href !== "/check-in" || isOn(CHECK_IN_DESK)),
    identity: {
      label: displayName(person?.fullName ?? fullName, address),
      email: address,
      avatarUrl: person?.avatarUrl ?? null,
    },
  };
}

export async function ConsoleShell({ children }: { children: React.ReactNode }) {
  return (
    <SidebarLayout {...await sidebarProps()}>
      {children}
      {/* One mount for the whole console, so a thread survives moving between
          screens. It fetches nothing until somebody opens it. */}
      <ChatPanel />
    </SidebarLayout>
  );
}

/** The server owns identity and permissions; the sidebar only renders them. */
export async function Shell({
  personId,
  fullName,
  email,
  templateCount,
  children,
}: {
  personId: string;
  fullName?: string | null;
  email?: string | null;
  templateCount?: number;
  children: React.ReactNode;
}) {
  // currentPerson is memoized per request, including the page's earlier check.
  return (
    <SidebarPage {...await sidebarProps({ fullName, email, templateCount })}>
      {children}
      <ChatPanel />
    </SidebarPage>
  );
}
