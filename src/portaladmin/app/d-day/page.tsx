import { redirect } from "next/navigation";

/**
 * The section, which is not itself a screen.
 *
 * D-Day collects what only matters once doors open. Today that is the board
 * and nothing else, so opening the section lands on it rather than on an index
 * listing one thing. When check-in and the rest arrive this becomes a real
 * page; until then a redirect is more honest than a menu of one.
 */
export default function DDay() {
  redirect("/d-day/dashboard");
}
