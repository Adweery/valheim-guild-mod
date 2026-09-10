using System;
using System.Collections.Generic;
using System.Linq;
namespace ValheimGuildTelemetry;

public static class QuestTracker
{
    // Chapter II starts at the verified Elder milestone, not at unconfirmed manual chores.
    public static List<int> Select(QuestSnapshot snapshot)
    {
        if(snapshot?.quests==null) return new List<int>();
        bool swamp=snapshot.quests.Any(q=>q.completed && q.chapter.StartsWith("Chapter II",StringComparison.Ordinal));
        string chapter=swamp ? "Chapter II" : "Chapter I -";
        return snapshot.quests.Where(q=>!q.completed)
            .OrderBy(q=>q.automatic && q.current>0 ? 0 : q.chapter.StartsWith(chapter,StringComparison.Ordinal) ? 1 : q.chapter.StartsWith("Bonus",StringComparison.Ordinal) ? 3 : 2)
            .ThenBy(q=>q.id).Take(5).Select(q=>q.id).ToList();
    }
}
