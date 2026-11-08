using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Noxel.App.Controls;
using Noxel.App.Infrastructure;
using Noxel.App.Services;
using Noxel.App.ViewModels;
using Noxel.Core.Color;
using Noxel.Core.Display;
using Noxel.Core.Presets;
using Noxel.Windows.DisplayApi;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Noxel.App.Views;

public sealed partial class PresetsPage : Page
{
    private readonly NoxelController _c = App.Controller!;
    private Preset? _sel;
    private ColorSettings _editColor = ColorSettings.Default;
    private List<MonitorInfo> _mons = new();
    private bool _updating;

    public PresetsPage()
    {
        InitializeComponent();
        HotkeyBox.GestureChanged += OnHotkeyChanged;
        HotkeyBox.Rejected += (_, msg) => { HotkeyError.Text = msg; HotkeyError.Visibility = Visibility.Visible; };
        Loaded += (_, _) =>
        {
            _c.PresetsChanged += OnExternalChange;
            _c.ColorChanged += OnExternalChange;
            _sel = _c.ActivePreset ?? _c.Presets.All.FirstOrDefault();
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _c.PresetsChanged -= OnExternalChange;
            _c.ColorChanged -= OnExternalChange;
        };
    }

    private void OnExternalChange() => Refresh();

    private void Refresh()
    {
        _updating = true;
        try
        {
            var items = _c.Presets.Search(SearchBox.Text).Select(p => new PresetItem
            {
                Preset = p,
                IsActive = p.Id == App.Settings.ActivePresetId,
                HotkeyError = _c.Hotkeys.PresetErrors.GetValueOrDefault(p.Id),
            }).ToList();
            List.ItemsSource = items;
            if (_sel is not null) _sel = _c.Presets.Find(_sel.Id);
            var match = items.FirstOrDefault(i => i.Preset.Id == _sel?.Id);
            List.SelectedItem = match;
        }
        finally { _updating = false; }
        LoadEditor(_sel);
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => Refresh();

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (List.SelectedItem is PresetItem i) { _sel = i.Preset; LoadEditor(_sel); }
    }

    // ------------------------------------------------------------ editor

    private void LoadEditor(Preset? p)
    {
        Editor.Visibility = p is null ? Visibility.Collapsed : Visibility.Visible;
        if (p is null) return;
        _updating = true;
        try
        {
            bool locked = p.IsBuiltIn;
            EditorTitle.Text = p.Name;
            BuiltInNote.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            NameBox.Text = p.Name;
            DescBox.Text = p.Description;
            NameBox.IsEnabled = DescBox.IsEnabled = !locked;
            IncludeDisplay.IsChecked = p.Display is not null;
            IncludeColor.IsChecked = p.Color is not null;
            IncludeDisplay.IsEnabled = IncludeColor.IsEnabled = !locked;

            _mons = _c.Monitors.Where(m => m.IsActive).ToList();
            var monItems = new List<string> { "Primary monitor" };
            monItems.AddRange(_mons.Select(m => m.DisplayLabel));
            int mi = 0;
            if (p.MonitorId is not null)
            {
                var found = DisplayService.Match(_mons, p.MonitorId, fallbackToPrimary: false);
                if (found is not null) mi = 1 + _mons.IndexOf(found);
                else { monItems.Add($"{p.MonitorName ?? "Saved monitor"} (not connected)"); mi = monItems.Count - 1; }
            }
            PMonitorBox.ItemsSource = monItems;
            PMonitorBox.SelectedIndex = mi;
            FillResolutions(p.Display);
            DisplayFields.Visibility = p.Display is not null ? Visibility.Visible : Visibility.Collapsed;
            PMonitorBox.IsEnabled = PResBox.IsEnabled = PHzBox.IsEnabled = !locked;

            _editColor = p.Color ?? _c.WorkingColor;
            ColorOnSwitch.IsOn = p.ColorEnabled;
            ColorOnSwitch.IsEnabled = UseCurrentColors.IsEnabled = !locked;
            ColorSummary.Text = _editColor.Summary();
            ColorFields.Visibility = p.Color is not null ? Visibility.Visible : Visibility.Collapsed;

            HotkeyBox.Gesture = p.Hotkey;
            HotkeyBox.Validator = g => _c.Hotkeys.FindConflict(g, null, p.Id);
            var hkErr = _c.Hotkeys.PresetErrors.GetValueOrDefault(p.Id);
            HotkeyError.Text = hkErr ?? "";
            HotkeyError.Visibility = hkErr is null ? Visibility.Collapsed : Visibility.Visible;
            MetaText.Text = p.IsBuiltIn ? "Built-in preset" : $"Created {p.Created.LocalDateTime:g} · modified {p.Modified.LocalDateTime:g}";

            SaveButton.IsEnabled = !locked;
            UpdateButton.IsEnabled = !locked;
            DeleteButton.IsEnabled = _c.Presets.CanDelete(p.Id);
            ToolTipService.SetToolTip(DeleteButton, DeleteButton.IsEnabled ? null : "Built-in presets and the Custom slot can't be deleted.");
        }
        finally { _updating = false; }
    }

    private MonitorInfo? EditorMonitor => PMonitorBox.SelectedIndex switch
    {
        0 => _mons.FirstOrDefault(m => m.IsPrimary) ?? _mons.FirstOrDefault(),
        var i when i > 0 && i <= _mons.Count => _mons[i - 1],
        _ => null,
    };

    private void FillResolutions(PresetDisplay? d)
    {
        var m = EditorMonitor;
        var groups = m?.Resolutions.ToList() ?? new List<ResolutionGroup>();
        PResBox.Tag = groups;
        PResBox.ItemsSource = new[] { "Keep current resolution" }.Concat(groups.Select(g => $"{g.Text}  ·  {g.AspectLabel}")).ToList();
        int ri = d is null || d.KeepsResolution ? 0 : 1 + groups.FindIndex(g => g.Width == d.Width && g.Height == d.Height);
        if (ri < 0) ri = 0;
        PResBox.SelectedIndex = ri;
        FillRates(d?.RefreshHz ?? 0);
    }

    private void FillRates(int wanted)
    {
        var groups = PResBox.Tag as List<ResolutionGroup> ?? new();
        IReadOnlyList<int> rates = PResBox.SelectedIndex > 0 ? groups[PResBox.SelectedIndex - 1].RefreshRates
            : (EditorMonitor?.Modes.Select(x => x.RefreshHz).Distinct().OrderByDescending(x => x).ToList() ?? new List<int>());
        PHzBox.Tag = rates;
        PHzBox.ItemsSource = new[] { "Highest available" }.Concat(rates.Select(r => $"{r} Hz")).ToList();
        int hi = wanted == 0 ? 0 : 1 + rates.ToList().IndexOf(wanted);
        PHzBox.SelectedIndex = hi < 0 ? 0 : hi;
    }

    private void OnPMonitorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _updating = true;
        try { FillResolutions(null); } finally { _updating = false; }
    }

    private void OnPResChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        _updating = true;
        try { FillRates(0); } finally { _updating = false; }
    }

    private void OnIncludeChanged(object sender, RoutedEventArgs e)
    {
        DisplayFields.Visibility = IncludeDisplay.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ColorFields.Visibility = IncludeColor.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private Preset? BuildFromEditor()
    {
        if (_sel is null) return null;
        PresetDisplay? display = null;
        if (IncludeDisplay.IsChecked == true)
        {
            var groups = PResBox.Tag as List<ResolutionGroup> ?? new();
            var rates = PHzBox.Tag as IReadOnlyList<int> ?? Array.Empty<int>();
            var g = PResBox.SelectedIndex > 0 ? groups[PResBox.SelectedIndex - 1] : null;
            int hz = PHzBox.SelectedIndex > 0 ? rates[PHzBox.SelectedIndex - 1] : 0;
            display = new PresetDisplay { Width = g?.Width ?? 0, Height = g?.Height ?? 0, RefreshHz = hz };
        }
        string? monitorId = _sel.MonitorId, monitorName = _sel.MonitorName;
        if (PMonitorBox.SelectedIndex == 0) { monitorId = null; monitorName = null; }
        else if (PMonitorBox.SelectedIndex > 0 && PMonitorBox.SelectedIndex <= _mons.Count)
        {
            var m = _mons[PMonitorBox.SelectedIndex - 1];
            monitorId = m.StableId; monitorName = m.FriendlyName;
        }
        if (display is null && IncludeColor.IsChecked != true)
        {
            _c.Raise("Preset", "A preset needs a display mode, colors, or both.", NoticeKind.Warning);
            return null;
        }
        return _sel with
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _sel.Name : NameBox.Text.Trim(),
            Description = DescBox.Text.Trim(),
            Display = display,
            MonitorId = display is null ? null : monitorId,
            MonitorName = display is null ? null : monitorName,
            Color = IncludeColor.IsChecked == true ? _editColor : null,
            ColorEnabled = ColorOnSwitch.IsOn,
        };
    }

    private bool SaveEditor(bool quiet)
    {
        if (_sel is null || _sel.IsBuiltIn) return true;
        var p = BuildFromEditor();
        if (p is null) return false;
        try
        {
            _sel = _c.Presets.Update(p);
            if (!quiet) _c.Raise("Preset saved", $"\"{_sel.Name}\" was saved.", NoticeKind.Success);
            return true;
        }
        catch (Exception ex)
        {
            _c.Raise("Preset", ex.Message, NoticeKind.Error);
            return false;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => SaveEditor(quiet: false);

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_sel is null || !SaveEditor(quiet: true)) return;
        await _c.ApplyPresetAsync(_sel.Id);
    }

    private async void OnApplyItem(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PresetItem i) await _c.ApplyPresetAsync(i.Preset.Id);
    }

    private void OnStar(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PresetItem i) _c.Presets.SetFavorite(i.Preset.Id, !i.Preset.IsFavorite);
    }

    private void OnHotkeyChanged(HotkeyRecorder _, string? gesture)
    {
        if (_sel is null) return;
        HotkeyError.Visibility = Visibility.Collapsed;
        _sel = _c.Presets.Update(_sel with { Hotkey = gesture });
        var err = _c.Hotkeys.PresetErrors.GetValueOrDefault(_sel.Id);
        if (err is not null) { HotkeyError.Text = err; HotkeyError.Visibility = Visibility.Visible; }
    }

    private void OnUseCurrentColors(object sender, RoutedEventArgs e)
    {
        _editColor = _c.WorkingColor;
        ColorOnSwitch.IsOn = _c.ColorEnabled || ColorOnSwitch.IsOn;
        ColorSummary.Text = _editColor.Summary() + "  (unsaved)";
    }

    private async void OnEditColors(object sender, RoutedEventArgs e)
    {
        if (_sel is null) return;
        if (!SaveEditor(quiet: true)) return;
        await _c.ApplyPresetAsync(_sel.Id);
        App.Main?.NavigateTo("Color");
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (_sel is null) return;
        _sel = _c.Presets.Duplicate(_sel.Id);
        Refresh();
    }

    private void OnFromCurrent(object sender, RoutedEventArgs e)
    {
        if (_sel is null || _sel.IsBuiltIn) return;
        _sel = _c.CaptureCurrent(_sel.Name, includeDisplay: _sel.Display is not null || IncludeDisplay.IsChecked == true, includeColor: true, existing: _sel.Id);
        _c.Raise("Preset updated", $"\"{_sel.Name}\" now matches what's active.", NoticeKind.Success);
        Refresh();
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_sel is null || !_c.Presets.CanDelete(_sel.Id)) return;
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
            Title = $"Delete \"{_sel.Name}\"?",
            Content = "This removes the preset from NOXEL. Export it first if you might want it back.",
            PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        var id = _sel.Id;
        _sel = null;
        _c.Presets.Delete(id);
        if (App.Settings.ActivePresetId == id) { App.Settings.ActivePresetId = null; _c.SaveSoon(); }
        _sel = _c.Presets.All.FirstOrDefault();
        Refresh();
    }

    private async void OnNew(object sender, RoutedEventArgs e)
    {
        var name = new TextBox { PlaceholderText = "Preset name", MaxLength = 48 };
        var display = new CheckBox { Content = "Include the current resolution and refresh rate", IsChecked = true };
        var color = new CheckBox { Content = "Include the current Color Studio look", IsChecked = true };
        var dlg = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme,
            Title = "New preset from current settings",
            Content = new StackPanel { Spacing = 12, Children = { name, display, color } },
            PrimaryButtonText = "Create", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        if (display.IsChecked != true && color.IsChecked != true) { _c.Raise("Preset", "Include a display mode, colors, or both.", NoticeKind.Warning); return; }
        _sel = _c.CaptureCurrent(string.IsNullOrWhiteSpace(name.Text) ? "My preset" : name.Text.Trim(), display.IsChecked == true, color.IsChecked == true);
        Refresh();
    }

    private void OnRestoreBuiltIns(object sender, RoutedEventArgs e)
    {
        _c.Presets.RestoreBuiltIns();
        _c.Raise("Built-in presets restored", "Favorites and shortcuts of built-in presets were reset and the Custom slot was cleared.", NoticeKind.Success);
    }

    // ------------------------------------------------------------ import / export

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".noxel");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.Hwnd(App.Main!));
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await ImportFiles(new[] { file });
    }

    private async Task ImportFiles(IEnumerable<StorageFile> files)
    {
        int count = 0;
        var problems = new List<string>();
        foreach (var f in files)
        {
            try
            {
                var props = await f.GetBasicPropertiesAsync();
                if (props.Size > 1_000_000) { problems.Add($"{f.Name}: larger than 1 MB."); continue; }
                var text = await FileIO.ReadTextAsync(f);
                var (imported, skipped) = _c.Presets.Import(text);
                count += imported.Count;
                problems.AddRange(skipped.Select(s => $"{f.Name}: {s}"));
                if (imported.Count > 0) _sel = imported[0];
            }
            catch (Exception ex) { problems.Add($"{f.Name}: {ex.Message}"); }
        }
        if (count > 0) _c.Raise("Import", $"Imported {count} preset{(count == 1 ? "" : "s")}." + (problems.Count > 0 ? " " + string.Join(" ", problems) : ""), problems.Count > 0 ? NoticeKind.Warning : NoticeKind.Success);
        else _c.Raise("Import", "Nothing was imported. " + string.Join(" ", problems), NoticeKind.Error);
        Refresh();
    }

    private async void OnExportAll(object sender, RoutedEventArgs e) => await Export(_c.Presets.All.Where(p => !p.IsBuiltIn).Select(p => p.Id).ToList(), "NOXEL presets");

    private async void OnExportOne(object sender, RoutedEventArgs e)
    {
        if (_sel is not null) await Export(new List<Guid> { _sel.Id }, _sel.Name);
    }

    private async Task Export(List<Guid> ids, string suggested)
    {
        if (ids.Count == 0) { _c.Raise("Export", "There are no custom presets to export yet.", NoticeKind.Info); return; }
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = Sanitize(suggested) };
        picker.FileTypeChoices.Add("NOXEL preset file", new List<string> { ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.Hwnd(App.Main!));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            await FileIO.WriteTextAsync(file, _c.Presets.Export(ids));
            _c.Raise("Export", $"Saved {ids.Count} preset{(ids.Count == 1 ? "" : "s")} to {file.Name}.", NoticeKind.Success);
        }
        catch (Exception ex) { _c.Raise("Export", ex.Message, NoticeKind.Error); }
    }

    private static string Sanitize(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Import presets";
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        var files = items.OfType<StorageFile>().Where(f => f.FileType is ".json" or ".noxel").Take(20).ToList();
        if (files.Count > 0) await ImportFiles(files);
    }
}
