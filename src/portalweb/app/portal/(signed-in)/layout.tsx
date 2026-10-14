import {apiFetch} from "@/lib/api";
import {isOn} from "@/lib/features";
import { Tabs } from "./tabs";

/**
 * The chrome only a signed-in applicant sees.
 *
 * A route group rather than a path segment, so these pages are still
 * `/portal`, `/portal/profile` and `/portal/messages` — the addresses that go
 * in emails and get bookmarked — while `/portal/sign-in` sits outside and gets
 * no tabs.
 *
 * The tabs are chrome, not a gate. Each page checks the session for itself,
 * because a layout that redirected would be a check somebody could route
 * around by adding a page beside it.
 */
export default async function SignedInLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  let directoryEligible=false;
  if(isOn("enable_directory_feature")){try{const response=await apiFetch("/portal/directory/status");if(response.ok)directoryEligible=(await response.json()).eligible===true}catch{}}
  return (
    <>
      <Tabs directoryEligible={directoryEligible} />
      <main>{children}</main>
    </>
  );
}
