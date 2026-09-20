using System.Runtime.Versioning;
using HultUI.Core.Accessibility;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// AT-SPI2 interface names, well-known paths, numeric enum mappings, and the
/// state bitfield helpers.
/// </summary>
/// <remarks>
/// The numeric role codes are the ABI-stable values from
/// <c>atspi-constants.h</c> (<c>AtspiRole</c>) — verified against a live GTK
/// application on the a11y bus. An assistive technology reads a role number and
/// maps it back to a name, so these must match the toolkit-wide values exactly.
/// </remarks>
[SupportedOSPlatform("linux")]
internal static class AtSpiConstants
{
    // ── Session-bus discovery of the a11y bus address.
    public const string A11yBusService = "org.a11y.Bus";
    public const string A11yBusPath = "/org/a11y/bus";
    public const string A11yBusInterface = "org.a11y.Bus";

    // ── Registry on the a11y bus. Applications Embed their root here.
    public const string RegistryService = "org.a11y.atspi.Registry";
    public const string RegistryRootPath = "/org/a11y/atspi/accessible/root";
    public const string SocketInterface = "org.a11y.atspi.Socket";

    // ── Per-object interfaces we export.
    public const string AccessibleInterface = "org.a11y.atspi.Accessible";
    public const string ComponentInterface = "org.a11y.atspi.Component";
    public const string ApplicationInterface = "org.a11y.atspi.Application";
    public const string ActionInterface = "org.a11y.atspi.Action";
    public const string ValueInterface = "org.a11y.atspi.Value";
    public const string CacheInterface = "org.a11y.atspi.Cache";

    // ── Standard freedesktop interfaces every object answers.
    public const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    public const string IntrospectableInterface = "org.freedesktop.DBus.Introspectable";
    public const string PeerInterface = "org.freedesktop.DBus.Peer";

    // ── Event interfaces (signals).
    public const string EventObjectInterface = "org.a11y.atspi.Event.Object";

    // ── Object-path scheme. The root accessible is a well-known path; every
    // other node is addressed by its ElementId.
    public const string AccessiblePathPrefix = "/org/a11y/atspi/accessible/";
    public const string RootPath = "/org/a11y/atspi/accessible/root";
    public const string AccessibleNamespace = "/org/a11y/atspi/accessible";
    public const string CachePath = "/org/a11y/atspi/cache";
    public const string NullPath = "/org/a11y/atspi/null";

    // ── Coordinate types for Component (AtspiCoordType).
    public const uint CoordTypeScreen = 0;
    public const uint CoordTypeWindow = 1;
    public const uint CoordTypeParent = 2;

    // ── Component layer (AtspiComponentLayer). Widgets live in the widget layer.
    public const uint LayerWidget = 3;

    // ── AtspiRole numeric codes (atspi-constants.h). Only the subset we map.
    public const uint RoleInvalid = 0;
    public const uint RoleAlert = 2;
    public const uint RoleCheckBox = 7;
    public const uint RoleComboBox = 11;
    public const uint RoleDialog = 16;
    public const uint RoleFiller = 20;
    public const uint RoleFrame = 23;
    public const uint RoleImage = 27;
    public const uint RoleLabel = 29;
    public const uint RoleList = 31;
    public const uint RoleListItem = 32;
    public const uint RolePageTab = 37;
    public const uint RolePageTabList = 38;
    public const uint RolePanel = 39;
    public const uint RolePushButton = 43;
    public const uint RoleRadioButton = 44;
    public const uint RoleScrollPane = 49;
    public const uint RoleSeparator = 50;
    public const uint RoleSlider = 51;
    public const uint RoleProgressBar = 46;
    public const uint RoleSpinButton = 52;
    public const uint RoleText = 61;
    public const uint RoleToggleButton = 62;
    public const uint RoleTree = 65;
    public const uint RoleLink = 88;
    public const uint RoleApplication = 75;
    public const uint RoleHeading = 83;

    public static uint MapRole(AccessibilityRole role) => role switch
    {
        AccessibilityRole.Button => RolePushButton,
        AccessibilityRole.TextInput => RoleText,
        AccessibilityRole.Label => RoleLabel,
        AccessibilityRole.Image => RoleImage,
        AccessibilityRole.List => RoleList,
        AccessibilityRole.ListItem => RoleListItem,
        AccessibilityRole.ScrollView => RoleScrollPane,
        AccessibilityRole.Slider => RoleSlider,
        AccessibilityRole.ProgressBar => RoleProgressBar,
        AccessibilityRole.SpinButton => RoleSpinButton,
        AccessibilityRole.Checkbox => RoleCheckBox,
        AccessibilityRole.ComboBox => RoleComboBox,
        AccessibilityRole.RadioButton => RoleRadioButton,
        AccessibilityRole.Switch => RoleToggleButton,
        AccessibilityRole.Tab => RolePageTab,
        AccessibilityRole.TabPanel => RolePageTabList,
        AccessibilityRole.Dialog => RoleDialog,
        AccessibilityRole.Alert => RoleAlert,
        AccessibilityRole.Heading => RoleHeading,
        AccessibilityRole.Link => RoleLink,
        AccessibilityRole.Separator => RoleSeparator,
        AccessibilityRole.Group => RolePanel,
        AccessibilityRole.Tree => RoleTree,
        AccessibilityRole.TreeItem => RoleListItem,
        AccessibilityRole.Application => RoleApplication,
        _ => RolePanel,
    };

    /// <summary>The lowercase, space-separated role name AT-SPI clients expect.</summary>
    public static string RoleName(uint role) => role switch
    {
        RoleAlert => "alert",
        RoleCheckBox => "check box",
        RoleComboBox => "combo box",
        RoleDialog => "dialog",
        RoleFiller => "filler",
        RoleFrame => "frame",
        RoleImage => "image",
        RoleLabel => "label",
        RoleList => "list",
        RoleListItem => "list item",
        RolePageTab => "page tab",
        RolePageTabList => "page tab list",
        RolePanel => "panel",
        RolePushButton => "push button",
        RoleRadioButton => "radio button",
        RoleScrollPane => "scroll pane",
        RoleSeparator => "separator",
        RoleSlider => "slider",
        RoleProgressBar => "progress bar",
        RoleSpinButton => "spin button",
        RoleText => "text",
        RoleToggleButton => "toggle button",
        RoleTree => "tree",
        RoleLink => "link",
        RoleApplication => "application",
        RoleHeading => "heading",
        _ => "unknown",
    };

    // ── AtspiStateType bit positions (atspi-constants.h). The state set is a
    // 64-bit field marshalled as two uint32 (word 0 = bits 0-31, word 1 = 32-63).
    public const int StateActive = 1;
    public const int StateChecked = 4;
    public const int StateEditable = 7;
    public const int StateEnabled = 8;
    public const int StateExpandable = 9;
    public const int StateExpanded = 10;
    public const int StateFocusable = 11;
    public const int StateFocused = 12;
    public const int StateSelectable = 22;
    public const int StateSelected = 23;
    public const int StateSensitive = 24;
    public const int StateShowing = 25;
    public const int StateVisible = 30;

    // ── AT-SPI property-name strings used in PropertyChange signals and read
    // by clients through org.freedesktop.DBus.Properties.
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
