using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ValheimGuildTelemetry;

public static class JsonCodec
{
    public static string Write(ProgressState state)
    {
        using (var stream=new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(ProgressState)).WriteObject(stream,state);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
    public static ProgressState Read(string json)
    {
        using (var stream=new MemoryStream(Encoding.UTF8.GetBytes(json)))
            return (ProgressState)new DataContractJsonSerializer(typeof(ProgressState)).ReadObject(stream);
    }
}
