using System;
using System.Drawing;
using System.Windows.Forms;
using PaintDotNet;

namespace MtfTexPaintDotNet
{
    public sealed class MtfTexSaveConfigWidget : SaveConfigWidget
    {
        private sealed class CompressionOption
        {
            public string Text { get; }
            public Re5CompressionMode Mode { get; }

            public CompressionOption(string text, Re5CompressionMode mode)
            {
                Text = text;
                Mode = mode;
            }

            public override string ToString() => Text;
        }

        private readonly Panel scrollPanel;
        private readonly TableLayoutPanel stack;

        private readonly ComboBox profileCombo;
        private readonly Label profileDescriptionLabel;

        private readonly ComboBox compressionCombo;
        private readonly CheckBox generateMipmapsCheck;
        private readonly ComboBox mipFilterCombo;
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

            profileCombo = MakeCombo(new[] { "Resident Evil 5", "Resident Evil 6" });
            profileCombo.SelectedIndexChanged += OnProfileChanged;

            profileDescriptionLabel = new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0),
            };

            compressionCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                IntegralHeight = false,
                Height = 24,
                Margin = Padding.Empty,
            };
            compressionCombo.SelectedIndexChanged += OnAnySettingChanged;

            generateMipmapsCheck = new CheckBox
            {
                AutoSize = true,
                Text = "Generate mipmaps",
                Margin = Padding.Empty,
            };
            generateMipmapsCheck.CheckedChanged += OnAnySettingChanged;

            mipFilterCombo = MakeCombo(new[]
            {
                "Bicubic",
                "Bicubic (Smooth)",
                "Bilinear",
                "Bilinear (Low Quality)",
                "Adaptive (Sharp)",
                "Lanczos",
                "Fant",
                "Nearest Neighbor",
            });
            mipFilterCombo.SelectedIndexChanged += OnAnySettingChanged;

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
                CreateField("Target Game", profileCombo),
                profileDescriptionLabel));

            AddStackRow(CreateGroup("Compression",
                CreateField("Mode", compressionCombo)));

            AddStackRow(CreateGroup("Mipmaps",
                generateMipmapsCheck,
                CreateField("Filter", mipFilterCombo)));

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
            MtfGameProfile profile = current.Profile;
            Re5ResolvedSaveSettings resolved = Re5SaveDefaults.Resolve(current);

            initializing = true;
            try
            {
                profileCombo.SelectedIndex = ClampIndex((int)profile, profileCombo.Items.Count);
                ConfigureCompressionOptions(profile, resolved.Compression);
                generateMipmapsCheck.Checked = resolved.GenerateMipmaps;
                mipFilterCombo.SelectedIndex = ClampIndex((int)current.MipResampling, mipFilterCombo.Items.Count);
                alphaCombo.SelectedIndex = resolved.ForceOpaque ? (int)Re5AlphaMode.ForceOpaque : (int)Re5AlphaMode.Preserve;

                profileDescriptionLabel.Text = GetProfileDescription(profile);
                UpdateMipFilterEnabledState();
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
                MtfGameProfile profile = GetSelectedProfile();
                ApplyProfileDefaultsToControls(profile);
                profileDescriptionLabel.Text = GetProfileDescription(profile);
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

            UpdateMipFilterEnabledState();
            UpdateToken();
            RefreshSummary(Token as MtfTexSaveConfigToken ?? ReadTokenFromWidget());
        }

        private void ResetToDefaults()
        {
            InitWidgetFromToken(new MtfTexSaveConfigToken());
            UpdateToken();
        }

        private void ApplyProfileDefaultsToControls(MtfGameProfile profile)
        {
            Re5CompressionMode compression = profile switch
            {
                MtfGameProfile.RE6 => Re5CompressionMode.Dxt5,
                _ => Re5CompressionMode.Auto,
            };

            ConfigureCompressionOptions(profile, compression);
            generateMipmapsCheck.Checked = true;
            mipFilterCombo.SelectedIndex = (int)MtfMipResamplingAlgorithm.Cubic;
            alphaCombo.SelectedIndex = (int)Re5AlphaMode.Preserve;
            UpdateMipFilterEnabledState();
        }

        private void ConfigureCompressionOptions(MtfGameProfile profile, Re5CompressionMode preferredMode)
        {
            compressionCombo.BeginUpdate();
            try
            {
                compressionCombo.Items.Clear();

                if (profile == MtfGameProfile.RE6)
                {
                    AddCompressionOption("Auto", Re5CompressionMode.Auto);
                    AddCompressionOption("BC1 (Linear, DXT1)", Re5CompressionMode.Dxt1);
                    AddCompressionOption("BC3 (Linear, DXT5)", Re5CompressionMode.Dxt5);
                    AddCompressionOption("BC5 (Linear, Unsigned)", Re5CompressionMode.Bc5);
                    AddCompressionOption("RGBA8 (Linear)", Re5CompressionMode.Rgba8);
                }
                else
                {
                    AddCompressionOption("Auto", Re5CompressionMode.Auto);
                    AddCompressionOption("BC1 (Linear, DXT1)", Re5CompressionMode.Dxt1);
                    AddCompressionOption("BC2 (Linear, DXT3)", Re5CompressionMode.Dxt3);
                    AddCompressionOption("BC3 (Linear, DXT5)", Re5CompressionMode.Dxt5);
                }

                SelectCompressionOption(GetSupportedCompressionOrDefault(profile, preferredMode));
            }
            finally
            {
                compressionCombo.EndUpdate();
            }
        }

        private void AddCompressionOption(string text, Re5CompressionMode mode)
        {
            compressionCombo.Items.Add(new CompressionOption(text, mode));
        }

        private void SelectCompressionOption(Re5CompressionMode mode)
        {
            for (int i = 0; i < compressionCombo.Items.Count; i++)
            {
                if (compressionCombo.Items[i] is CompressionOption item && item.Mode == mode)
                {
                    compressionCombo.SelectedIndex = i;
                    return;
                }
            }

            compressionCombo.SelectedIndex = compressionCombo.Items.Count > 0 ? 0 : -1;
        }

        private static Re5CompressionMode GetSupportedCompressionOrDefault(MtfGameProfile profile, Re5CompressionMode mode)
        {
            if (profile == MtfGameProfile.RE6)
            {
                return mode switch
                {
                    Re5CompressionMode.Auto => Re5CompressionMode.Auto,
                    Re5CompressionMode.Dxt1 => Re5CompressionMode.Dxt1,
                    Re5CompressionMode.Dxt5 => Re5CompressionMode.Dxt5,
                    Re5CompressionMode.Bc5 => Re5CompressionMode.Bc5,
                    Re5CompressionMode.Rgba8 => Re5CompressionMode.Rgba8,
                    _ => Re5CompressionMode.Dxt5,
                };
            }

            return mode switch
            {
                Re5CompressionMode.Auto => Re5CompressionMode.Auto,
                Re5CompressionMode.Dxt1 => Re5CompressionMode.Dxt1,
                Re5CompressionMode.Dxt3 => Re5CompressionMode.Dxt3,
                Re5CompressionMode.Dxt5 => Re5CompressionMode.Dxt5,
                _ => Re5CompressionMode.Auto,
            };
        }

        private MtfTexSaveConfigToken ReadTokenFromWidget()
        {
            return new MtfTexSaveConfigToken
            {
                Profile = GetSelectedProfile(),
                Compression = GetSelectedCompressionMode(),
                GenerateMipmaps = generateMipmapsCheck.Checked,
                MipResampling = GetSelectedMipFilter(),
                AlphaMode = (Re5AlphaMode)ClampIndex(alphaCombo.SelectedIndex, alphaCombo.Items.Count),
            };
        }

        private MtfGameProfile GetSelectedProfile()
        {
            return (MtfGameProfile)ClampIndex(profileCombo.SelectedIndex, profileCombo.Items.Count);
        }

        private void UpdateMipFilterEnabledState()
        {
            mipFilterCombo.Enabled = generateMipmapsCheck.Checked;
        }

        private MtfMipResamplingAlgorithm GetSelectedMipFilter()
        {
            int idx = ClampIndex(mipFilterCombo.SelectedIndex, mipFilterCombo.Items.Count);
            return (MtfMipResamplingAlgorithm)idx;
        }

        private Re5CompressionMode GetSelectedCompressionMode()
        {
            if (compressionCombo.SelectedItem is CompressionOption item)
            {
                return item.Mode;
            }

            return Re5CompressionMode.Auto;
        }

        private void RefreshSummary(MtfTexSaveConfigToken current)
        {
            string targetLine = current.Profile == MtfGameProfile.RE6
                ? "Target: RE6 2D profile (BC1/BC3/BC5/RGBA8 save backend)"
                : "Target: RE5 2D profile (BC1/BC2/BC3 save backend)";

            summaryLabel.Text =
                $"Profile: {GetProfileDisplayName(current.Profile)}\r\n" +
                $"Compression: {GetCompressionDisplayName(current.Compression)}\r\n" +
                $"Mipmaps: {(current.GenerateMipmaps ? "Generate full chain" : "Top level only")}\r\n" +
                $"Mip Filter: {(current.GenerateMipmaps ? GetMipFilterDisplayName(current.MipResampling) : "N/A")}\r\n" +
                $"Alpha: {(current.AlphaMode == Re5AlphaMode.ForceOpaque ? "Force opaque" : "Preserve alpha")}\r\n" +
                targetLine;

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
                mipFilterCombo.Width = inner;
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

        private static string GetProfileDisplayName(MtfGameProfile profile)
        {
            return profile switch
            {
                MtfGameProfile.RE6 => "RE6",
                _ => "RE5",
            };
        }

        private static string GetCompressionDisplayName(Re5CompressionMode mode)
        {
            return mode switch
            {
                Re5CompressionMode.Dxt1 => "BC1 (Linear, DXT1)",
                Re5CompressionMode.Dxt3 => "BC2 (Linear, DXT3)",
                Re5CompressionMode.Dxt5 => "BC3 (Linear, DXT5)",
                Re5CompressionMode.Bc5 => "BC5 (Linear, Unsigned)",
                Re5CompressionMode.Rgba8 => "RGBA8 (Linear)",
                _ => "Auto",
            };
        }

        private static string GetMipFilterDisplayName(MtfMipResamplingAlgorithm algorithm)
        {
            return algorithm switch
            {
                MtfMipResamplingAlgorithm.CubicSmooth => "Bicubic (Smooth)",
                MtfMipResamplingAlgorithm.Linear => "Bilinear",
                MtfMipResamplingAlgorithm.LinearLowQuality => "Bilinear (Low Quality)",
                MtfMipResamplingAlgorithm.AdaptiveHighQuality => "Adaptive (Sharp)",
                MtfMipResamplingAlgorithm.Lanczos3 => "Lanczos",
                MtfMipResamplingAlgorithm.Fant => "Fant",
                MtfMipResamplingAlgorithm.NearestNeighbor => "Nearest Neighbor",
                _ => "Bicubic",
            };
        }

        private static string GetProfileDescription(MtfGameProfile profile)
        {
            return profile switch
            {
                MtfGameProfile.RE6 => "RE6 2D texture target. Shows only RE6 save modes: BC1, BC3, BC5, and RGBA8.",
                _ => "RE5 2D texture target. Shows only RE5 save modes: BC1, BC2, and BC3.",
            };
        }
    }
}
