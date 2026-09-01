using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MiVibe.Remote.Tray;

internal sealed class StatusWindow : Form
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private static readonly Color CanvasColor = Color.FromArgb(243, 244, 246);
    private static readonly Color CardColor = Color.White;
    private static readonly Color InkColor = Color.FromArgb(17, 24, 39);
    private static readonly Color MutedInkColor = Color.FromArgb(100, 116, 139);
    private static readonly Color BorderColor = Color.FromArgb(203, 213, 225);
    private static readonly Color AccentColor = Color.FromArgb(37, 99, 235);
    private static readonly Color DarkCanvasColor = Color.FromArgb(24, 27, 33);
    private static readonly Color DarkCardColor = Color.FromArgb(36, 40, 48);
    private static readonly Color DarkInkColor = Color.FromArgb(241, 245, 249);
    private static readonly Color DarkMutedInkColor = Color.FromArgb(148, 163, 184);
    private static readonly Color DarkBorderColor = Color.FromArgb(71, 85, 105);
    private static readonly Color ConnectedBackColor = Color.FromArgb(230, 244, 238);
    private static readonly Color ConnectedForeColor = Color.FromArgb(15, 107, 79);
    private static readonly Color WaitingBackColor = Color.FromArgb(255, 241, 224);
    private static readonly Color WaitingForeColor = Color.FromArgb(139, 75, 0);
    private static readonly Color ErrorBackColor = Color.FromArgb(253, 235, 233);
    private static readonly Color ErrorForeColor = Color.FromArgb(165, 42, 36);

    private readonly TableLayoutPanel rootLayout;
    private readonly TableLayoutPanel cardFrame;
    private readonly TableLayoutPanel cardLayout;
    private readonly Panel accentRail;
    private readonly Label titleLabel;
    private readonly Label introLabel;
    private readonly Label statusLabel;
    private readonly Label deviceNameLabel;
    private readonly Label detailLabel;
    private readonly Label batteryValueLabel;
    private readonly Label batteryMetaLabel;
    private readonly Button connectButton;
    private readonly Button pauseButton;
    private readonly Button audioRouteButton;
    private readonly CheckBox startWithWindowsCheckBox;
    private readonly Label residentHintLabel;
    private readonly LinkLabel safeExitLink;
    private readonly ToolTip toolTip;

    private TrayUiState currentState = TrayUiState.Initial;
    private bool allowClose;

    public StatusWindow()
    {
        Text = "MiVibe Remote";
        AccessibleName = "MiVibe Remote 状态窗口";
        AccessibleDescription = "查看小米遥控器连接、电量和语音桥状态。";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScroll = true;
        ClientSize = new Size(460, 390);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Font = SystemFonts.MessageBoxFont;

        titleLabel = CreateLabel("MiVibe Remote", autoSize: true);
        titleLabel.Font = new Font(Font.FontFamily, 15.5F, FontStyle.Bold, GraphicsUnit.Point);
        titleLabel.AutoEllipsis = true;
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        titleLabel.AccessibleRole = AccessibleRole.StaticText;

        introLabel = CreateLabel(
            "小米遥控器的 Vibe Coding 语音入口",
            autoSize: true);
        introLabel.Font = new Font(
            Font.FontFamily,
            8.5F,
            FontStyle.Regular,
            GraphicsUnit.Point);
        introLabel.AutoSize = false;
        introLabel.AutoEllipsis = true;
        introLabel.Dock = DockStyle.Fill;
        introLabel.TextAlign = ContentAlignment.TopLeft;
        introLabel.AccessibleName = "产品简介";

        statusLabel = CreateLabel("正在启动", autoSize: true);
        statusLabel.Anchor = AnchorStyles.Right;
        statusLabel.BorderStyle = BorderStyle.FixedSingle;
        statusLabel.Padding = new Padding(9, 4, 9, 4);
        statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        statusLabel.AccessibleRole = AccessibleRole.StatusBar;
        statusLabel.AccessibleName = "连接状态：正在启动";

        deviceNameLabel = CreateLabel("小米蓝牙语音遥控器", autoSize: false);
        deviceNameLabel.Dock = DockStyle.Fill;
        deviceNameLabel.Font = new Font(Font, FontStyle.Bold);
        deviceNameLabel.TextAlign = ContentAlignment.BottomLeft;
        deviceNameLabel.AutoEllipsis = true;
        deviceNameLabel.AccessibleName = "遥控器名称";

        batteryValueLabel = CreateLabel("电量未知", autoSize: false);
        batteryValueLabel.Dock = DockStyle.Fill;
        batteryValueLabel.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold, GraphicsUnit.Point);
        batteryValueLabel.TextAlign = ContentAlignment.BottomRight;
        batteryValueLabel.AutoEllipsis = true;
        batteryValueLabel.AccessibleName = "遥控器电量：未知";

        detailLabel = CreateLabel("正在准备语音桥…", autoSize: false);
        detailLabel.Dock = DockStyle.Fill;
        detailLabel.TextAlign = ContentAlignment.MiddleLeft;
        detailLabel.AutoEllipsis = true;
        detailLabel.AccessibleName = "语音桥状态";

        batteryMetaLabel = CreateLabel("连接后读取电量", autoSize: false);
        batteryMetaLabel.Dock = DockStyle.Fill;
        batteryMetaLabel.TextAlign = ContentAlignment.TopLeft;
        batteryMetaLabel.AutoEllipsis = true;
        batteryMetaLabel.AccessibleName = "电量更新时间";

        connectButton = CreateButton("连接遥控器(&R)", 0);
        connectButton.AccessibleName = "连接遥控器";
        connectButton.AccessibleDescription = "建立或重新建立与小米遥控器的蓝牙会话。";
        connectButton.Click += (_, _) =>
            ConnectOrReconnectRequested?.Invoke(this, EventArgs.Empty);

        pauseButton = CreateButton("暂停语音桥(&P)", 1);
        pauseButton.AccessibleName = "暂停语音桥";
        pauseButton.AccessibleDescription = "安全停止当前语音桥或暂停自动重连。";
        pauseButton.Click += (_, _) => PauseRequested?.Invoke(this, EventArgs.Empty);

        audioRouteButton = CreateButton("检查音频路由(&A)", 2);
        audioRouteButton.AccessibleName = "检查音频路由";
        audioRouteButton.AccessibleDescription = "检查 VB-CABLE、录音和播放设备配置。";
        audioRouteButton.Click += (_, _) => AudioRouteRequested?.Invoke(this, EventArgs.Empty);

        startWithWindowsCheckBox = new CheckBox
        {
            Anchor = AnchorStyles.Left,
            AutoCheck = false,
            AutoSize = true,
            Margin = new Padding(12, 0, 0, 0),
            TabIndex = 3,
            Text = "开机自动启动(&S)",
            AccessibleName = "开机自动启动",
            AccessibleDescription = "控制 MiVibe Remote 是否在登录 Windows 后自动启动。"
        };
        startWithWindowsCheckBox.Click += (_, _) =>
            StartWithWindowsToggleRequested?.Invoke(this, EventArgs.Empty);

        residentHintLabel = CreateLabel(
            "开关键 Typeless · TV 键 Codex Voice · 按住麦克风说话\r\n关闭窗口后仍在托盘运行",
            autoSize: true);
        residentHintLabel.Anchor = AnchorStyles.Left;
        residentHintLabel.Font = new Font(Font.FontFamily, 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        residentHintLabel.AccessibleName = "关闭窗口后，MiVibe Remote 仍在托盘运行";

        safeExitLink = new LinkLabel
        {
            Anchor = AnchorStyles.Right,
            AutoSize = true,
            Margin = Padding.Empty,
            TabIndex = 4,
            Text = "安全退出(&X)",
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
        toolTip.SetToolTip(connectButton, "安全清理旧会话后重新连接，不会强制终止进程。");
        toolTip.SetToolTip(startWithWindowsCheckBox, "使用当前 Windows 用户的登录启动项，默认关闭。");
        toolTip.SetToolTip(safeExitLink, "退出前会恢复按键钩子并关闭蓝牙会话。");

        rootLayout = CreateRootLayout();
        cardFrame = CreateCardFrame();
        accentRail = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            AccessibleRole = AccessibleRole.None
        };
        cardLayout = CreateCardLayout();

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
        statusLabel.Text = statusText;
        statusLabel.AccessibleName = $"连接状态：{statusText}";
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

    protected override void OnActivated(EventArgs eventArgs)
    {
        base.OnActivated(eventArgs);
        ApplyTheme();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            toolTip?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Label CreateLabel(string text, bool autoSize)
    {
        return new Label
        {
            AutoSize = autoSize,
            Margin = Padding.Empty,
            Text = text,
            UseMnemonic = false
        };
    }

    private static Button CreateButton(string text, int tabIndex)
    {
        return new Button
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Margin = Padding.Empty,
            TabIndex = tabIndex,
            Text = text,
            UseVisualStyleBackColor = false
        };
    }

    private TableLayoutPanel CreateRootLayout()
    {
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(20, 16, 20, 12),
            RowCount = 5
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        return layout;
    }

    private static TableLayoutPanel CreateCardFrame()
    {
        var frame = new TableLayoutPanel
        {
            BorderStyle = BorderStyle.FixedSingle,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 6),
            Padding = Padding.Empty,
            RowCount = 1
        };
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 4F));
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        return frame;
    }

    private TableLayoutPanel CreateCardLayout()
    {
        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(14, 9, 12, 8),
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30F));
        return layout;
    }

    private void BuildLayout()
    {
        var identityLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 2
        };
        identityLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        identityLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        identityLayout.Controls.Add(titleLabel, 0, 0);
        identityLayout.Controls.Add(introLabel, 0, 1);

        var headerLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        headerLayout.Controls.Add(identityLayout, 0, 0);
        headerLayout.Controls.Add(statusLabel, 1, 0);

        cardLayout.Controls.Add(deviceNameLabel, 0, 0);
        cardLayout.Controls.Add(batteryValueLabel, 1, 0);
        cardLayout.Controls.Add(detailLabel, 0, 1);
        cardLayout.SetColumnSpan(detailLabel, 2);
        cardLayout.Controls.Add(batteryMetaLabel, 0, 2);
        cardLayout.SetColumnSpan(batteryMetaLabel, 2);
        cardFrame.Controls.Add(accentRail, 0, 0);
        cardFrame.Controls.Add(cardLayout, 1, 0);

        var actionLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 4),
            RowCount = 1
        };
        actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        connectButton.Margin = new Padding(0, 0, 5, 0);
        pauseButton.Margin = new Padding(5, 0, 0, 0);
        actionLayout.Controls.Add(connectButton, 0, 0);
        actionLayout.Controls.Add(pauseButton, 1, 0);

        var utilityLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 4),
            RowCount = 1
        };
        utilityLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        utilityLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        audioRouteButton.Margin = new Padding(0, 0, 5, 0);
        utilityLayout.Controls.Add(audioRouteButton, 0, 0);
        utilityLayout.Controls.Add(startWithWindowsCheckBox, 1, 0);

        var footerLayout = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 8, 0, 0),
            RowCount = 1
        };
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        footerLayout.Controls.Add(residentHintLabel, 0, 0);
        footerLayout.Controls.Add(safeExitLink, 1, 0);

        rootLayout.Controls.Add(headerLayout, 0, 0);
        rootLayout.Controls.Add(cardFrame, 0, 1);
        rootLayout.Controls.Add(actionLayout, 0, 2);
        rootLayout.Controls.Add(utilityLayout, 0, 3);
        rootLayout.Controls.Add(footerLayout, 0, 4);
    }

    private void ApplyBatteryState(TrayUiState state)
    {
        bool hasValidLevel = state.BatteryPercent is >= 0 and <= 100;
        string valueText;
        string metaText;

        if (!hasValidLevel || state.BatteryFreshness == BatteryFreshness.Unknown)
        {
            valueText = "电量未知";
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
            : $"遥控器电量：{valueText}";
        batteryMetaLabel.Text = metaText;
        batteryMetaLabel.AccessibleName = $"电量状态：{metaText}";
    }

    private void ApplyActionState(TrayUiState state)
    {
        bool busy = state.OperationInProgress;

        switch (state.Connection)
        {
            case ConnectionPhase.Connected:
                SetConnectButton("重新连接遥控器(&R)", "重新连接遥控器", !busy);
                SetPauseButton("暂停语音桥(&P)", "暂停语音桥", !busy);
                break;
            case ConnectionPhase.ReconnectWaiting:
                SetConnectButton("立即重连(&R)", "立即重新连接遥控器", !busy);
                SetPauseButton("暂停自动重连(&P)", "暂停自动重连", !busy);
                break;
            case ConnectionPhase.Starting:
            case ConnectionPhase.Connecting:
                SetConnectButton("正在连接…", "正在连接遥控器", enabled: false);
                SetPauseButton("暂停连接(&P)", "暂停连接", !busy);
                break;
            case ConnectionPhase.Stopping:
                SetConnectButton("正在安全停止…", "正在安全停止语音桥", enabled: false);
                SetPauseButton("请稍候", "正在安全停止，请稍候", enabled: false);
                break;
            case ConnectionPhase.Paused:
                SetConnectButton("连接遥控器(&R)", "连接遥控器", !busy);
                SetPauseButton("已暂停", "语音桥已暂停", enabled: false);
                break;
            case ConnectionPhase.Disconnected:
            case ConnectionPhase.Error:
            default:
                SetConnectButton("连接遥控器(&R)", "连接遥控器", !busy);
                SetPauseButton("暂停语音桥(&P)", "暂停语音桥", enabled: false);
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

    private void ApplyTheme()
    {
        bool highContrast = SystemInformation.HighContrast;
        bool dark = !highContrast && IsDarkThemePreferred();
        Color canvas = highContrast
            ? SystemColors.Window
            : dark ? DarkCanvasColor : CanvasColor;
        Color card = highContrast
            ? SystemColors.Window
            : dark ? DarkCardColor : CardColor;
        Color ink = highContrast
            ? SystemColors.WindowText
            : dark ? DarkInkColor : InkColor;
        Color mutedInk = highContrast
            ? SystemColors.WindowText
            : dark ? DarkMutedInkColor : MutedInkColor;
        Color border = highContrast
            ? SystemColors.WindowText
            : dark ? DarkBorderColor : BorderColor;
        Color accent = highContrast ? SystemColors.Highlight : AccentColor;

        BackColor = canvas;
        ForeColor = ink;
        rootLayout.BackColor = canvas;
        cardFrame.BackColor = border;
        cardLayout.BackColor = card;
        accentRail.BackColor = accent;
        titleLabel.ForeColor = ink;
        introLabel.ForeColor = mutedInk;
        deviceNameLabel.ForeColor = ink;
        detailLabel.ForeColor = mutedInk;
        batteryValueLabel.ForeColor = ink;
        batteryMetaLabel.ForeColor = mutedInk;
        residentHintLabel.ForeColor = mutedInk;
        startWithWindowsCheckBox.ForeColor = ink;
        startWithWindowsCheckBox.BackColor = canvas;

        StylePrimaryButton(connectButton, highContrast, accent);
        StyleSecondaryButton(pauseButton, highContrast, canvas, ink, border);
        StyleSecondaryButton(audioRouteButton, highContrast, canvas, ink, border);

        safeExitLink.LinkColor = highContrast ? SystemColors.HotTrack : accent;
        safeExitLink.ActiveLinkColor = highContrast ? SystemColors.Highlight : ErrorForeColor;
        safeExitLink.VisitedLinkColor = safeExitLink.LinkColor;
        ApplyStatusTone(currentState.Connection);
        ApplyTitleBarTheme();
    }

    private static void StylePrimaryButton(
        Button button,
        bool highContrast,
        Color accent)
    {
        if (highContrast)
        {
            button.FlatStyle = FlatStyle.System;
            button.UseVisualStyleBackColor = true;
            return;
        }

        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = accent;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderColor = accent;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(29, 78, 216);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 64, 175);
    }

    private static void StyleSecondaryButton(
        Button button,
        bool highContrast,
        Color canvas,
        Color ink,
        Color border)
    {
        if (highContrast)
        {
            button.FlatStyle = FlatStyle.System;
            button.UseVisualStyleBackColor = true;
            return;
        }

        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = canvas;
        button.ForeColor = ink;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(236, 239, 235);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(226, 231, 227);
    }

    private void ApplyStatusTone(ConnectionPhase phase)
    {
        if (SystemInformation.HighContrast)
        {
            statusLabel.BackColor = SystemColors.Highlight;
            statusLabel.ForeColor = SystemColors.HighlightText;
            return;
        }

        bool dark = IsDarkThemePreferred();
        (Color backColor, Color foreColor) = (dark, phase) switch
        {
            (true, ConnectionPhase.Connected) =>
                (Color.FromArgb(18, 63, 52), Color.FromArgb(110, 231, 183)),
            (true, ConnectionPhase.Starting or
                ConnectionPhase.Connecting or
                ConnectionPhase.ReconnectWaiting) =>
                (Color.FromArgb(68, 48, 21), Color.FromArgb(251, 191, 36)),
            (true, ConnectionPhase.Error) =>
                (Color.FromArgb(73, 40, 43), Color.FromArgb(252, 165, 165)),
            (true, _) =>
                (Color.FromArgb(51, 65, 85), Color.FromArgb(203, 213, 225)),
            (false, ConnectionPhase.Connected) => (ConnectedBackColor, ConnectedForeColor),
            (false, ConnectionPhase.Starting or
                ConnectionPhase.Connecting or
                ConnectionPhase.ReconnectWaiting) => (WaitingBackColor, WaitingForeColor),
            (false, ConnectionPhase.Error) => (ErrorBackColor, ErrorForeColor),
            _ => (Color.FromArgb(235, 238, 235), MutedInkColor)
        };

        statusLabel.BackColor = backColor;
        statusLabel.ForeColor = foreColor;
    }

    private static bool IsDarkThemePreferred()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            return false;
        }
    }

    private void ApplyTitleBarTheme()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        int enabled = !SystemInformation.HighContrast && IsDarkThemePreferred() ? 1 : 0;
        try
        {
            _ = DwmSetWindowAttribute(
                Handle,
                DwmwaUseImmersiveDarkMode,
                ref enabled,
                sizeof(int));
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older Windows builds keep the standard title bar.
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    private static string GetStatusText(TrayUiState state)
    {
        return state.Connection switch
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
    }

    private static string GetDefaultDetail(ConnectionPhase phase)
    {
        return phase switch
        {
            ConnectionPhase.Starting => "正在准备语音桥…",
            ConnectionPhase.Connecting => "正在连接小米遥控器…",
            ConnectionPhase.Connected => "语音桥运行中 · Typeless / Codex Voice 可用",
            ConnectionPhase.ReconnectWaiting => "蓝牙会话已断开，正在等待自动恢复",
            ConnectionPhase.Paused => "语音桥已暂停，按键钩子已恢复",
            ConnectionPhase.Stopping => "正在安全清理蓝牙会话和按键钩子…",
            ConnectionPhase.Disconnected => "请确认 Windows 蓝牙已开启且遥控器已配对",
            ConnectionPhase.Error => "连接失败，请检查遥控器和蓝牙状态",
            _ => ""
        };
    }

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
}
