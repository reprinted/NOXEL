// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 barry

using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Noxel.App.Animations;
using Noxel.App.Infrastructure;
using Noxel.App.Services;

namespace Noxel.App.Controls;

/// <summary>
/// NOXEL's notification stack (bottom-right of the content area).
///  • Glass card: status icon, title, optional message, optional action buttons, close button.
///  • Temporary notices (success/info ≈ 2.5 s, warnings 6 s) show a thin accent countdown bar that the compositor
///    shrinks to zero (no per-frame UI work). Hovering or focusing a card pauses both the timer and the bar.
///  • Errors and notices with actions (update decisions etc.) never auto-dismiss.
///  • At most three are visible; more wait in a queue; an identical notice already shown is refreshed instead
///    of duplicated. Cards slide 10 px into place and fade (≈ 200 ms); the rest glide up when one leaves.
/// </summary>
public sealed class NotificationHost : StackPanel
{
    public const int MaxVisible = 3;
    private readonly Queue<Notice> _queue = new();
    private readonly List<Card> _cards = new();

    public NotificationHost()
    {
        Spacing = 10;
        VerticalAlignment = VerticalAlignment.Bottom;
        HorizontalAlignment = HorizontalAlignment.Right;
        MaxWidth = 400;
    }

    public static TimeSpan LifetimeFor(Notice n) => n.Persistent || n.Actions is { Count: > 0 } || n.Kind == NoticeKind.Error
        ? TimeSpan.Zero
        : n.Kind == NoticeKind.Warning ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(2.5);

    public void Show(Notice n)
    {
        var same = _cards.FirstOrDefault(c => c.Notice.Title == n.Title && c.Notice.Message == n.Message && !c.Closing);
        if (same is not null) { same.Restart(); return; }
        if (_queue.Any(q => q.Title == n.Title && q.Message == n.Message)) return;
        if (_cards.Count(c => !c.Closing) >= MaxVisible) { _queue.Enqueue(n); return; }
        var card = new Card(this, n);
        _cards.Add(card);
        Children.Add(card.Root);
        card.Enter();
    }

    /// <summary>Closes notices whose key matches (e.g. an "update available" card once the user acted on it).</summary>
    public void Dismiss(string key)
    {
        foreach (var c in _cards.Where(c => c.Notice.Key == key && !c.Closing).ToList()) c.Close();
    }

    private void Remove(Card c)
    {
        Children.Remove(c.Root);
        _cards.Remove(c);
        if (_queue.Count > 0 && _cards.Count(x => !x.Closing) < MaxVisible) Show(_queue.Dequeue());
    }

    private sealed class Card
    {
        private readonly NotificationHost _host;
        public Notice Notice { get; }
        public Border Root { get; }
        public bool Closing { get; private set; }
        private readonly Border? _bar;
        private readonly TimeSpan _life;
        private DispatcherQueueTimer? _timer;
        private readonly Stopwatch _elapsed = new();
        private TimeSpan _remaining;
        private bool _paused;

        public Card(NotificationHost host, Notice n)
        {
            _host = host;
            Notice = n;
            _life = LifetimeFor(n);
            _remaining = _life;

            var (glyph, colorKey) = n.Kind switch
            {
                NoticeKind.Success => ("", "NoxelSuccess"),
                NoticeKind.Warning => ("", "NoxelWarning"),
                NoticeKind.Error => ("", "NoxelDanger"),
                _ => ("", "NoxelAccentText"),
            };

            var grid = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Top,
                Background = (Brush)Res("NoxelGlassFillSubtle"),
                Child = new FontIcon { Glyph = glyph, FontSize = 14, Foreground = (Brush)Res(colorKey) },
            };
            grid.Children.Add(icon);

            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = n.Title, FontWeight = FontWeights.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Res("NoxelTextPrimary"), FontFamily = (FontFamily)Application.Current.Resources["NoxelFontBody"],
            });
            if (!string.IsNullOrWhiteSpace(n.Message))
                text.Children.Add(new TextBlock { Text = n.Message, Style = (Style)Application.Current.Resources["MutedText"], MaxLines = 5, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var close = new Button
            {
                Style = (Style)Application.Current.Resources["IconButton"], MinWidth = 28, MinHeight = 28, Padding = new Thickness(6),
                VerticalAlignment = VerticalAlignment.Top, Content = new FontIcon { Glyph = "", FontSize = 10 },
            };
            AutomationProperties.SetName(close, "Dismiss notification");
            close.Click += (_, _) => { n.OnDismissed?.Invoke(); Close(); };
            Grid.SetColumn(close, 2);
            grid.Children.Add(close);

            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(grid);
            if (n.Actions is { Count: > 0 })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(40, 0, 0, 0) };
                for (int i = 0; i < n.Actions.Count; i++)
                {
                    var a = n.Actions[i];
                    var b = new Button
                    {
                        Content = a.Label, MinHeight = 32, Padding = new Thickness(14, 5, 14, 6),
                        CornerRadius = (CornerRadius)Application.Current.Resources["NoxelRadiusControl"],
                    };
                    if (i == 0) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                    b.Click += (_, _) => { a.Run(); if (a.Closes) Close(); };
                    row.Children.Add(b);
                }
                body.Children.Add(row);
            }

            var layout = new Grid();
            layout.Children.Add(body);
            if (_life > TimeSpan.Zero)
            {
                _bar = new Border
                {
                    Height = 2.5, CornerRadius = new CornerRadius(1.25), Background = (Brush)Res("NoxelAccent"), Opacity = 0.85,
                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, -8),
                };
                layout.Children.Add(_bar);
            }

            Root = new Border
            {
                Child = layout,
                Background = (Brush)Res("NoxelNoticeFill"),
                BorderBrush = (Brush)Res("NoxelGlassStroke"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)Application.Current.Resources["NoxelRadiusCard"],
                Padding = new Thickness(14, 12, 10, 14),
                MinWidth = 280,
                Shadow = new ThemeShadow(),
                Translation = new Vector3(0, 0, 24),
            };
            // Brushes are resolved from the theme dictionaries once, so re-resolve them when the theme changes.
            var titleBlock = (TextBlock)text.Children[0];
            var glyphIcon = (FontIcon)icon.Child;
            Root.ActualThemeChanged += (_, _) =>
            {
                Root.Background = (Brush)Res("NoxelNoticeFill");
                Root.BorderBrush = (Brush)Res("NoxelGlassStroke");
                icon.Background = (Brush)Res("NoxelGlassFillSubtle");
                glyphIcon.Foreground = (Brush)Res(colorKey);
                titleBlock.Foreground = (Brush)Res("NoxelTextPrimary");
                if (_bar is not null) _bar.Background = (Brush)Res("NoxelAccent");
            };
            AutomationProperties.SetName(Root, n.Title);
            AutomationProperties.SetLiveSetting(Root, n.Kind == NoticeKind.Error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
            Root.PointerEntered += (_, _) => Pause();
            Root.PointerExited += (_, _) => Resume();
            Root.GotFocus += (_, _) => Pause();
            Root.LostFocus += (_, _) => Resume();

            // Cards glide into their new slot when the stack changes (compositor implicit animation).
            var v = ElementCompositionPreview.GetElementVisual(Root);
            var c = v.Compositor;
            var implicitAnims = c.CreateImplicitAnimationCollection();
            var glide = c.CreateVector3KeyFrameAnimation();
            glide.Target = "Offset";
            glide.InsertExpressionKeyFrame(1f, "this.FinalValue", Motion.EaseInOut(c));
            glide.Duration = TimeSpan.FromMilliseconds(220);
            implicitAnims["Offset"] = glide;
            if (Motion.Enabled) v.ImplicitAnimations = implicitAnims;
        }

        private static object Res(string key) => Application.Current.Resources.ThemeDictionariesLookup(key);

        public void Enter()
        {
            Motion.Appear(Root, 12);
            var peer = FrameworkElementAutomationPeer.FromElement(Root) ?? FrameworkElementAutomationPeer.CreatePeerForElement(Root);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            Restart();
        }

        public void Restart()
        {
            if (_life <= TimeSpan.Zero || Closing) return;
            _remaining = _life;
            _elapsed.Restart();
            StartBar(1f, _life);
            _timer ??= CreateTimer();
            _timer.Stop();
            _timer.Interval = _remaining;
            if (!_paused) _timer.Start();
            else { _elapsed.Stop(); PauseBar(); }
        }

        private DispatcherQueueTimer CreateTimer()
        {
            var t = _host.DispatcherQueue.CreateTimer();
            t.IsRepeating = false;
            t.Tick += (_, _) => Close();
            return t;
        }

        private void Pause()
        {
            if (_paused || _life <= TimeSpan.Zero || Closing) return;
            _paused = true;
            _timer?.Stop();
            _elapsed.Stop();
            _remaining -= _elapsed.Elapsed;
            if (_remaining < TimeSpan.FromMilliseconds(400)) _remaining = TimeSpan.FromMilliseconds(400);
            PauseBar();
        }

        private void Resume()
        {
            if (!_paused || Closing) return;
            _paused = false;
            _elapsed.Restart();
            ResumeBar();
            if (_timer is null) return;
            _timer.Interval = _remaining;
            _timer.Start();
        }

        // -------- countdown bar: Scale.X 1 → 0, linear, on the compositor --------
        private void StartBar(float from, TimeSpan duration)
        {
            if (_bar is null) return;
            if (!Motion.Enabled) { _bar.Visibility = Visibility.Collapsed; return; }
            var v = ElementCompositionPreview.GetElementVisual(_bar);
            v.CenterPoint = Vector3.Zero;
            var c = v.Compositor;
            var a = c.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(0f, from);
            a.InsertKeyFrame(1f, 0f, c.CreateLinearEasingFunction());
            a.Duration = duration;
            v.StartAnimation("Scale.X", a);
        }

        private void PauseBar()
        {
            if (_bar is null) return;
            ElementCompositionPreview.GetElementVisual(_bar).TryGetAnimationController("Scale.X")?.Pause();
        }

        private void ResumeBar()
        {
            if (_bar is null) return;
            ElementCompositionPreview.GetElementVisual(_bar).TryGetAnimationController("Scale.X")?.Resume();
        }

        public void Close()
        {
            if (Closing) return;
            Closing = true;
            _timer?.Stop();
            Motion.Disappear(Root, () => _host.Remove(this));
        }
    }
}
