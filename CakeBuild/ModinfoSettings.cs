using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using System;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
namespace CakeBuild;

public class ModinfoSettingsRoot
{
    [JsonProperty("shared")]
    public SharedInfo Shared { get; set; }

    [JsonProperty("submods")]
    public Dictionary<string, SubmodInfo> Submods { get; set; }
}

public class SharedInfo
{
    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("side")]
    public string Side { get; set; }

    [JsonProperty("authors")]
    public List<string> Authors { get; set; }

    [JsonProperty("contributors")]
    public List<string> Contributors { get; set; }

    [JsonProperty("website")]
    public string Website { get; set; }
}

public class SubmodInfo
{
    [JsonProperty("modid")]
    public string ModId { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("version")]
    public string Version { get; set; }

    [JsonProperty("dependencies")]
    [JsonConverter(typeof(DependenciesConverter))]
    public IReadOnlyList<ModDependency> Dependencies { get; set; } = new List<ModDependency>().AsReadOnly();

}

// Converter adapted from Vintagestory.API.Common.ModInfo.DependenciesConverter
public class DependenciesConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return typeof(IEnumerable<ModDependency>).IsAssignableFrom(objectType);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        var jo = JObject.Load(reader);
        var list = jo.Properties()
            .Select(p => new ModDependency(p.Name, p.Value.Type == JTokenType.Null ? "" : (string?)p.Value))
            .ToList();
        return list.AsReadOnly();
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        foreach (ModDependency item in (IEnumerable<ModDependency>)value)
        {
            writer.WritePropertyName(item.ModID);
            writer.WriteValue(item.Version);
        }

        writer.WriteEndObject();
    }
}

// Example:
// var root = JsonConvert.DeserializeObject<ModinfosRoot>(json);
// var firstSubmod = root.Submods["biodiversityAquatic"];
