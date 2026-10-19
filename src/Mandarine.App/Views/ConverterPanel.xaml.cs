using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Mandarine.App.Interop;
using Mandarine.App.Services;
using Mandarine.Core;
using Mandarine.Core.Actions;
using static Mandarine.App.Interop.NativeMethods;

namespace Mandarine.App.Views;

internal enum PanelMode
{
    /// <summary>Opened by the drag gesture: invisible until files hover it, closes when the drag ends elsewhere.</summary>
    Gesture,

    /// <summary>Opened with known files (context menu, "Send to", drop on the header): actions are clicked.</summary>
    Picker,

    /// <summary>Persistent drop target opened from the notification area.</summary>
    DropZone,
}

/// <summary>The format menu shown at the cursor.</summary>
public partial class ConverterPanel : Window
{
    private static readonly TimeSpan ReleaseGrace = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan ResultLinger = TimeSpan.FromSeconds(5);

    private readonly IPanelHost _host;
    private readonly DispatcherTimer _lingerTimer;
    private PanelMode _mode;
    private State _state = State.Armed;
    private IReadOnlyList<string> _files = [];
    private bool _filesFromDrag;
    private bool _dropHandled;
    private CancellationTokenSource? _cancellation;
    private IReadOnlyList<string> _outputs = [];
    private (ActionItem Item, IReadOnlyList<string> Files)? _retry;
    private ExternalTool _missingTool;
    private DoneCommand _primaryCommand;
    private DoneCommand _secondaryCommand;
    private bool _movedByUser;

    /// <summary>Where the user last left the drop zone, so reopening it does not snap back to the corner.</summary>
    private static POINT? _dropZonePosition;

    internal ConverterPanel(IPanelHost host, PanelMode mode)
    {
        InitializeComponent();
        _host = host;
        _mode = mode;
        _lingerTimer = new DispatcherTimer { Interval = ResultLinger };
        _lingerTimer.Tick += (_, _) => OnLingerElapsed();
        SizeChanged += (_, _) => KeepOnScreen();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(KeepOnScreen, DispatcherPriority.Loaded);
        MouseEnter += (_, _) => _lingerTimer.Stop();
        MouseLeave += (_, _) =>
        {
            if (_state == State.Done && _primaryCommand != DoneCommand.Install && ErrorText.Visibility != Visibility.Visible)
            {
                _lingerTimer.Start();
            }
        };
    }

    private enum State
    {
        Armed,
        Hint,
        Actions,
        Working,
        Done,
    }

    private enum DoneCommand
    {
        None,
        Reveal,
        Open,
        Install,
        Settings,
        Close,
    }

    // ---- Opening ----

    /// <summary>Gesture mode: an almost transparent window under the cursor, revealed when files hover it.</summary>
    internal void ShowArmed(POINT cursor)
    {
        // Opacity 0.01, not 0: fully transparent pixels of a layered window let drops fall through.
        Card.Opacity = 0.01;
        SetState(State.Armed);
        PlaceAt(cursor, cursorOnHeader: true);
        Show();
    }

    internal void ShowPicker(IReadOnlyList<string> files, POINT cursor)
    {
        LoadFiles(files, fromDrag: false);
        PlaceAt(cursor, cursorOnHeader: false);
        Show();
        Activate();
        FocusFirstAction();
    }

    internal void ShowDropZone()
    {
        SetState(State.Hint);
        var cursor = CursorPosition();
        var (work, scale) = ScreenGeometry.MonitorAt(cursor);
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var width = (int)Math.Round(Width * scale);
        FitHeight(work, scale);
        if (_dropZonePosition is { } saved)
        {
            _movedByUser = true;
            SetWindowPos(hwnd, HWND_TOPMOST, saved.X, saved.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
        else
        {
            SetWindowPos(hwnd, HWND_TOPMOST, work.Right - width, work.Bottom - (int)(260 * scale), 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }

        Show();
        Activate();
    }

    /// <summary>The panel has no title bar: it is moved by grabbing anywhere outside its buttons.</summary>
    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
        _movedByUser = true;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_mode == PanelMode.DropZone && GetWindowRect(hwnd, out var rect))
        {
            _dropZonePosition = new POINT(rect.Left, rect.Top);
        }

        KeepOnScreen();
    }

    internal bool IsDropZone => _mode == PanelMode.DropZone;

    /// <summary>Called when the mouse button goes up after the gesture that opened this panel.</summary>
    internal void OnGestureReleased()
    {
        if (_mode != PanelMode.Gesture)
        {
            return;
        }

        // The drop (if any) reaches us through OLE slightly after the raw mouse-up seen by the hook.
        var timer = new DispatcherTimer { Interval = ReleaseGrace };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_mode == PanelMode.Gesture && !_dropHandled && _state is State.Armed or State.Actions)
            {
                Close();
            }
        };
        timer.Start();
    }

    // ---- Content ----

    private void LoadFiles(IReadOnlyList<string> files, bool fromDrag)
    {
        _files = files;
        _filesFromDrag = fromDrag;

        var items = _host.Catalog.For(files)
            .Select(a => new ActionItem(a, !_host.Tools.IsAvailable(a.Requires)))
            .ToList();
        var conversions = items.Where(i => i.Action.Group == ActionGroup.Convert).ToList();
        var tools = items.Where(i => i.Action.Group == ActionGroup.Tools).ToList();
        ConvertList.ItemsSource = conversions;
        ToolList.ItemsSource = tools;
        ConvertHeader.Visibility = conversions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolsHeader.Visibility = tools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ToolsHeader.Margin = conversions.Count > 0 ? new Thickness(2, 14, 0, 6) : new Thickness(2, 10, 0, 6);

        FooterText.Inlines.Clear();
        if (items.Any(i => i.NeedsInstall))
        {
            FooterText.Inlines.Add(new Run("\uE896") { FontFamily = (FontFamily)FindResource("IconFont") });
            FooterText.Inlines.Add(new Run("  nécessite un outil gratuit, installé au premier usage. "));
        }

        FooterText.Inlines.Add(new Run(fromDrag
            ? "Relâchez sur une action, ou sur l'en-tête pour choisir au clic."
            : "Cliquez sur une action. Échap pour fermer."));

        TitleText.Text = files.Count == 1 ? Path.GetFileName(Path.TrimEndingDirectorySeparator(files[0])) : FileKinds.Describe(files);
        SubtitleText.Text = files.Count == 1 ? Join(FileKinds.Describe(files), SizeOf(files)) : SizeOf(files);
        KindGlyph.Text = GlyphFor(files);
        SetState(State.Actions);
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    private static string? SizeOf(IReadOnlyList<string> files)
    {
        var paths = files.Where(File.Exists).ToList();
        return paths.Count == 0 ? null : FormatSize(paths.Sum(p => new FileInfo(p).Length));
    }

    private static string FormatSize(long bytes)
    {
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        return bytes switch
        {
            < 1024 => $"{bytes} octets",
            < 1024 * 1024 => string.Format(culture, "{0:0} Ko", bytes / 1024d),
            < 1024L * 1024 * 1024 => string.Format(culture, "{0:0.#} Mo", bytes / (1024d * 1024)),
            _ => string.Format(culture, "{0:0.##} Go", bytes / (1024d * 1024 * 1024)),
        };
    }

    private static string GlyphFor(IReadOnlyList<string> files)
    {
        var kinds = files.Select(FileKinds.Detect).Distinct().ToList();
        return kinds.Count != 1 ? "\uE8B7" : kinds[0] switch
        {
            FileKind.Image => "\uE91B",
            FileKind.Audio => "\uE8D6",
            FileKind.Video => "\uE714",
            FileKind.Folder => "\uE8B7",
            FileKind.Archive => "\uE7B8",
            FileKind.Json or FileKind.Yaml or FileKind.Xml or FileKind.Csv or FileKind.Html => "\uE943",
            _ => "\uE8A5",
        };
    }

    private void SetState(State state)
    {
        _state = state;
        HintView.Visibility = state == State.Hint ? Visibility.Visible : Visibility.Collapsed;
        ActionsView.Visibility = state == State.Actions ? Visibility.Visible : Visibility.Collapsed;
        WorkingView.Visibility = state == State.Working ? Visibility.Visible : Visibility.Collapsed;
        DoneView.Visibility = state == State.Done ? Visibility.Visible : Visibility.Collapsed;

        if (state == State.Hint)
        {
            TitleText.Text = "Mandarine";
            SubtitleText.Text = "Zone de dépôt";
            KindGlyph.Text = "\uE898";
        }
    }

    private void Reveal()
    {
        if (Card.Opacity < 1)
        {
            Card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        }
    }

    private void FocusFirstAction() =>
        Dispatcher.BeginInvoke(() => MoveFocus(new TraversalRequest(FocusNavigationDirection.First)), DispatcherPriority.Input);

    // ---- Drag and drop on the panel ----

    protected override void OnPreviewDragEnter(DragEventArgs e)
    {
        base.OnPreviewDragEnter(e);
        if (_state is State.Working || (_state is State.Done && _mode != PanelMode.DropZone))
        {
            return;
        }

        var files = ReadFiles(e.Data);
        if (files.Count == 0)
        {
            return;
        }

        if (_state != State.Actions || !_filesFromDrag || !files.SequenceEqual(_files))
        {
            _lingerTimer.Stop();
            LoadFiles(files, fromDrag: true);
        }

        Reveal();
    }

    /// <summary>Dragging over the background (not a tile): dropping there keeps the panel open for clicking.</summary>
    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = _state == State.Actions && _filesFromDrag ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        e.Handled = true;
        var files = ReadFiles(e.Data);
        if (_state != State.Actions || files.Count == 0)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = DragDropEffects.Copy;
        _dropHandled = true;
        if (_mode == PanelMode.Gesture)
        {
            _mode = PanelMode.Picker;
        }

        LoadFiles(files, fromDrag: false);
        Activate();
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        var position = e.GetPosition(Card);
        var left = position.X < 0 || position.Y < 0 || position.X > Card.ActualWidth || position.Y > Card.ActualHeight;
        if (left && _mode == PanelMode.DropZone && _state == State.Actions && _filesFromDrag)
        {
            SetState(State.Hint);
        }
    }

    private void OnActionsPreviewDragOver(object sender, DragEventArgs e)
    {
        // ScrollViewer does not scroll by itself while something is dragged.
        const double edge = 28;
        var y = e.GetPosition(ActionsView).Y;
        if (y < edge)
        {
            ActionsView.ScrollToVerticalOffset(ActionsView.VerticalOffset - 8);
        }
        else if (y > ActionsView.ActualHeight - edge)
        {
            ActionsView.ScrollToVerticalOffset(ActionsView.VerticalOffset + 8);
        }
    }

    private static IReadOnlyList<string> ReadFiles(IDataObject data)
    {
        try
        {
            return data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths
                ? paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList()
                : [];
        }
        catch (COMException)
        {
            return [];
        }
    }

    // ---- Tiles ----

    private static ActionItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as ActionItem;

    private void OnTileDragEnter(object sender, DragEventArgs e)
    {
        if (ItemOf(sender) is { } item && _state == State.Actions && _filesFromDrag)
        {
            item.IsHot = true;
        }
    }

    private void OnTileDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _state == State.Actions && _filesFromDrag ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTileDragLeave(object sender, DragEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            item.IsHot = false;
        }
    }

    private void OnTileDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (ItemOf(sender) is not { } item)
        {
            return;
        }

        item.IsHot = false;
        var files = ReadFiles(e.Data);
        if (_state != State.Actions || files.Count == 0)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        // Copy, never Move: Explorer must not delete the original even though Shift (= move) is held.
        e.Effects = DragDropEffects.Copy;
        _dropHandled = true;

        // Return immediately: Explorer is blocked until the drop handler returns.
        Dispatcher.BeginInvoke(() => _ = RunAsync(item, files));
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item && _state == State.Actions && _files.Count > 0)
        {
            _ = RunAsync(item, _files);
        }
    }

    // ---- Running ----

    private async Task RunAsync(ActionItem item, IReadOnlyList<string> files)
    {
        _lingerTimer.Stop();
        if (item.NeedsInstall && !_host.Tools.IsAvailable(item.Action.Requires))
        {
            ShowMissingTool(item.Action.Requires, item, files);
            return;
        }

        SetState(State.Working);
        WorkingText.Text = item.WorkingLabel + "…";
        WorkingDetail.Text = files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} fichiers";
        WorkingProgress.IsIndeterminate = true;
        _cancellation = new CancellationTokenSource();

        var progress = new Progress<ActionProgress>(p =>
        {
            if (p.FileFraction is not null || p.Count > 1)
            {
                WorkingProgress.IsIndeterminate = false;
                WorkingProgress.Value = p.Overall;
            }

            if (p.CurrentFile is { } current && p.Count > 1)
            {
                WorkingDetail.Text = $"{Path.GetFileName(current)} ({p.Index + 1}/{p.Count})";
            }
        });

        ActionResult result;
        try
        {
            var context = _host.CreateContext();
            var token = _cancellation.Token;
            result = await Task.Run(() => item.Action.RunAsync(files, context, progress, token), token);
        }
        catch (OperationCanceledException)
        {
            CloseOrReset();
            return;
        }
        catch (MissingToolException ex)
        {
            ShowMissingTool(ex.Tool, item, files);
            return;
        }
        catch (Exception ex)
        {
            result = new ActionResult([], [new ActionError(files[0], ex.Message)]);
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }

        ShowResult(result);
    }

    private void ShowResult(ActionResult result)
    {
        _outputs = result.OutputFiles;
        var copied = result.ClipboardText is { } text && TrySetClipboard(text);
        var succeeded = result.OutputFiles.Count;
        var failed = result.Errors.Count;

        DoneGlyph.Text = failed == 0 ? "\uE73E" : "\uE783";
        DoneGlyph.Foreground = (Brush)FindResource(failed == 0 ? "Success" : "Error");
        DoneTitle.Text = (failed, succeeded) switch
        {
            (0, _) when copied => "Copié dans le presse-papiers",
            (0, 0) => "Terminé",
            (0, 1) => "Terminé",
            (0, _) => $"{succeeded} éléments créés",
            (_, 0) => "La conversion a échoué",
            _ => $"{succeeded} réussi{(succeeded > 1 ? "s" : string.Empty)}, {failed} en échec",
        };
        DoneDetail.Text = succeeded switch
        {
            1 => Path.GetFileName(result.OutputFiles[0]),
            > 1 => Path.GetDirectoryName(result.OutputFiles[0]),
            _ => copied ? Preview(result.ClipboardText!) : string.Empty,
        };
        DoneDetail.Visibility = string.IsNullOrEmpty(DoneDetail.Text) ? Visibility.Collapsed : Visibility.Visible;
        ErrorText.Text = string.Join(Environment.NewLine, result.Errors.Take(3).Select(e => $"{Path.GetFileName(e.Input)} : {e.Message}"))
            + (failed > 3 ? $"{Environment.NewLine}… et {failed - 3} autre(s)." : string.Empty);
        ErrorText.Visibility = failed > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (succeeded == 1 && File.Exists(result.OutputFiles[0]))
        {
            SetButtons(("Ouvrir", DoneCommand.Open), ("Afficher dans l'Explorateur", DoneCommand.Reveal));
        }
        else if (succeeded > 0)
        {
            SetButtons(("Afficher dans l'Explorateur", DoneCommand.Reveal), null);
        }
        else
        {
            SetButtons(("Fermer", DoneCommand.Close), null);
        }

        SetState(State.Done);
        if (failed == 0 && !IsMouseOver)
        {
            _lingerTimer.Start();
        }
    }

    private static string Preview(string text) => text.Length <= 60 ? text : text[..57] + "…";

    private static bool TrySetClipboard(string text)
    {
        // Another application may hold the clipboard for a few milliseconds.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(60);
            }
        }

        return false;
    }

    private void ShowMissingTool(ExternalTool tool, ActionItem item, IReadOnlyList<string> files)
    {
        _missingTool = tool;
        _retry = (item, files);
        var name = ToolLocator.DisplayName(tool);
        DoneGlyph.Text = "\uE896";
        DoneGlyph.Foreground = (Brush)FindResource("Accent");
        DoneTitle.Text = $"{name} est nécessaire";
        DoneDetail.Text = ToolInstaller.Purpose(tool) + " Mandarine peut l'installer pour vous (winget), puis reprendra la conversion.";
        DoneDetail.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
        SetButtons(($"Installer {name}", DoneCommand.Install), ("J'ai déjà l'outil…", DoneCommand.Settings));
        SetState(State.Done);
        Activate();
    }

    private async Task InstallAndRetryAsync()
    {
        var name = ToolLocator.DisplayName(_missingTool);
        SetState(State.Working);
        WorkingText.Text = $"Installation de {name}…";
        WorkingDetail.Text = "Suivez la fenêtre d'installation.";
        WorkingProgress.IsIndeterminate = true;

        if (await _host.InstallToolAsync(_missingTool) && _retry is { } retry)
        {
            _retry = null;
            await RunAsync(new ActionItem(retry.Item.Action, needsInstall: false), retry.Files);
            return;
        }

        ShowResult(new ActionResult([], [new ActionError(name,
            $"{name} n'a pas pu être installé automatiquement. Indiquez son emplacement dans les paramètres.")]));
        SetButtons(("Paramètres", DoneCommand.Settings), ("Fermer", DoneCommand.Close));
    }

    private void SetButtons((string Label, DoneCommand Command)? primary, (string Label, DoneCommand Command)? secondary)
    {
        _primaryCommand = primary?.Command ?? DoneCommand.None;
        _secondaryCommand = secondary?.Command ?? DoneCommand.None;
        PrimaryButton.Content = primary?.Label;
        PrimaryButton.Visibility = primary is null ? Visibility.Collapsed : Visibility.Visible;
        SecondaryButton.Content = secondary?.Label;
        SecondaryButton.Visibility = secondary is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => Execute(_primaryCommand);

    private void OnSecondaryClick(object sender, RoutedEventArgs e) => Execute(_secondaryCommand);

    private void Execute(DoneCommand command)
    {
        switch (command)
        {
            case DoneCommand.Open when _outputs.Count > 0:
                Process.Start(new ProcessStartInfo(_outputs[0]) { UseShellExecute = true });
                CloseOrReset();
                break;
            case DoneCommand.Reveal when _outputs.Count > 0:
                Process.Start("explorer.exe", $"/select,\"{_outputs[0]}\"");
                CloseOrReset();
                break;
            case DoneCommand.Install:
                _ = InstallAndRetryAsync();
                break;
            case DoneCommand.Settings:
                CloseOrReset();
                Dispatcher.BeginInvoke(_host.OpenSettings);
                break;
            case DoneCommand.Close:
                CloseOrReset();
                break;
        }
    }

    // ---- Closing ----

    private void OnLingerElapsed()
    {
        _lingerTimer.Stop();
        if (_state == State.Done && !IsMouseOver)
        {
            CloseOrReset();
        }
    }

    private void CloseOrReset()
    {
        _lingerTimer.Stop();
        if (_mode == PanelMode.DropZone)
        {
            _files = [];
            SetState(State.Hint);
        }
        else
        {
            Close();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_state != State.Working && _mode != PanelMode.DropZone)
        {
            Close();
        }

        Dispatcher.BeginInvoke(_host.OpenSettings);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_state == State.Working)
        {
            _cancellation?.Cancel();
        }
        else
        {
            Close();
        }

        e.Handled = true;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_mode == PanelMode.Picker && _state == State.Actions)
        {
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _lingerTimer.Stop();
        _cancellation?.Cancel();
        base.OnClosed(e);
    }

    // ---- Placement (physical pixels, per-monitor DPI) ----

    private void PlaceAt(POINT cursor, bool cursorOnHeader)
    {
        var (work, scale) = ScreenGeometry.MonitorAt(cursor);
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var width = (int)Math.Round(Width * scale);

        // Cursor over the header: the format tiles are right below it, a short move away.
        var x = cursor.X - (width / 2);
        var y = cursorOnHeader ? cursor.Y - (int)Math.Round(42 * scale) : cursor.Y + (int)Math.Round(4 * scale);
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - (int)Math.Round(160 * scale)));

        FitHeight(work, scale);
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Long action lists scroll instead of running off a small or highly scaled screen.</summary>
    private void FitHeight(RECT work, double scale) => ActionsView.MaxHeight = Math.Max(220, (work.Height / scale) - 170);

    /// <summary>Slides the window back inside the work area when it grows (drop zone stays docked bottom-right).</summary>
    private void KeepOnScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        var (work, _) = ScreenGeometry.MonitorAt(new POINT((rect.Left + rect.Right) / 2, rect.Top));
        var dx = 0;
        var dy = _mode == PanelMode.DropZone && !_movedByUser ? work.Bottom - rect.Bottom : 0;
        if (rect.Bottom + dy > work.Bottom)
        {
            dy = work.Bottom - rect.Bottom;
        }

        if (rect.Top + dy < work.Top)
        {
            dy = work.Top - rect.Top;
        }

        if (rect.Right > work.Right)
        {
            dx = work.Right - rect.Right;
        }

        if (rect.Left + dx < work.Left)
        {
            dx = work.Left - rect.Left;
        }

        if (dx != 0 || dy != 0)
        {
            SetWindowPos(hwnd, IntPtr.Zero, rect.Left + dx, rect.Top + dy, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }
}
