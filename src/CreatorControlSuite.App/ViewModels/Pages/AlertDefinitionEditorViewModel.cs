using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class AlertDefinitionEditorViewModel : ViewModelBase
{
    private static readonly HashSet<string> Animations =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Fade",
            "Slide",
            "Zoom",
            "Bounce"
        };

    public string TextTemplate
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string MediaPath
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string SoundPath
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string AudioOutputDeviceId
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public double SoundStartSeconds
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    }

    public double SoundEndSeconds
    {
        get;
        set => SetProperty(ref field, Math.Max(0, value));
    }

    public string DurationSeconds
    {
        get;
        set => SetProperty(ref field, value);
    } = "8";

    public string Priority
    {
        get;
        set => SetProperty(ref field, value);
    } = "100";

    public string FontFace
    {
        get;
        set => SetProperty(ref field, value);
    } = "Segoe UI";

    public string FontSize
    {
        get;
        set => SetProperty(ref field, value);
    } = "44";

    public string FontColor
    {
        get;
        set => SetProperty(ref field, value);
    } = "#FFFFFF";

    public string Animation
    {
        get;
        set => SetProperty(ref field, value);
    } = "Fade";

    public void Load(AlertDefinitionSettings definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        TextTemplate = definition.TextTemplate;
        MediaPath = definition.MediaPath;
        SoundPath = definition.SoundPath;
        AudioOutputDeviceId = definition.AudioOutputDeviceId;
        SoundStartSeconds = definition.SoundStartSeconds;
        SoundEndSeconds = definition.SoundEndSeconds;
        DurationSeconds = definition.DurationSeconds.ToString();
        Priority = definition.Priority.ToString();
        FontFace = definition.FontFace;
        FontSize = definition.FontSize.ToString();
        FontColor = definition.FontColor;
        Animation = Animations.Contains(definition.Animation)
            ? definition.Animation
            : "Fade";
    }

    public bool TryApplyTo(
        AlertDefinitionSettings definition,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!int.TryParse(
                DurationSeconds.Trim(),
                out int duration) ||
            duration < 1)
        {
            error = "Die Alert-Dauer muss mindestens eine Sekunde betragen.";
            return false;
        }

        if (!int.TryParse(Priority.Trim(), out int priority))
        {
            error = "Die Alert-Priorität ist ungültig.";
            return false;
        }

        if (!int.TryParse(FontSize.Trim(), out int fontSize) ||
            fontSize < 1)
        {
            error = "Die Alert-Schriftgröße ist ungültig.";
            return false;
        }

        definition.TextTemplate = TextTemplate.Trim();
        definition.MediaPath = MediaPath.Trim();
        definition.SoundPath = SoundPath.Trim();
        definition.AudioOutputDeviceId =
            AudioOutputDeviceId.Trim();
        definition.SoundStartSeconds =
            Math.Max(0, SoundStartSeconds);
        definition.SoundEndSeconds =
            SoundEndSeconds <= 0
                ? 0
                : Math.Max(
                    definition.SoundStartSeconds,
                    SoundEndSeconds);
        definition.DurationSeconds = duration;
        definition.Priority = priority;
        definition.FontFace = string.IsNullOrWhiteSpace(FontFace)
            ? "Segoe UI"
            : FontFace.Trim();
        definition.FontSize = fontSize;
        definition.FontColor = string.IsNullOrWhiteSpace(FontColor)
            ? "#FFFFFF"
            : FontColor.Trim();
        definition.Animation = Animations.Contains(Animation)
            ? Animation
            : "Fade";

        error = "";
        Load(definition);
        return true;
    }
}
