using System;
namespace ValheimGuildTelemetry;

public enum JournalAction { None, Open, Close }
public static class JournalInputPolicy
{
    public static JournalAction Decide(string key,string configured,bool open,bool blocked,bool editing,bool modified)
    {
        if(blocked || modified) return JournalAction.None;
        if(open && key=="Escape") return JournalAction.Close;
        if(key=="F8" || (!editing && string.Equals(key,configured,StringComparison.OrdinalIgnoreCase)))
            return open ? JournalAction.Close : JournalAction.Open;
        return JournalAction.None;
    }
}
