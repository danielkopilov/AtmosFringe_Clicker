using System.Runtime.InteropServices;
using System.Text;
using WinButton = System.Windows.Forms.Button;
using WinLabel = System.Windows.Forms.Label;

public class MainForm : Form
{
    private WinButton _grabButton;
    private WinLabel _statusLabel;
    private const string SaveFolder = @"C:\temp\rec\FullFrame";
    private const string AtmosFringePath = @"C:\Program Files (x86)\AtmosFringe\AtmosFringe3_3.exe";
    private string _lastSavedFile = string.Empty;

    #region Win32
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc f, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumWindowsProc f, IntPtr lParam);
    [DllImport("user32.dll")] static extern int  GetWindowText(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern int  GetClassName(IntPtr hWnd, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, string l);

    const uint WM_SETTEXT = 0x000C;
    const uint WM_KEYDOWN = 0x0100;
    const uint WM_KEYUP   = 0x0101;
    const uint GA_ROOT    = 2;

    static void PostKey(IntPtr hWnd, ushort vk)
    {
        PostMessage(hWnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
        PostMessage(hWnd, WM_KEYUP,   (IntPtr)vk, IntPtr.Zero);
    }
    #endregion

    private static string GetNextSaveFileName()
    {
        Directory.CreateDirectory(SaveFolder);
        int i = 1;
        while (File.Exists(Path.Combine(SaveFolder, $"{i}.bmp"))) i++;
        return $"{i}.bmp"; // filename only — dialog is already in the right folder
    }

    private static string GetNextSaveFilePath()
    {
        Directory.CreateDirectory(SaveFolder);
        int i = 1;
        while (File.Exists(Path.Combine(SaveFolder, $"{i}.bmp"))) i++;
        return Path.Combine(SaveFolder, $"{i}.bmp");
    }

    public MainForm()
    {
        Text = "AtmosFringe Clicker";
        Size = new Size(300, 160);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _grabButton = new WinButton
        {
            Text = "Grab",
            Size = new Size(200, 50),
            Location = new Point(45, 20),
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        };
        _grabButton.Click += GrabButton_Click;

        _statusLabel = new WinLabel
        {
            Text = "Ready",
            AutoSize = true,
            Location = new Point(45, 85),
            Font = new Font("Segoe UI", 9)
        };

        Controls.Add(_grabButton);
        Controls.Add(_statusLabel);
    }

    // Entry point — runs on UI thread so SendKeys works
    private async void GrabButton_Click(object? sender, EventArgs e)
    {
        _grabButton.Enabled = false;
        SetStatus("Grabbing...");
        try
        {
            // Step 1: Find Vision window (fast, UI thread is fine)
            IntPtr vision = FindWindowByTitle(IsVisionTitle);
            if (vision == IntPtr.Zero)
                throw new InvalidOperationException("Could not find the Device Center Vision window.\nPlease open it first.");

            IntPtr root = GetAncestor(vision, GA_ROOT);
            if (root == IntPtr.Zero) root = vision;

            // Step 2: Bring to foreground
            ShowWindow(root, 9); // SW_RESTORE
            SetForegroundWindow(root);
            await Task.Delay(80);
            SetForegroundWindow(vision);
            await Task.Delay(80);

            // Step 3: SendKeys MUST run on the UI thread — this is the only reliable menu trigger
            SendKeys.SendWait("%C"); // Alt+C = open Capture menu
            await Task.Delay(100);
            SendKeys.SendWait("S");  // S = jumps to "Stop" (first S item)
            await Task.Delay(50);
            SendKeys.SendWait("S");  // S = jumps to "Save still image" (second S item)
            await Task.Delay(50);
            SendKeys.SendWait("{ENTER}"); // Confirm selection
            await Task.Delay(100);

            // Step 4: Determine the filename that will be saved
            _lastSavedFile = GetNextSaveFilePath();

            // Step 5: Handle Save As dialog on background thread
            await Task.Run(HandleSaveAsDialog);

            // Step 6: Load the saved image in AtmosFringe
            SetStatus("Loading in AtmosFringe...");
            await LoadInAtmosFringe(_lastSavedFile);

            SetStatus("Done!");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Grab Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _grabButton.Enabled = true; }
    }

    private void HandleSaveAsDialog()
    {
        // Wait for Save As dialog — poll every 30ms, max 3s
        IntPtr dialog = IntPtr.Zero;
        for (int i = 0; i < 100; i++)
        {
            dialog = FindSaveAsDialog();
            if (dialog != IntPtr.Zero) break;
            Thread.Sleep(30);
        }

        if (dialog == IntPtr.Zero)
            throw new InvalidOperationException("Save As dialog did not appear.");

        SetForegroundWindow(dialog);
        Thread.Sleep(30);

        // Write filename directly into Edit control — no typing simulation
        IntPtr edit = FindChildByClass(dialog, "Edit");
        if (edit == IntPtr.Zero)
            throw new InvalidOperationException("Could not find file name field.");

        SendMessage(edit, WM_SETTEXT, IntPtr.Zero, GetNextSaveFileName());
        Thread.Sleep(20);

        // Enter to save + Enter to confirm extension popup
        PostKey(dialog, 0x0D);
        Thread.Sleep(80);
        PostKey(dialog, 0x0D);
    }

    private async Task LoadInAtmosFringe(string filePath)
    {
        // Find AtmosFringe by its exact title "AtmosFringe  [Interferogram Analysis Software]"
        IntPtr afWnd = FindWindowByTitle(t =>
            t.StartsWith("AtmosFringe", StringComparison.OrdinalIgnoreCase) &&
            t.Contains("Interferogram", StringComparison.OrdinalIgnoreCase));

        if (afWnd == IntPtr.Zero)
        {
            System.Diagnostics.Process.Start(AtmosFringePath);
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(500);
                afWnd = FindWindowByTitle(t =>
                    t.StartsWith("AtmosFringe", StringComparison.OrdinalIgnoreCase) &&
                    t.Contains("Interferogram", StringComparison.OrdinalIgnoreCase));
                if (afWnd != IntPtr.Zero) break;
            }
            // Extra wait after fresh launch so the app is fully ready
            await Task.Delay(1500);
        }

        if (afWnd == IntPtr.Zero)
            throw new InvalidOperationException("Could not find or launch AtmosFringe.");

        // Snapshot windows BEFORE opening the menu
        var windowsBefore = GetAllTopLevelWindowHandles();

        // Bring AtmosFringe to foreground — same pattern that works for Device Center
        ShowWindow(afWnd, 9);
        SetForegroundWindow(afWnd);
        await Task.Delay(150);
        SetForegroundWindow(afWnd);
        await Task.Delay(150);

        // Alt+F opens File menu with "Load Image" already highlighted (first item)
        // Press Enter immediately — no DOWN needed
        SendKeys.SendWait("%F");
        await Task.Delay(100);
        SendKeys.SendWait("{ENTER}");
        await Task.Delay(100);

        // Handle the Open dialog on background thread
        await Task.Run(() => HandleOpenDialog(filePath, windowsBefore));
    }

    private HashSet<IntPtr> GetAllTopLevelWindowHandles()
    {
        var set = new HashSet<IntPtr>();
        EnumWindows((hWnd, _) => { set.Add(hWnd); return true; }, IntPtr.Zero);
        return set;
    }

    private void HandleOpenDialog(string filePath, HashSet<IntPtr> windowsBefore)
    {
        IntPtr dialog = IntPtr.Zero;
        string foundTitle = "";
        var sb = new StringBuilder(256);

        for (int i = 0; i < 100; i++)
        {
            // Strategy 1: new top-level window with a non-empty title
            EnumWindows((hWnd, _) =>
            {
                if (!windowsBefore.Contains(hWnd))
                {
                    GetWindowText(hWnd, sb, 256);
                    var t = sb.ToString();
                    // Must have a real title — skip phantom empty-title windows
                    if (t.Length > 0)
                    { foundTitle = t; dialog = hWnd; return false; }
                }
                // Also check if already-existing windows now have a child dialog
                // (AtmosFringe opens "Load Interferogram Image" as owned by its main window)
                EnumChildWindows(hWnd, (child, _) =>
                {
                    if (!windowsBefore.Contains(child))
                    {
                        GetWindowText(child, sb, 256);
                        var t = sb.ToString();
                        if (t.Contains("Load", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("Open", StringComparison.OrdinalIgnoreCase) ||
                            t.Contains("Image", StringComparison.OrdinalIgnoreCase))
                        { foundTitle = t; dialog = child; return false; }
                    }
                    return true;
                }, IntPtr.Zero);
                return dialog == IntPtr.Zero;
            }, IntPtr.Zero);



            if (dialog != IntPtr.Zero) break;
            Thread.Sleep(30);
        }

        if (dialog == IntPtr.Zero)
        {
            var info = new StringBuilder("AtmosFringe Open dialog did not appear.\n\nAll top-level windows:\n");
            var sb4 = new StringBuilder(256);
            EnumWindows((hWnd, _) =>
            {
                GetWindowText(hWnd, sb4, 256);
                var t = sb4.ToString();
                bool isNew = !windowsBefore.Contains(hWnd);
                if (t.Length > 0) info.AppendLine($"  {(isNew ? "[NEW] " : "      ")}'{t}'");
                return true;
            }, IntPtr.Zero);
            throw new InvalidOperationException(info.ToString());
        }

        SetForegroundWindow(dialog);
        Thread.Sleep(30);

        IntPtr edit = FindChildByClass(dialog, "Edit");
        if (edit == IntPtr.Zero)
        {
            // Dump all child controls for diagnosis
            var info = new StringBuilder($"Could not find Edit in dialog: '{foundTitle}'\n\nChild controls:\n");
            var sb2 = new StringBuilder(256);
            EnumChildWindows(dialog, (child, _) =>
            {
                var cls = new StringBuilder(64);
                GetClassName(child, cls, 64);
                GetWindowText(child, sb2, 256);
                info.AppendLine($"  Class:'{cls}' Text:'{sb2}'");
                return true;
            }, IntPtr.Zero);
            throw new InvalidOperationException(info.ToString());
        }

        SendMessage(edit, WM_SETTEXT, IntPtr.Zero, filePath);
        Thread.Sleep(20);
        PostKey(dialog, 0x0D); // Enter to confirm
    }

    private static bool IsOpenDialogTitle(string t) =>
        t.Equals("Open", StringComparison.OrdinalIgnoreCase) ||
        t.Equals("Load", StringComparison.OrdinalIgnoreCase) ||
        t.Equals("Load Image", StringComparison.OrdinalIgnoreCase) ||
        t.Equals("Open Image", StringComparison.OrdinalIgnoreCase) ||
        t.Equals("Select Image", StringComparison.OrdinalIgnoreCase) ||
        t.Equals("Open File", StringComparison.OrdinalIgnoreCase);

    private IntPtr FindSaveAsDialog()
    {
        IntPtr found = IntPtr.Zero;
        var sb = new StringBuilder(256);
        EnumWindows((hWnd, _) =>
        {
            GetWindowText(hWnd, sb, 256);
            if (sb.ToString().Contains("Save As", StringComparison.OrdinalIgnoreCase))
            { found = hWnd; return false; }
            EnumChildWindows(hWnd, (child, _) =>
            {
                GetWindowText(child, sb, 256);
                if (sb.ToString().Contains("Save As", StringComparison.OrdinalIgnoreCase))
                { found = child; return false; }
                return true;
            }, IntPtr.Zero);
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsVisionTitle(string t) =>
        t.Contains("DirectShow") || t.Contains("FG -") || t.Contains("FG-") ||
        t.Contains("LOS")        || t.Contains("GigE") || t.Contains("CameraLink") ||
        t.Contains("Analog")     || t.Contains("SDI");

    private IntPtr FindWindowByTitle(Func<string, bool> match)
    {
        IntPtr found = IntPtr.Zero;
        var sb = new StringBuilder(256);
        EnumWindows((hWnd, _) =>
        {
            GetWindowText(hWnd, sb, 256);
            if (match(sb.ToString())) { found = hWnd; return false; }
            EnumChildWindows(hWnd, (child, _) =>
            {
                GetWindowText(child, sb, 256);
                if (match(sb.ToString())) { found = child; return false; }
                return true;
            }, IntPtr.Zero);
            return found == IntPtr.Zero;
        }, IntPtr.Zero);
        return found;
    }

    private IntPtr FindChildByClass(IntPtr parent, string cls)
    {
        IntPtr found = IntPtr.Zero;
        var sb = new StringBuilder(64);
        EnumChildWindows(parent, (hWnd, _) =>
        {
            GetClassName(hWnd, sb, 64);
            if (sb.ToString().Equals(cls, StringComparison.OrdinalIgnoreCase)) { found = hWnd; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private void SetStatus(string msg)
    {
        if (_statusLabel.InvokeRequired) _statusLabel.Invoke(() => _statusLabel.Text = msg);
        else _statusLabel.Text = msg;
    }
}
