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
    ProgressBar
}
