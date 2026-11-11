using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SaltSteamEditor.Core;
using Wpf.Ui.Appearance;

namespace SaltSteamEditor;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly List<PreviewField> _samples = SongTemplate.Fields.Select(field => new PreviewField(field)).ToList();
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private PluginConfig? _config;
    private string _savedFormat = SongTemplate.Default;
    private string _lastFormat = SongTemplate.Default;
    private bool _ready;
    private bool _restoring;
    private bool _darkTheme = true;
    private IReadOnlyList<TemplateSegment> _segments = [];

    public MainWindow()
    {
        InitializeComponent();
        ApplyFluentTheme();
        PresetBox.ItemsSource = SongTemplate.Presets;
        PresetBox.SelectedIndex = 0;
        FieldButtons.ItemsSource = SongTemplate.Fields;
        SampleFields.ItemsSource = _samples;
        foreach (var sample in _samples)
            sample.PropertyChanged += (_, _) => RefreshPreview();
        TemplateBox.Text = SongTemplate.Default;
        TemplateBox.CaretIndex = TemplateBox.Text.Length;
        _ready = true;
        RefreshEditor();
    }

    private bool IsDirty => TemplateBox.Text != _savedFormat;

    public void OpenPath(string path)
    {
        if (!ConfirmDiscard()) return;
        try
        {
            var loaded = PluginConfig.Load(path);
            _config = loaded;
            _savedFormat = loaded.SongFormat;
            _restoring = true;
            TemplateBox.Text = loaded.SongFormat;
            _lastFormat = loaded.SongFormat;
            _restoring = false;
            _undo.Clear();
            _redo.Clear();
            FileLabel.Text = loaded.Path;
            FileLabel.ToolTip = loaded.Path;
            ReloadButton.IsEnabled = true;
            RefreshEditor();
            SetStatus("配置已打开。保存时只修改 songFormat，并保留其他字段。");
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            ShowError("无法打开配置", exception);
        }
        finally
        {
            _restoring = false;
        }
    }

    private void OpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开 SaltSteamPlugin 的 config.json",
            Filter = "JSON 配置 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) OpenPath(dialog.FileName);
    }

    private void ReloadClick(object sender, RoutedEventArgs e)
    {
        if (_config != null) OpenPath(_config.Path);
    }

    private void SaveClick(object sender, RoutedEventArgs e) => SaveCurrent();
    private void SaveAsClick(object sender, RoutedEventArgs e) => SaveAs();

    private bool SaveCurrent()
    {
        if (_config == null) return SaveAs();
        try
        {
            var backup = _config.Save(TemplateBox.Text);
            Saved(backup);
            return true;
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            ShowError("保存失败", exception);
            return false;
        }
    }

    private bool SaveAs()
    {
        if (!SongTemplate.CanSave(TemplateBox.Text))
        {
            SetStatus("请先移除模板中的控制字符。", true);
            return false;
        }
        var dialog = new SaveFileDialog
        {
            Title = "另存为插件配置",
            Filter = "JSON 配置 (*.json)|*.json",
            FileName = "config.json",
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return false;
        // Saving to the current path must retain the original conflict baseline.
        if (_config != null && string.Equals(Path.GetFullPath(dialog.FileName), _config.Path, StringComparison.OrdinalIgnoreCase))
            return SaveCurrent();
        try
        {
            var result = PluginConfig.Export(dialog.FileName, TemplateBox.Text);
            _config = result.Config;
            FileLabel.Text = _config.Path;
            FileLabel.ToolTip = _config.Path;
            ReloadButton.IsEnabled = true;
            Saved(result.Backup);
            return true;
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            ShowError("无法另存为", exception);
            return false;
        }
    }

    private void Saved(string? backup)
    {
        _savedFormat = TemplateBox.Text;
        RefreshEditor();
        SetStatus(backup == null
            ? "配置已另存。将它放到原插件实际使用的配置位置后生效。"
            : "已保存。原文件备份：" + backup);
    }

    private bool ConfirmDiscard()
    {
        if (!IsDirty) return true;
        var choice = MessageBox.Show(this, "当前模板尚未保存，是否先保存？", "未保存的模板",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return choice == MessageBoxResult.No || (choice == MessageBoxResult.Yes && SaveCurrent());
    }

    private void OnClosing(object? sender, CancelEventArgs e) => e.Cancel = !ConfirmDiscard();

    private void TemplateChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        if (!_restoring && _lastFormat != TemplateBox.Text)
        {
            _undo.Push(_lastFormat);
            _redo.Clear();
        }
        _lastFormat = TemplateBox.Text;
        RefreshEditor();
    }

    private void RefreshEditor()
    {
        _segments = SongTemplate.Parse(TemplateBox.Text);
        SegmentList.ItemsSource = _segments;
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
        SaveButton.IsEnabled = SongTemplate.CanSave(TemplateBox.Text);
        Title = (IsDirty ? "* " : "") + "Salt Steam 状态编辑器";
        AppTitleBar.Title = Title;
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (!_ready) return;
        var preview = SongTemplate.Render(TemplateBox.Text, _samples.ToDictionary(field => field.Key, field => field.Value));
        PreviewText.Text = preview.Length == 0 ? "（状态内容为空）" : preview;
        LengthLabel.Text = $"{preview.EnumerateRunes().Count()} 字符 / {Encoding.UTF8.GetByteCount(preview)} UTF-8 字节";
        ValidationText.Text = SongTemplate.Describe(TemplateBox.Text, preview);
    }

    private void PresetClick(object sender, RoutedEventArgs e)
    {
        if (PresetBox.SelectedItem is TemplatePreset preset)
        {
            TemplateBox.Text = preset.Format;
            TemplateBox.CaretIndex = TemplateBox.Text.Length;
        }
    }

    private void InsertFieldClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string token }) Insert(token);
    }

    private void InsertLiteralClick(object sender, RoutedEventArgs e) => Insert(NewLiteralBox.Text);

    private void Insert(string text)
    {
        var selected = SegmentList.SelectedIndex;
        if (selected >= 0)
        {
            var offset = _segments.Take(selected + 1).Sum(segment => segment.Text.Length);
            TemplateBox.Text = TemplateBox.Text.Insert(offset, text);
            TemplateBox.CaretIndex = offset + text.Length;
        }
        else
        {
            var offset = TemplateBox.SelectionStart;
            var length = TemplateBox.SelectionLength;
            TemplateBox.Text = TemplateBox.Text.Remove(offset, length).Insert(offset, text);
            TemplateBox.CaretIndex = offset + text.Length;
        }
    }

    private void SegmentSelected(object sender, SelectionChangedEventArgs e)
    {
        var index = SegmentList.SelectedIndex;
        var selected = index >= 0 && index < _segments.Count ? _segments[index] : null;
        LeftButton.IsEnabled = index > 0;
        RightButton.IsEnabled = selected != null && index < _segments.Count - 1;
        RemoveButton.IsEnabled = selected != null;
        LiteralPanel.Visibility = selected is { IsField: false } ? Visibility.Visible : Visibility.Collapsed;
        LiteralBox.Text = selected?.Text ?? "";
        SelectionHint.Text = selected == null ? "未选择片段" : selected.IsField ? selected.Text : "固定文字";
    }

    private void TemplateFocused(object sender, KeyboardFocusChangedEventArgs e) => SegmentList.SelectedIndex = -1;
    private void MoveLeftClick(object sender, RoutedEventArgs e) => MoveSegment(-1);
    private void MoveRightClick(object sender, RoutedEventArgs e) => MoveSegment(1);

    private void MoveSegment(int direction)
    {
        var index = SegmentList.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _segments.Count) return;
        var items = _segments.ToList();
        (items[index], items[target]) = (items[target], items[index]);
        var offset = items.Take(target).Sum(segment => segment.Text.Length);
        TemplateBox.Text = string.Concat(items.Select(segment => segment.Text));
        SelectSegmentAt(offset);
    }

    private void SelectSegmentAt(int offset)
    {
        var current = 0;
        for (var i = 0; i < _segments.Count; i++)
        {
            current += _segments[i].Text.Length;
            if (current > offset)
            {
                SegmentList.SelectedIndex = i;
                break;
            }
        }
    }

    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        var index = SegmentList.SelectedIndex;
        if (index >= 0)
            TemplateBox.Text = string.Concat(_segments.Where((_, i) => i != index).Select(segment => segment.Text));
    }

    private void UpdateLiteralClick(object sender, RoutedEventArgs e)
    {
        var index = SegmentList.SelectedIndex;
        if (index < 0 || _segments[index].IsField) return;
        var offset = _segments.Take(index).Sum(segment => segment.Text.Length);
        var literal = LiteralBox.Text;
        TemplateBox.Text = string.Concat(_segments.Select((segment, i) => i == index ? literal : segment.Text));
        SelectSegmentAt(offset);
    }

    private void UndoClick(object sender, RoutedEventArgs e) => RestoreHistory(_undo, _redo);
    private void RedoClick(object sender, RoutedEventArgs e) => RestoreHistory(_redo, _undo);

    private void RestoreHistory(Stack<string> source, Stack<string> destination)
    {
        if (source.Count == 0) return;
        destination.Push(TemplateBox.Text);
        _restoring = true;
        TemplateBox.Text = source.Pop();
        _restoring = false;
        RefreshEditor();
    }

    private void SampleModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        foreach (var sample in _samples)
        {
            var field = SongTemplate.Fields.First(field => field.Key == sample.Key);
            sample.Value = SampleMode.SelectedIndex switch
            {
                1 when sample.Key is "album" or "albumArtist" or "mainLyrics" or "subLyrics" => "",
                2 when sample.Key == "title" => "在漫长的夜晚听见远方传来的声音（现场特别录音版本）",
                2 when sample.Key == "mainLyrics" => string.Concat(Enumerable.Repeat("让晚风带着我们去远方，", 10)),
                _ => field.Example
            };
        }
    }

    private void CopyPreviewClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SongTemplate.Render(TemplateBox.Text, _samples.ToDictionary(field => field.Key, field => field.Value)));
            SetStatus("已复制预览文本。");
        }
        catch (ExternalException exception)
        {
            ShowError("剪贴板被其他程序占用，请重试", exception);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Key == Key.O) { OpenClick(sender, e); e.Handled = true; }
        else if (e.Key == Key.S)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) SaveAs();
            else SaveCurrent();
            e.Handled = true;
        }
        else if ((e.Key is Key.Z or Key.Y) && (TemplateBox.IsKeyboardFocused || Keyboard.FocusedElement is not TextBox))
        {
            if (e.Key == Key.Z) RestoreHistory(_undo, _redo);
            else RestoreHistory(_redo, _undo);
            e.Handled = true;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files)
            OpenPath(files[0]);
        else SetStatus("请一次拖入一个 config.json 文件。", true);
        e.Handled = true;
    }

    private static bool IsFileError(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException or System.Security.SecurityException;

    private void ShowError(string title, Exception exception)
    {
        SetStatus(title + "：" + exception.Message, true);
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.ToolTip = message;
        if (error)
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 143, 128));
        else
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
    }

    private void ThemeClick(object sender, RoutedEventArgs e)
    {
        _darkTheme = !_darkTheme;
        ApplyFluentTheme();
        var palette = _darkTheme
            ? new[] { "#202020", "#282828", "#222222", "#404040", "#F4F4F4", "#B5B5B5", "#8FCBFF", "#293D4E", "#102535" }
            : new[] { "#F3F3F3", "#FFFFFF", "#F7F7F7", "#E1E1E1", "#1A1A1A", "#616161", "#005A9E", "#E8F1FA", "#FFFFFF" };
        var names = new[] { "Canvas", "Surface", "Inset", "Stroke", "Ink", "Muted", "Accent", "TokenFill", "AccentInk" };
        for (var i = 0; i < names.Length; i++)
            Application.Current.Resources[names[i]] = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette[i]));
        ThemeButton.Content = _darkTheme ? "浅色" : "深色";
        ThemeButton.Icon = new Wpf.Ui.Controls.SymbolIcon(_darkTheme
            ? Wpf.Ui.Controls.SymbolRegular.WeatherSunny24 : Wpf.Ui.Controls.SymbolRegular.WeatherMoon24);
    }

    private void AboutClick(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    private void ApplyFluentTheme()
    {
        var theme = _darkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);
        ApplicationAccentColorManager.Apply(System.Windows.Media.Color.FromRgb(0, 112, 196), theme, false, false);
    }
}
