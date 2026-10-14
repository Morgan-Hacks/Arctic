import { currentPerson } from "@/lib/api";
import { CHECK_IN_DESK, READING_MODE, isOn } from "@/lib/features";
import { sectionsFor } from "./sections";
import { SidebarLayout, SidebarPage } from "./sidebar";
import { ChatPanel } from "@/components/assistant/chat-panel";
import { displayName } from "@/lib/person-profile";
import { READING_MODE_COOKIE, initialReadingMode } from "../../../libs/ui/reading-mode";
import { cookies } from "next/headers";

type SidebarOptions = {
  fullName?: string | null;
  email?: string | null;
  templateCount?: number;
};

/**
 * Reading mode, resolved here rather than in the client sidebar.
 *
 * `isOn` is server-only by design, so the flag cannot be read in a "use client"
 * component — which means the shell decides and passes the answer down, the
 * same way it passes identity and permissions.
 *
 * Undefined means no cookie, and the client settles it from the OS preference
 * after mount.
 */
async function readingMode() {
  if (!isOn(READING_MODE)) return undefined;
  return { enabled: true as const, initial: initialReadingMode((await cookies()).get(READING_MODE_COOKIE)?.value) };
}

async function sidebarProps({ fullName, email, templateCount }: SidebarOptions = {}) {
  const person = await currentPerson();
  const address = email ?? person?.email ?? null;
  return {
    sections: [ ...(isOn("enable_directory_feature") ? [{href:"/directory",label:"Hacker directory"}] : []), ...sectionsFor(
      person?.permissions ?? new Set<string>(),
      templateCount === undefined ? {} : { "/templates": templateCount },
      // A tab that only ever refuses teaches its reader that the console
      // is broken, so the desk stays out of the nav until its flag is on.
    ).filter((section) => section.href !== "/check-in" || isOn(CHECK_IN_DESK))],
    identity: {
      label: displayName(person?.fullName ?? fullName, address),
      email: address,
      avatarUrl: person?.avatarUrl ?? null,
    },
  };
}

export async function ConsoleShell({ children }: { children: React.ReactNode }) {
  return (
    <SidebarLayout {...await sidebarProps()} reading={await readingMode()}>
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
