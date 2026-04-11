// See https://aka.ms/new-console-template for more information

using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

using var app = new Application();
app.Theme = Theme.Dark;
app.SetRoot(new FedoraSetup().Mount());
app.Run();

partial class FedoraSetup : Component
{
    private const int Pages = 2;
    [Reactive] public partial int PageIndex { get; set; }
    [Reactive] public partial bool NeoVim { get; set; }
    [Reactive] public partial bool Zsh { get; set; }
    [Reactive] public partial bool ZshTools { get; set; }
    [Reactive] public partial bool Starship { get; set; }
    [Reactive] public partial bool RipGrep { get; set; }
    [Reactive] public partial bool Zoxide { get; set; }
    [Reactive] public partial bool Eza { get; set; }
    [Reactive] public partial bool FdFind { get; set; }
    [Reactive] public partial bool Mpv { get; set; }
    [Reactive] public partial bool Steam { get; set; }
    [Reactive] public partial bool HeroicLauncher { get; set; }
    [Reactive] public partial bool GamingUtilities { get; set; }
    
    public override Element Build()
    {
        // TODO: Default font sizes (Normal, heading, details, etc.)
        // TODO: Enable/disable
        // TODO: Features på färger, typ Brighten, etc
        var percent = 100f * (PageIndex + 1f) / Pages;
        var progressBar = Flex.Row().FlexGrow(1).CornerRadius(4f).BorderColor(Theme.Dark.Border).Children(
            Box.New().Background(Theme.Dark.Border).Width(Dimension.Percent(percent)).AlignItems(AlignItems.Center).Children(
                TextElement.New($"{percent}%").Padding(new Thickness(4f, 0f)) // TODO: Some way to center instead of padding
            )
        );
        progressBar.Box.BorderWidth = new Thickness(2f);
        return Flex.Column(gap: 8).Padding(8f).Children(
            Flex.Row(gap: 8).Children(
                progressBar,
                Button.New("Previous").OnClick(_ => PageIndex = Math.Max(PageIndex-1, 0)),
                Button.New("Next").OnClick(_ => PageIndex = Math.Min(PageIndex+1, Pages - 1))
            ),
            GetPage(PageIndex)
        );
    }

    private Element GetPage(int pageIndex)
    {
        return pageIndex switch {
            0 => Flex.Column(gap: 8f).Children(
                Box.New().Children(
                    Text.New("Required").FontSize(16f).FontWeight(FontWeight.Bold),
                    Text.New("- Parallel Downloads"),
                    Flex.Row(gap: 4f).Children(
                        Text.New("- NVIDIA drivers"),
                        Text.New("(i)").TextColor(Color.FromHex("aaaaff")).Tooltip("This requires creating a MOK key")
                    ),
                    Flex.Row(gap: 4f).Children(
                        Text.New("- CPU performance profile"),
                        Text.New("(i)").TextColor(Color.FromHex("aaaaff")).Tooltip("Sets CPU performance profile to latency-performance")
                    )
                ),
                Box.New().Children(
                    Text.New("Terminal tools").FontSize(16f).FontWeight(FontWeight.Bold),
                    Checkbox.New(NeoVim, "Neovim").OnCheckedChanged(value => NeoVim = value),
                    Checkbox.New(Zsh, "zsh").OnCheckedChanged(value => Zsh = value),
                    Flex.Row(gap: 4f).Children(
                        Checkbox.New(ZshTools, "zsh tools").OnCheckedChanged(value => ZshTools = value),
                        Text.New("(i)").TextColor(Color.FromHex("aaaaff")).Tooltip("zsh-autosuggestions, zsh-syntax-highlighting, zsh-completions")
                    ),
                    Checkbox.New(Starship, "Starship").OnCheckedChanged(value => Starship = value),
                    Checkbox.New(RipGrep, "RipGrep").OnCheckedChanged(value => RipGrep = value),
                    Checkbox.New(Zoxide, "Zoxide").OnCheckedChanged(value => Zoxide = value),
                    Checkbox.New(Eza, "Eza").OnCheckedChanged(value => Eza = value),
                    Checkbox.New(FdFind, "FdFind").OnCheckedChanged(value => FdFind = value)
                )),
            1 => Flex.Column(gap: 8f).Children(
                Box.New().Children(
                    Text.New("Applications").FontSize(16f).FontWeight(FontWeight.Bold),
                    Checkbox.New(NeoVim, "Ghostty").OnCheckedChanged(value => NeoVim = value),
                    Checkbox.New(Mpv, "Mpv + Codecs").OnCheckedChanged(value => Mpv = value)
                ),
                Box.New().Children(
                    Text.New("Gaming").FontSize(16f).FontWeight(FontWeight.Bold),
                    Flex.Row(gap: 4f).Children(
                        Checkbox.New(GamingUtilities, "Gaming utilities").OnCheckedChanged(value => Steam  = value),
                        Text.New("(i)").TextColor(Color.FromHex("aaaaff")).Tooltip("GameMode, GameScope, MangoHud")
                    ),
                    Checkbox.New(Steam, "Steam").OnCheckedChanged(value => Steam  = value),
                    Checkbox.New(HeroicLauncher, "HeroicLauncher").OnCheckedChanged(value => HeroicLauncher  = value)
                )
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex, null)
        };
    }
}