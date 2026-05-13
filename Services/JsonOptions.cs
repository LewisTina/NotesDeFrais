using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotesDeFrais.Services;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
