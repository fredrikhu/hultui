using HultUI;
using HultUI.Core.Styling;
using HultUI.Samples.Media;

using var app = new Application("HultUI - Video", 960, 620);
app.Theme = Theme.Dark;

using var sample = new MediaSampleApp(app, app.PrimaryWindow);
app.SetRoot(sample.Mount());
app.Run();
