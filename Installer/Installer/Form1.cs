using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace Installer
{
    public partial class Form1 : Form
    {
        private const string DownloadUrl = "https://aveliss.pages.dev/download/AvelisPortable.zip";
        private const string VersionUrl = "https://aveliss.pages.dev/download/version.txt";

        private const string InstallerVersionUrl = "https://7cead19f.aveliss.pages.dev/download/installer-version.txt";
        private const string InstallerVersionUrlOld = "https://aveliss.pages.dev/download/installer-version.txt";

        private string _currentVersion;
        private string _baseDir;
        private string _extractDir;
        private string _localVersionFile;
        private long _zipSize;
        private CancellationTokenSource _cts;
        private bool _isInstalling = false;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        public Form1()
        {
            InitializeComponent();
            guna2DragControl1.TargetControl = this;
            this.guna2Button1.Click += new System.EventHandler(this.guna2Button1_Click);

            this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint, true);

            this.MouseDown += Form1_MouseDown;
            HookDrag(this);
        }

        private void HookDrag(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c == guna2Button1 || c == guna2ControlBox_close || c == guna2ControlBox1)
                    continue;

                c.MouseDown += Form1_MouseDown;

                if (c.HasChildren)
                    HookDrag(c);
            }
        }

        private void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private string FetchString(string url)
        {
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Avelis-Bootstrapper/1.0");
                return wc.DownloadString(url).Trim();
            }
        }

        private async void UpdateInstallerLabelsAsync()
        {
            try
            {
                string newVersion = null;
                string oldVersion = null;

                var tasks = new[]
                {
                    Task.Run(() => FetchString(InstallerVersionUrl)),
                    Task.Run(() => FetchString(InstallerVersionUrlOld))
                };
                await Task.WhenAll(tasks);

                newVersion = tasks[0].Result?.Trim();
                oldVersion = tasks[1].Result?.Trim();

                string label17Text = string.IsNullOrEmpty(newVersion) ? "Installer Unknown" : $"Installer {newVersion}";
                if (InvokeRequired)
                    Invoke(new Action(() => label17.Text = label17Text));
                else
                    label17.Text = label17Text;

                string label18Text;
                if (string.IsNullOrEmpty(oldVersion) || string.IsNullOrEmpty(newVersion))
                    label18Text = "Outdated: Unknown";
                else if (oldVersion != newVersion)
                    label18Text = "Outdated: yes";
                else
                    label18Text = "Outdated: no";

                if (InvokeRequired)
                    Invoke(new Action(() => label18.Text = label18Text));
                else
                    label18.Text = label18Text;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Installer labels fetch error: {ex.Message}");
                if (InvokeRequired)
                {
                    Invoke(new Action(() => {
                        label17.Text = "Installer Unknown";
                        label18.Text = "Outdated: Unknown";
                    }));
                }
                else
                {
                    label17.Text = "Installer Unknown";
                    label18.Text = "Outdated: Unknown";
                }
            }
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            UpdateInstallerLabelsAsync();

            guna2Button1.Enabled = false;
            guna2Button1.Text = "Checking...";
            label11.Text = "Checking...";

            try
            {
                var info = await Task.Run(() => GetVersionInfo());

                if (!info.HasValue)
                {
                    MessageBox.Show("Failed to fetch latest version. Check your internet connection.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    guna2Button1.Text = "Retry";
                    guna2Button1.Enabled = true;
                    return;
                }

                _currentVersion = info.Value.Version;
                _zipSize = info.Value.ZipSize;

                _baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Avelis", _currentVersion);
                _extractDir = Path.Combine(_baseDir, "extracted");
                _localVersionFile = Path.Combine(_extractDir, "version.txt");

                UpdateUIWithVersionInfo();

                string installedVersion = GetInstalledVersion();
                bool needsUpdate = installedVersion == null || installedVersion != _currentVersion;

                if (needsUpdate)
                {
                    guna2Button1.Text = _zipSize > 0
                        ? $"Install / Update ({FormatFileSize(_zipSize)})"
                        : "Install / Update";
                }
                else
                {
                    guna2Button1.Text = "Launch";
                }

                guna2Button1.Enabled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Load error: {ex}");
                MessageBox.Show($"Failed to initialize: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                guna2Button1.Text = "Retry";
                guna2Button1.Enabled = true;
            }
        }

        private void UpdateUIWithVersionInfo()
        {
            label6.Text = "ProjectAvelis";
            label7.Text = _currentVersion ?? "Unknown";
            label10.Text = DateTime.Now.ToString("MMM d, yyyy");
            label11.Text = GetInstalledVersion() ?? "Not installed";

            long installerSize = new FileInfo(Application.ExecutablePath).Length;
            label13.Text = FormatFileSize(installerSize);
            label14.Text = _zipSize > 0 ? FormatFileSize(_zipSize) : "Unknown";
        }

        private string GetInstalledVersion()
        {
            try
            {
                if (File.Exists(_localVersionFile))
                    return File.ReadAllText(_localVersionFile).Trim();
            }
            catch { }
            return null;
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 0) return "Unknown";
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private async void guna2Button1_Click(object sender, EventArgs e)
        {
            if (_isInstalling)
            {
                _cts?.Cancel();
                return;
            }

            if (string.IsNullOrEmpty(_currentVersion))
            {
                Form1_Load(sender, e);
                return;
            }

            string installedVersion = GetInstalledVersion();
            bool needsUpdate = installedVersion == null || installedVersion != _currentVersion;

            if (!needsUpdate)
            {
                LaunchApp();
                return;
            }

            await PerformInstallationAsync();
        }

        private async Task PerformInstallationAsync()
        {
            _isInstalling = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            guna2Button1.Text = "Cancel";
            guna2ControlBox_close.Enabled = false;
            guna2ProgressBar1.Visible = true;
            guna2ProgressBar1.Value = 0;

            try
            {
                UpdateStatus("Cleaning up...", 0);
                await Task.Run(() => CleanDirectory(_baseDir), token);

                UpdateStatus("Downloading...", 5);
                string zipPath = await DownloadZipAsync(DownloadUrl, token);

                if (string.IsNullOrEmpty(zipPath))
                {
                    MessageBox.Show("Download failed or was cancelled.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ResetUI();
                    return;
                }

                UpdateStatus("Extracting...", 60);
                await Task.Run(() => ExtractZip(zipPath, token), token);

                UpdateStatus("Finalizing...", 95);
                await Task.Run(() => SaveVersionFile(_currentVersion), token);

                long installSize = await Task.Run(() => GetDirectorySize(_extractDir));
                label14.Text = FormatFileSize(installSize);
                label11.Text = _currentVersion;

                UpdateStatus("Ready to launch", 100);

                guna2Button1.Text = "Launch";
                guna2ControlBox_close.Enabled = true;
                _isInstalling = false;
                guna2ProgressBar1.Visible = false;

                await Task.Delay(500);
                LaunchApp();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Cancelled", 0);
                ResetUI();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Installation error: {ex}");
                string innerMsg = ex.InnerException?.Message;
                string displayMsg = !string.IsNullOrEmpty(innerMsg) ? $"{ex.Message}\n\nDetails: {innerMsg}" : ex.Message;
                MessageBox.Show($"Installation failed:\n{displayMsg}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ResetUI();
            }
        }

        private void ResetUI()
        {
            guna2Button1.Text = "Retry";
            guna2Button1.Enabled = true;
            guna2ControlBox_close.Enabled = true;
            _isInstalling = false;
            guna2ProgressBar1.Visible = false;
            guna2ProgressBar1.Value = 0;
        }

        private void UpdateStatus(string status, int progressPercent)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string, int>(UpdateStatus), status, progressPercent);
                return;
            }

            if (_isInstalling)
            {
                guna2Button1.Text = $"{status}";
                guna2ProgressBar1.Value = Math.Min(progressPercent, 100);
            }
        }

        private void CleanDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                int retries = 5;
                while (retries-- > 0)
                {
                    try
                    {
                        Directory.Delete(path, true);
                        break;
                    }
                    catch (IOException)
                    {
                        if (retries == 0) throw;
                        Thread.Sleep(200);
                    }
                }
            }
            Directory.CreateDirectory(path);
        }

        private async Task<string> DownloadZipAsync(string zipUrl, CancellationToken token)
        {
            string zipPath = Path.Combine(_baseDir, "AvelisPortable.zip");
            string tempPath = zipPath + ".tmp";

            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Avelis-Bootstrapper/1.0");
                    var tcs = new TaskCompletionSource<bool>();
                    Exception error = null;
                    var sw = Stopwatch.StartNew();

                    wc.DownloadProgressChanged += (s, e) =>
                    {
                        try
                        {
                            this.Invoke((MethodInvoker)delegate
                            {
                                int progress = Math.Min(5 + (e.ProgressPercentage * 55 / 100), 60);
                                double elapsed = sw.Elapsed.TotalSeconds;
                                double speed = elapsed > 0 ? (e.BytesReceived / 1024.0 / 1024.0) / elapsed : 0;
                                guna2ProgressBar1.Value = progress;
                                guna2Button1.Text = $"Downloading {e.ProgressPercentage}% ({speed:0.0} MB/s)";
                            });
                        }
                        catch { }
                    };

                    wc.DownloadFileCompleted += (s, e) =>
                    {
                        error = e.Error;
                        if (e.Cancelled)
                            tcs.TrySetCanceled();
                        else
                            tcs.TrySetResult(true);
                    };

                    using (token.Register(() => wc.CancelAsync()))
                    {
                        wc.DownloadFileAsync(new Uri(zipUrl), tempPath);
                        await tcs.Task;
                    }

                    if (error != null) throw error;
                }

                File.Move(tempPath, zipPath);
                return zipPath;
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw;
            }
        }

        private void ExtractZip(string zipPath, CancellationToken token)
        {
            if (Directory.Exists(_extractDir))
                Directory.Delete(_extractDir, true);
            Directory.CreateDirectory(_extractDir);

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                int total = archive.Entries.Count;
                int processed = 0;

                foreach (var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();

                    string destPath = Path.Combine(_extractDir, entry.FullName);
                    string destDir = Path.GetDirectoryName(destPath);

                    if (!string.IsNullOrEmpty(destDir))
                        Directory.CreateDirectory(destDir);

                    if (!string.IsNullOrEmpty(entry.Name))
                    {
                        entry.ExtractToFile(destPath, true);
                        File.SetAttributes(destPath, FileAttributes.Normal);
                    }

                    processed++;
                    if (processed % 5 == 0 || processed == total)
                    {
                        int progress = 60 + (int)((processed * 35.0) / total);
                        UpdateStatus("Extracting...", Math.Min(progress, 95));
                    }
                }
            }

            File.Delete(zipPath);

            var files = Directory.GetFiles(_extractDir);
            var dirs = Directory.GetDirectories(_extractDir);

            if (files.Length == 0 && dirs.Length == 1)
            {
                string nested = dirs[0];
                string tempMove = nested + "_temp";

                Directory.Move(nested, tempMove);

                foreach (string f in Directory.GetFiles(tempMove, "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested();

                    string relative = f.Substring(tempMove.Length + 1);
                    string target = Path.Combine(_extractDir, relative);
                    string targetDir = Path.GetDirectoryName(target);

                    if (!string.IsNullOrEmpty(targetDir))
                        Directory.CreateDirectory(targetDir);

                    if (File.Exists(target))
                        File.Delete(target);

                    File.Move(f, target);
                }

                foreach (string d in Directory.GetDirectories(tempMove, "*", SearchOption.AllDirectories))
                {
                    string relative = d.Substring(tempMove.Length + 1);
                    string target = Path.Combine(_extractDir, relative);
                    if (!Directory.Exists(target))
                        Directory.CreateDirectory(target);
                }

                Directory.Delete(tempMove, true);
            }
        }

        private void SaveVersionFile(string versionContent)
        {
            if (!string.IsNullOrEmpty(versionContent))
            {
                File.WriteAllText(_localVersionFile, versionContent);
            }
        }

        private void LaunchApp()
        {
            try
            {
                string[] exes = Directory.GetFiles(_extractDir, "ProjectAvelis.exe", SearchOption.AllDirectories);

                if (exes.Length == 0)
                {
                    string msg = "ProjectAvelis.exe not found.\nFiles found:\n" +
                        string.Join("\n", Directory.GetFiles(_extractDir, "*", SearchOption.AllDirectories).Take(20));
                    MessageBox.Show(msg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string exe = exes[0];
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false
                };
                Process.Start(psi);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private long GetDirectorySize(string path)
        {
            long size = 0;
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
            return size;
        }

        private (string Version, long ZipSize)? GetVersionInfo()
        {
            string version = null;
            long zipSize = 0;

            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Avelis-Bootstrapper/1.0");
                    version = wc.DownloadString(VersionUrl).Trim();
                }

                try
                {
                    var request = (HttpWebRequest)WebRequest.Create(DownloadUrl);
                    request.Method = "HEAD";
                    request.UserAgent = "Avelis-Bootstrapper/1.0";
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.ContentLength > 0)
                            zipSize = response.ContentLength;
                    }
                }
                catch
                {
                }

                return (version, zipSize);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Version fetch error: {ex.Message}");
                return null;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!DesignMode)
                Form1_Load(this, EventArgs.Empty);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_isInstalling && e.CloseReason == CloseReason.UserClosing)
            {
                _cts?.Cancel();
                e.Cancel = true;
            }
            base.OnFormClosing(e);
        }
    }
}
