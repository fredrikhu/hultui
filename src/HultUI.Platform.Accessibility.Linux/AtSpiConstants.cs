using System.Runtime.Versioning;
using HultUI.Core.Accessibility;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// AT-SPI2 interface names, well-known paths, and the role enum mapping from
/// <see cref="AccessibilityRole"/> to AT-SPI's numeric role codes.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class AtSpiConstants
{
    // Well-known bus + path for discovering the a11y bus address.
    public const string A11yBusService = "org.a11y.Bus";
    public const string A11yBusPath = "/org/a11y/bus";
    public const string A11yBusInterface = "org.a11y.Bus";

    // Registry path on the a11y bus. Applications Embed their root here.
    public const string RegistryService = "org.a11y.atspi.Registry";
    public const string RegistryRootPath = "/org/a11y/atspi/accessible/root";
    public const string SocketInterface = "org.a11y.atspi.Socket";

    // Per-object interfaces we export.
    public const string AccessibleInterface = "org.a11y.atspi.Accessible";
    public const string ComponentInterface = "org.a11y.atspi.Component";
    public const string ApplicationInterface = "org.a11y.atspi.Application";
    public const string ActionInterface = "org.a11y.atspi.Action";
    public const string ValueInterface = "org.a11y.atspi.Value";
    public const string EditableTextInterface = "org.a11y.atspi.EditableText";

    // Path prefix for exported accessible objects — we append the ElementId.
    public const string AccessiblePathPrefix = "/org/a11y/atspi/accessible/";

    // AT-SPI role numeric codes. Full list at:
    // https://docs.gtk.org/atspi2/enum.Role.html — we map only the subset we care about.
    public const uint AtSpiRole_Invalid = 0;
    public const uint AtSpiRole_PushButton = 25;
    public const uint AtSpiRole_CheckBox = 45;
    public const uint AtSpiRole_RadioButton = 44;
    public const uint AtSpiRole_Text = 40;
    public const uint AtSpiRole_Label = 29;
    public const uint AtSpiRole_Image = 23;
    public const uint AtSpiRole_ScrollPane = 32;
    public const uint AtSpiRole_Panel = 54;
    public const uint AtSpiRole_Frame = 67;
    public const uint AtSpiRole_Separator = 39;
    public const uint AtSpiRole_PageTab = 36;
    public const uint AtSpiRole_Application = 75;

    public static uint MapRole(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Button => AtSpiRole_PushButton,
        AccessibilityRole.Checkbox => AtSpiRole_CheckBox,
        AccessibilityRole.RadioButton => AtSpiRole_RadioButton,
        AccessibilityRole.TextInput => AtSpiRole_Text,
        AccessibilityRole.Label => AtSpiRole_Label,
        AccessibilityRole.Image => AtSpiRole_Image,
        AccessibilityRole.ScrollView => AtSpiRole_ScrollPane,
        AccessibilityRole.Group => AtSpiRole_Panel,
        AccessibilityRole.Tab => AtSpiRole_PageTab,
        AccessibilityRole.Separator => AtSpiRole_Separator,
        AccessibilityRole.Application => AtSpiRole_Application,
        _ => AtSpiRole_Panel,
    };

    // AT-SPI property-name strings used in PropertyChange signals.
    public const string PropAccessibleName = "accessible-name";
    public const string PropAccessibleDescription = "accessible-description";
    public const string PropAccessibleValue = "accessible-value";
    public const string PropAccessibleRole = "accessible-role";

    public static string MapProperty(string accessibilityProperty) => accessibilityProperty switch
    {
        AccessibilityProperties.Name => PropAccessibleName,
        AccessibilityProperties.Description => PropAccessibleDescription,
        AccessibilityProperties.Value => PropAccessibleValue,
        _ => "",
    };
}
