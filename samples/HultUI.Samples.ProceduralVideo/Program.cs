using HultUI;
using HultUI.Core.Styling;
using HultUI.Samples.ProceduralVideo;

using var app = new Application("HultUI - Procedural Video", 1000, 680);
app.Theme = Theme.Dark;

using var sample = new ProceduralVideoApp(app, app.PrimaryWindow);
app.SetRoot(sample.Mount());
app.Run();
