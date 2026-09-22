// Resolve ambiguity up front, matching the other apps: the builder class
// HultUI.Core.Builders.Text collides with the HultUI.Text namespace once
// HultUI.* namespaces are in scope.
global using Text = HultUI.Core.Builders.Text;
