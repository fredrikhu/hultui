using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;

namespace Aperture.Core;

/// <summary>
/// The one place the official SDK needs help: <b>Jellyfin writes ids without dashes</b>.
///
/// <para>A server sends <c>"Id":"1e2c7967a01c49188f978bcf30aa8448"</c> — the <c>N</c> format
/// — while the generated models type those fields as <see cref="Guid"/>, and
/// <c>System.Text.Json</c> reads only the dashed <c>D</c> form. The two Kiota generations
/// fail differently and both are fatal: 1.x throws <c>JsonException</c> on the first user
/// the client looks at, and 2.x quietly yields <see cref="Guid.Empty"/> — which is worse,
/// because every later request then asks the server about the nil user and gets a polite,
/// empty answer.</para>
///
/// <para>The fix is a converter that accepts either shape, registered on the serialization
/// context Kiota is built with. Nothing else in the client has to know: the parse node and
/// writer factories here are handed to the request adapter, and the generated code goes on
/// believing in <see cref="Guid"/>.</para>
/// </summary>
internal static class JellyfinSerialization
{
    /// <summary>Reads <c>N</c> or <c>D</c>; writes <c>N</c>, which is what the server's own
    /// URLs and payloads use.</summary>
    private sealed class JellyfinGuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // Guid.TryParse takes N, D, B, P and X. A value that is none of them is not an
            // id at all, and Guid.Empty is the honest answer — the alternative is throwing
            // away a whole page of items over one malformed field.
            var text = reader.GetString();
            return Guid.TryParse(text, out var value) ? value : Guid.Empty;
        }

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("N"));
    }

    private static readonly KiotaJsonSerializationContext Context = CreateContext();

    private static KiotaJsonSerializationContext CreateContext()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JellyfinGuidConverter());
        return new KiotaJsonSerializationContext(options);
    }

    /// <summary>The JSON reader every Aperture connection uses.</summary>
    public static IParseNodeFactory ParseNodeFactory { get; } = new JsonParseNodeFactory(Context);

    /// <summary>The JSON writer, so ids we send back match the ones we were given.</summary>
    public static ISerializationWriterFactory SerializationWriterFactory { get; } =
        new JsonSerializationWriterFactory(Context);
}
