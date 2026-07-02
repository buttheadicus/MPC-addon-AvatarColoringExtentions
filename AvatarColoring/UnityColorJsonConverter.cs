using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MultiplayerChat.AvatarColoring;

// UnityEngine.Color exposes .linear (and related) in a way Newtonsoft treats as a self-referencing graph on BS 1.40+.
// Snapshot AvatarData with r/g/b/a only so draft serialize/deserialize succeeds.
internal sealed class UnityColorJsonConverter : JsonConverter<Color>
{
    public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("r");
        writer.WriteValue(value.r);
        writer.WritePropertyName("g");
        writer.WriteValue(value.g);
        writer.WritePropertyName("b");
        writer.WriteValue(value.b);
        writer.WritePropertyName("a");
        writer.WriteValue(value.a);
        writer.WriteEndObject();
    }

    public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        var jo = JObject.Load(reader);
        return new Color(
            jo.Value<float?>("r") ?? 0f,
            jo.Value<float?>("g") ?? 0f,
            jo.Value<float?>("b") ?? 0f,
            jo.Value<float?>("a") ?? 1f);
    }
}
