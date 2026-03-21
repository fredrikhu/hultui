using Xunit;

namespace HultUI.Core.Tests.Reactivity;

/// <summary>
/// Ensures all reactive tests run sequentially since they share static state
/// in ReactiveRuntime.
/// </summary>
[CollectionDefinition("Reactive")]
public class ReactiveTestCollection;
