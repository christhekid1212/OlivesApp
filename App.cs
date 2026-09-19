using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OlivesApp;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Velopack.VelopackApp.Build().Run();
        if (args.Contains("--self-test")) return SelfTest.Run();
        try { DataStorage.Initialize(); }
        catch (Exception ex)
        {
            MessageBox.Show("Δεν ήταν δυνατή η προετοιμασία του αρχείου.\nΤα παλιά αρχεία παραμένουν στη θέση τους.\n\n" + ex.Message, "OlivesApp", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
        var app = new Application();
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show("Παρουσιάστηκε σφάλμα: " + e.Exception.Message, "OlivesApp", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        app.Run(new MainWindow());
        return 0;
    }
}

public sealed class MainWindow : Window
{
    private readonly Archive archive = new(DataStorage.Folder);
    private readonly List<(TextBox Weight, TextBox Bins)> inputs = [];
    private readonly Brush green = new SolidColorBrush(Color.FromRgb(70, 91, 49));
    private bool editing;
    private TextBox sequence = null!;

    public MainWindow()
    {
        Title = "OlivesApp";
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/olive.ico"));
        Width = 820; Height = 780; MinWidth = 660; MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(246, 247, 242));
        FontFamily = new FontFamily("Segoe UI"); FontSize = 15;
        Closing += (_, e) => { if (editing && !ConfirmCancel()) e.Cancel = true; };
        Home();
        ContentRendered += CheckUpdatesOnStartup;
    }

    private StackPanel Page(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(38, 28, 38, 28) };
        panel.Children.Add(new TextBlock { Text = "OLIVESAPP  /  ΑΡΧΕΙΟ ΠΑΡΑΓΩΓΗΣ", Foreground = green, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        panel.Children.Add(new TextBlock { Text = title, FontSize = 34, FontWeight = FontWeights.SemiBold, Foreground = green });
        panel.Children.Add(new TextBlock { Text = subtitle, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 26) });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return panel;
    }

    private Button Button(string label, Action action, bool primary = true)
    {
        var button = new Button { Content = label, Padding = new Thickness(22, 12, 22, 12), Margin = new Thickness(0, 0, 12, 0), Background = primary ? green : Brushes.White, Foreground = primary ? Brushes.White : green, BorderBrush = green, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, FontSize = 16 };
        button.Click += (_, _) => action();
        return button;
    }

    private void Home()
    {
        editing = false;
        var panel = Page("OlivesApp", "Οι καταχωρήσεις σας, απλά και οργανωμένα.");
        panel.Children.Add(new TextBlock { Text = "Διαχείριση ελιάς", FontSize = 22, Margin = new Thickness(0, 35, 0, 10) });
        panel.Children.Add(new TextBlock { Text = "Δημιουργήστε μια καταχώρηση ή ανοίξτε το αρχείο σας.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 28) });
        var buttons = new WrapPanel();
        buttons.Children.Add(Button("Καταχώρηση", Entry));
        buttons.Children.Add(Button("Αναζήτηση", () =>
        {
            try { Directory.CreateDirectory(archive.Folder); Process.Start(new ProcessStartInfo(archive.Folder) { UseShellExecute = true }); }
            catch (Exception ex) { Error(ex); }
        }, false));
        panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock { Text = "Έκδοση " + Updates.CurrentVersion, FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "ΤΟΠΙΚΗ ΑΠΟΘΗΚΕΥΣΗ\n" + archive.Folder, FontSize = 12, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 65, 0, 0) });
        var pageContent = (UIElement)Content;
        Content = null;
        var layout = new DockPanel();
        var closeButton = Button("Κλείσιμο", Close, false);
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        closeButton.Margin = new Thickness(38, 12, 38, 28);
        var footer = new DockPanel();
        DockPanel.SetDock(closeButton, Dock.Right);
        footer.Children.Add(closeButton);
        var importLink = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Εισαγωγή παλιού αρχείου excel")) { Foreground = green };
        importLink.Click += (_, _) => ImportArchive();
        var importText = new TextBlock { Margin = new Thickness(38, 12, 12, 28), VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        importText.Inlines.Add(importLink);
        footer.Children.Add(importText);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        layout.Children.Add(pageContent);
        Content = layout;
    }

    private void CheckUpdatesOnStartup(object? sender, EventArgs e)
    {
        ContentRendered -= CheckUpdatesOnStartup;
        new UpdateWindow { Owner = this }.ShowDialog();
    }

    private void Entry()
    {
        long next;
        try { next = archive.NextNumber(); } catch (Exception ex) { Error(ex); return; }
        editing = true; inputs.Clear();
        var panel = Page("Καταχώρηση", "Συμπληρώστε το βάρος σε kg και τα τεμάχια ανά κατηγορία.");
        var grid = new Grid();
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 10; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(43) });
        void Add(UIElement element, int row, int column) { Grid.SetRow(element, row); Grid.SetColumn(element, column); grid.Children.Add(element); }
        string[] headers = ["Κατηγορία", "Βάρος (kg)", "Bins Τεμάχια"];
        for (int i = 0; i < 3; i++) Add(new Border { Background = green, Padding = new Thickness(12, 9, 12, 9), Child = new TextBlock { Text = headers[i], Foreground = Brushes.White, FontWeight = FontWeights.SemiBold } }, 0, i);
        for (int i = 0; i < Archive.Categories.Length; i++)
        {
            Add(new Border { Background = Brushes.White, BorderBrush = Brushes.Gainsboro, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(12, 9, 12, 9), Child = new TextBlock { Text = Archive.Categories[i].ToString(), FontWeight = FontWeights.SemiBold } }, i + 1, 0);
            var weight = Cell($"Βάρος κατηγορίας {Archive.Categories[i]}", true);
            var bins = Cell($"Bins κατηγορίας {Archive.Categories[i]}", false);
            Add(weight, i + 1, 1); Add(bins, i + 1, 2); inputs.Add((weight, bins));
        }
        var totalWeight = new TextBlock();
        var totalBins = new TextBlock();
        TextBlock[] totals = [new TextBlock { Text = "Σύνολα" }, totalWeight, totalBins];
        for (int i = 0; i < totals.Length; i++)
        {
            totals[i].Foreground = Brushes.White;
            totals[i].FontWeight = FontWeights.SemiBold;
            Add(new Border { Background = green, Padding = new Thickness(12, 9, 12, 9), Child = totals[i] }, 9, i);
        }
        void UpdateTotals()
        {
            decimal weightSum = 0;
            long binsSum = 0;
            foreach (var input in inputs)
            {
                if (Values.Weight(input.Weight.Text, out var weight)) weightSum += weight;
                if (Values.Bins(input.Bins.Text, out var bins)) binsSum += bins;
            }
            totalWeight.Text = weightSum.ToString("0.###", CultureInfo.GetCultureInfo("el-GR"));
            totalBins.Text = binsSum.ToString(CultureInfo.GetCultureInfo("el-GR"));
        }
        foreach (var input in inputs)
        {
            input.Weight.TextChanged += (_, _) => UpdateTotals();
            input.Bins.TextChanged += (_, _) => UpdateTotals();
        }
        UpdateTotals();
        panel.Children.Add(grid);
        panel.Children.Add(new TextBlock { Text = "Δεκαδικά με κόμμα ή τελεία · Τα κενά πεδία αποθηκεύονται ως 0.", FontSize = 12, Foreground = Brushes.DimGray, Margin = new Thickness(0, 12, 0, 20) });
        var footer = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var numberPanel = new StackPanel { Margin = new Thickness(0, 0, 25, 12) };
        numberPanel.Children.Add(new TextBlock { Text = "Αύξων αριθμός", FontSize = 12, Foreground = Brushes.DimGray });
        sequence = new TextBox { Text = next.ToString("D6"), IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 23, MinWidth = 110 };
        numberPanel.Children.Add(sequence); footer.Children.Add(numberPanel);
        footer.Children.Add(Button("Αποθήκευση", Save));
        footer.Children.Add(Button("Άκυρο", () => { if (ConfirmCancel()) Home(); }, false));
        panel.Children.Add(footer);
        inputs[0].Weight.Focus();
    }

    private void ImportArchive()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Επιλέξτε τον παλιό φάκελο Αρχείο" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            int count = DataStorage.Import(picker.FolderName, archive.Folder);
            MessageBox.Show($"Η εισαγωγή ολοκληρώθηκε. Νέα αρχεία: {count}.\nΤα πρωτότυπα διατηρήθηκαν.", "OlivesApp", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { Error(ex); }
    }

    internal static TextBox Cell(string name, bool allowDecimal)
    {
        var box = new TextBox { Padding = new Thickness(10, 8, 10, 8), BorderBrush = Brushes.Gainsboro, BorderThickness = new Thickness(0, 0, 1, 1), VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 16 };
        System.Windows.Automation.AutomationProperties.SetName(box, name);
        string Proposed(string text) => box.Text.Remove(box.SelectionStart, box.SelectionLength).Insert(box.SelectionStart, text);
        box.PreviewTextInput += (_, e) => e.Handled = !Values.CanEdit(Proposed(e.Text), allowDecimal);
        box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Space) e.Handled = true; };
        DataObject.AddPastingHandler(box, (_, e) =>
        {
            if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !Values.CanEdit(Proposed(text), allowDecimal))
                e.CancelCommand();
        });
        // Covers drag/drop, input methods and programmatic text changes as well.
        string lastValid = "";
        box.TextChanged += (_, _) =>
        {
            if (Values.CanEdit(box.Text, allowDecimal)) { lastValid = box.Text; return; }
            int caret = box.CaretIndex;
            box.Text = lastValid;
            box.CaretIndex = Math.Min(caret, lastValid.Length);
        };
        return box;
    }

    private void Save()
    {
        var rows = new List<EntryRow>();
        for (int i = 0; i < inputs.Count; i++)
        {
            var (weight, bins) = inputs[i];
            if (!Values.Weight(weight.Text, out var w)) { Invalid(weight, $"Κατηγορία {Archive.Categories[i]}: πληκτρολογήστε έγκυρο, μη αρνητικό βάρος (έως 3 δεκαδικά ψηφία)."); return; }
            if (!Values.Bins(bins.Text, out var b)) { Invalid(bins, $"Κατηγορία {Archive.Categories[i]}: τα Bins πρέπει να είναι μη αρνητικός ακέραιος αριθμός."); return; }
            rows.Add(new(Archive.Categories[i], w, b));
        }
        if (!rows.Any(r => r.Weight > 0 || r.Bins > 0)) { Invalid(inputs[0].Weight, "Συμπληρώστε τουλάχιστον ένα βάρος ή ένα τεμάχιο πριν από την αποθήκευση."); return; }
        if (MessageBox.Show("Είστε σίγουροι ότι θέλετε να προχωρήσετε στην αποθήκευση της καταχώρησης;", "Επιβεβαίωση αποθήκευσης", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            var file = archive.Save(rows);
            editing = false;
            MessageBox.Show("Η καταχώρηση αποθηκεύτηκε επιτυχώς.\n\n" + file, "Αποθήκευση", MessageBoxButton.OK, MessageBoxImage.Information);
            Home();
        }
        catch (Exception ex) { Error(ex); }
    }

    private static void Invalid(TextBox box, string message) { MessageBox.Show(message, "Έλεγχος στοιχείων", MessageBoxButton.OK, MessageBoxImage.Warning); box.Focus(); box.SelectAll(); }
    private static bool ConfirmCancel() => MessageBox.Show("Είστε σίγουροι ότι θέλετε να ακυρώσετε την καταχώρηση;\nΤα στοιχεία που δεν έχουν αποθηκευτεί θα χαθούν.", "Επιβεβαίωση ακύρωσης", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    private static void Error(Exception ex) => MessageBox.Show("Η ενέργεια δεν ολοκληρώθηκε. Τα στοιχεία της φόρμας διατηρούνται.\n\n" + ex.Message, "OlivesApp", MessageBoxButton.OK, MessageBoxImage.Error);
}

public static class Values
{
    public static bool CanEdit(string text, bool allowDecimal)
    {
        if (text.Length == 0) return true;
        if (text.Any(c => c < '0' || c > '9'))
        {
            if (!allowDecimal || text.Any(c => (c < '0' || c > '9') && c != ',' && c != '.')) return false;
            if (text.Count(c => c == ',' || c == '.') > 1) return false;
        }
        if (!allowDecimal) return Bins(text, out _);
        int separator = text.IndexOfAny([',', '.']);
        if (separator >= 0 && text.Length - separator - 1 > 3) return false;
        return text is "," or "." || Weight(text, out _);
    }

    public static bool Weight(string text, out decimal value)
    {
        text = text.Trim().Replace(',', '.');
        if (text.Length == 0) { value = 0; return true; }
        return decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) && value >= 0 && value <= 999999999 && decimal.Round(value, 3) == value;
    }
    public static bool Bins(string text, out int value)
    {
        text = text.Trim();
        if (text.Length == 0) { value = 0; return true; }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
    }
}
