using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Noxel.App.Infrastructure;
using Noxel.App.Services;

namespace Noxel.App.Views;

/// <summary>
/// Topmost "Keep these display settings?" prompt, centred on the monitor that changed so it is visible even
/// when the main window is hidden or on another display. Revert has focus, Esc reverts, closing reverts,
/// and the countdown reverts automatically.
/// </summary>
public sealed partial class ConfirmDisplayWindow : Window
{
    private readonly PendingDisplayChange _p;
    private bool _resolved;

    public ConfirmDisplayWindow(PendingDisplayChange p)
    {
        InitializeComponent();
        _p = p;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragBar);
        WindowHelper.SetIcon(this);
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.IsResizable = false;
            op.IsMaximizable = false;
            op.IsMinimizable = false;
            op.IsAlwaysOnTop = true;
        }
        double s = WindowHelper.Scale(this);
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32((int)(470 * s), (int)(290 * s)));
        ApplyTheme();

        Detail.Text = $"{p.Monitor.FriendlyName} is now {p.NewMode.Width}×{p.NewMode.Height} at {p.NewMode.RefreshHz} Hz (was {p.OldMode.Width}×{p.OldMode.Height} at {p.OldMode.RefreshHz} Hz).";
        if (!string.IsNullOrEmpty(p.Note)) { Note.Text = p.Note; Note.Visibility = Visibility.Visible; }
        Progress.Maximum = p.SecondsLeft;
        UpdateCountdown();
        p.Tick += () => DispatcherQueue.TryEnqueue(UpdateCountdown);
        App.Controller!.Changes.PendingEnded += OnEnded;
        AppWindow.Closing += (_, _) => { if (!_resolved) Resolve(ChangeDecision.Revert); };
    }

    public void ApplyTheme() => ThemeService.ApplyToWindow(this, Root, App.Settings);

    public void ShowCentered()
    {
        var m = _p.Monitor;
        var cur = App.Controller?.Monitors.FirstOrDefault(x => x.StableId == m.StableId) ?? m;
        WindowHelper.CenterOnPoint(this, cur.PositionX + 40, cur.PositionY + 40);
        WindowHelper.BringToFront(this);
        RevertButton.Focus(FocusState.Programmatic);
    }

    private void UpdateCountdown()
    {
        Progress.Value = Math.Max(0, _p.SecondsLeft);
        Countdown.Text = _p.SecondsLeft == 1 ? "Reverting in 1 second." : $"Reverting in {Math.Max(0, _p.SecondsLeft)} seconds.";
    }

    private void OnEnded(PendingDisplayChange p, ChangeDecision d)
    {
        if (!ReferenceEquals(p, _p)) return;
        _resolved = true;
        App.Controller!.Changes.PendingEnded -= OnEnded;
        DispatcherQueue.TryEnqueue(Close);
    }

    private void Resolve(ChangeDecision d)
    {
        if (_resolved) return;
        _resolved = true;
        _ = App.Controller!.Changes.ResolveAsync(d);
    }

    private void OnKeep(object sender, RoutedEventArgs e) => Resolve(ChangeDecision.Keep);
    private void OnRevert(object sender, RoutedEventArgs e) => Resolve(ChangeDecision.Revert);

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Escape) { Resolve(ChangeDecision.Revert); e.Handled = true; }
    }
}
