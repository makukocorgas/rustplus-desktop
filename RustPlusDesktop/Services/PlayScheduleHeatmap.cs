// PlayScheduleHeatmap.cs
// Backend port of RustPlayerTrack's "Play Schedule" heatmap aggregation.
// Self-contained — no dependencies beyond .NET.
//
// What it does:
//   Takes a list of play sessions (start/end timestamps) and produces a
//   7-day × 24-hour grid where each cell holds the AVERAGE minutes played
//   in that (day-of-week, hour) slot per week over the selected window.
//   That average drives a 6-level intensity scale (0 = empty … 5 = hottest)
//   for rendering a GitHub-style heatmap.
//
// Algorithm:
//   1. Window filter uses INTERVAL OVERLAP, not just "started within" —
//      a session starting 11 PM the day before the window that runs 2h
//      into it still counts for those 2 hours.
//   2. Each session is clamped to 24h max (abandoned/never-closed sessions
//      would otherwise skew a whole row).
//   3. The session is walked hour-slot by hour-slot; each slot gets the
//      exact minutes the session overlapped it (a 19:45–21:10 session adds
//      15 min to the 19:00 cell, 60 to 20:00, 10 to 21:00).
//   4. Cell totals are divided by the number of weeks in the window so a
//      30d and a 7d view are comparable ("avg per week").
//   5. Intensity = cell / maxCell bucketed at 15% / 35% / 55% / 75%.

using System;
using System.Collections.Generic;

namespace RustPlusDesk.Services;

/// <summary>A single play session. End == null means "still online" (now).</summary>
public sealed record PlaySession(DateTimeOffset Start, DateTimeOffset? End);

public sealed class HeatmapCell
{
    public int Day { get; init; }                 // 0 = Sunday … 6 = Saturday
    public int Hour { get; init; }                // 0–23
    public double TotalMinutes { get; set; }      // raw minutes across the window
    public int SessionCount { get; set; }         // sessions touching this slot
    public double AverageMinutes { get; set; }    // TotalMinutes / weeksInWindow
    public int IntensityLevel { get; set; }       // 0 (empty) … 5 (hottest)
}

public sealed class HeatmapResult
{
    /// <summary>[day 0–6][hour 0–23]</summary>
    public required HeatmapCell[][] Cells { get; init; }
    public double MaxAverageMinutes { get; init; }
    public int WeeksInWindow { get; init; }
    public int SessionsInWindow { get; init; }

    /// <summary>Inclusive window bounds in the viewer's timezone. Null for "all time".</summary>
    public DateTimeOffset? WindowStart { get; init; }
    public DateTimeOffset? WindowEnd { get; init; }

    /// <summary>True when older data exists before WindowStart (i.e. a "previous" navigation is possible).</summary>
    public bool HasOlderData { get; init; }
}

public static class PlayScheduleHeatmap
{
    /// <summary>
    /// Build the 7×24 heatmap for the given sessions.
    /// </summary>
    /// <param name="sessions">All known sessions for the player (any order).</param>
    /// <param name="windowDays">7 / 30 / 90, or null for "all time".</param>
    /// <param name="offset">0 = most recent window, 1 = the one before, …</param>
    /// <param name="tz">Timezone the day/hour buckets are anchored to
    /// (pass the VIEWER's timezone — bucketing in UTC shifts everyone's
    /// evenings onto the wrong row). Defaults to the server's local zone.</param>
    /// <param name="now">Injectable clock for tests.</param>
    public static HeatmapResult Build(
        IReadOnlyList<PlaySession> sessions,
        int? windowDays = 30,
        int offset = 0,
        TimeZoneInfo? tz = null,
        DateTimeOffset? now = null)
    {
        tz ??= TimeZoneInfo.Local;
        var nowTz = TimeZoneInfo.ConvertTime(now ?? DateTimeOffset.UtcNow, tz);

        // ── 1. Window bounds (inclusive), in the viewer's timezone ─────────
        DateTimeOffset? windowStart = null, windowEnd = null;
        int weeksInWindow = 1;
        if (windowDays is int days && days > 0)
        {
            if (days == 7)
            {
                // Calendar-week alignment: Monday..Sunday, matching a real calendar
                // week rather than a rolling "last 7 days from today" lookback —
                // that rolling window desynced the weekday row from its date label
                // whenever "today" wasn't a Sunday.
                int dow = (int)nowTz.DayOfWeek; // Sunday = 0 … Saturday = 6
                int daysSinceMonday = dow == 0 ? 6 : dow - 1;
                var mondayThisWeek = nowTz.Date.AddDays(-daysSinceMonday);
                var weekStartDate = mondayThisWeek.AddDays(-7 * offset);
                windowStart = new DateTimeOffset(weekStartDate, nowTz.Offset);
                windowEnd = new DateTimeOffset(weekStartDate.AddDays(7).AddMilliseconds(-1), nowTz.Offset);
                weeksInWindow = 1;
            }
            else
            {
                // End = today 23:59:59.999, shifted back by offset*days.
                var endDate = nowTz.Date.AddDays(-(offset * days));
                windowEnd = new DateTimeOffset(endDate.AddDays(1).AddMilliseconds(-1), nowTz.Offset);
                windowStart = new DateTimeOffset(endDate.AddDays(-days + 1), nowTz.Offset);
                weeksInWindow = Math.Max(1, (int)Math.Ceiling(days / 7.0));
            }
        }

        // ── 2. Filter by interval overlap ──────────────────────────────────
        var filtered = new List<PlaySession>();
        foreach (var s in sessions)
        {
            var end = s.End ?? nowTz;
            if (windowStart is null ||
                (s.Start <= windowEnd && end >= windowStart))
            {
                filtered.Add(s);
            }
        }

        // "All time": average over the span from the earliest session to now.
        if (windowDays is null && filtered.Count > 0)
        {
            var earliest = DateTimeOffset.MaxValue;
            foreach (var s in filtered)
                if (s.Start < earliest) earliest = s.Start;
            var spanDays = (nowTz - earliest).TotalDays;
            weeksInWindow = Math.Max(1, (int)Math.Ceiling(spanDays / 7.0));
        }

        // ── 3. Init 7×24 grid ──────────────────────────────────────────────
        var grid = new HeatmapCell[7][];
        for (var d = 0; d < 7; d++)
        {
            grid[d] = new HeatmapCell[24];
            for (var h = 0; h < 24; h++)
                grid[d][h] = new HeatmapCell { Day = d, Hour = h };
        }

        // ── 4. Distribute each session's minutes across its hour slots ────
        foreach (var s in filtered)
        {
            var start = TimeZoneInfo.ConvertTime(s.Start, tz);
            var end = TimeZoneInfo.ConvertTime(s.End ?? nowTz, tz);

            // The filter above only checks OVERLAP with the window — a session that
            // starts before windowStart (e.g. last week) or ends after windowEnd still
            // passes it as long as some part of it touches this window. Without
            // clamping here, the hour-walk below would distribute its out-of-window
            // hours into day-of-week cells too, corrupting THIS window's grid with data
            // from a different occurrence of that weekday (day-of-week alone can't tell
            // "this Monday" from "last Monday" apart).
            if (windowStart is DateTimeOffset wStart && start < wStart) start = wStart;
            if (windowEnd is DateTimeOffset wEnd && end > wEnd) end = wEnd;
            if (end <= start) continue;

            // NOTE: there used to be a "clamp runaway sessions at 24h" step here
            // (maxEnd = start.AddHours(24)). That's wrong for a genuinely long,
            // continuous multi-day session (e.g. a real 95h session, which does
            // happen and is reported as-is elsewhere in the same UI as "Longest
            // Session"): applying the 24h cap AFTER clamping `start` up to
            // windowStart re-bases the cutoff to "windowStart + 24h" (e.g. Tuesday
            // 00:00), truncating every later day in the window even though the
            // session's real activity continues through it — the exact bug that
            // made only the window's first day ever show data. The window clamp
            // above already bounds the walk to at most the window's span, so no
            // extra cap is needed; the hour-walk below correctly distributes a
            // multi-day session across every real day/hour it touches.

            // Cursor floored to the top of the start hour.
            var cursor = new DateTimeOffset(
                start.Year, start.Month, start.Day, start.Hour, 0, 0, start.Offset);

            while (cursor < end)
            {
                var slotStart = cursor > start ? cursor : start;
                var slotEndCandidate = cursor.AddHours(1);
                var slotEnd = slotEndCandidate < end ? slotEndCandidate : end;
                var minutes = (slotEnd - slotStart).TotalMinutes;

                if (minutes > 0)
                {
                    var day = (int)slotStart.DayOfWeek;   // Sunday == 0, matches JS
                    var hour = cursor.Hour;
                    grid[day][hour].TotalMinutes += minutes;
                    grid[day][hour].SessionCount += 1;
                }
                cursor = cursor.AddHours(1);
            }
        }

        // ── 5. Weekly averages + max ───────────────────────────────────────
        double max = 0;
        foreach (var row in grid)
            foreach (var cell in row)
            {
                cell.AverageMinutes = cell.TotalMinutes / weeksInWindow;
                if (cell.AverageMinutes > max) max = cell.AverageMinutes;
            }

        // ── 6. Intensity levels (same thresholds as the web UI) ───────────
        foreach (var row in grid)
            foreach (var cell in row)
                cell.IntensityLevel = Intensity(cell.AverageMinutes, max);

        // Is there any session that starts before this window, so "previous" navigation
        // has somewhere to go? For "all time" there's never an older window.
        bool hasOlderData = windowStart is DateTimeOffset ws && sessions.Count > 0 &&
            SessionStartsBefore(sessions, ws);

        return new HeatmapResult
        {
            Cells = grid,
            MaxAverageMinutes = max,
            WeeksInWindow = weeksInWindow,
            SessionsInWindow = filtered.Count,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            HasOlderData = hasOlderData,
        };
    }

    private static bool SessionStartsBefore(IReadOnlyList<PlaySession> sessions, DateTimeOffset cutoff)
    {
        foreach (var s in sessions)
            if (s.Start < cutoff) return true;
        return false;
    }

    /// <summary>0 = empty, then 5 buckets at &lt;15% / &lt;35% / &lt;55% / &lt;75% / rest of max.</summary>
    public static int Intensity(double avg, double max)
    {
        if (avg <= 0 || max <= 0) return 0;
        var r = avg / max;
        return r < 0.15 ? 1
             : r < 0.35 ? 2
             : r < 0.55 ? 3
             : r < 0.75 ? 4
             : 5;
    }
}
