using System.Globalization;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class TwitchGoalsPageViewModel : ViewModelBase
{
    private double _liveFollowerCount;
    private double _liveSubscriptionCount;

    public TwitchGoalsPageViewModel()
    {
        SaveCommand = new AsyncRelayCommand(
            _ => SaveRequestedAsync?.Invoke() ?? Task.CompletedTask);
    }

    public string OverlayScene
    {
        get;
        set => SetProperty(ref field, value);
    } = "CCS Ziele & Overlay-Daten";

    public string FollowerTitle
    {
        get;
        set => SetProperty(ref field, value);
    } = "Follower-Ziel";

    public string FollowerCurrent
    {
        get;
        set => SetProperty(ref field, value);
    } = "0";

    public string FollowerTarget
    {
        get;
        set => SetProperty(ref field, value);
    } = "200";

    public string FollowerFontFace
    {
        get;
        set => SetProperty(ref field, value);
    } = "Segoe UI";

    public string FollowerFontSize
    {
        get;
        set => SetProperty(ref field, value);
    } = "36";

    public string SubscriptionTitle
    {
        get;
        set => SetProperty(ref field, value);
    } = "Sub-Ziel";

    public string SubscriptionCurrent
    {
        get;
        set => SetProperty(ref field, value);
    } = "0";

    public string SubscriptionTarget
    {
        get;
        set => SetProperty(ref field, value);
    } = "25";

    public string SubscriptionFontFace
    {
        get;
        set => SetProperty(ref field, value);
    } = "Segoe UI";

    public string SubscriptionFontSize
    {
        get;
        set => SetProperty(ref field, value);
    } = "36";

    public string DonationTitle
    {
        get;
        set => SetProperty(ref field, value);
    } = "Donation-Ziel";

    public string DonationCurrent
    {
        get;
        set => SetProperty(ref field, value);
    } = "0";

    public string DonationTarget
    {
        get;
        set => SetProperty(ref field, value);
    } = "100";

    public string DonationCurrency
    {
        get;
        set => SetProperty(ref field, value);
    } = "EUR";

    public string DonationReason
    {
        get;
        set => SetProperty(ref field, value);
    } = "";

    public string DonationFontFace
    {
        get;
        set => SetProperty(ref field, value);
    } = "Segoe UI";

    public string DonationFontSize
    {
        get;
        set => SetProperty(ref field, value);
    } = "36";

    public AsyncRelayCommand SaveCommand { get; }

    public Func<Task>? SaveRequestedAsync { get; set; }

    public void Load(
        ObsSettings obs,
        TwitchSettings twitch,
        double liveFollowerCount,
        double liveSubscriptionCount)
    {
        OverlayScene = obs.GoalOverlayScene;
        LoadGoal(
            twitch.FollowerGoal,
            value => FollowerTitle = value,
            value => FollowerCurrent = value,
            value => FollowerTarget = value,
            value => FollowerFontFace = value,
            value => FollowerFontSize = value);
        LoadGoal(
            twitch.SubGoal,
            value => SubscriptionTitle = value,
            value => SubscriptionCurrent = value,
            value => SubscriptionTarget = value,
            value => SubscriptionFontFace = value,
            value => SubscriptionFontSize = value);
        LoadGoal(
            twitch.DonationGoal,
            value => DonationTitle = value,
            value => DonationCurrent = value,
            value => DonationTarget = value,
            value => DonationFontFace = value,
            value => DonationFontSize = value);
        DonationCurrency = twitch.DonationGoal.Currency;
        DonationReason = twitch.DonationGoal.Reason;
        UpdateLiveCounts(liveFollowerCount, liveSubscriptionCount);
    }

    public void UpdateLiveCounts(
        double followerCount,
        double subscriptionCount)
    {
        _liveFollowerCount = Math.Max(0, followerCount);
        _liveSubscriptionCount = Math.Max(0, subscriptionCount);
        FollowerCurrent = _liveFollowerCount.ToString("0", CultureInfo.InvariantCulture);
        SubscriptionCurrent =
            _liveSubscriptionCount.ToString("0", CultureInfo.InvariantCulture);
    }

    public void ApplyTo(ObsSettings obs, TwitchSettings twitch)
    {
        obs.GoalOverlayScene = string.IsNullOrWhiteSpace(OverlayScene)
            ? "CCS Ziele & Overlay-Daten"
            : OverlayScene.Trim();
        ApplyGoal(
            twitch.FollowerGoal,
            FollowerTitle,
            _liveFollowerCount > 0
                ? _liveFollowerCount.ToString(CultureInfo.InvariantCulture)
                : FollowerCurrent,
            FollowerTarget,
            FollowerFontFace,
            FollowerFontSize,
            "Follower-Ziel");
        ApplyGoal(
            twitch.SubGoal,
            SubscriptionTitle,
            _liveSubscriptionCount > 0
                ? _liveSubscriptionCount.ToString(CultureInfo.InvariantCulture)
                : SubscriptionCurrent,
            SubscriptionTarget,
            SubscriptionFontFace,
            SubscriptionFontSize,
            "Sub-Ziel");
        ApplyGoal(
            twitch.DonationGoal,
            DonationTitle,
            DonationCurrent,
            DonationTarget,
            DonationFontFace,
            DonationFontSize,
            "Donation-Ziel");
        twitch.DonationGoal.Currency = DonationCurrency.Trim();
        twitch.DonationGoal.Reason = DonationReason.Trim();
    }

    private static void LoadGoal(
        TwitchGoalSettings goal,
        Action<string> title,
        Action<string> current,
        Action<string> target,
        Action<string> fontFace,
        Action<string> fontSize)
    {
        title(goal.Title);
        current(goal.Current.ToString("0.##", CultureInfo.InvariantCulture));
        target(goal.Target.ToString("0.##", CultureInfo.InvariantCulture));
        fontFace(goal.FontFace);
        fontSize(goal.FontSize.ToString(CultureInfo.InvariantCulture));
    }

    private static void ApplyGoal(
        TwitchGoalSettings goal,
        string title,
        string current,
        string target,
        string fontFace,
        string fontSize,
        string defaultTitle)
    {
        goal.Title = string.IsNullOrWhiteSpace(title)
            ? defaultTitle
            : title.Trim();
        goal.Current = ParseDouble(current, goal.Current);
        goal.Target = ParseDouble(target, goal.Target);
        goal.FontFace = fontFace.Trim();
        goal.FontSize = int.TryParse(fontSize, out int size)
            ? size
            : 36;
    }

    private static double ParseDouble(string text, double fallback) =>
        double.TryParse(
            text.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double value)
            ? value
            : fallback;
}
