using MorganHacks.Applications.Assistant;
using MorganHacks.Applications.Forms;
using MorganHacks.Applications.Services;
using MorganHacks.Lark.Data.Data;

namespace MorganHacks.Api;

/// <summary>
/// The reads the assistant needs that live outside its own assembly.
/// </summary>
/// <remarks>
/// <c>MorganHacks.Applications</c> references no other project, which is what
/// keeps the tool layer from quietly growing reach. These three adapters are
/// the seam: each implements a port declared over there against a store that
/// lives over here, so the dependency points inwards and the tool layer still
/// cannot reach anything it was not handed.
/// </remarks>
public sealed class EmailReachAdapter(EmailAnalyticsStore analytics) : IEmailReachRead
{
    public async Task<EmailReach?> ReadAsync(CancellationToken ct = default)
    {
        var read = await analytics.ReadAsync(ct);

        // Nothing sent is nothing to report, rather than a row of zeroes. A
        // zero open rate before the first send reads as a failing campaign.
        if (read.SentEmails == 0)
        {
            return null;
        }

        return new EmailReach(
            Sent: read.SentEmails,
            Tracked: read.TrackedEmails,
            Clicked: read.ClickedEmails,
            TotalClicks: read.TotalClicks,

            // The overall read counts click tracking rather than opens, so
            // these are the honest nulls-as-zero: opens are per campaign and
            // this figure is the event's whole mail. Said as zero rather than
            // invented, and the model is told what the numbers are.
            OpenTracked: 0,
            Opened: 0,
            AutomatedEvents: 0);
    }
}

/// <summary>
/// Every question on the application form, and what people did with it.
/// </summary>
/// <remarks>
/// Counted here rather than in SQL because the answers are a JSONB document
/// whose keys change between form versions, and the mapping from a version's
/// key to a question already exists in <see cref="FormQuestions"/>. Doing it
/// in the database would mean a second copy of that mapping in a language
/// that cannot share it.
/// </remarks>
public sealed class FormUptakeAdapter(IFormStore forms, IResponseStore responses)
    : IFormUptakeRead
{
    /// <summary>
    /// How many responses to read.
    /// </summary>
    /// <remarks>
    /// A ceiling rather than the whole table. A skip rate over a thousand
    /// responses is the same skip rate as over five thousand, and the question
    /// this answers -- which questions do people not answer -- does not get
    /// a better answer from reading every row of a cohort that is still open.
    /// </remarks>
    private const int Read = 1000;

    public async Task<IReadOnlyList<QuestionUptake>?> ReadAsync(
        Guid eventId, CancellationToken ct = default)
    {
        var form = (await forms.ForEventAsync(eventId, ct))
            .FirstOrDefault(f => f.IsApplication);

        if (form is null)
        {
            return null;
        }

        // Every version, not the published one, for the reason the applicant
        // detail screen reads them all: a question rebuilt under a new key
        // would file half the cohort's answers under a heading nobody saw.
        var questions = FormQuestions.From(await forms.HistoryAsync(form.Id, ct));
        var page = await responses.PageAsync(eventId, questions, null, Read, ct);

        if (page.Items.Count == 0)
        {
            return [];
        }

        var uptake = new List<QuestionUptake>();

        foreach (var field in questions.Published)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var answered = 0;
            var skipped = 0;

            foreach (var response in page.Items)
            {
                if (!response.Answers.TryGetValue(field.Key, out var value)
                    || Blank(value))
                {
                    skipped++;
                    continue;
                }

                answered++;

                // Only the answers that come from a fixed list are counted
                // out. Free text is counted but never quoted: a distribution
                // of what people typed is a list of what people typed, and
                // some of them wrote their name in it.
                if (field.Type is FieldType.Select or FieldType.Radio or FieldType.Checkboxes)
                {
                    foreach (var one in Chosen(value))
                    {
                        counts[one] = counts.GetValueOrDefault(one) + 1;
                    }
                }
            }

            var total = answered + skipped;

            uptake.Add(new QuestionUptake(
                field.Label ?? field.Key,
                answered,
                skipped,
                counts
                    .OrderByDescending(pair => pair.Value)
                    .Select(pair => new AnalysisRow(
                        pair.Key,
                        pair.Value,
                        total == 0 ? 0 : (double)pair.Value / total))
                    .ToArray()));
        }

        return uptake;
    }

    private static bool Blank(System.Text.Json.JsonElement value) =>
        value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Null => true,
            System.Text.Json.JsonValueKind.Undefined => true,
            System.Text.Json.JsonValueKind.String =>
                string.IsNullOrWhiteSpace(value.GetString()),
            System.Text.Json.JsonValueKind.Array => value.GetArrayLength() == 0,
            _ => false,
        };

    private static IEnumerable<string> Chosen(System.Text.Json.JsonElement value)
    {
        if (value.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var one in value.EnumerateArray())
            {
                var text = one.ValueKind == System.Text.Json.JsonValueKind.String
                    ? one.GetString()
                    : one.ToString();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }
            }

            yield break;
        }

        var single = value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : value.ToString();

        if (!string.IsNullOrWhiteSpace(single))
        {
            yield return single;
        }
    }
}

/// <summary>
/// What has left the shelf, and how fast.
/// </summary>
/// <remarks>
/// The ledger exists precisely so this question is answerable -- the migration
/// that made it says the question asked at one in the morning is "there were
/// two hundred, where did the other hundred and sixty go". This is that
/// question with a rate attached.
/// </remarks>
public sealed class InventoryBurnAdapter(IInventoryStore inventory) : IInventoryBurnRead
{
    public async Task<IReadOnlyList<Burn>> ReadAsync(
        Guid eventId, CancellationToken ct = default)
    {
        var items = await inventory.ListAsync(eventId, ct);
        var burn = new List<Burn>();

        foreach (var item in items)
        {
            var movements = await inventory.MovementsAsync(item.Id, ct);

            if (movements is null || movements.Count == 0)
            {
                burn.Add(new Burn(item.Name, item.Count, 0, 0, null, null));
                continue;
            }

            // Out and in counted apart. Netting them would make a shelf that
            // was emptied and refilled look untouched, which is the opposite
            // of what somebody deciding whether to reorder needs to know.
            var issued = movements.Where(m => m.Delta < 0).Sum(m => -m.Delta);
            var restocked = movements.Where(m => m.Delta > 0).Sum(m => m.Delta);

            var first = movements.Min(m => m.OccurredAt);
            var elapsed = (DateTimeOffset.UtcNow - first).TotalDays;

            // A rate needs time to have passed. Everything issued in the last
            // hour divided by a fraction of a day is a number in the
            // thousands, and it would be reported as a forecast.
            double? perDay = elapsed >= 1 && issued > 0 ? issued / elapsed : null;

            burn.Add(new Burn(
                item.Name,
                item.Count,
                issued,
                restocked,
                perDay,
                perDay is > 0 ? (int)Math.Floor(item.Count / perDay.Value) : null));
        }

        return burn;
    }
}
