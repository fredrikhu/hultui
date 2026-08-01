// Resolve ambiguity: the class HultUI.Core.Builders.Text conflicts with
// the namespace HultUI.Text when our code lives under a HultUI.* using tree.
global using Text = HultUI.Core.Builders.Text;
global using TextArea = HultUI.Core.Builders.TextArea;
