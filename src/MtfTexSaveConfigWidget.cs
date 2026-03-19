using System;
using System.Drawing;
using System.Windows.Forms;
using PaintDotNet;

namespace MtfTexPaintDotNet
{
    public sealed class MtfTexSaveConfigWidget : SaveConfigWidget
    {
        private readonly Panel scrollPanel;
        private readonly TableLayoutPanel stack;

        private readonly ComboBox profileCombo;
        private readonly Label profileDescriptionLabel;

        private readonly ComboBox compressionCombo;
        private readonly CheckBox generateMipmapsCheck;
        private readonly ComboBox alphaCombo;

        private readonly Label summaryLabel;
        private readonly Button defaultsButton;

        private bool initializing;

        public MtfTexSaveConfigWidget()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Dock = DockStyle.Fill;
            AutoScroll = false;
            AutoSize = false;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            MinimumSize = new Size(220, 0);

            scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Margin = Padding.Empty,
                Padding = new Padding(6, 6, 8, 6),
            };

            stack = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 0,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            profileCombo = MakeCombo(new[]
            {
                "RE5 Generic",
                "RE5 BM",
                "RE5 MM",
                "RE5 NM",
            });
            profileCombo.SelectedIndexChanged += OnProfileChanged;

            profileDescriptionLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0),
            };

            compressionCombo = MakeCombo(new[] { "Auto", "DXT1", "DXT3", "DXT5" });
            compressionCombo.SelectedIndexChanged += OnAnySettingChanged;

            generateMipmapsCheck = new CheckBox
            {
                AutoSize = true,
                Text = "Generate mipmaps",
                Margin = Padding.Empty,
            };
            generateMipmapsCheck.CheckedChanged += OnAnySettingChanged;

            alphaCombo = MakeCombo(new[] { "Preserve alpha", "Force opaque" });
            alphaCombo.SelectedIndexChanged += OnAnySettingChanged;

            summaryLabel = new Label
            {
                AutoSize = true,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(6),
                Margin = new Padding(0),
            };

            defaultsButton = new Button
            {
                Text = "Defaults",
                AutoSize = false,
                Height = 28,
                Margin = new Padding(0, 4, 0, 0),
            };
            defaultsButton.Click += (_, _) => ResetToDefaults();

            AddStackRow(CreateGroup("Profile",
                CreateField("Preset", profileCombo),
                profileDescriptionLabel));

            AddStackRow(CreateGroup("Compression",
                CreateField("Mode", compressionCombo)));

            AddStackRow(CreateGroup("Mipmaps",
                generateMipmapsCheck));

            AddStackRow(CreateGroup("Alpha",
                CreateField("Handling", alphaCombo)));

            AddStackRow(CreateGroup("Summary", summaryLabel));
            AddStackRow(defaultsButton);

            scrollPanel.Controls.Add(stack);
            Controls.Add(scrollPanel);

            SizeChanged += (_, _) => UpdateLayoutWidths();
            scrollPanel.SizeChanged += (_, _) => UpdateLayoutWidths();
        }

        protected override void InitFileType()
        {
            fileType = new MtfTexFileType();
        }

        protected override void InitTokenFromWidget()
        {
            token = ReadTokenFromWidget();
        }

        protected override void InitWidgetFromToken(SaveConfigToken sourceToken)
        {
            MtfTexSaveConfigToken current = sourceToken as MtfTexSaveConfigToken ?? new MtfTexSaveConfigToken();
            Re5ResolvedSaveSettings resolved = Re5SaveDefaults.Resolve(current);

            initializing = true;
            try
            {
                profileCombo.SelectedIndex = ClampIndex((int)current.Profile, profileCombo.Items.Count);
                compressionCombo.SelectedIndex = ClampIndex((int)resolved.Compression, compressionCombo.Items.Count);
                generateMipmapsCheck.Checked = resolved.GenerateMipmaps;
                alphaCombo.SelectedIndex = resolved.ForceOpaque ? (int)Re5AlphaMode.ForceOpaque : (int)Re5AlphaMode.Preserve;

                profileDescriptionLabel.Text = GetProfileDescription((Re5SaveProfile)ClampIndex(profileCombo.SelectedIndex, profileCombo.Items.Count));
                RefreshSummary(ReadTokenFromWidget());
            }
            finally
            {
                initializing = false;
            }

            UpdateLayoutWidths();
        }

        private void OnProfileChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            initializing = true;
            try
            {
                ApplyProfileDefaultsToControls((Re5SaveProfile)ClampIndex(profileCombo.SelectedIndex, profileCombo.Items.Count));
                profileDescriptionLabel.Text = GetProfileDescription((Re5SaveProfile)ClampIndex(profileCombo.SelectedIndex, profileCombo.Items.Count));
            }
            finally
            {
                initializing = false;
            }

            UpdateToken();
            RefreshSummary(Token as MtfTexSaveConfigToken ?? ReadTokenFromWidget());
            UpdateLayoutWidths();
        }

        private void OnAnySettingChanged(object? sender, EventArgs e)
        {
            if (initializing)
            {
                return;
            }

            UpdateToken();
            RefreshSummary(Token as MtfTexSaveConfigToken ?? ReadTokenFromWidget());
        }

        private void ResetToDefaults()
        {
            InitWidgetFromToken(new MtfTexSaveConfigToken());
            UpdateToken();
        }

        private void ApplyProfileDefaultsToControls(Re5SaveProfile profile)
        {
            Re5CompressionMode compression = profile switch
            {
                Re5SaveProfile.MM => Re5CompressionMode.Dxt5,
                Re5SaveProfile.NM => Re5CompressionMode.Dxt5,
                _ => Re5CompressionMode.Auto,
            };

            compressionCombo.SelectedIndex = (int)compression;
            generateMipmapsCheck.Checked = true;
            alphaCombo.SelectedIndex = (int)Re5AlphaMode.Preserve;
        }

        private MtfTexSaveConfigToken ReadTokenFromWidget()
        {
            return new MtfTexSaveConfigToken
            {
                Profile = (Re5SaveProfile)ClampIndex(profileCombo.SelectedIndex, profileCombo.Items.Count),
                Compression = (Re5CompressionMode)ClampIndex(compressionCombo.SelectedIndex, compressionCombo.Items.Count),
                GenerateMipmaps = generateMipmapsCheck.Checked,
                AlphaMode = (Re5AlphaMode)ClampIndex(alphaCombo.SelectedIndex, alphaCombo.Items.Count),
            };
        }

        private void RefreshSummary(MtfTexSaveConfigToken current)
        {
            summaryLabel.Text =
                $"Profile: {GetProfileDisplayName(current.Profile)}\r\n" +
                $"Compression: {GetCompressionDisplayName(current.Compression)}\r\n" +
                $"Mipmaps: {(current.GenerateMipmaps ? "Generate full chain" : "Top level only")}\r\n" +
                $"Alpha: {(current.AlphaMode == Re5AlphaMode.ForceOpaque ? "Force opaque" : "Preserve alpha")}\r\n" +
                "Header: Safe RE5 PC generic";

            UpdateLayoutWidths();
        }

        private void AddStackRow(Control control)
        {
            control.Margin = new Padding(0, 0, 0, 8);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            stack.Controls.Add(control, 0, stack.RowCount++);
        }

        private GroupBox CreateGroup(string title, params Control[] children)
        {
            GroupBox box = new GroupBox
            {
                Text = title,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8),
                Margin = Padding.Empty,
            };

            TableLayoutPanel body = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 0,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            foreach (Control child in children)
            {
                child.Margin = new Padding(0, 4, 0, 0);
                child.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
                body.Controls.Add(child, 0, body.RowCount++);
            }

            box.Controls.Add(body);
            return box;
        }

        private static TableLayoutPanel CreateField(string labelText, Control control)
        {
            Label label = new Label
            {
                AutoSize = true,
                Text = labelText,
                Margin = Padding.Empty,
            };

            TableLayoutPanel field = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 0,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            };
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            control.Margin = new Padding(0, 4, 0, 0);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

            field.Controls.Add(label, 0, field.RowCount++);
            field.Controls.Add(control, 0, field.RowCount++);
            return field;
        }

        private static ComboBox MakeCombo(string[] items)
        {
            ComboBox combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                IntegralHeight = false,
                Height = 24,
                Margin = Padding.Empty,
            };
            combo.Items.AddRange(items);
            return combo;
        }

        private void UpdateLayoutWidths()
        {
            if (IsDisposed)
            {
                return;
            }

            int available = scrollPanel.ClientSize.Width - scrollPanel.Padding.Horizontal;
            if (scrollPanel.VerticalScroll.Visible)
            {
                available -= SystemInformation.VerticalScrollBarWidth;
            }
            available = Math.Max(170, available);

            stack.SuspendLayout();
            try
            {
                stack.Width = available;

                foreach (Control c in stack.Controls)
                {
                    c.Width = available;
                }

                int inner = Math.Max(120, available - 28);
                profileCombo.Width = inner;
                compressionCombo.Width = inner;
                alphaCombo.Width = inner;
                defaultsButton.Width = inner;
                profileDescriptionLabel.MaximumSize = new Size(inner, 0);
                summaryLabel.MaximumSize = new Size(inner, 0);
            }
            finally
            {
                stack.ResumeLayout(true);
            }
        }

        private static int ClampIndex(int value, int itemCount)
        {
            if (itemCount <= 0)
            {
                return 0;
            }

            if (value < 0)
            {
                return 0;
            }

            if (value >= itemCount)
            {
                return itemCount - 1;
            }

            return value;
        }

        private static string GetProfileDisplayName(Re5SaveProfile profile)
        {
            return profile switch
            {
                Re5SaveProfile.BM => "RE5 BM",
                Re5SaveProfile.MM => "RE5 MM",
                Re5SaveProfile.NM => "RE5 NM",
                _ => "RE5 Generic",
            };
        }

        private static string GetCompressionDisplayName(Re5CompressionMode mode)
        {
            return mode switch
            {
                Re5CompressionMode.Dxt1 => "DXT1",
                Re5CompressionMode.Dxt3 => "DXT3",
                Re5CompressionMode.Dxt5 => "DXT5",
                _ => "Auto",
            };
        }

        private static string GetProfileDescription(Re5SaveProfile profile)
        {
            return profile switch
            {
                Re5SaveProfile.BM => "Color texture preset. Uses color-friendly defaults.",
                Re5SaveProfile.MM => "Material or mask preset. Defaults toward DXT5 for safer channel retention.",
                Re5SaveProfile.NM => "Normal or data texture preset. Defaults toward DXT5 with mipmaps on.",
                _ => "Neutral fallback preset for a generic RE5 PC TEX file.",
            };
        }
    }
}
