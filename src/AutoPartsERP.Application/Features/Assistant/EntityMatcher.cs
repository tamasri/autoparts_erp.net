using Dapper;

namespace AutoPartsERP.Application.Features.Assistant;

public sealed record MatchCandidate(Guid Id, string Label, string? Detail, double Score);

public enum MatchKind
{
    Found,
    Ambiguous,
    NotFound
}

public sealed record MatchOutcome(MatchKind Kind, MatchCandidate? Match, IReadOnlyList<MatchCandidate> Candidates);

/// <summary>
/// Finds the customer or item a message refers to, tolerating Arabic spelling variations and typos. Both the stored names and the
/// searched text go through the database function <c>ar_norm</c> (hamza forms, ة/ه, ى/ي, tashkeel, digits, punctuation), then
/// pg_trgm scores them: <c>similarity</c> for the whole name and <c>word_similarity</c> for a name that is part of a longer one.
/// The assistant only acts on a clear winner; anything else is put to the user as a numbered choice (<see cref="Decide"/>).
/// </summary>
public static class EntityMatcher
{
    /// <summary>Taken without asking: at least this similar…</summary>
    public const double AcceptScore = 0.75;

    /// <summary>…and at least this far ahead of the next candidate (so "حسن" does not silently become "حسين").</summary>
    public const double AcceptLead = 0.15;

    /// <summary>Below this nothing is offered ("not found").</summary>
    public const double MinScore = 0.3;

    public const int MaxChoices = 5;

    public static MatchOutcome Decide(IReadOnlyList<MatchCandidate> candidates)
    {
        var ranked = candidates.Where(c => c.Score >= MinScore).OrderByDescending(c => c.Score).ThenBy(c => c.Label, StringComparer.Ordinal).ToList();
        if (ranked.Count == 0)
        {
            return new MatchOutcome(MatchKind.NotFound, null, []);
        }

        var top = ranked[0];
        var lead = ranked.Count > 1 ? top.Score - ranked[1].Score : 1;
        if (top.Score >= AcceptScore && lead >= AcceptLead)
        {
            return new MatchOutcome(MatchKind.Found, top, [top]);
        }

        // Offer the ones that are still in the running, best first.
        var choices = ranked.Where(c => c.Score >= top.Score - 0.3).Take(MaxChoices).ToList();
        return new MatchOutcome(MatchKind.Ambiguous, null, choices);
    }

    public static async Task<MatchOutcome> FindCustomerAsync(IDbConnection connection, string text, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<MatchCandidate>(new CommandDefinition(
            """
            WITH q AS (SELECT ar_norm(@text) AS t)
            SELECT c.id AS Id, c.name AS Label, c.code AS Detail,
                   GREATEST(similarity(ar_norm(c.name), q.t), word_similarity(q.t, ar_norm(c.name)),
                            CASE WHEN lower(c.code) = lower(@text) THEN 1 ELSE 0 END)::float8 AS Score
            FROM customers c, q
            WHERE c.is_active AND q.t <> ''
              AND (ar_norm(c.name) % q.t OR q.t <% ar_norm(c.name) OR lower(c.code) = lower(@text))
            ORDER BY Score DESC, c.name
            LIMIT 10;
            """,
            new { text }, cancellationToken: cancellationToken));
        return Decide(rows.ToList());
    }

    public static async Task<MatchOutcome> FindItemAsync(IDbConnection connection, string text, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<MatchCandidate>(new CommandDefinition(
            """
            WITH q AS (SELECT ar_norm(@text) AS t)
            SELECT s.id AS Id, COALESCE(NULLIF(s.name_ar, ''), s.name) AS Label, s.code AS Detail,
                   GREATEST(similarity(ar_norm(s.name), q.t), similarity(ar_norm(s.name_ar), q.t),
                            word_similarity(q.t, ar_norm(s.name)), word_similarity(q.t, ar_norm(s.name_ar)),
                            CASE WHEN ar_norm(s.code) = q.t OR s.barcode = @text THEN 1 ELSE 0 END)::float8 AS Score
            FROM skus s, q
            WHERE s.is_active AND q.t <> ''
              AND (ar_norm(s.name) % q.t OR ar_norm(s.name_ar) % q.t OR q.t <% ar_norm(s.name) OR q.t <% ar_norm(s.name_ar)
                   OR ar_norm(s.code) = q.t OR s.barcode = @text)
            ORDER BY Score DESC, s.code
            LIMIT 10;
            """,
            new { text }, cancellationToken: cancellationToken));
        return Decide(rows.ToList());
    }
}
