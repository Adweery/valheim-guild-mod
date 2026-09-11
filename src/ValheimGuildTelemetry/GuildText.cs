using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValheimGuildTelemetry;

// Translate display text only. Quest IDs, database values and character names stay intact.
[Serializable] public class GuildTranslation { public string sk, en; }

public static class GuildText
{
    private static readonly Dictionary<string,string> English=new Dictionary<string,string>(StringComparer.Ordinal);
    private static readonly Dictionary<string,string> Slovak=new Dictionary<string,string>(StringComparer.Ordinal);
    static GuildText()
    {
        using(var stream=typeof(GuildText).Assembly.GetManifestResourceStream("ValheimGuildTelemetry.translations.json"))
        using(var reader=new StreamReader(stream,Encoding.UTF8))
        {
            foreach(var entry in QuestJson.Read<GuildTranslation[]>(reader.ReadToEnd()))
            {
                if(string.IsNullOrEmpty(entry.sk) || entry.en==null) throw new InvalidDataException("Invalid guild translation entry");
                English.Add(entry.sk,entry.en);
                if(entry.en.Length>0) Slovak.Add(entry.en,entry.sk);
            }
        }
    }
    public static bool IsSlovak(string language)
    {
        var value=(language ?? "").Trim();
        return value.Equals("Slovak",StringComparison.OrdinalIgnoreCase)
            || value.Equals("Slovenčina",StringComparison.OrdinalIgnoreCase)
            || value.Equals("sk",StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("sk-",StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("sk_",StringComparison.OrdinalIgnoreCase);
    }
    public static string Translate(string text,string language)
    {
        if(text==null) return null;
        bool sk=IsSlovak(language);
        var table=sk ? Slovak : English;
        if(table.TryGetValue(text,out var translated)) return translated;
        // Legacy cached chest labels and queued completion titles contain dynamic suffixes.
        var chest=sk ? "Chest (" : "Truhla (";
        if(text.StartsWith(chest,StringComparison.Ordinal)) return (sk ? "Truhla (" : "Chest (")+text.Substring(chest.Length);
        int reward=text.LastIndexOf("  •  +",StringComparison.Ordinal);
        if(reward>0) return Translate(text.Substring(0,reward),language)+text.Substring(reward);
        // Custom administrator-authored text has no implied translation.
        return text;
    }
}
