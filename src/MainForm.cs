using FolderIconPlus.Internals;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FolderIconPlus
{
    public class MainForm : Form
    {
        // Double-buffered ListView so the owner-drawn grid doesn't flicker.
        class BufferedListView : ListView
        {
            public BufferedListView() { DoubleBuffered = true; }
        }

        const int ThumbSize = 64;

        readonly TextBox txtFolder = new();
        readonly TextBox txtSearch = new();
        readonly Button btnBrowse = new();
        readonly CheckBox chkRecursive = new();
        readonly Button btnAuto = new();
        readonly Button btnPacks = new();
        readonly Button btnRescan = new();
        readonly BufferedListView list = new();
        readonly Label lblStatus = new();
        readonly System.Windows.Forms.Timer searchTimer = new();

        List<IconEntry> allIcons = [];
        List<IconHit> results = [];

        // UI-thread-only thumbnail cache; images are decoded on the thread pool as they scroll into view.
        readonly Dictionary<string, Bitmap> thumbs = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> loading = new(StringComparer.OrdinalIgnoreCase);

        readonly bool closeAfterApply;

        public MainForm(string folder, bool recursive, bool closeAfterApply)
        {
            this.closeAfterApply = closeAfterApply;

            Text = "FolderIcon+";
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(860, 620);
            MinimumSize = new Size(560, 420);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            // ---- top panel -------------------------------------------------
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8),
                ColumnCount = 3
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            top.Controls.Add(MakeLabel("Folder:"), 0, 0);
            txtFolder.Dock = DockStyle.Fill;
            top.Controls.Add(txtFolder, 1, 0);
            btnBrowse.Text = "Browse...";
            btnBrowse.AutoSize = true;
            top.Controls.Add(btnBrowse, 2, 0);

            top.Controls.Add(MakeLabel("Search:"), 0, 1);
            txtSearch.Dock = DockStyle.Fill;
            top.Controls.Add(txtSearch, 1, 1);

            chkRecursive.Text = "Include all subfolders";
            chkRecursive.AutoSize = true;
            btnAuto.Text = "Auto-match subfolders by name";
            btnAuto.AutoSize = true;
            btnPacks.Text = "Open icon packs folder";
            btnPacks.AutoSize = true;
            btnRescan.Text = "Rescan";
            btnRescan.AutoSize = true;

            var opts = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                WrapContents = false
            };
            opts.Controls.Add(chkRecursive);
            opts.Controls.Add(btnAuto);
            opts.Controls.Add(btnPacks);
            opts.Controls.Add(btnRescan);
            top.Controls.Add(opts, 1, 2);

            // ---- icon grid -------------------------------------------------
            list.View = View.LargeIcon;
            list.VirtualMode = true;
            list.OwnerDraw = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.ShowItemToolTips = true;
            list.Dock = DockStyle.Fill;
            list.LargeImageList = new ImageList { ImageSize = new Size(ThumbSize + 16, ThumbSize + 16), ColorDepth = ColorDepth.Depth32Bit }; // controls cell size

            lblStatus.Dock = DockStyle.Bottom;
            lblStatus.Height = 26;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Padding = new Padding(8, 0, 0, 0);

            Controls.Add(list);       // Fill must be added first so docking works out
            Controls.Add(lblStatus);
            Controls.Add(top);

            // ---- events ----------------------------------------------------
            searchTimer.Interval = 120;
            searchTimer.Tick += delegate { searchTimer.Stop(); RunSearch(); };
            txtSearch.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); };
            txtSearch.KeyDown += OnSearchKeyDown;
            txtFolder.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                SetSearchFromFolder();
                txtSearch.Focus();
                txtSearch.SelectAll();
            };

            list.RetrieveVirtualItem += OnRetrieveVirtualItem;
            list.DrawItem += OnDrawItem;
            list.ItemActivate += delegate { ApplySelected(); };   // Enter or double-click

            btnBrowse.Click += OnBrowse;
            btnAuto.Click += OnAutoMatch;
            btnPacks.Click += delegate { OpenPackFolder(); };
            btnRescan.Click += delegate { LoadIcons(); };

            KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.F5) LoadIcons();
            };

            if (!string.IsNullOrEmpty(folder))
            {
                txtFolder.Text = folder;
                SetSearchFromFolder();
            }
            chkRecursive.Checked = recursive;

            Shown += delegate
            {
                txtSearch.Focus();
                txtSearch.SelectAll();
                LoadIcons();
            };
        }

        static Label MakeLabel(string text)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left
            };
            return l;
        }

        // ------------------------------------------------------------------ search / loading

        void SetSearchFromFolder()
        {
            string f = txtFolder.Text.Trim().Trim('"').TrimEnd('\\', '/');
            string name = Path.GetFileName(f);
            txtSearch.Text = string.IsNullOrEmpty(name) ? f : name;
        }

        void LoadIcons()
        {
            lblStatus.Text = "Scanning icon packs...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<IconEntry> all = IconIndex.Load();
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        allIcons = all;
                        foreach (Bitmap b in thumbs.Values) b?.Dispose();
                        thumbs.Clear();
                        RunSearch();
                    });
                }
                catch (Exception) { /* form closed while scanning */ }
            });
        }

        void RunSearch()
        {
            results = IconIndex.Search(allIcons, txtSearch.Text);
            list.VirtualListSize = results.Count;
            list.SelectedIndices.Clear();
            if (results.Count > 0) SelectIndex(0);   // first hit pre-selected, so type + Enter just works
            list.Invalidate();

            if (allIcons.Count == 0)
                lblStatus.Text = "No icons found. Put icon packs (.ico/.png) in: " + IconIndex.UserPackDir;
            else
                lblStatus.Text = results.Count + " of " + allIcons.Count + " icons   |   Enter = apply   |   Down = move into the grid   |   Esc = close";
        }

        void SelectIndex(int i)
        {
            list.SelectedIndices.Clear();
            list.SelectedIndices.Add(i);
            list.EnsureVisible(i);
        }

        void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                ApplySelected();
            }
            else if (e.KeyCode == Keys.Down && results.Count > 0)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                list.Focus();
            }
        }

        // ------------------------------------------------------------------ virtual, owner-drawn grid

        void OnRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            IconEntry icon = results[e.ItemIndex].Icon;
            var item = new ListViewItem(icon.Name)
            {
                ToolTipText = icon.FilePath
            };
            e.Item = item;
        }

        void OnDrawItem(object sender, DrawListViewItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= results.Count) return;
            IconEntry icon = results[e.ItemIndex].Icon;
            Rectangle b = e.Bounds;
            Graphics g = e.Graphics;

            bool selected = (e.State & ListViewItemStates.Selected) != 0;
            Color textColor = SystemColors.ControlText;
            if (selected)
            {
                if (list.Focused)
                {
                    g.FillRectangle(SystemBrushes.Highlight, b);
                    textColor = SystemColors.HighlightText;
                }
                else
                {
                    using (var br = new SolidBrush(Color.FromArgb(70, SystemColors.Highlight)))
                        g.FillRectangle(br, b);
                    using var pen = new Pen(SystemColors.Highlight);
                    g.DrawRectangle(pen, b.X, b.Y, b.Width - 1, b.Height - 1);
                }
            }

            Bitmap thumb = GetThumb(icon.FilePath);
            if (thumb != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(thumb, b.X + (b.Width - ThumbSize) / 2, b.Y + 6, ThumbSize, ThumbSize);
            }

            Rectangle textRect = new(b.X + 2, b.Y + ThumbSize + 10, b.Width - 4, b.Height - ThumbSize - 12);
            TextRenderer.DrawText(g, icon.Name, list.Font, textRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }

        Bitmap GetThumb(string path)
        {
            if (thumbs.TryGetValue(path, out Bitmap bmp)) return bmp;

            if (loading.Add(path))
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Bitmap loaded = LoadThumb(path);
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            loading.Remove(path);
                            if (thumbs.Count > 2500)   // keep memory bounded on huge packs
                            {
                                foreach (Bitmap old in thumbs.Values) old?.Dispose();
                                thumbs.Clear();
                            }
                            thumbs[path] = loaded;
                            list.Invalidate();
                        });
                    }
                    catch (Exception) { loaded?.Dispose(); }
                });
            }
            return null;
        }

        static Bitmap LoadThumb(string path)
        {
            try
            {
                if (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var ico = new Icon(path, new Size(ThumbSize, ThumbSize));
                        using Bitmap raw = ico.ToBitmap();
                        return ScaleToThumb(raw);
                    }
                    catch (Exception) { /* fall through to the generic image loader */ }
                }
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using Image img = Image.FromStream(fs);
                return ScaleToThumb(img);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static Bitmap ScaleToThumb(Image img)
        {
            var bmp = new Bitmap(ThumbSize, ThumbSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float scale = Math.Min((float)ThumbSize / img.Width, (float)ThumbSize / img.Height);
                float w = img.Width * scale, h = img.Height * scale;
                g.DrawImage(img, (ThumbSize - w) / 2f, (ThumbSize - h) / 2f, w, h);
            }
            return bmp;
        }

        // ------------------------------------------------------------------ actions

        string GetFolder()
        {
            string folder = txtFolder.Text.Trim().Trim('"');
            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                MessageBox.Show(this, "Please choose a folder that exists.", "FolderIcon+",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtFolder.Focus();
                return null;
            }
            return folder;
        }

        void ApplySelected()
        {
            string folder = GetFolder();
            if (folder == null) return;

            if (list.SelectedIndices.Count == 0)
            {
                lblStatus.Text = "Select an icon first.";
                return;
            }
            IconEntry icon = results[list.SelectedIndices[0]].Icon;
            bool recursive = chkRecursive.Checked;

            if (recursive &&
                MessageBox.Show(this, "Apply \"" + icon.Name + "\" to:\n" + folder + "\n\nand ALL of its subfolders?",
                                "FolderIcon+", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            var errors = new List<string>();
            int count = 0;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (recursive)
                {
                    count = FolderIconService.ApplyTree(folder, icon.FilePath, true, errors);
                }
                else
                {
                    FolderIconService.Apply(folder, icon.FilePath);
                    count = 1;
                }
                FolderIconService.RefreshShell();
            }
            catch (Exception ex)
            {
                errors.Add(folder + ": " + ex.Message);
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            ShowErrors(errors);
            lblStatus.Text = "Applied \"" + icon.Name + "\" to " + count + (count == 1 ? " folder." : " folders.");
            if (closeAfterApply && errors.Count == 0) Close();
        }

        void OnAutoMatch(object sender, EventArgs e)
        {
            string folder = GetFolder();
            if (folder == null) return;
            if (allIcons.Count == 0)
            {
                MessageBox.Show(this, "No icons found. Put icon packs in:\n" + IconIndex.UserPackDir, "FolderIcon+");
                return;
            }
            if (MessageBox.Show(this,
                    "For every subfolder of:\n" + folder + "\n\nFolderIcon+ will look for an icon whose name matches the folder's name and apply it. " +
                    "Folders without a match are left alone.\n\nContinue?",
                    "Auto-match subfolders", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            var errors = new List<string>();
            int count = 0;
            Cursor = Cursors.WaitCursor;
            try
            {
                count = FolderIconService.AutoMatchTree(folder, allIcons, false, errors);
                FolderIconService.RefreshShell();
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            ShowErrors(errors);
            lblStatus.Text = "Auto-match changed " + count + (count == 1 ? " folder." : " folders.");
        }

        void ShowErrors(List<string> errors)
        {
            if (errors.Count == 0) return;
            var sb = new StringBuilder();
            sb.AppendLine(errors.Count + " folder(s) could not be changed:");
            for (int i = 0; i < errors.Count && i < 8; i++) sb.AppendLine(errors[i]);
            if (errors.Count > 8) sb.AppendLine("...");
            MessageBox.Show(this, sb.ToString(), "FolderIcon+", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void OnBrowse(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog();
            dlg.Description = "Choose the folder to change";
            if (Directory.Exists(txtFolder.Text)) dlg.SelectedPath = txtFolder.Text;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            txtFolder.Text = dlg.SelectedPath;
            SetSearchFromFolder();
            txtSearch.Focus();
            txtSearch.SelectAll();
        }

        void OpenPackFolder()
        {
            try
            {
                Directory.CreateDirectory(IconIndex.UserPackDir);
                Process.Start("explorer.exe", "\"" + IconIndex.UserPackDir + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "FolderIcon+");
            }
        }
    }
}
