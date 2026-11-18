using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Tests that change <see cref="HultUI.Core.Events.KeyChord.SuperIsCtrl"/>, a process-wide
/// switch, run alone so no other test sees the other platform's rule.
/// </summary>
[CollectionDefinition("KeyboardPlatform", DisableParallelization = true)]
public class KeyboardPlatformCollection;
