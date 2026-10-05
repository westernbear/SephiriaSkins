using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
namespace SephiriaSkins.Core;
public static class Json
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        MissingMemberHandling = MissingMemberHandling.Error,
        TypeNameHandling = TypeNameHandling.None,
        MaxDepth = 48,
        Formatting = Formatting.Indented
    };
    public static T Read<T>(string text) => JsonConvert.DeserializeObject<T>(text, Settings)
        ?? throw new InvalidDataException("JSON document is null.");
    public static string Write(object value) => JsonConvert.SerializeObject(value, Settings);
}
