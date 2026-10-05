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
}
