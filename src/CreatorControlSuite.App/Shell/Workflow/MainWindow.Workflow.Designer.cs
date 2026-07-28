#nullable enable

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CreatorControlSuite.App.Core.Eventing;
using CreatorControlSuite.App.Helpers;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.Services.CreatorIntelligence;
using CreatorControlSuite.App.Themes;
using CreatorControlSuite.App.Twitch;
using CreatorControlSuite.App.ViewModels;
using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.App.Views.Dialogs;
using CreatorControlSuite.App.Views.Pages.Music;
using CreatorControlSuite.App.Views.Pages.Workflow;
using CreatorControlSuite.Core.Automation;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Diagnostics;
using CreatorControlSuite.Core.Eventing;
using CreatorControlSuite.Core.Ipc;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Core.Music;
using CreatorControlSuite.Core.Profiles;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Core.Twitch;
using CreatorControlSuite.Core.Updates;
using CreatorControlSuite.Core.Validation;
using CreatorControlSuite.Modules.Alerts;
using CreatorControlSuite.Modules.Alerts.Models;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.OBS.Models;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Extensions;
using CreatorControlSuite.Modules.Overlay.Models;
using CreatorControlSuite.Modules.Spotify;
using CreatorControlSuite.Modules.Spotify.Models;
using CreatorControlSuite.Modules.StreamDeck;
using CreatorControlSuite.Modules.StreamDeck.Models;
using CreatorControlSuite.Modules.Twitch;
using CreatorControlSuite.Modules.Twitch.Models;
using CreatorControlSuite.Modules.Workflow;
using CreatorControlSuite.Modules.Workflow.Models;
using CreatorControlSuite.Modules.YouTubeMusic;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using MultiPcDeviceRecord = CreatorControlSuite.Core.Security.PairedAgentDevice;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private void RefreshWorkflowDesigner()
    {
        var groups = _timedAutomationRules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.WorkflowGroup))
            .Select(rule => rule.WorkflowGroup.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

        string selected = WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerGroupBox.Text?.Trim() ?? "";
        WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerGroupBox.ItemsSource = groups;
        if (string.IsNullOrWhiteSpace(selected) && groups.Count > 0)
        {
            selected = groups[0];
            WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerGroupBox.SelectedItem = selected;
        }
        else
        {
            WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerGroupBox.Text = selected;
        }

        WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerCanvas.Children.Clear();
        if (string.IsNullOrWhiteSpace(selected))
        {
            WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerStatusText.Text = "Keine Workflow-Gruppe vorhanden. Weise Automatisierungsregeln zuerst einen Gruppennamen zu.";
            return;
        }

        var rules = _timedAutomationRules
            .Where(rule => string.Equals(rule.WorkflowGroup?.Trim(), selected, StringComparison.OrdinalIgnoreCase))
            .OrderBy(rule => rule.WorkflowOrder)
            .ThenBy(rule => rule.Name)
            .ToList();

        if (rules.Count == 0)
        {
            WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerStatusText.Text = $"Die Gruppe ‘{selected}’ enthält keine Regeln.";
            return;
        }

        for (int index = 0; index < rules.Count; index++)
        {
            TimedAutomationRuleSettings rule = rules[index];
            if (rule.DesignerX <= 0 && rule.DesignerY <= 0)
            {
                rule.DesignerX = 80 + (index * 250);
                rule.DesignerY = 120;
            }
        }

        foreach (TimedAutomationRuleSettings? rule in rules)
        {
            TimedAutomationRuleSettings? next = ResolveWorkflowDesignerNextRule(rule, rules);
            if (next is not null)
            {
                DrawWorkflowDesignerConnection(rule, next, "Erfolg", Brushes.SeaGreen);
            }

            TimedAutomationRuleSettings? failure = rules.FirstOrDefault(candidate => candidate.Id == rule.FailureRuleId);
            if (failure is not null)
            {
                DrawWorkflowDesignerConnection(rule, failure, "Fehler", Brushes.IndianRed);
            }
        }

        foreach (TimedAutomationRuleSettings? rule in rules)
        {
            DrawWorkflowDesignerNode(rule);
        }

        WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerStatusText.Text = $"{rules.Count} Knoten in ‘{selected}’. Knoten können mit der Maus verschoben werden; Positionen werden beim Loslassen gespeichert.";
    }

    private TimedAutomationRuleSettings? ResolveWorkflowDesignerNextRule(TimedAutomationRuleSettings rule, IReadOnlyList<TimedAutomationRuleSettings> groupRules)
    {
        if (!string.IsNullOrWhiteSpace(rule.NextRuleId))
        {
            TimedAutomationRuleSettings? explicitNext = groupRules.FirstOrDefault(candidate => candidate.Id == rule.NextRuleId);
            if (explicitNext is not null)
            {
                return explicitNext;
            }
        }

        return groupRules
            .Where(candidate => candidate.WorkflowOrder > rule.WorkflowOrder)
            .OrderBy(candidate => candidate.WorkflowOrder)
            .FirstOrDefault();
    }

    private void DrawWorkflowDesignerConnection(TimedAutomationRuleSettings from, TimedAutomationRuleSettings to, string label, Brush brush)
    {
        double x1 = from.DesignerX + 210;
        double y1 = from.DesignerY + 42;
        double x2 = to.DesignerX;
        double y2 = to.DesignerY + 42;
        var line = new System.Windows.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = 3
        };
        WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerCanvas.Children.Add(line);

        var text = new TextBlock { Text = label, Foreground = brush, FontWeight = FontWeights.SemiBold, Background = Brushes.Black };
        Canvas.SetLeft(text, ((x1 + x2) / 2) - 20);
        Canvas.SetTop(text, ((y1 + y2) / 2) - 18);
        WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerCanvas.Children.Add(text);
    }

    private void DrawWorkflowDesignerNode(TimedAutomationRuleSettings rule)
    {
        SolidColorBrush statusBrush = rule.LastRunStatus.Contains("Erfolg", StringComparison.OrdinalIgnoreCase)
            ? Brushes.SeaGreen
            : rule.LastRunStatus.Contains("Fehler", StringComparison.OrdinalIgnoreCase)
                ? Brushes.IndianRed
                : Brushes.DimGray;

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = rule.Name, FontWeight = FontWeights.Bold, FontSize = 14, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"{rule.ActionType} · Reihenfolge {rule.WorkflowOrder}", Foreground = Brushes.LightGray, Margin = new Thickness(0, 4, 0, 0) });
        panel.Children.Add(new TextBlock { Text = rule.LastRunStatus, Foreground = statusBrush, Margin = new Thickness(0, 5, 0, 0) });

        var border = new Border
        {
            Width = 210,
            MinHeight = 84,
            Background = new SolidColorBrush(Color.FromRgb(20, 29, 36)),
            BorderBrush = statusBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12),
            Child = panel,
            Tag = rule,
            Cursor = Cursors.SizeAll
        };
        Canvas.SetLeft(border, rule.DesignerX);
        Canvas.SetTop(border, rule.DesignerY);

        Point dragOffset = default;
        border.MouseLeftButtonDown += (_, args) =>
        {
            dragOffset = args.GetPosition(border);
            border.CaptureMouse();
            args.Handled = true;
        };
        border.MouseMove += (_, args) =>
        {
            if (!border.IsMouseCaptured || args.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point position = args.GetPosition(WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerCanvas);
            Canvas.SetLeft(border, Math.Max(0, position.X - dragOffset.X));
            Canvas.SetTop(border, Math.Max(0, position.Y - dragOffset.Y));
        };
        border.MouseLeftButtonUp += async (_, args) =>
        {
            if (!border.IsMouseCaptured)
            {
                return;
            }

            border.ReleaseMouseCapture();
            rule.DesignerX = Canvas.GetLeft(border);
            rule.DesignerY = Canvas.GetTop(border);
            await _settingsStore.SaveAsync(_settings);
            RefreshWorkflowDesigner();
            args.Handled = true;
        };

        WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerCanvas.Children.Add(border);
    }

    private async Task AutoLayoutWorkflowDesignerAsync()
    {
        string group = WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerGroupBox.Text?.Trim() ?? "";
        var rules = _timedAutomationRules
            .Where(rule => string.Equals(rule.WorkflowGroup?.Trim(), group, StringComparison.OrdinalIgnoreCase))
            .OrderBy(rule => rule.WorkflowOrder)
            .ThenBy(rule => rule.Name)
            .ToList();
        for (int index = 0; index < rules.Count; index++)
        {
            rules[index].DesignerX = 70 + (index % 5 * 280);
            rules[index].DesignerY = 90 + (index / 5 * 170);
        }
        await _settingsStore.SaveAsync(_settings);
        RefreshWorkflowDesigner();
    }

    private void SetWorkflowDesignerZoom(double zoom)
    {
        zoom = Math.Clamp(zoom, 0.5, 2.0);
        WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerScale.ScaleX = zoom;
        WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerScale.ScaleY = zoom;
        WorkflowPageViewHost.WorkflowDesignerViewHost.ResetZoomWorkflowDesignerButton.Content = $"{zoom:P0}";
    }

    private void ValidateWorkflowDesigner()
    {
        string group = WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerGroupBox.Text?.Trim() ?? "";
        var rules = _timedAutomationRules
            .Where(rule => string.Equals(rule.WorkflowGroup?.Trim(), group, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var issues = new List<string>();
        if (rules.Count == 0)
        {
            issues.Add("Die ausgewählte Gruppe enthält keine Regeln.");
        }

        foreach (IGrouping<int, TimedAutomationRuleSettings>? duplicate in rules.GroupBy(rule => rule.WorkflowOrder).Where(g => g.Count() > 1))
        {
            issues.Add($"Reihenfolge {duplicate.Key} ist mehrfach vergeben.");
        }

        foreach (TimedAutomationRuleSettings? rule in rules)
        {
            if (!string.IsNullOrWhiteSpace(rule.NextRuleId) && rules.All(candidate => candidate.Id != rule.NextRuleId))
            {
                issues.Add($"{rule.Name}: Erfolgspfad zeigt außerhalb der Gruppe.");
            }

            if (!string.IsNullOrWhiteSpace(rule.FailureRuleId) && rules.All(candidate => candidate.Id != rule.FailureRuleId))
            {
                issues.Add($"{rule.Name}: Fehlerpfad zeigt außerhalb der Gruppe.");
            }
        }
        WorkflowPageViewHost.WorkflowDesignerViewHost.WorkflowDesignerStatusText.Text = issues.Count == 0
            ? $"Graph ‘{group}’ ist gültig: {rules.Count} erreichbare Knoten, keine doppelten Reihenfolgen."
            : "Graphprüfung: " + string.Join(" | ", issues);
    }
}
