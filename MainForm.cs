using System.Runtime.InteropServices;
using System.Text;
using WinButton = System.Windows.Forms.Button;

public class MainForm : Form
{
    private WinButton _grabButton;
    private WinButton _openDCButton;
    private WinButton _browseFolderButton;
    private RichTextBox _folderLabel;   // shows current save folder
    private RichTextBox _imageLabel;    // "Last saved image: N"
    private RichTextBox _statusLabel;   // "Status: Done!"
    private const string DeviceCenterPath = @"C:\CI Systems\CTE\DeviceCenter64\DeviceCenter_x64.exe";
    private const string DefaultSaveFolder = @"C:\temp\rec\FullFrame";
    private string _saveFolder = @"C:\temp\rec\FullFrame";
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
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    static extern IntPtr SendMessagePtr(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
    [DllImport("user32.dll")] static extern bool SetFocus(IntPtr hWnd);

    const uint WM_SETTEXT  = 0x000C;
    const uint WM_KEYDOWN  = 0x0100;
    const uint WM_KEYUP    = 0x0101;
    const uint BM_CLICK    = 0x00F5;
    const uint GA_ROOT     = 2;
    const ushort VK_CONTROL = 0x11;
    // Standard control IDs inside a Windows IFileOpenDialog / GetOpenFileName dialog
    const int FILENAME_COMBO_ID = 0x047C;   // filename ComboBoxEx32
    const int OPEN_BUTTON_ID    = 0x0001;   // "Open" / "OK" button

    static void PostKey(IntPtr hWnd, ushort vk, bool ctrl = false)
    {
        if (ctrl) PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_CONTROL, IntPtr.Zero);
        PostMessage(hWnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
        PostMessage(hWnd, WM_KEYUP,   (IntPtr)vk, IntPtr.Zero);
        if (ctrl) PostMessage(hWnd, WM_KEYUP, (IntPtr)VK_CONTROL, IntPtr.Zero);
    }
    #endregion

    private string GetNextSaveFileName()
    {
        Directory.CreateDirectory(_saveFolder);
        int i = 1;
        while (File.Exists(Path.Combine(_saveFolder, $"{i}.bmp"))) i++;
        return $"{i}.bmp";
    }

    private string GetNextSaveFilePath()
    {
        Directory.CreateDirectory(_saveFolder);
        int i = 1;
        while (File.Exists(Path.Combine(_saveFolder, $"{i}.bmp"))) i++;
        return Path.Combine(_saveFolder, $"{i}.bmp");
    }

    public MainForm()
    {
        Text = "AtmosFringe Clicker";
        Size = new Size(320, 270);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        _grabButton = new WinButton
        {
            Text = "Grab",
            Size = new Size(200, 50),
            Location = new Point(55, 15),
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        };
        _grabButton.Click += GrabButton_Click;

        _openDCButton = new WinButton
        {
            Text = "Open Device Center",
            Size = new Size(200, 32),
            Location = new Point(55, 75),
            Font = new Font("Segoe UI", 10)
        };
        _openDCButton.Click += OpenDCButton_Click;

        _browseFolderButton = new WinButton
        {
            Text = "Browse Save Folder...",
            Size = new Size(200, 32),
            Location = new Point(55, 117),
            Font = new Font("Segoe UI", 10)
        };
        _browseFolderButton.Click += BrowseFolderButton_Click;

        // Shows the currently selected save folder path
        _folderLabel = new RichTextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            Size = new Size(280, 20),
            Location = new Point(15, 158),
            Font = new Font("Segoe UI", 8),
            ScrollBars = RichTextBoxScrollBars.None,
            TabStop = false,
            WordWrap = true
        };
        SetFolderLabel(_saveFolder);

        // Row: "Last saved image: N"
        _imageLabel = new RichTextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            Size = new Size(260, 22),
            Location = new Point(20, 182),
            Font = new Font("Segoe UI", 9),
            ScrollBars = RichTextBoxScrollBars.None,
            TabStop = false
        };
        SetImageLabel("-");

        // Row: "Status: Ready"
        _statusLabel = new RichTextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Control,
            Size = new Size(260, 22),
            Location = new Point(20, 204),
            Font = new Font("Segoe UI", 9),
            ScrollBars = RichTextBoxScrollBars.None,
            TabStop = false
        };
        SetStatus("Ready");

        Controls.Add(_grabButton);
        Controls.Add(_openDCButton);
        Controls.Add(_browseFolderButton);
        Controls.Add(_folderLabel);
        Controls.Add(_imageLabel);
        Controls.Add(_statusLabel);
    }

    private void BrowseFolderButton_Click(object? sender, EventArgs e)
    {
        string? selected = null;
        string startFolder = _saveFolder;

        // Run on a dedicated STA thread — required for shell dialogs in some environments
        var thread = new Thread(() =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Select folder to save images",
                InitialDirectory = startFolder,
                FileName = "Select Folder",
                Filter = "Folder|*.none",
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                selected = Path.GetDirectoryName(dlg.FileName);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (selected != null)
        {
            _saveFolder = selected;
            SetFolderLabel(_saveFolder);
        }
    }

    private void SetFolderLabel(string folder)
    {
        _folderLabel.Clear();
        _folderLabel.SelectionColor = Color.Black;
        _folderLabel.AppendText("Save folder: ");
        _folderLabel.SelectionColor = Color.Blue;
        _folderLabel.AppendText(folder);
    }

    private void OpenDCButton_Click(object? sender, EventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(DeviceCenterPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not open Device Center", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

            // Step 4: Determine the filename that will be saved and store it
            _lastSavedFile = GetNextSaveFilePath();

            // Step 5: Handle Save As dialog — types _lastSavedFile full path into Device Center
            await HandleSaveAsDialog();

            // Update "Last saved image" label with the number (e.g. "75")
            string imageNumber = Path.GetFileNameWithoutExtension(_lastSavedFile);
            SetImageLabel(imageNumber);

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

    private async Task HandleSaveAsDialog()
    {
        // Poll for the Save As dialog — max 3 seconds
        IntPtr dialog = IntPtr.Zero;
        for (int i = 0; i < 100; i++)
        {
            dialog = FindSaveAsDialog();
            if (dialog != IntPtr.Zero) break;
            await Task.Delay(30);
        }

        if (dialog == IntPtr.Zero)
            throw new InvalidOperationException("Save As dialog did not appear.");

        // Bring dialog to foreground so SendKeys targets it
        ShowWindow(dialog, 9);
        SetForegroundWindow(dialog);
        await Task.Delay(300);

        // Use _lastSavedFile — already computed with the correct _saveFolder path
        // Escape SendKeys special characters in the path
        string escaped = _lastSavedFile
            .Replace("{", "{{").Replace("}", "}}")
            .Replace("(", "{(}").Replace(")", "{)}")
            .Replace("[", "{[}").Replace("]", "{]}");
        SendKeys.SendWait(escaped);
        await Task.Delay(150);

        // Enter to save + Enter again to confirm the extension change popup
        SendKeys.SendWait("{ENTER}");
        await Task.Delay(150);
        SendKeys.SendWait("{ENTER}");
        await Task.Delay(150);
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

        // Retry loop: send Alt+F → Enter, then wait up to 3s for the dialog to appear.
        // On first launch the app may still be initializing so keystrokes can be swallowed —
        // retry up to 5 times until HandleOpenDialog confirms the dialog is visible.
        bool dialogOpened = false;
        for (int attempt = 0; attempt < 5 && !dialogOpened; attempt++)
        {
            // Bring AtmosFringe to foreground
            ShowWindow(afWnd, 9);
            SetForegroundWindow(afWnd);
            await Task.Delay(200);
            SetForegroundWindow(afWnd);
            await Task.Delay(200);

            // Alt+F opens File menu, Enter selects the first (already highlighted) item
            SendKeys.SendWait("%F");
            await Task.Delay(150);
            SendKeys.SendWait("{ENTER}");
            await Task.Delay(150);

            // Check if the dialog appeared (quick poll, 3s max)
            dialogOpened = await Task.Run(() => WaitForOpenDialog(3000));

            if (!dialogOpened)
            {
                // Dismiss any partially-open menu before retrying
                SendKeys.SendWait("{ESC}");
                await Task.Delay(200);
            }
        }

        if (!dialogOpened)
            throw new InvalidOperationException("AtmosFringe Load Image dialog did not appear after retries.");

        // Dialog is confirmed open — fill in the path on the UI thread
        await HandleOpenDialog(filePath);
    }

    private HashSet<IntPtr> GetAllTopLevelWindowHandles()
    {
        var set = new HashSet<IntPtr>();
        EnumWindows((hWnd, _) => { set.Add(hWnd); return true; }, IntPtr.Zero);
        return set;
    }

    // Polls for the Load Interferogram dialog. Returns true as soon as it appears,
    // false if it does not appear within timeoutMs.
    private bool WaitForOpenDialog(int timeoutMs)
    {
        var sb = new StringBuilder(256);
        int elapsed = 0;
        while (elapsed < timeoutMs)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                GetWindowText(hWnd, sb, 256);
                var t = sb.ToString();
                if (t.Contains("Load Interferogram", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Load Image", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Open Image", StringComparison.OrdinalIgnoreCase))
                { found = hWnd; return false; }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) return true;
            Thread.Sleep(50);
            elapsed += 50;
        }
        return false;
    }

    private async Task HandleOpenDialog(string filePath)
    {
        // Locate the dialog window
        IntPtr dialog = IntPtr.Zero;
        var sb = new StringBuilder(256);
        for (int i = 0; i < 40; i++)
        {
            EnumWindows((hWnd, _) =>
            {
                GetWindowText(hWnd, sb, 256);
                var t = sb.ToString();
                if (t.Contains("Load Interferogram", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Load Image", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("Open Image", StringComparison.OrdinalIgnoreCase))
                { dialog = hWnd; return false; }
                return true;
            }, IntPtr.Zero);
            if (dialog != IntPtr.Zero) break;
            await Task.Delay(50);
        }
        if (dialog == IntPtr.Zero)
            throw new InvalidOperationException("AtmosFringe Open dialog did not appear.");

        // Bring dialog to foreground so SendKeys targets it
        ShowWindow(dialog, 9);
        SetForegroundWindow(dialog);
        await Task.Delay(300);   // let it fully paint and accept input

        // Type the full path directly into the File name field — works on any Windows file dialog.
        // Escape any SendKeys special chars in the path (brackets, etc.)
        string escaped = filePath.Replace("{", "{{").Replace("}", "}}");
        SendKeys.SendWait(escaped);
        await Task.Delay(150);

        // Press Enter — same as clicking Open
        SendKeys.SendWait("{ENTER}");
        await Task.Delay(150);
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
        if (_statusLabel.InvokeRequired) { _statusLabel.Invoke(() => SetStatus(msg)); return; }
        _statusLabel.Clear();
        _statusLabel.SelectionColor = Color.Black;
        _statusLabel.AppendText("Status: ");
        _statusLabel.SelectionColor = Color.Blue;
        _statusLabel.AppendText(msg);
    }

    private void SetImageLabel(string imageNumber)
    {
        if (_imageLabel.InvokeRequired) { _imageLabel.Invoke(() => SetImageLabel(imageNumber)); return; }
        _imageLabel.Clear();
        _imageLabel.SelectionColor = Color.Black;
        _imageLabel.AppendText("Last saved image: ");
        _imageLabel.SelectionColor = Color.Blue;
        _imageLabel.AppendText(imageNumber);
    }
}
