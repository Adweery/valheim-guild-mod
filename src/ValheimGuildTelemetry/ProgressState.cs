using System;
using System.Collections.Generic;
using System.Linq;

namespace ValheimGuildTelemetry;

[Serializable] public class Counter { public string kind; public string key; public long count; }
[Serializable] public class Session { public string id; public long sequence; }
[Serializable] public class PlayerProgress
{
    public string id;
    public string name;
    public long last_seen;
    public List<Counter> counters = new List<Counter>();
    public List<Session> sessions = new List<Session>();
}
[Serializable] public class ProgressState
{
    public int schema = 1;
    public string world;
    public string uid;
    public long updated_at;
    public List<PlayerProgress> players = new List<PlayerProgress>();

    public bool Apply(string playerId, string playerName, string sessionId, long sequence,
                      string kind, string key, int amount, long now)
    {
        if (string.IsNullOrEmpty(playerId) || playerId.Length > 100 ||
            !Guid.TryParseExact(sessionId,"N",out _) || sequence < 1 ||
            !new[]{"hello","kill","craft","biome"}.Contains(kind) ||
            key == null || key.Length > 100 || amount < 0 || amount > 5000 ||
            (kind != "hello" && (amount < 1 || key.Length == 0))) return false;
        var p = players.Find(x => x.id == playerId);
        if (p == null)
        {
            if (players.Count >= 100) return false;
            p = new PlayerProgress { id=playerId };
            players.Add(p);
        }
        var session = p.sessions.Find(x => x.id == sessionId);
        if (session != null && sequence <= session.sequence) return false;
        if (session == null)
        {
            if (p.sessions.Count >= 128) p.sessions.RemoveAt(0);
            session = new Session { id=sessionId };
            p.sessions.Add(session);
        }
        session.sequence=sequence;
        p.name=(playerName ?? "Unknown").Substring(0,Math.Min(80,(playerName ?? "Unknown").Length));
        p.last_seen=now;
        if (kind != "hello")
        {
            var counter=p.counters.Find(x => x.kind == kind && x.key == key);
            if (counter == null)
            {
                if (p.counters.Count >= 2048) return false;
                counter=new Counter {kind=kind,key=key};
                p.counters.Add(counter);
            }
            counter.count=kind == "biome" ? 1 : checked(counter.count+amount);
        }
        updated_at=now;
        return true;
    }
}
