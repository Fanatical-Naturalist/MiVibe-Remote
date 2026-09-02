using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MiVibe.Remote.Tray;

internal sealed class StatusWindow : Form
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    private static readonly Color CanvasColor = Color.FromArgb(244, 244, 245);
    private static readonly Color SurfaceColor = Color.White;
    private static readonly Color ElevatedSurfaceColor = Color.White;
    private static readonly Color InkColor = Color.FromArgb(6, 39, 26);
    private static readonly Color MutedInkColor = Color.FromArgb(70, 91, 81);
    private static readonly Color SecondaryInkColor = Color.FromArgb(133, 145, 139);
    private static readonly Color HairlineColor = Color.FromArgb(220, 226, 222);
    private static readonly Color AccentColor = Color.FromArgb(0, 111, 83);
    private static readonly Color AccentHoverColor = Color.FromArgb(0, 91, 68);
    private static readonly Color WarmAccentColor = Color.FromArgb(245, 130, 32);
    private static readonly Color NightForestColor = Color.FromArgb(6, 39, 26);
    private static readonly Color StatusSecondaryColor = Color.FromArgb(190, 213, 203);
    private static readonly Color ConnectedBackColor = Color.FromArgb(19, 91, 68);
    private static readonly Color ConnectedForeColor = Color.White;
    private static readonly Color WaitingBackColor = Color.FromArgb(255, 238, 220);
    private static readonly Color WaitingForeColor = Color.FromArgb(126, 60, 10);
    private static readonly Color ErrorBackColor = Color.FromArgb(255, 226, 218);
    private static readonly Color ErrorForeColor = Color.FromArgb(152, 45, 22);
    private static readonly Color DarkCanvasColor = Color.FromArgb(5, 24, 16);
    private static readonly Color DarkSurfaceColor = Color.FromArgb(9, 39, 28);
    private static readonly Color DarkElevatedSurfaceColor = Color.FromArgb(12, 48, 35);
    private static readonly Color DarkStatusSurfaceColor = Color.FromArgb(0, 74, 55);
    private static readonly Color DarkInkColor = Color.FromArgb(244, 244, 245);
    private static readonly Color DarkMutedInkColor = Color.FromArgb(183, 204, 194);
    private static readonly Color DarkSecondaryInkColor = Color.FromArgb(124, 151, 138);
    private static readonly Color DarkHairlineColor = Color.FromArgb(35, 84, 65);
    private static readonly Color DarkSecondaryButtonColor = Color.FromArgb(14, 55, 40);
    private static readonly Color DarkSecondaryButtonHoverColor = Color.FromArgb(18, 70, 51);

    private readonly TableLayoutPanel rootLayout;
    private readonly SurfacePanel statusCard;
    private readonly SurfacePanel controlsCard;
    private readonly List<SurfacePanel> workflowCards = [];
    private readonly RemoteGuideControl remoteGuide;
    private readonly Label titleLabel;
    private readonly Label introLabel;
    private readonly PillBadge statusBadge;
    private readonly Button themeToggleButton;
    private readonly Label deviceNameLabel;
    private readonly Label detailLabel;
    private readonly Label batteryValueLabel;
    private readonly Label batteryMetaLabel;
    private readonly Button connectButton;
    private readonly Button pauseButton;
    private readonly Button audioRouteButton;
    private readonly CheckBox startWithWindowsCheckBox;
    private readonly LinkLabel safeExitLink;
    private readonly ToolTip toolTip;

    private TrayUiState currentState = TrayUiState.Initial;
    private bool darkMode = IsSystemDarkModePreferred();
    private bool allowClose;

    public StatusWindow()
    {
        Text = "MiVibe Remote";
        AccessibleName = "MiVibe Remote 控制中心";
        AccessibleDescription = "查看小米遥控器连接、电量、按键映射和语音输入工作流。";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScroll = true;
        BackColor = darkMode ? DarkCanvasColor : CanvasColor;
        ClientSize = new Size(1460, 1080);
        MinimumSize = new Size(1200, 900);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Font = CreateUiFont(9.5F, FontStyle.Regular);

        titleLabel = CreateLabel("MiVibe Remote");
        titleLabel.Font = CreateDisplayFont(27F, FontStyle.Bold);
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        titleLabel.AutoEllipsis = false;
        titleLabel.AccessibleRole = AccessibleRole.StaticText;

        introLabel = CreateLabel("把小米遥控器变成你的 Vibe Coding 语音入口");
        introLabel.Font = CreateUiFont(10.75F, FontStyle.Regular);
        introLabel.Dock = DockStyle.Fill;
        introLabel.TextAlign = ContentAlignment.MiddleLeft;
        introLabel.AutoEllipsis = true;
        introLabel.AccessibleName = "产品简介";

        statusBadge = new PillBadge
        {
            Anchor = AnchorStyles.Right,
            AccessibleName = "连接状态：正在启动",
            Margin = Padding.Empty,
            Size = new Size(118, 34),
            Text = "正在启动"
        };

        themeToggleButton = CreateThemeButton();
        themeToggleButton.Click += (_, _) =>
        {
            darkMode = !darkMode;
            ApplyTheme();
        };

        deviceNameLabel = CreateLabel("小米蓝牙语音遥控器");
        deviceNameLabel.Dock = DockStyle.Fill;
        deviceNameLabel.Font = CreateUiFont(14F, FontStyle.Bold);
        deviceNameLabel.TextAlign = ContentAlignment.MiddleLeft;
        deviceNameLabel.AutoEllipsis = true;
        deviceNameLabel.AccessibleName = "遥控器名称";

        detailLabel = CreateLabel("正在准备语音桥…");
        detailLabel.Dock = DockStyle.Fill;
        detailLabel.Font = CreateUiFont(9.5F, FontStyle.Regular);
        detailLabel.TextAlign = ContentAlignment.MiddleLeft;
        detailLabel.AutoEllipsis = true;
        detailLabel.AccessibleName = "语音桥状态";

        batteryMetaLabel = CreateLabel("连接后读取电量");
        batteryMetaLabel.Dock = DockStyle.Fill;
        batteryMetaLabel.Font = CreateUiFont(8.75F, FontStyle.Regular);
        batteryMetaLabel.TextAlign = ContentAlignment.MiddleLeft;
        batteryMetaLabel.AutoEllipsis = true;
        batteryMetaLabel.AccessibleName = "电量更新时间";

        batteryValueLabel = CreateLabel("—");
        batteryValueLabel.Dock = DockStyle.Fill;
        batteryValueLabel.Font = CreateDisplayFont(26F, FontStyle.Bold);
        batteryValueLabel.TextAlign = ContentAlignment.MiddleRight;
        batteryValueLabel.AutoEllipsis = false;
        batteryValueLabel.AccessibleName = "遥控器电量：未知";

        connectButton = CreateButton("重新连接", primary: true, tabIndex: 0);
        connectButton.AccessibleName = "重新连接遥控器";
        connectButton.AccessibleDescription = "安全清理旧会话后，重新建立与小米遥控器的蓝牙连接。";
        connectButton.Click += (_, _) =>
            ConnectOrReconnectRequested?.Invoke(this, EventArgs.Empty);

        pauseButton = CreateButton("暂停语音桥", primary: false, tabIndex: 1);
        pauseButton.AccessibleName = "暂停语音桥";
        pauseButton.AccessibleDescription = "安全停止当前语音桥或暂停自动重连。";
        pauseButton.Click += (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty);

        audioRouteButton = CreateButton("检查音频", primary: false, tabIndex: 2);
        audioRouteButton.AccessibleName = "检查音频路由";
        audioRouteButton.AccessibleDescription = "检查 VB-CABLE、录音和播放设备配置。";
        audioRouteButton.Click += (_, _) => AudioRouteRequested?.Invoke(this, EventArgs.Empty);

        startWithWindowsCheckBox = new CheckBox
        {
            Anchor = AnchorStyles.Left,
            AutoCheck = false,
            AutoSize = true,
            BackColor = Color.Transparent,
            Font = CreateUiFont(9.25F, FontStyle.Regular),
            Margin = Padding.Empty,
            TabIndex = 3,
            Text = "登录 Windows 后自动启动",
            AccessibleName = "开机自动启动",
            AccessibleDescription = "控制 MiVibe Remote 是否在登录 Windows 后自动启动。"
        };
        startWithWindowsCheckBox.Click += (_, _) =>
            StartWithWindowsToggleRequested?.Invoke(this, EventArgs.Empty);

        safeExitLink = new LinkLabel
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            Font = CreateUiFont(8.75F, FontStyle.Regular),
            Margin = Padding.Empty,
            TabIndex = 4,
            Text = "安全退出",
            TextAlign = ContentAlignment.MiddleRight,
            AccessibleName = "安全退出 MiVibe Remote",
            AccessibleDescription = "安全清理蓝牙连接和按键钩子，然后退出应用。"
        };
        safeExitLink.LinkClicked += (_, _) => SafeExitRequested?.Invoke(this, EventArgs.Empty);

        toolTip = new ToolTip
        {
            AutoPopDelay = 8000,
            InitialDelay = 450,
            ReshowDelay = 100,
            ShowAlways = true
        };
        toolTip.SetToolTip(connectButton, "先安全清理旧会话，再重新连接遥控器。");
        toolTip.SetToolTip(startWithWindowsCheckBox, "只影响当前 Windows 用户，默认关闭。");
        toolTip.SetToolTip(safeExitLink, "退出前会恢复按键钩子并关闭蓝牙会话。");

        rootLayout = CreateRootLayout();
        statusCard = CreateStatusCard();
        controlsCard = CreateControlsCard();
        remoteGuide = new RemoteGuideControl
        {
            DarkMode = darkMode,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 8)
        };

        BuildLayout();
        Controls.Add(rootLayout);

        ApplyTheme();
        ApplyState(TrayUiState.Initial);
        Shown += OnShown;
    }

    public event EventHandler? ConnectOrReconnectRequested;

    public event EventHandler? PauseRequested;

    public event EventHandler? AudioRouteRequested;

    public event EventHandler? StartWithWindowsToggleRequested;

    public event EventHandler? SafeExitRequested;

    public void ShowOrActivate()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(StatusWindow));
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        if (!Visible)
        {
            Show();
        }

        Activate();
        BringToFront();
    }

    public void ApplyState(TrayUiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ApplyState(state)));
            return;
        }

        currentState = state;
        string deviceName = string.IsNullOrWhiteSpace(state.DeviceName)
            ? "小米蓝牙语音遥控器"
            : state.DeviceName.Trim();
        string statusText = GetStatusText(state);
        string detailText = string.IsNullOrWhiteSpace(state.Detail)
            ? GetDefaultDetail(state.Connection)
            : state.Detail.Trim();

        deviceNameLabel.Text = deviceName;
        deviceNameLabel.AccessibleName = $"遥控器名称：{deviceName}";
        statusBadge.Text = statusText;
        statusBadge.AccessibleName = $"连接状态：{statusText}";
        detailLabel.Text = detailText;
        detailLabel.AccessibleName = $"语音桥状态：{detailText}";

        ApplyBatteryState(state);
        ApplyActionState(state);
        startWithWindowsCheckBox.Checked = state.StartWithWindows;
        startWithWindowsCheckBox.AccessibleDescription = state.StartWithWindows
            ? "已启用。点击可关闭登录 Windows 后自动启动。"
            : "已关闭。点击可在登录 Windows 后自动启动。";
        ApplyStatusTone(state.Connection);
    }

    public void AllowCloseAndDispose()
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(AllowCloseAndDispose));
            return;
        }

        allowClose = true;
        Close();
        if (!IsDisposed)
        {
            Dispose();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (!allowClose && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(eventArgs);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void OnSystemColorsChanged(EventArgs eventArgs)
    {
        base.OnSystemColorsChanged(eventArgs);
        ApplyTheme();
        ApplyStatusTone(currentState.Connection);
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        ApplyTitleBarTheme();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            toolTip?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Font CreateUiFont(float size, FontStyle style) =>
        new("Microsoft YaHei UI", size, style, GraphicsUnit.Point);

    private static Font CreateDisplayFont(float size, FontStyle style) =>
        new("Segoe UI Variable Display", size, style, GraphicsUnit.Point);

    private static Label CreateLabel(string text) =>
        new()
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Text = text,
            UseMnemonic = false
        };

    private static Button CreateButton(string text, bool primary, int tabIndex)
    {
        var button = new Button
        {
            AutoEllipsis = true,
            BackColor = primary ? AccentColor : Color.White,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Font = CreateUiFont(9.25F, FontStyle.Regular),
            ForeColor = primary ? Color.White : InkColor,
            Margin = Padding.Empty,
            TabIndex = tabIndex,
            Text = text,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? AccentColor : HairlineColor;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.MouseOverBackColor = primary
            ? AccentHoverColor
            : SurfaceColor;
        button.FlatAppearance.MouseDownBackColor = primary
            ? Color.FromArgb(0, 76, 57)
            : Color.FromArgb(237, 240, 238);
        button.Resize += (_, _) => ApplyRoundedRegion(button, 10);
        button.HandleCreated += (_, _) => ApplyRoundedRegion(button, 10);
        return button;
    }

    private static Button CreateThemeButton()
    {
        var button = new Button
        {
            Anchor = AnchorStyles.Right,
            AutoEllipsis = true,
            FlatStyle = FlatStyle.Flat,
            Font = CreateUiFont(8.75F, FontStyle.Regular),
            Margin = Padding.Empty,
            Size = new Size(112, 34),
            TabIndex = 4,
            Text = "浅色模式",
            UseVisualStyleBackColor = false,
            AccessibleName = "切换界面主题",
            AccessibleDescription = "在深色与浅色界面之间切换，仅影响当前运行窗口。"
        };
        button.Resize += (_, _) => ApplyRoundedRegion(button, 12);
        button.HandleCreated += (_, _) => ApplyRoundedRegion(button, 12);
        return button;
    }

    private TableLayoutPanel CreateRootLayout()
    {
        var layout = new TableLayoutPanel
        {
            AutoSize = false,
            BackColor = darkMode ? DarkCanvasColor : CanvasColor,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Height = 1080,
            Margin = Padding.Empty,
            Padding = new Padding(40, 28, 40, 18),
            RowCount = 8
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 430F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        return layout;
    }

    private SurfacePanel CreateStatusCard()
    {
        var card = CreateSurfacePanel(16);
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 4, 0, 10);
        card.Padding = new Padding(28, 14, 28, 14);

        var layout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));

        var identity = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 3
        };
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
        identity.RowStyles.Add(new RowStyle(SizeType.Percent, 28F));
        identity.Controls.Add(deviceNameLabel, 0, 0);
        identity.Controls.Add(detailLabel, 0, 1);
        identity.Controls.Add(batteryMetaLabel, 0, 2);

        var battery = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        battery.RowStyles.Add(new RowStyle(SizeType.Percent, 72F));
        battery.RowStyles.Add(new RowStyle(SizeType.Percent, 28F));
        battery.Controls.Add(batteryValueLabel, 0, 0);
        var batteryCaption = CreateLabel("遥控器电量");
        batteryCaption.Dock = DockStyle.Fill;
        batteryCaption.Font = CreateUiFont(8.5F, FontStyle.Regular);
        batteryCaption.Tag = "status-secondary";
        batteryCaption.TextAlign = ContentAlignment.MiddleRight;
        battery.Controls.Add(batteryCaption, 0, 1);

        layout.Controls.Add(identity, 0, 0);
        layout.Controls.Add(battery, 1, 0);
        card.Controls.Add(layout);
        return card;
    }

    private SurfacePanel CreateControlsCard()
    {
        var card = CreateSurfacePanel(16);
        card.Dock = DockStyle.Fill;
        card.Margin = new Padding(0, 6, 0, 0);
        card.Padding = new Padding(18, 10, 18, 9);

        var layout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var buttons = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        connectButton.Margin = new Padding(0, 0, 8, 0);
        pauseButton.Margin = new Padding(4, 0, 4, 0);
        audioRouteButton.Margin = new Padding(8, 0, 0, 0);
        buttons.Controls.Add(connectButton, 0, 0);
        buttons.Controls.Add(pauseButton, 1, 0);
        buttons.Controls.Add(audioRouteButton, 2, 0);

        var preferenceRow = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(4, 7, 4, 0),
            RowCount = 1
        };
        preferenceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
        preferenceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        preferenceRow.Controls.Add(startWithWindowsCheckBox, 0, 0);
        var residentHint = CreateLabel("关闭窗口后仍在托盘运行");
        residentHint.Dock = DockStyle.Fill;
        residentHint.Font = CreateUiFont(8.5F, FontStyle.Regular);
        residentHint.Tag = "secondary";
        residentHint.TextAlign = ContentAlignment.MiddleRight;
        residentHint.AccessibleName = "关闭窗口后，MiVibe Remote 仍在托盘运行";
        preferenceRow.Controls.Add(residentHint, 1, 0);

        layout.Controls.Add(buttons, 0, 0);
        layout.Controls.Add(preferenceRow, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private void BuildLayout()
    {
        var identityLayout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        identityLayout.Controls.Add(titleLabel, 0, 0);
        identityLayout.Controls.Add(introLabel, 0, 1);

        var headerLayout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16F));
        headerLayout.Controls.Add(identityLayout, 0, 0);
        headerLayout.Controls.Add(themeToggleButton, 1, 0);
        headerLayout.Controls.Add(statusBadge, 2, 0);

        Control mappingHeading = CreateSectionHeading(
            "遥控器按键",
            "实拍校准插画 · 当前配置只读");

        Control workflowHeading = CreateSectionHeading(
            "语音输入工作流",
            "短按切换 · 长按采音");

        var workflows = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        workflows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        workflows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        SurfacePanel typelessCard = CreateWorkflowCard(
            "Typeless",
            "文本输入",
            ["轻触开关键", "按住麦克风说话", "松开后再轻触开关键"]);
        SurfacePanel voiceCard = CreateWorkflowCard(
            "Codex Voice",
            "Ctrl + Alt + *",
            ["轻触菜单键", "按住麦克风说话", "松开完成本段，可继续接话"]);
        typelessCard.Margin = new Padding(0, 0, 10, 0);
        voiceCard.Margin = new Padding(10, 0, 0, 0);
        workflows.Controls.Add(typelessCard, 0, 0);
        workflows.Controls.Add(voiceCard, 1, 0);

        var footer = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(4, 2, 4, 0),
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));
        var versionHint = CreateLabel("0.2 Preview · 关闭窗口不会中断语音桥");
        versionHint.Dock = DockStyle.Fill;
        versionHint.Font = CreateUiFont(8.5F, FontStyle.Regular);
        versionHint.Tag = "secondary";
        versionHint.TextAlign = ContentAlignment.MiddleLeft;
        versionHint.AutoEllipsis = true;
        footer.Controls.Add(versionHint, 0, 0);
        footer.Controls.Add(safeExitLink, 1, 0);

        rootLayout.Controls.Add(headerLayout, 0, 0);
        rootLayout.Controls.Add(statusCard, 0, 1);
        rootLayout.Controls.Add(mappingHeading, 0, 2);
        rootLayout.Controls.Add(remoteGuide, 0, 3);
        rootLayout.Controls.Add(workflowHeading, 0, 4);
        rootLayout.Controls.Add(workflows, 0, 5);
        rootLayout.Controls.Add(controlsCard, 0, 6);
        rootLayout.Controls.Add(footer, 0, 7);
    }

    private Control CreateSectionHeading(string title, string subtitle)
    {
        var layout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(4, 13, 4, 2),
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));

        var titleText = CreateLabel(title);
        titleText.Dock = DockStyle.Fill;
        titleText.Font = CreateUiFont(12.5F, FontStyle.Bold);
        titleText.TextAlign = ContentAlignment.MiddleLeft;
        var subtitleText = CreateLabel(subtitle);
        subtitleText.Dock = DockStyle.Fill;
        subtitleText.Font = CreateUiFont(9F, FontStyle.Regular);
        subtitleText.Tag = "secondary";
        subtitleText.TextAlign = ContentAlignment.MiddleRight;

        layout.Controls.Add(titleText, 0, 0);
        layout.Controls.Add(subtitleText, 1, 0);
        return layout;
    }

    private SurfacePanel CreateWorkflowCard(
        string title,
        string subtitle,
        IReadOnlyList<string> steps)
    {
        var card = CreateSurfacePanel(16);
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(24, 15, 24, 13);
        workflowCards.Add(card);

        var layout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var heading = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
        var titleText = CreateLabel(title);
        titleText.Dock = DockStyle.Fill;
        titleText.Font = CreateUiFont(12F, FontStyle.Bold);
        titleText.TextAlign = ContentAlignment.MiddleLeft;
        var subtitleText = CreateLabel(subtitle);
        subtitleText.Dock = DockStyle.Fill;
        subtitleText.Font = CreateUiFont(8.75F, FontStyle.Regular);
        subtitleText.Tag = "secondary";
        subtitleText.TextAlign = ContentAlignment.MiddleRight;
        heading.Controls.Add(titleText, 0, 0);
        heading.Controls.Add(subtitleText, 1, 0);

        var stepLayout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = steps.Count
        };
        for (int index = 0; index < steps.Count; index++)
        {
            stepLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / steps.Count));
            stepLayout.Controls.Add(CreateWorkflowStep(index + 1, steps[index]), 0, index);
        }

        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(stepLayout, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private static Control CreateWorkflowStep(int number, string text)
    {
        var row = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        var numberLabel = CreateLabel(number.ToString("00"));
        numberLabel.Dock = DockStyle.Fill;
        numberLabel.Font = CreateDisplayFont(9.5F, FontStyle.Bold);
        numberLabel.Tag = "warm-accent";
        numberLabel.TextAlign = ContentAlignment.MiddleLeft;
        var textLabel = CreateLabel(text);
        textLabel.Dock = DockStyle.Fill;
        textLabel.Font = CreateUiFont(9.5F, FontStyle.Regular);
        textLabel.TextAlign = ContentAlignment.MiddleLeft;
        textLabel.AutoEllipsis = true;
        row.Controls.Add(numberLabel, 0, 0);
        row.Controls.Add(textLabel, 1, 0);
        return row;
    }

    private static SurfacePanel CreateSurfacePanel(int radius) =>
        new()
        {
            BorderColor = HairlineColor,
            CornerRadius = radius,
            FillColor = SurfaceColor
        };

    private void ApplyBatteryState(TrayUiState state)
    {
        bool hasValidLevel = state.BatteryPercent is >= 0 and <= 100;
        string valueText;
        string metaText;

        if (!hasValidLevel || state.BatteryFreshness == BatteryFreshness.Unknown)
        {
            valueText = "—";
            metaText = "连接后读取电量";
        }
        else
        {
            valueText = $"{state.BatteryPercent}%";
            string updatedText = FormatUpdatedAt(state.BatteryUpdatedAt);
            metaText = state.BatteryFreshness == BatteryFreshness.LastKnown
                ? $"上次读取 · {updatedText}"
                : $"电量已更新 · {updatedText}";
        }

        batteryValueLabel.Text = valueText;
        batteryValueLabel.AccessibleName = state.BatteryFreshness == BatteryFreshness.LastKnown && hasValidLevel
            ? $"遥控器电量：{state.BatteryPercent}%，上次读取"
            : $"遥控器电量：{(hasValidLevel ? state.BatteryPercent + "%" : "未知")}";
        batteryMetaLabel.Text = metaText;
        batteryMetaLabel.AccessibleName = $"电量状态：{metaText}";
    }

    private void ApplyActionState(TrayUiState state)
    {
        bool busy = state.OperationInProgress;

        switch (state.Connection)
        {
            case ConnectionPhase.Connected:
                SetConnectButton("重新连接", "重新连接遥控器", !busy);
                SetPauseButton("暂停语音桥", "暂停语音桥", !busy);
                break;
            case ConnectionPhase.ReconnectWaiting:
                SetConnectButton("立即重连", "立即重新连接遥控器", !busy);
                SetPauseButton("暂停自动重连", "暂停自动重连", !busy);
                break;
            case ConnectionPhase.Starting:
            case ConnectionPhase.Connecting:
                SetConnectButton("正在连接…", "正在连接遥控器", enabled: false);
                SetPauseButton("暂停连接", "暂停连接", !busy);
                break;
            case ConnectionPhase.Stopping:
                SetConnectButton("正在安全停止…", "正在安全停止语音桥", enabled: false);
                SetPauseButton("请稍候", "正在安全停止，请稍候", enabled: false);
                break;
            case ConnectionPhase.Paused:
                SetConnectButton("连接遥控器", "连接遥控器", !busy);
                SetPauseButton("已暂停", "语音桥已暂停", enabled: false);
                break;
            case ConnectionPhase.Disconnected:
            case ConnectionPhase.Error:
            default:
                SetConnectButton("连接遥控器", "连接遥控器", !busy);
                SetPauseButton("暂停语音桥", "暂停语音桥", enabled: false);
                break;
        }

        audioRouteButton.Enabled = state.Connection != ConnectionPhase.Stopping;
        startWithWindowsCheckBox.Enabled = state.Connection != ConnectionPhase.Stopping;
        safeExitLink.Enabled = state.Connection != ConnectionPhase.Stopping;
    }

    private void SetConnectButton(string text, string accessibleName, bool enabled)
    {
        connectButton.Text = text;
        connectButton.AccessibleName = accessibleName;
        connectButton.Enabled = enabled;
    }

    private void SetPauseButton(string text, string accessibleName, bool enabled)
    {
        pauseButton.Text = text;
        pauseButton.AccessibleName = accessibleName;
        pauseButton.Enabled = enabled;
    }

    private static bool IsSystemDarkModePreferred()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme
                ? appsUseLightTheme == 0
                : true;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or
                System.Security.SecurityException or
                IOException)
        {
            return true;
        }
    }

    private void ApplyTheme()
    {
        bool highContrast = SystemInformation.HighContrast;
        Color canvas = highContrast
            ? SystemColors.Window
            : darkMode ? DarkCanvasColor : CanvasColor;
        Color surface = highContrast
            ? SystemColors.Window
            : darkMode ? DarkSurfaceColor : SurfaceColor;
        Color elevatedSurface = highContrast
            ? SystemColors.Window
            : darkMode ? DarkElevatedSurfaceColor : ElevatedSurfaceColor;
        Color ink = highContrast
            ? SystemColors.WindowText
            : darkMode ? DarkInkColor : InkColor;
        Color mutedInk = highContrast
            ? SystemColors.WindowText
            : darkMode ? DarkMutedInkColor : MutedInkColor;
        Color secondaryInk = highContrast
            ? SystemColors.WindowText
            : darkMode ? DarkSecondaryInkColor : SecondaryInkColor;
        Color border = highContrast
            ? SystemColors.WindowText
            : darkMode ? DarkHairlineColor : HairlineColor;
        Color statusSurface = highContrast
            ? SystemColors.Window
            : darkMode ? DarkStatusSurfaceColor : NightForestColor;
        Color statusSecondary = highContrast
            ? SystemColors.WindowText
            : darkMode ? Color.FromArgb(197, 221, 210) : StatusSecondaryColor;

        BackColor = canvas;
        ForeColor = ink;
        rootLayout.BackColor = canvas;
        statusCard.FillColor = statusSurface;
        statusCard.BorderColor = border;
        statusCard.OutsideColor = canvas;
        controlsCard.FillColor = surface;
        controlsCard.BorderColor = border;
        controlsCard.OutsideColor = canvas;
        foreach (SurfacePanel card in workflowCards)
        {
            card.FillColor = elevatedSurface;
            card.BorderColor = border;
            card.OutsideColor = canvas;
        }

        titleLabel.ForeColor = ink;
        introLabel.ForeColor = mutedInk;
        deviceNameLabel.ForeColor = highContrast ? SystemColors.WindowText : Color.White;
        detailLabel.ForeColor = statusSecondary;
        batteryValueLabel.ForeColor = highContrast ? SystemColors.WindowText : WarmAccentColor;
        batteryMetaLabel.ForeColor = statusSecondary;
        startWithWindowsCheckBox.ForeColor = ink;
        ApplySemanticTextColors(
            rootLayout,
            highContrast,
            secondaryInk,
            darkMode ? Color.FromArgb(51, 199, 155) : AccentColor,
            WarmAccentColor,
            statusSecondary);
        remoteGuide.DarkMode = darkMode;

        if (highContrast)
        {
            connectButton.FlatStyle = FlatStyle.System;
            pauseButton.FlatStyle = FlatStyle.System;
            audioRouteButton.FlatStyle = FlatStyle.System;
            themeToggleButton.FlatStyle = FlatStyle.System;
            connectButton.UseVisualStyleBackColor = true;
            pauseButton.UseVisualStyleBackColor = true;
            audioRouteButton.UseVisualStyleBackColor = true;
            themeToggleButton.UseVisualStyleBackColor = true;
            themeToggleButton.Enabled = false;
            themeToggleButton.Text = "系统高对比度";
        }
        else
        {
            StyleButton(connectButton, primary: true, ink, surface, border);
            StyleButton(pauseButton, primary: false, ink, surface, border);
            StyleButton(audioRouteButton, primary: false, ink, surface, border);
            StyleButton(themeToggleButton, primary: false, ink, surface, border);
            themeToggleButton.Enabled = true;
            themeToggleButton.Text = darkMode ? "浅色模式" : "深色模式";
        }

        safeExitLink.LinkColor = highContrast ? SystemColors.HotTrack : MutedInkColor;
        if (darkMode && !highContrast)
        {
            safeExitLink.LinkColor = DarkMutedInkColor;
        }

        safeExitLink.ActiveLinkColor = highContrast
            ? SystemColors.Highlight
            : darkMode ? Color.FromArgb(255, 180, 169) : ErrorForeColor;
        safeExitLink.VisitedLinkColor = safeExitLink.LinkColor;
        ApplyStatusTone(currentState.Connection);
        ApplyTitleBarTheme();
        Invalidate(true);
    }

    private static void ApplySemanticTextColors(
        Control root,
        bool highContrast,
        Color secondaryInk,
        Color accent,
        Color warmAccent,
        Color statusSecondary)
    {
        foreach (Control child in root.Controls)
        {
            if (string.Equals(child.Tag as string, "secondary", StringComparison.Ordinal))
            {
                child.ForeColor = highContrast ? SystemColors.WindowText : secondaryInk;
            }
            else if (string.Equals(child.Tag as string, "accent", StringComparison.Ordinal))
            {
                child.ForeColor = highContrast ? SystemColors.HotTrack : accent;
            }
            else if (string.Equals(child.Tag as string, "warm-accent", StringComparison.Ordinal))
            {
                child.ForeColor = highContrast ? SystemColors.HotTrack : warmAccent;
            }
            else if (string.Equals(child.Tag as string, "status-secondary", StringComparison.Ordinal))
            {
                child.ForeColor = highContrast ? SystemColors.WindowText : statusSecondary;
            }

            if (child.HasChildren)
            {
                ApplySemanticTextColors(
                    child,
                    highContrast,
                    secondaryInk,
                    accent,
                    warmAccent,
                    statusSecondary);
            }
        }
    }

    private void StyleButton(
        Button button,
        bool primary,
        Color ink,
        Color surface,
        Color border)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary
            ? darkMode ? WarmAccentColor : AccentColor
            : darkMode ? DarkSecondaryButtonColor : surface;
        button.ForeColor = primary
            ? darkMode ? NightForestColor : Color.White
            : ink;
        button.FlatAppearance.BorderColor = primary
            ? button.BackColor
            : border;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.MouseOverBackColor = primary
            ? darkMode ? Color.FromArgb(255, 155, 75) : AccentHoverColor
            : darkMode ? DarkSecondaryButtonHoverColor : Color.FromArgb(238, 242, 239);
        button.FlatAppearance.MouseDownBackColor = primary
            ? darkMode ? Color.FromArgb(214, 104, 16) : Color.FromArgb(0, 76, 57)
            : darkMode ? Color.FromArgb(8, 43, 30) : Color.FromArgb(229, 235, 231);
    }

    private void ApplyStatusTone(ConnectionPhase phase)
    {
        if (SystemInformation.HighContrast)
        {
            statusBadge.BadgeBackColor = SystemColors.Highlight;
            statusBadge.BadgeForeColor = SystemColors.HighlightText;
            return;
        }

        (Color backColor, Color foreColor) = darkMode
            ? phase switch
            {
                ConnectionPhase.Connected =>
                    (Color.FromArgb(14, 81, 61), Color.FromArgb(184, 243, 220)),
                ConnectionPhase.Starting or
                    ConnectionPhase.Connecting or
                    ConnectionPhase.ReconnectWaiting =>
                    (Color.FromArgb(75, 43, 21), Color.FromArgb(255, 208, 165)),
                ConnectionPhase.Error =>
                    (Color.FromArgb(74, 37, 34), Color.FromArgb(255, 180, 169)),
                _ => (DarkSurfaceColor, DarkMutedInkColor)
            }
            : phase switch
            {
                ConnectionPhase.Connected => (ConnectedBackColor, ConnectedForeColor),
                ConnectionPhase.Starting or
                    ConnectionPhase.Connecting or
                    ConnectionPhase.ReconnectWaiting => (WaitingBackColor, WaitingForeColor),
                ConnectionPhase.Error => (ErrorBackColor, ErrorForeColor),
                _ => (Color.FromArgb(224, 231, 227), NightForestColor)
            };

        statusBadge.BadgeBackColor = backColor;
        statusBadge.BadgeForeColor = foreColor;
    }

    private void ApplyTitleBarTheme()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        int useDarkMode = darkMode && !SystemInformation.HighContrast ? 1 : 0;
        int cornerPreference = 2;
        int captionColor = ColorTranslator.ToWin32(
            SystemInformation.HighContrast
                ? SystemColors.Window
                : darkMode ? DarkCanvasColor : CanvasColor);
        int textColor = ColorTranslator.ToWin32(
            SystemInformation.HighContrast
                ? SystemColors.WindowText
                : darkMode ? DarkInkColor : InkColor);
        try
        {
            _ = DwmSetWindowAttribute(
                Handle,
                DwmwaUseImmersiveDarkMode,
                ref useDarkMode,
                sizeof(int));
            _ = DwmSetWindowAttribute(
                Handle,
                DwmwaWindowCornerPreference,
                ref cornerPreference,
                sizeof(int));
            _ = DwmSetWindowAttribute(
                Handle,
                DwmwaCaptionColor,
                ref captionColor,
                sizeof(int));
            _ = DwmSetWindowAttribute(
                Handle,
                DwmwaTextColor,
                ref textColor,
                sizeof(int));
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows builds keep the standard title bar.
        }
    }

    private static void ApplyRoundedRegion(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0)
        {
            return;
        }

        using GraphicsPath path = CreateRoundedPath(
            new RectangleF(0, 0, control.Width, control.Height),
            radius);
        Region? oldRegion = control.Region;
        control.Region = new Region(path);
        oldRegion?.Dispose();
    }

    private static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2F, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        if (diameter <= 1F)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    private static string GetStatusText(TrayUiState state) =>
        state.Connection switch
        {
            ConnectionPhase.Starting => "正在启动",
            ConnectionPhase.Connecting => "正在连接",
            ConnectionPhase.Connected => "已连接",
            ConnectionPhase.ReconnectWaiting when state.ReconnectDelaySeconds is > 0 =>
                $"{state.ReconnectDelaySeconds} 秒后重试",
            ConnectionPhase.ReconnectWaiting => "等待重连",
            ConnectionPhase.Paused => "已暂停",
            ConnectionPhase.Stopping => "正在停止",
            ConnectionPhase.Disconnected => "未连接",
            ConnectionPhase.Error => "连接失败",
            _ => "状态未知"
        };

    private static string GetDefaultDetail(ConnectionPhase phase) =>
        phase switch
        {
            ConnectionPhase.Starting => "正在准备语音桥…",
            ConnectionPhase.Connecting => "正在连接小米遥控器…",
            ConnectionPhase.Connected =>
                "BLE · ATVV 16 kHz · VB-CABLE · Typeless / Codex Voice 可用",
            ConnectionPhase.ReconnectWaiting => "蓝牙会话已断开，正在等待自动恢复",
            ConnectionPhase.Paused => "语音桥已暂停，按键钩子已恢复",
            ConnectionPhase.Stopping => "正在安全清理蓝牙会话和按键钩子…",
            ConnectionPhase.Disconnected => "请确认 Windows 蓝牙已开启且遥控器已配对",
            ConnectionPhase.Error => "连接失败，请检查遥控器和蓝牙状态",
            _ => ""
        };

    private static string FormatUpdatedAt(DateTimeOffset? updatedAt)
    {
        if (updatedAt is null)
        {
            return "时间未知";
        }

        TimeSpan age = DateTimeOffset.Now - updatedAt.Value.ToLocalTime();
        if (age >= TimeSpan.Zero && age < TimeSpan.FromMinutes(1))
        {
            return "刚刚";
        }

        return updatedAt.Value.ToLocalTime().ToString("HH:mm");
    }

    private void OnShown(object? sender, EventArgs eventArgs)
    {
        Rectangle workArea = Screen.FromControl(this).WorkingArea;
        int targetWidth = Math.Min(Width, Math.Max(MinimumSize.Width, workArea.Width - 48));
        int targetHeight = Math.Min(Height, Math.Max(MinimumSize.Height, workArea.Height - 48));
        Size = new Size(targetWidth, targetHeight);
        Location = new Point(
            workArea.Left + ((workArea.Width - Width) / 2),
            workArea.Top + ((workArea.Height - Height) / 2));

        if (connectButton.Enabled)
        {
            connectButton.Focus();
        }
        else if (pauseButton.Enabled)
        {
            pauseButton.Focus();
        }
        else
        {
            audioRouteButton.Focus();
        }
    }

    private sealed class SurfacePanel : Panel
    {
        private Color fillColor = SurfaceColor;
        private Color borderColor = HairlineColor;
        private Color outsideColor = CanvasColor;

        public SurfacePanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color FillColor
        {
            get => fillColor;
            set
            {
                fillColor = value;
                Invalidate();
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor
        {
            get => borderColor;
            set
            {
                borderColor = value;
                Invalidate();
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color OutsideColor
        {
            get => outsideColor;
            set
            {
                outsideColor = value;
                Invalidate();
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int CornerRadius { get; set; } = 16;

        protected override void OnPaintBackground(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            eventArgs.Graphics.Clear(
                SystemInformation.HighContrast ? SystemColors.Window : outsideColor);
            using GraphicsPath path = CreateRoundedPath(
                new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F),
                CornerRadius);
            using var brush = new SolidBrush(fillColor);
            eventArgs.Graphics.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = CreateRoundedPath(
                new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F),
                CornerRadius);
            using var pen = new Pen(borderColor, 1F);
            eventArgs.Graphics.DrawPath(pen, path);
        }
    }

    private sealed class PillBadge : Label
    {
        private Color badgeBackColor = WaitingBackColor;
        private Color badgeForeColor = WaitingForeColor;

        public PillBadge()
        {
            AutoSize = false;
            BackColor = Color.Transparent;
            Font = CreateUiFont(9F, FontStyle.Bold);
            TextAlign = ContentAlignment.MiddleCenter;
            UseMnemonic = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BadgeBackColor
        {
            get => badgeBackColor;
            set
            {
                badgeBackColor = value;
                Invalidate();
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BadgeForeColor
        {
            get => badgeForeColor;
            set
            {
                badgeForeColor = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = CreateRoundedPath(
                new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F),
                Height / 2F);
            using var brush = new SolidBrush(badgeBackColor);
            eventArgs.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(
                eventArgs.Graphics,
                Text,
                Font,
                ClientRectangle,
                badgeForeColor,
                TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis);
        }
    }
}
