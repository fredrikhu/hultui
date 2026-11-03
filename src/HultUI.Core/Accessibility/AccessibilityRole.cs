namespace HultUI.Core.Accessibility;

public enum AccessibilityRole : byte
{
    None,
    Button,
    TextInput,
    Label,
    Image,
    List,
    ListItem,
    ScrollView,
    Slider,
    Checkbox,
    RadioButton,
    Switch,
    Tab,
    TabPanel,
    Dialog,
    Alert,
    Heading,
    Link,
    Separator,
    Group,
    Tree,
    TreeItem,
    Application,
    ComboBox,
    /// <summary>A determinate or indeterminate progress indicator: a bar or a spinner.
    /// Read-only range value when determinate.</summary>
    ProgressBar,
    /// <summary>A numeric field with step buttons; a writable range value.</summary>
    SpinButton,
    /// <summary>A document, such as a web page's root: AT-SPI's "document web".</summary>
    Document,
    /// <summary>A row of menus across the top of a window (File, Edit, …).</summary>
    MenuBar,
    /// <summary>An entry in a menu or a menu bar; one that opens a menu reports
    /// <see cref="Elements.Element.AccessibleExpanded"/>.</summary>
    MenuItem,
    /// <summary>A table column's header, which sorts the column when it is pressed.</summary>
    ColumnHeader,
    /// <summary>The strip that holds a set of <see cref="Tab"/>s: UIA's Tab control type,
    /// AT-SPI's page tab list. One tab is <see cref="Tab"/>; what it shows is
    /// <see cref="TabPanel"/>.</summary>
    TabList,
    /// <summary>A list of commands that opens from a button and goes away again: UIA's Menu
    /// control, AT-SPI's menu. Its entries are <see cref="MenuItem"/>s, directly inside it, and
    /// one is selected while the pointer is on it. A box with a click that holds a menu is the
    /// backdrop that closes it, and no button. A context menu and a menu bar's menu have it
    /// too, and publish their items themselves, as menu items selected by the keys and the
    /// pointer; a dropdown's list, which publishes its items the same way, is a
    /// <see cref="List"/>.</summary>
    Menu,
}
