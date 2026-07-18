using Xunit;

namespace HultUI.Core.Tests.Animation;

/// <summary>
/// Serializes tests that share <c>AnimationManager</c>'s static tween lists.
/// Without this definition the <c>[Collection("Animation")]</c> attribute on
/// the test classes had no effect — xunit treats an undefined collection name
/// as an implicit empty definition, which still groups the classes but was
/// never declared anywhere.
/// </summary>
[CollectionDefinition("Animation")]
public class AnimationTestCollection
{
}
