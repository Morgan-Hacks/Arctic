namespace MorganHacks.Applications.Assistant;

/// <summary>One of the console's screens, as the nav bar offers it.</summary>
/// <param name="Route">
/// The console's own path, which is the only kind of destination this
/// catalogue holds. Nothing here is a link to anywhere outside the console,
/// and nothing here came from the model.
/// </param>
/// <param name="Needs">
/// The permissions any one of which makes the screen worth opening, read as
/// any-of, exactly as the nav table in the console reads them.
/// </param>
public sealed record PortalScreen(
    string Route, string Label, string Does, IReadOnlyList<string> Needs);

/// <summary>
/// Something that has to be true before a workflow can be followed at all.
/// </summary>
/// <remarks>
/// Its own type rather than a sentence, for the sake of <paramref name="Needs"/>.
/// "Create an event first" is advice to a super admin and a dead end to
/// everybody else, and the difference between the two is a permission -- so
/// the permission that clears a prerequisite is carried beside it, where the
/// guidance can check it, rather than left in prose for the model to notice.
/// </remarks>
/// <param name="Needs">
/// The permissions any one of which would let the asking person clear this
/// themselves. Empty when clearing it is not gated, or not gated by anything
/// this catalogue knows about.
/// </param>
public sealed record Prerequisite(string Says, IReadOnlyList<string> Needs);

/// <summary>
/// One thing somebody can do in the console, written down.
/// </summary>
/// <param name="Before">
/// What has to be true before the steps can be followed at all. Separate from
/// <see cref="Steps"/> because a prerequisite is not an instruction: "there
/// has to be an event" is something to go and find out, and listing it as
/// step one would read as something to go and do.
/// </param>
/// <param name="Steps">
/// What to do, in the order the screen presents it, in the words the screen
/// uses. The labels are quoted from the components rather than paraphrased --
/// an organizer told to "choose Create" while looking at a button that says
/// "Add item" has been given a wrong answer, not a loose one.
/// </param>
/// <param name="Screens">
/// Every screen this guidance may name, so that a route in an answer is one
/// that came out of this file rather than one the model composed.
/// </param>
public sealed record Workflow(
    string Title,
    string About,
    IReadOnlyList<string> Needs,
    IReadOnlyList<Prerequisite> Before,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> Notes,
    IReadOnlyList<PortalScreen> Screens);

/// <summary>
/// How to use the console, as a table rather than as a habit of the model's.
/// </summary>
/// <remarks>
/// <para>
/// The assistant could already answer questions about the data and could not
/// answer "how do I add a new item to inventory", because every read it has
/// returns figures and no figure says what a button is called. Left to its own
/// knowledge it would describe a plausible dialog, which is the worst
/// available answer: an organizer cannot tell a remembered screen from a real
/// one, and will go looking for a field that was never there.
/// </para>
/// <para>
/// So guidance is written down here or it does not exist. The tool that serves
/// it refuses a workflow this file does not hold, and the brief tells the model
/// to say so rather than fill the gap -- an honest limitation being the one
/// answer somebody can safely act on.
/// </para>
///
/// ## Read off the components, and named here so it can be checked
///
/// <para>
/// Every label, hint and empty state below was taken from the console's own
/// source, and a reviewer should be able to check each one against it:
/// </para>
/// <list type="bullet">
/// <item><c>src/portaladmin/app/sections.ts</c> -- the nav bar, its labels,
/// its routes, and the permission each section is listed under.</item>
/// <item><c>src/portaladmin/app/inventory/page.tsx</c> -- the Inventory
/// screen, where Add item sits, and both of its empty states.</item>
/// <item><c>src/portaladmin/app/inventory/add-item-dialog.tsx</c> -- the Add
/// item dialog: its fields, which of them are marked optional, the hint under
/// Low at, and the message a bad count is refused with.</item>
/// <item><c>src/portaladmin/app/inventory/event-picker.tsx</c> -- why the
/// event is sometimes a picker and sometimes a line of text.</item>
/// </list>
/// <para>
/// Keeping it in this repository rather than in a wiki is the point. A label
/// changed in one of those files and not here is a diff a reviewer can see,
/// which a wiki page nobody opened is not.
/// </para>
///
/// ## Deliberately small
///
/// <para>
/// Two entries, not twenty. What makes this file worth consulting is that
/// every line in it has been checked against the code that draws the screen,
/// and a catalogue covering every panel in the console would be one nobody
/// could vouch for -- which puts the model back to describing plausible
/// screens, except now with the console's authority behind it. Entries get
/// added one at a time, each read off its own component.
/// </para>
///
/// ## Permissions are described, never decided
///
/// <para>
/// A workflow names the permissions its screens are behind so that an answer
/// can say "this needs inventory.manage, which you do not hold" instead of
/// walking somebody into a refusal. That is description. Nothing in this file
/// gates anything, and the permission names stay honest about where the gate
/// actually is: atlas enforces on the read, and the console hiding a nav link
/// hides nothing. Guidance is given to somebody without the permission, because
/// knowing what a flow is and being able to finish it are different things, and
/// declining to explain it would leave them guessing as well as blocked.
/// </para>
/// </remarks>
public static class Workflows
{
    /// <summary>
    /// The nav bar, as the console builds it.
    /// </summary>
    /// <remarks>
    /// A copy of <c>SECTIONS</c> in <c>src/portaladmin/app/sections.ts</c>, in
    /// its order, with that file's permissions, and with D-Day's two children
    /// flattened in after it. Copied rather than shared because the two live in
    /// different repositories' languages, and the failure mode of drift is the
    /// mild one here: a stale label gives a slightly wrong instruction, where
    /// the console's own copy of these strings decides what anybody sees.
    /// Neither copy is the gate.
    /// </remarks>
    public static readonly IReadOnlyList<PortalScreen> Sections =
    [
        new("/", "Home", "the console's front page, which summarises the event", []),
        new("/events", "Events", "the list of events, and where a new one is set up",
            ["applications.view"]),
        new("/people", "People", "the organizer team and their roles", ["people.view"]),
        new("/forms", "Forms", "every form built for this event", ["applications.view"]),
        new("/applicants", "Applicants", "the applicants list", ["applications.view"]),
        new("/inventory", "Inventory", "what logistics has on hand, per event",
            ["inventory.manage"]),
        new("/mail", "Email Campaign", "email campaigns and how they performed",
            ["email.view_stats"]),
        new("/templates", "Templates", "the email templates",
            ["email.manage_templates", "email.delete_templates"]),
        new("/audit", "Audit", "the audit log", ["audit.view"]),
        new("/d-day", "D-Day", "the screens that only matter once doors open",
            ["applications.view", "checkin.scan", "checkin.view_stats"]),
        new("/d-day/dashboard", "Dashboard", "the day-of board, under D-Day",
            ["applications.view"]),
        new("/check-in", "Check-in", "the check-in desk, under D-Day",
            ["checkin.scan", "checkin.view_stats"]),
    ];

    /// <summary>
    /// One section by its route, so a workflow names a screen that is in the
    /// table above rather than one spelled out a second time beside it.
    /// </summary>
    private static PortalScreen Section(string route) =>
        Sections.Single(screen => screen.Route == route);

    /// <summary>
    /// The workflows, by the name the model asks for.
    /// </summary>
    /// <remarks>
    /// The keys are hyphenated and spelled out in full, the shape the readings
    /// use, because they go into the tool schema as a closed list: a model
    /// choosing between <c>add-inventory-item</c> and <c>find-a-screen</c> is
    /// choosing between two things it can read.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, Workflow> All =
        new Dictionary<string, Workflow>(StringComparer.Ordinal)
        {
            ["add-inventory-item"] = new(
                Title: "Adding an item to inventory",
                About: "Starting a new item on the shelf, counted in at however "
                       + "many are already on hand.",
                Needs: ["inventory.manage"],
                Before:
                [
                    new("The shelf is kept per event, so there has to be an event to "
                        + "keep it against. With none, Inventory shows \"No events "
                        + "yet\" and says to create one under Events.",
                        []),

                    // Carried as a prerequisite with its permission on it,
                    // rather than written in as a step, because for most of the
                    // people who will read it, it is not one. events.manage is
                    // super admin's alone, so "create an event first" is a
                    // message to pass on rather than a thing to go and do, and
                    // guidance that does not know the difference sends a
                    // logistics volunteer off to a screen that will refuse them.
                    new("Creating that event is itself behind events.manage, which "
                        + "super admin holds alone. Somebody without it has to ask "
                        + "rather than clear this prerequisite themselves.",
                        ["events.manage"]),
                ],
                Steps:
                [
                    "Open Inventory from the nav bar.",

                    "Check which event's shelf this is. The event shows under the "
                    + "heading: where there is more than one it is a picker labelled "
                    + "Event, and choosing from it reloads the screen; where there is "
                    + "only one it is shown as plain text and there is nothing to "
                    + "choose.",

                    "Choose Add item, in the heading beside the title. A dialog opens, "
                    + "also headed Add item.",

                    "Fill in Name. It is the only field the dialog insists on -- its "
                    + "Add item button stays disabled until Name has something in it.",

                    "Category, Location and Note come next and are all marked "
                    + "optional. Any of them can be left empty.",

                    "Set Starting count. It begins at 0 and takes zero or a whole "
                    + "number above it; anything else is refused with \"Starting "
                    + "count has to be zero or a whole number above it.\"",

                    "Low at, beside the count, is optional. It is the mark the item "
                    + "gets flagged at: \"Flagged as low once the count falls to this "
                    + "or below.\"",

                    "Choose Add item to save. The dialog closes and the item appears "
                    + "in the list.",
                ],
                Notes:
                [
                    // The question that opened the issue was asked of an empty
                    // shelf, and this is the sentence that answers it: nothing
                    // about the steps depends on there being anything there yet.
                    "An empty shelf changes none of this. With no items the screen "
                    + "shows \"Nothing logged yet\" where the list would be, and Add "
                    + "item is in the heading either way.",

                    "Every route under inventory asks for inventory.manage, so there "
                    + "is no read-only view of the shelf: somebody who can open the "
                    + "screen can add to it, and somebody without the permission gets "
                    + "the refusal instead of the screen.",

                    "The chosen event is in the address as an eventId parameter, so a "
                    + "link to one event's shelf can be sent to whoever is driving to "
                    + "the store.",
                ],
                Screens: [Section("/inventory"), Section("/events")]),

            ["find-a-screen"] = new(
                Title: "Finding a screen in the console",
                About: "Which section of the console holds what, and why a section "
                       + "somebody expected may not be in their nav bar.",
                Needs: [],
                Before: [],
                Steps:
                [
                    "The nav bar lists the sections. Home is always there; the rest "
                    + "appear for somebody holding a permission that makes the "
                    + "section worth opening.",

                    "Forms has a New Form shortcut under it for anybody holding "
                    + "forms.manage.",

                    "D-Day is a place rather than a screen, and holds the two that "
                    + "only matter on the day: Dashboard and Check-in.",
                ],
                Notes:
                [
                    // The invariant sections.ts is at pains to document, and the
                    // thing an answer about the nav bar can most easily get wrong.
                    // A missing tab is a courtesy, not a lock, and an answer that
                    // says otherwise describes a boundary that is not where it
                    // has just been claimed to be.
                    "A section missing from somebody's nav bar has not been locked by "
                    + "the bar. The console hides a link it knows would only refuse, "
                    + "so that a tab which can never do anything is not offered; the "
                    + "refusal itself comes from atlas when the screen tries to read, "
                    + "and typing the path in by hand reaches that screen's own \"you "
                    + "do not have ...\" page.",

                    "The permissions listed against a section are read as any-of: one "
                    + "of them is enough for the section to show.",
                ],
                Screens: Sections),
        };

    /// <summary>The workflow names, for the closed list the model picks from.</summary>
    public static IReadOnlyList<string> Keys => [.. All.Keys];

    /// <summary>
    /// The workflow of that name, or null for one this does not hold.
    /// </summary>
    /// <remarks>
    /// Null rather than a near match, for the reason <see cref="Screens"/>
    /// describes nothing it does not know: the caller turns null into "this is
    /// not written down", where a near match would turn it into steps for the
    /// wrong screen, said with exactly the confidence of the right ones.
    /// </remarks>
    public static Workflow? Of(string? key) =>
        key is not null && All.TryGetValue(key, out var found) ? found : null;
}
