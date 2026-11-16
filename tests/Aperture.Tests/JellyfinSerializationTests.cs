using System.Text;
using Aperture.Core;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// Jellyfin writes ids without dashes (the <c>N</c> format) and the generated models type them
/// as <see cref="Guid"/>. These read a payload the way a live server sends it, through the
/// factories every connection is built with (<see cref="JellyfinSerialization"/>), so a Kiota or
/// Jellyfin.Sdk upgrade that loses ids fails here instead of showing up as a library of nil ids.
/// </summary>
public class JellyfinSerializationTests
{
    private static readonly Guid ItemId = Guid.ParseExact("1e2c7967a01c49188f978bcf30aa8448", "N");
    private static readonly Guid ParentId = Guid.ParseExact("f137a2dd21bbc1b99aa5c0f6bf02a805", "N");

    private static readonly string Item =
        $$"""{"Name":"Arrival","Id":"{{ItemId:N}}","ParentId":"{{ParentId:N}}","Type":"Movie"}""";

    private static async Task<BaseItemDto?> Read(IParseNodeFactory factory, string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var node = await factory.GetRootParseNodeAsync("application/json", stream);
        return node.GetObjectValue(BaseItemDto.CreateFromDiscriminatorValue);
    }

    [Fact]
    public async Task DashlessIds_AreReadAsTheIdsTheServerMeant()
    {
        var item = await Read(JellyfinSerialization.ParseNodeFactory, Item);

        Assert.NotNull(item);
        Assert.Equal("Arrival", item.Name);
        Assert.Equal(ItemId, item.Id);
        Assert.Equal(ParentId, item.ParentId);
    }

    [Fact]
    public async Task DashedIds_AreReadToo()
    {
        var item = await Read(JellyfinSerialization.ParseNodeFactory,
            $$"""{"Name":"Arrival","Id":"{{ItemId:D}}"}""");

        Assert.Equal(ItemId, item?.Id);
    }

    /// <summary>The control: Kiota's own JSON factory, without the converter, does not get the
    /// id (1.x threw, 2.x has been seen to read <see cref="Guid.Empty"/>). If this starts to
    /// fail, Kiota or System.Text.Json reads the <c>N</c> format by itself now, and the
    /// converter in <see cref="JellyfinSerialization"/> may no longer be needed.</summary>
    [Fact]
    public async Task WithoutTheConverter_KiotaDoesNotGetTheId()
    {
        Guid? id;
        try
        {
            id = (await Read(new JsonParseNodeFactory(), Item))?.Id;
        }
        catch (Exception)
        {
            // Either failure counts: what matters is that the id is not there.
            id = null;
        }

        Assert.NotEqual(ItemId, id);
    }

    [Fact]
    public async Task AnIdWrittenBack_IsReadBackAsTheSameId()
    {
        using var writer = JellyfinSerialization.SerializationWriterFactory.GetSerializationWriter("application/json");
        writer.WriteObjectValue(null, new BaseItemDto { Id = ItemId, Name = "Arrival" });
        using var content = writer.GetSerializedContent();
        var json = new StreamReader(content).ReadToEnd();

        var item = await Read(JellyfinSerialization.ParseNodeFactory, json);
        Assert.Equal(ItemId, item?.Id);
    }
}
