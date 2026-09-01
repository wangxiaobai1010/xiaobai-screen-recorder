using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace XbPreview.Avalonia;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(
            new Uri("avares://XbPreview.Avalonia/"))
        {
            Source = new Uri(
                "avares://XbPreview.Avalonia/Styles/SkillRecorderStyles.axaml"),
        });
    }
}
