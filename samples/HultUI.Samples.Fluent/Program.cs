using HultUI;
using HultUI.Core.Styling;
using HultUI.Samples.Fluent;

// Borderless: the Fluent title bar is part of the page, with the app name on the left and the
// caption buttons drawn as elements. On Windows the frame keeps its DWM shadow, rounded
// corners and Snap Layouts — see the WindowMaximizeButton call in FluentApp.
using var app = new Application("Fluent Sample", 1280, 860, WindowStyle.Borderless);

var ui = new FluentApp(app, app.PrimaryWindow);
app.SetRoot(ui.Mount());
app.Run();
