using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MiVibe.Remote.Tray;

internal sealed class RemoteGuideControl : Control
{
    private const string ProductImageResourceName =
        "MiVibe.Remote.Tray.Assets.UserOwnedRemote2ProIllustration.png";
    private const float DesignWidth = 1450F;
    private const float DesignHeight = 460F;
    private readonly Image? productImage;
    private bool darkMode;
    private KeyBridgePhase keyBridgePhase;

    public RemoteGuideControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            value: true);
        DoubleBuffered = true;
        TabStop = false;
        productImage = LoadProductImage();
        BackColor = SystemInformation.HighContrast
            ? SystemColors.Window
            : CreatePalette(darkMode: true).Background;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "蓝牙语音遥控器示意图与当前功能";
        AccessibleDescription =
            "左侧显示依据用户自有实拍图校准的遥控器示意图，右侧列出当前按键映射。" +
            "开关键用于 Typeless 开始或结束；" +
            "麦克风键需要按住采音；方向环对应方向键；确认键对应 Enter；" +
            "Home 键单击执行一次 Delete；菜单键启动 Typeless Translate；TV 键原样输入反引号；" +
            "返回键执行一次 Backspace；音量加减切换 Codex 相邻任务，需要启用增强按键。" +
            "活动视图按当前已加载列表上下切换，到边界停止；请先关闭任务菜单。普通视图沿用默认任务快捷键。";
        MinimumSize = new Size(720, 320);
    }

    protected override Size DefaultSize => new(1080, 300);

    public void ApplyKeyBridgeState(KeyBridgePhase phase)
    {
        keyBridgePhase = phase;
        AccessibleDescription =
            "实拍遥控器与当前映射。开关键控制 Typeless，麦克风按住采音；" +
            "方向环移动，中心键 Enter；Home 执行一次 Delete；菜单启动 Typeless Translate；TV 保留原始输入。" +
            (phase == KeyBridgePhase.Active
                ? "增强按键已启用：返回执行一次 Backspace，音量加切换上一个任务，音量减切换下一个任务，仅在 Codex 位于前台时切换。" +
                  "活动视图按当前已加载列表上下切换，到边界停止；请先关闭任务菜单。普通视图沿用默认任务快捷键，无需新增绑定。"
                : $"增强按键暂不可用：{GetEnhancedKeyDetail("返回 Backspace / 音量切换任务")}");
        Invalidate();
    }

    private string GetEnhancedKeyDetail(string activeDetail) => keyBridgePhase switch
    {
        KeyBridgePhase.Active => activeDetail,
        KeyBridgePhase.Starting => "正在启动增强按键…",
        KeyBridgePhase.Calibrating => "按下方提示完成三键校准",
        KeyBridgePhase.Reconnecting => "连接恢复后可用",
        KeyBridgePhase.Error => "暂不可用 · 请在下方重新启用",
        KeyBridgePhase.Stopping => "正在停用增强按键…",
        _ => "启用增强按键后可用"
    };

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool DarkMode
    {
        get => darkMode;
        set
        {
            if (darkMode == value)
            {
                return;
            }

            darkMode = value;
            BackColor = CreatePalette(darkMode).Background;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);

        Graphics graphics = eventArgs.Graphics;
        GuidePalette palette = CreatePalette(darkMode);
        graphics.Clear(palette.Background);

        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        float dpiScale = Math.Max(1F, DeviceDpi / 96F);
        float outerInset = 12F * dpiScale;
        float availableWidth = Math.Max(1F, ClientSize.Width - (outerInset * 2F));
        float availableHeight = Math.Max(1F, ClientSize.Height - (outerInset * 2F));
        float scale = Math.Min(availableWidth / DesignWidth, availableHeight / DesignHeight);
        if (scale <= 0F)
        {
            return;
        }

        float artworkWidth = DesignWidth * scale;
        float artworkHeight = DesignHeight * scale;
        float offsetX = (ClientSize.Width - artworkWidth) / 2F;
        float offsetY = (ClientSize.Height - artworkHeight) / 2F;
        float hairlineWidth = Math.Max(1.1F, 1F / scale);

        GraphicsState state = graphics.Save();
        try
        {
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.TranslateTransform(offsetX, offsetY);
            graphics.ScaleTransform(scale, scale);

            DrawArtwork(graphics, palette, hairlineWidth);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    protected override void OnSystemColorsChanged(EventArgs eventArgs)
    {
        base.OnSystemColorsChanged(eventArgs);
        BackColor = SystemInformation.HighContrast
            ? SystemColors.Window
            : CreatePalette(darkMode).Background;
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs eventArgs)
    {
        base.OnDpiChangedAfterParent(eventArgs);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            productImage?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawArtwork(
        Graphics graphics,
        GuidePalette palette,
        float hairlineWidth)
    {
        FontFamily uiFontFamily =
            SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
        using var eyebrowFont = new Font(
            uiFontFamily,
            13F,
            FontStyle.Bold,
            GraphicsUnit.Pixel);
        using var panelTitleFont = new Font(
            uiFontFamily,
            23F,
            FontStyle.Bold,
            GraphicsUnit.Pixel);
        using var mappingTitleFont = new Font(
            uiFontFamily,
            18F,
            FontStyle.Bold,
            GraphicsUnit.Pixel);
        using var mappingDetailFont = new Font(
            uiFontFamily,
            15F,
            FontStyle.Regular,
            GraphicsUnit.Pixel);
        using var metaFont = new Font(
            uiFontFamily,
            14F,
            FontStyle.Regular,
            GraphicsUnit.Pixel);
        if (productImage is not null)
        {
            DrawWideGuide(
                graphics,
                palette,
                productImage,
                mappingTitleFont,
                mappingDetailFont,
                metaFont,
                hairlineWidth);
            return;
        }

        DrawSignalField(graphics, palette, hairlineWidth);
        DrawRemote(graphics, palette, hairlineWidth);
        DrawRemoteCaption(graphics, palette, eyebrowFont, metaFont);

        DrawMappingPanel(
            graphics,
            palette,
            panelTitleFont,
            mappingTitleFont,
            mappingDetailFont,
            metaFont,
            hairlineWidth);
    }

    private static Image? LoadProductImage()
    {
        using Stream? stream = typeof(RemoteGuideControl).Assembly
            .GetManifestResourceStream(ProductImageResourceName);
        if (stream is null)
        {
            return null;
        }

        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static void DrawProductPhoto(
        Graphics graphics,
        GuidePalette palette,
        Image image,
        Font eyebrowFont,
        Font metaFont,
        float hairlineWidth)
    {
        var stageBounds = new RectangleF(20F, 20F, 470F, 418F);
        using GraphicsPath stagePath = CreateRoundedRectangle(stageBounds, 26F);
        using var stageBrush = new SolidBrush(Color.FromArgb(5, 28, 20));
        using var borderPen = new Pen(palette.Hairline, hairlineWidth);
        graphics.FillPath(stageBrush, stagePath);

        GraphicsState clipState = graphics.Save();
        try
        {
            graphics.SetClip(stagePath, CombineMode.Replace);
            DrawImageContain(graphics, image, stageBounds, 14F);

            using var shadeBrush = new LinearGradientBrush(
                new RectangleF(stageBounds.X, stageBounds.Bottom - 92F, stageBounds.Width, 92F),
                Color.FromArgb(0, 5, 24, 16),
                Color.FromArgb(205, 5, 24, 16),
                LinearGradientMode.Vertical);
            graphics.FillRectangle(
                shadeBrush,
                stageBounds.X,
                stageBounds.Bottom - 92F,
                stageBounds.Width,
                92F);

            using var whiteBrush = new SolidBrush(Color.White);
            using var detailBrush = new SolidBrush(Color.FromArgb(215, 232, 224));
            graphics.DrawString(
                "BLUETOOTH VOICE REMOTE",
                eyebrowFont,
                whiteBrush,
                new RectangleF(48F, 370F, 414F, 22F),
                StringFormat.GenericTypographic);
            graphics.DrawString(
                "基于用户实拍图校准 · 无品牌标识",
                metaFont,
                detailBrush,
                new RectangleF(48F, 397F, 414F, 22F),
                StringFormat.GenericTypographic);
        }
        finally
        {
            graphics.Restore(clipState);
        }

        graphics.DrawPath(borderPen, stagePath);
    }

    private static void DrawImageContain(
        Graphics graphics,
        Image image,
        RectangleF destination,
        float inset)
    {
        RectangleF target = GetContainedBounds(image, destination, inset);

        graphics.DrawImage(
            image,
            target,
            new RectangleF(0F, 0F, image.Width, image.Height),
            GraphicsUnit.Pixel);
    }

    private static RectangleF GetContainedBounds(
        Image image,
        RectangleF destination,
        float inset)
    {
        RectangleF padded = RectangleF.Inflate(destination, -inset, -inset);
        float scale = Math.Min(
            padded.Width / image.Width,
            padded.Height / image.Height);
        float width = image.Width * scale;
        float height = image.Height * scale;
        return new RectangleF(
            padded.X + ((padded.Width - width) / 2F),
            padded.Y + ((padded.Height - height) / 2F),
            width,
            height);
    }

    private void DrawWideGuide(
        Graphics graphics,
        GuidePalette palette,
        Image image,
        Font titleFont,
        Font detailFont,
        Font chipFont,
        float hairlineWidth)
    {
        var stageBounds = new RectangleF(610F, 12F, 230F, 436F);
        using GraphicsPath stagePath = CreateRoundedRectangle(stageBounds, 24F);
        using var stageBrush = new SolidBrush(
            palette.HighContrast ? palette.Background : Color.FromArgb(5, 28, 20));
        using var stageBorder = new Pen(palette.Hairline, hairlineWidth);
        graphics.FillPath(stageBrush, stagePath);

        RectangleF imageBounds = GetContainedBounds(image, stageBounds, 8F);
        GraphicsState stageState = graphics.Save();
        try
        {
            graphics.SetClip(stagePath, CombineMode.Replace);
            graphics.DrawImage(
                image,
                imageBounds,
                new RectangleF(0F, 0F, image.Width, image.Height),
                GraphicsUnit.Pixel);
        }
        finally
        {
            graphics.Restore(stageState);
        }

        graphics.DrawPath(stageBorder, stagePath);

        PointF Anchor(float x, float y) => new(
            imageBounds.X + (imageBounds.Width * x),
            imageBounds.Y + (imageBounds.Height * y));

        bool enhanced = keyBridgePhase == KeyBridgePhase.Active;
        Color enhancedAccent = enhanced ? palette.Accent : palette.Unavailable;
        WideGuideCallout[] callouts =
        [
            new("开关键", "轻触 · Typeless 开始 / 完成", "NUMPAD ÷", Anchor(0.367F, 0.103F), 16F, palette.WarmAccent, true, false),
            new("返回键", GetEnhancedKeyDetail("单击退格 · 长按也只触发一次"), "BACKSPACE", Anchor(0.372F, 0.372F), 124F, enhancedAccent, enhanced, false),
            new("Home 键", "单击 · 删除光标后的字符", "DELETE", Anchor(0.372F, 0.467F), 232F, palette.Accent, true, false),
            new("菜单键", "轻触 · 启动 Typeless Translate", "右 SHIFT + T", Anchor(0.372F, 0.563F), 340F, palette.Accent, true, false),
            new("麦克风键", "按住说话 · 松开结束", "HOLD TO TALK", Anchor(0.633F, 0.103F), 16F, palette.WarmAccent, true, true),
            new("方向环 / 中心确认", "方向键移动 · Enter 确认", "NAV / ENTER", Anchor(0.500F, 0.255F), 124F, palette.Accent, true, true),
            new("音量 + / −", GetEnhancedKeyDetail("＋ 上一个 / − 下一个任务 · 活动视图按列表"), "切换任务", Anchor(0.633F, 0.419F), 232F, enhancedAccent, enhanced, true),
            new("TV 键", "保留原始反引号输入", "ORIGINAL INPUT", Anchor(0.633F, 0.563F), 340F, palette.WarmAccent, true, true)
        ];

        foreach (WideGuideCallout callout in callouts)
        {
            DrawWideGuideConnector(graphics, palette, callout, hairlineWidth);
        }

        foreach (WideGuideCallout callout in callouts)
        {
            DrawWideGuideCard(
                graphics,
                palette,
                callout,
                titleFont,
                detailFont,
                chipFont,
                hairlineWidth);
        }
    }

    private static void DrawWideGuideConnector(
        Graphics graphics,
        GuidePalette palette,
        WideGuideCallout callout,
        float hairlineWidth)
    {
        const float cardHeight = 92F;
        float cardEdgeX = callout.RightSide ? 950F : 500F;
        float elbowX = callout.RightSide ? 890F : 560F;
        float cardCenterY = callout.Top + (cardHeight / 2F);
        using var pen = new Pen(callout.Accent, Math.Max(1.7F, hairlineWidth))
        {
            DashStyle = callout.Available ? DashStyle.Solid : DashStyle.Dash,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLines(
            pen,
            [
                callout.Anchor,
                new PointF(elbowX, callout.Anchor.Y),
                new PointF(elbowX, cardCenterY),
                new PointF(cardEdgeX, cardCenterY)
            ]);

        using var dot = new SolidBrush(callout.Accent);
        graphics.FillEllipse(dot, callout.Anchor.X - 4.5F, callout.Anchor.Y - 4.5F, 9F, 9F);
        using var ring = new Pen(palette.CalloutLine, Math.Max(1.4F, hairlineWidth));
        graphics.DrawEllipse(ring, callout.Anchor.X - 7.5F, callout.Anchor.Y - 7.5F, 15F, 15F);
    }

    private static void DrawWideGuideCard(
        Graphics graphics,
        GuidePalette palette,
        WideGuideCallout callout,
        Font titleFont,
        Font detailFont,
        Font chipFont,
        float hairlineWidth)
    {
        var bounds = new RectangleF(callout.RightSide ? 950F : 20F, callout.Top, 480F, 92F);
        using GraphicsPath path = CreateRoundedRectangle(bounds, 18F);
        using var fillBrush = new SolidBrush(palette.Surface);
        using var borderPen = new Pen(palette.Hairline, hairlineWidth);
        graphics.FillPath(fillBrush, path);
        graphics.DrawPath(borderPen, path);

        using var accentBrush = new SolidBrush(callout.Accent);
        graphics.FillRectangle(accentBrush, bounds.X, bounds.Y + 15F, 5F, 62F);

        SizeF chipSize = graphics.MeasureString(callout.Chip, chipFont);
        float chipWidth = Math.Clamp(chipSize.Width + 22F, 78F, 164F);
        var chipBounds = new RectangleF(
            bounds.Right - chipWidth - 16F,
            bounds.Y + 14F,
            chipWidth,
            27F);
        using GraphicsPath chipPath = CreateRoundedRectangle(chipBounds, 10F);
        using var chipFill = new SolidBrush(palette.Background);
        using var chipBorder = new Pen(callout.Accent, hairlineWidth);
        graphics.FillPath(chipFill, chipPath);
        graphics.DrawPath(chipBorder, chipPath);

        using var chipBrush = new SolidBrush(callout.Accent);
        using var centered = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(callout.Chip, chipFont, chipBrush, chipBounds, centered);

        using var titleBrush = new SolidBrush(palette.Text);
        using var detailBrush = new SolidBrush(
            callout.Available ? palette.MutedText : palette.Unavailable);
        using var leftAligned = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(
            callout.Title,
            titleFont,
            titleBrush,
            new RectangleF(bounds.X + 24F, bounds.Y + 11F, 250F, 32F),
            leftAligned);
        graphics.DrawString(
            callout.Detail,
            detailFont,
            detailBrush,
            new RectangleF(bounds.X + 24F, bounds.Y + 48F, bounds.Width - 48F, 28F),
            leftAligned);
    }

    private static void DrawSignalField(
        Graphics graphics,
        GuidePalette palette,
        float hairlineWidth)
    {
        if (palette.HighContrast)
        {
            return;
        }

        var glowBounds = new RectangleF(35F, 34F, 380F, 380F);
        using GraphicsPath glowPath = new();
        glowPath.AddEllipse(glowBounds);
        using var glowBrush = new PathGradientBrush(glowPath)
        {
            CenterColor = Color.FromArgb(22, palette.Accent),
            SurroundColors = [Color.FromArgb(0, palette.Accent)],
            FocusScales = new PointF(0.2F, 0.18F)
        };
        graphics.FillPath(glowBrush, glowPath);

        using var orbitPen = new Pen(Color.FromArgb(25, palette.Accent), hairlineWidth)
        {
            DashStyle = DashStyle.Dot
        };
        graphics.DrawEllipse(orbitPen, 56F, 48F, 338F, 352F);
        graphics.DrawEllipse(orbitPen, 84F, 76F, 282F, 296F);

        using var axisPen = new Pen(Color.FromArgb(22, palette.WarmAccent), hairlineWidth)
        {
            DashPattern = [2F, 8F]
        };
        graphics.DrawLine(axisPen, 225F, 44F, 225F, 414F);

        using var nodeBrush = new SolidBrush(Color.FromArgb(72, palette.Accent));
        graphics.FillEllipse(nodeBrush, 54F, 223F, 5F, 5F);
        using var warmNodeBrush = new SolidBrush(Color.FromArgb(110, palette.WarmAccent));
        graphics.FillEllipse(warmNodeBrush, 391F, 223F, 5F, 5F);
    }

    private static void DrawSectionHeading(
        Graphics graphics,
        GuidePalette palette,
        Font headingFont,
        Font telemetryFont)
    {
        using var accentBrush = new SolidBrush(palette.Accent);
        using var textBrush = new SolidBrush(palette.MutedText);
        graphics.FillEllipse(accentBrush, 24F, 25F, 6F, 6F);
        graphics.DrawString(
            "当前实际功能",
            headingFont,
            textBrush,
            new PointF(38F, 20F),
            StringFormat.GenericTypographic);

        using var telemetryFormat = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Near
        };
        graphics.DrawString(
            "BLE  ·  ATVV 16 kHz  ·  VB-CABLE",
            telemetryFont,
            textBrush,
            new RectangleF(620F, 19F, 316F, 24F),
            telemetryFormat);
    }

    private static void DrawRemote(
        Graphics graphics,
        GuidePalette palette,
        float hairlineWidth)
    {
        var body = new RectangleF(152F, 20F, 146F, 410F);

        if (!palette.HighContrast)
        {
            using GraphicsPath shadowPath = CreateRoundedRectangle(
                new RectangleF(body.X + 5F, body.Y + 8F, body.Width, body.Height),
                30F);
            using var shadowBrush = new SolidBrush(Color.FromArgb(24, Color.Black));
            graphics.FillPath(shadowBrush, shadowPath);
        }

        using GraphicsPath bodyPath = CreateRoundedRectangle(body, 30F);
        using Brush bodyBrush = CreateBodyBrush(body, palette);
        using var bodyBorderPen = new Pen(palette.BodyBorder, hairlineWidth)
        {
            Alignment = PenAlignment.Inset
        };
        graphics.FillPath(bodyBrush, bodyPath);
        graphics.DrawPath(bodyBorderPen, bodyPath);

        RectangleF innerBody = body;
        innerBody.Inflate(-2.5F, -2.5F);
        using GraphicsPath innerHighlightPath = CreateRoundedRectangle(innerBody, 27F);
        using var innerHighlightPen = new Pen(palette.BodyHighlight, hairlineWidth);
        graphics.DrawPath(innerHighlightPen, innerHighlightPath);

        DrawTopButton(graphics, palette, new PointF(182F, 61F), 15F, hairlineWidth);
        DrawPowerGlyph(
            graphics,
            palette,
            new PointF(182F, 61F),
            hairlineWidth,
            palette.Text);

        DrawTopButton(graphics, palette, new PointF(268F, 61F), 15F, hairlineWidth);
        DrawMicrophoneGlyph(
            graphics,
            palette,
            new PointF(268F, 61F),
            hairlineWidth,
            palette.Text);

        DrawDirectionPad(graphics, palette, new PointF(225F, 135F), hairlineWidth);

        DrawRoundButton(graphics, palette, new PointF(182F, 224F), 17F, hairlineWidth);
        DrawBackGlyph(graphics, palette, new PointF(182F, 224F), hairlineWidth);

        DrawRoundButton(graphics, palette, new PointF(182F, 276F), 17F, hairlineWidth);
        DrawHomeGlyph(graphics, palette, new PointF(182F, 276F), hairlineWidth);

        DrawVolumeRocker(
            graphics,
            palette,
            new RectangleF(249F, 207F, 38F, 88F),
            hairlineWidth);

        DrawRoundButton(graphics, palette, new PointF(182F, 334F), 17F, hairlineWidth);
        DrawMenuGlyph(graphics, palette, new PointF(182F, 334F), hairlineWidth);

        DrawRoundButton(graphics, palette, new PointF(268F, 334F), 17F, hairlineWidth);
        DrawTvGlyph(graphics, palette, new PointF(268F, 334F), hairlineWidth);

        using GraphicsPath endCapPath = CreateRoundedRectangle(
            new RectangleF(160F, 417F, 130F, 8F),
            4F);
        using var endCapBrush = new SolidBrush(palette.ButtonFace);
        graphics.FillPath(endCapBrush, endCapPath);
    }

    private static void DrawRemoteCaption(
        Graphics graphics,
        GuidePalette palette,
        Font eyebrowFont,
        Font metaFont)
    {
        using var titleBrush = new SolidBrush(palette.Text);
        using var metaBrush = new SolidBrush(palette.SecondaryText);
        using var centeredFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near
        };
        graphics.DrawString(
            "BLUETOOTH VOICE REMOTE",
            eyebrowFont,
            titleBrush,
            new RectangleF(20F, 2F, 410F, 22F),
            centeredFormat);
        graphics.DrawString(
            "ATVV · 16 kHz · VB-CABLE",
            metaFont,
            metaBrush,
            new RectangleF(20F, 434F, 410F, 22F),
            centeredFormat);
    }

    private void DrawMappingPanel(
        Graphics graphics,
        GuidePalette palette,
        Font panelTitleFont,
        Font mappingTitleFont,
        Font mappingDetailFont,
        Font metaFont,
        float hairlineWidth)
    {
        var panelBounds = new RectangleF(510F, 20F, 920F, 418F);
        using GraphicsPath panelPath = CreateRoundedRectangle(panelBounds, 24F);
        using var panelBrush = new SolidBrush(palette.Surface);
        using var borderPen = new Pen(palette.Hairline, hairlineWidth);
        graphics.FillPath(panelBrush, panelPath);
        graphics.DrawPath(borderPen, panelPath);

        using var titleBrush = new SolidBrush(palette.Text);
        using var metaBrush = new SolidBrush(palette.MutedText);
        graphics.DrawString(
            "按键映射",
            panelTitleFont,
            titleBrush,
            new RectangleF(542F, 37F, 330F, 34F),
            StringFormat.GenericTypographic);
        graphics.DrawString(
            "0.3 按键配置 · 增强按键需单独启用",
            metaFont,
            metaBrush,
            new RectangleF(542F, 69F, 500F, 24F),
            StringFormat.GenericTypographic);

        DrawAvailabilitySummary(graphics, palette, metaFont, hairlineWidth);

        MappingRow[] rows =
        [
            new("开关", "Typeless 开始 / 结束", "轻触", true, true),
            new("麦克风", "遥控器麦克风采音", "按住", true, true),
            new("方向 / 确认", "方向键 · Enter", "导航", true, false),
            new("Home", "删除光标后的字符", "Delete", true, false),
            new("菜单", "Typeless Translate", "右 Shift + T", true, false),
            new("TV", "按键原样输入", "`", true, false),
            new("返回", GetEnhancedKeyDetail("单次删除光标前的字符"), "Backspace", keyBridgePhase == KeyBridgePhase.Active, false),
            new("音量", GetEnhancedKeyDetail("Codex 前台：相邻任务 · 活动视图按列表"), "切换任务", keyBridgePhase == KeyBridgePhase.Active, false)
        ];

        const float firstRowTop = 96F;
        const float rowHeight = 40F;
        for (int index = 0; index < rows.Length; index++)
        {
            DrawMappingRow(
                graphics,
                palette,
                mappingTitleFont,
                mappingDetailFont,
                metaFont,
                rows[index],
                firstRowTop + (index * rowHeight),
                rowHeight,
                index < rows.Length - 1,
                hairlineWidth);
        }
    }

    private void DrawAvailabilitySummary(
        Graphics graphics,
        GuidePalette palette,
        Font font,
        float hairlineWidth)
    {
        var bounds = new RectangleF(1258F, 41F, 138F, 34F);
        using GraphicsPath path = CreateRoundedRectangle(bounds, 17F);
        Color fillColor = palette.HighContrast
            ? SystemColors.Highlight
            : palette.WarmAccent;
        Color textColor = palette.HighContrast
            ? SystemColors.HighlightText
            : Color.White;
        using var fillBrush = new SolidBrush(fillColor);
        using var borderPen = new Pen(
            palette.HighContrast ? SystemColors.HighlightText : palette.WarmAccent,
            hairlineWidth);
        graphics.FillPath(fillBrush, path);
        graphics.DrawPath(borderPen, path);
        using var textBrush = new SolidBrush(textColor);
        using var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(keyBridgePhase == KeyBridgePhase.Active ? "增强已启用" : "增强未就绪", font, textBrush, bounds, textFormat);
    }

    private static void DrawMappingRow(
        Graphics graphics,
        GuidePalette palette,
        Font titleFont,
        Font detailFont,
        Font chipFont,
        MappingRow row,
        float top,
        float height,
        bool drawDivider,
        float hairlineWidth)
    {
        float centerY = top + (height / 2F);
        Color titleColor = row.Available ? palette.Text : palette.SecondaryText;
        Color detailColor = row.Available ? palette.MutedText : palette.SecondaryText;
        Color dotColor = row.Available
            ? row.SystemLevel ? palette.WarmAccent : palette.Accent
            : palette.SecondaryText;

        using var dotBrush = new SolidBrush(dotColor);
        graphics.FillEllipse(dotBrush, 542F, centerY - 3F, 6F, 6F);

        using var titleBrush = new SolidBrush(titleColor);
        using var detailBrush = new SolidBrush(detailColor);
        using var verticalFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(
            row.Title,
            titleFont,
            titleBrush,
            new RectangleF(564F, top, 170F, height),
            verticalFormat);
        graphics.DrawString(
            row.Detail,
            detailFont,
            detailBrush,
            new RectangleF(738F, top, 412F, height),
            verticalFormat);

        DrawMappingChip(
            graphics,
            palette,
            chipFont,
            row.Shortcut,
            row.Available,
            centerY,
            hairlineWidth);

        if (drawDivider)
        {
            using var dividerPen = new Pen(palette.Hairline, hairlineWidth);
            graphics.DrawLine(dividerPen, 542F, top + height, 1398F, top + height);
        }
    }

    private static void DrawMappingChip(
        Graphics graphics,
        GuidePalette palette,
        Font font,
        string text,
        bool available,
        float centerY,
        float hairlineWidth)
    {
        SizeF measured = graphics.MeasureString(text, font);
        float width = Math.Clamp(measured.Width + 28F, 64F, 174F);
        var bounds = new RectangleF(1398F - width, centerY - 15F, width, 30F);
        using GraphicsPath path = CreateRoundedRectangle(bounds, 9F);
        Color fillColor = palette.HighContrast ? SystemColors.Window : palette.Background;
        Color borderColor = palette.HighContrast
            ? SystemColors.WindowText
            : available ? palette.CalloutLine : palette.Hairline;
        Color textColor = available ? palette.Text : palette.SecondaryText;
        using var fillBrush = new SolidBrush(fillColor);
        using var borderPen = new Pen(borderColor, hairlineWidth);
        graphics.FillPath(fillBrush, path);
        graphics.DrawPath(borderPen, path);
        using var textBrush = new SolidBrush(textColor);
        using var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(text, font, textBrush, bounds, textFormat);
    }

    private static Brush CreateBodyBrush(RectangleF body, GuidePalette palette)
    {
        if (palette.HighContrast)
        {
            return new SolidBrush(palette.BodyCenter);
        }

        var brush = new LinearGradientBrush(
            body,
            palette.BodyEdge,
            palette.BodyEdge,
            LinearGradientMode.Horizontal)
        {
            InterpolationColors = new ColorBlend
            {
                Colors =
                [
                    palette.BodyEdge,
                    Color.FromArgb(248, 249, 248),
                    palette.BodyCenter,
                    Color.FromArgb(251, 251, 250),
                    palette.BodyEdge
                ],
                Positions = [0F, 0.16F, 0.5F, 0.84F, 1F]
            }
        };
        return brush;
    }

    private static void DrawRoundButton(
        Graphics graphics,
        GuidePalette palette,
        PointF center,
        float radius,
        float hairlineWidth)
    {
        var buttonBounds = new RectangleF(
            center.X - radius,
            center.Y - radius,
            radius * 2F,
            radius * 2F);

        if (!palette.HighContrast)
        {
            using var shadowBrush = new SolidBrush(Color.FromArgb(25, Color.Black));
            RectangleF shadowBounds = buttonBounds;
            shadowBounds.Offset(0F, 2F);
            graphics.FillEllipse(shadowBrush, shadowBounds);
        }

        using Brush buttonBrush = palette.HighContrast
            ? new SolidBrush(palette.ButtonFace)
            : new LinearGradientBrush(
                buttonBounds,
                Color.FromArgb(62, 64, 66),
                palette.ButtonFace,
                LinearGradientMode.Vertical);
        using var borderPen = new Pen(palette.ButtonBorder, hairlineWidth);
        graphics.FillEllipse(buttonBrush, buttonBounds);
        graphics.DrawEllipse(borderPen, buttonBounds);
    }

    private static void DrawTopButton(
        Graphics graphics,
        GuidePalette palette,
        PointF center,
        float radius,
        float hairlineWidth)
    {
        var buttonBounds = new RectangleF(
            center.X - radius,
            center.Y - radius,
            radius * 2F,
            radius * 2F);

        using Brush buttonBrush = palette.HighContrast
            ? new SolidBrush(palette.BodyCenter)
            : new LinearGradientBrush(
                buttonBounds,
                Color.FromArgb(250, 250, 249),
                Color.FromArgb(217, 220, 219),
                LinearGradientMode.Vertical);
        using var borderPen = new Pen(palette.BodyBorder, hairlineWidth);
        graphics.FillEllipse(buttonBrush, buttonBounds);
        graphics.DrawEllipse(borderPen, buttonBounds);
    }

    private static void DrawDirectionPad(
        Graphics graphics,
        GuidePalette palette,
        PointF center,
        float hairlineWidth)
    {
        const float outerRadius = 56F;
        const float centerRadius = 22F;
        var outerBounds = new RectangleF(
            center.X - outerRadius,
            center.Y - outerRadius,
            outerRadius * 2F,
            outerRadius * 2F);

        if (!palette.HighContrast)
        {
            using var shadowBrush = new SolidBrush(Color.FromArgb(30, Color.Black));
            RectangleF shadowBounds = outerBounds;
            shadowBounds.Offset(0F, 3F);
            graphics.FillEllipse(shadowBrush, shadowBounds);
        }

        using Brush ringBrush = palette.HighContrast
            ? new SolidBrush(palette.ButtonFace)
            : new LinearGradientBrush(
                outerBounds,
                Color.FromArgb(66, 68, 70),
                Color.FromArgb(28, 29, 31),
                LinearGradientMode.Vertical);
        using var ringBorderPen = new Pen(palette.ButtonBorder, hairlineWidth);
        graphics.FillEllipse(ringBrush, outerBounds);
        graphics.DrawEllipse(ringBorderPen, outerBounds);

        var centerBounds = new RectangleF(
            center.X - centerRadius,
            center.Y - centerRadius,
            centerRadius * 2F,
            centerRadius * 2F);
        using var centerBrush = new SolidBrush(palette.CenterButtonFace);
        using var centerBorderPen = new Pen(palette.CenterButtonBorder, hairlineWidth);
        graphics.FillEllipse(centerBrush, centerBounds);
        graphics.DrawEllipse(centerBorderPen, centerBounds);

        using var glyphPen = new Pen(palette.ButtonGlyph, Math.Max(1.7F, hairlineWidth))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        DrawChevron(graphics, glyphPen, new PointF(center.X, center.Y - 39F), Direction.Up);
        DrawChevron(graphics, glyphPen, new PointF(center.X, center.Y + 39F), Direction.Down);
        DrawChevron(graphics, glyphPen, new PointF(center.X - 39F, center.Y), Direction.Left);
        DrawChevron(graphics, glyphPen, new PointF(center.X + 39F, center.Y), Direction.Right);
    }

    private static void DrawChevron(
        Graphics graphics,
        Pen pen,
        PointF center,
        Direction direction)
    {
        PointF[] points = direction switch
        {
            Direction.Up =>
                [new(center.X - 5F, center.Y + 3F), new(center.X, center.Y - 3F), new(center.X + 5F, center.Y + 3F)],
            Direction.Down =>
                [new(center.X - 5F, center.Y - 3F), new(center.X, center.Y + 3F), new(center.X + 5F, center.Y - 3F)],
            Direction.Left =>
                [new(center.X + 3F, center.Y - 5F), new(center.X - 3F, center.Y), new(center.X + 3F, center.Y + 5F)],
            _ =>
                [new(center.X - 3F, center.Y - 5F), new(center.X + 3F, center.Y), new(center.X - 3F, center.Y + 5F)]
        };
        graphics.DrawLines(pen, points);
    }

    private static void DrawVolumeRocker(
        Graphics graphics,
        GuidePalette palette,
        RectangleF bounds,
        float hairlineWidth)
    {
        if (!palette.HighContrast)
        {
            RectangleF shadowBounds = bounds;
            shadowBounds.Offset(0F, 2F);
            using GraphicsPath shadowPath = CreateRoundedRectangle(shadowBounds, bounds.Width / 2F);
            using var shadowBrush = new SolidBrush(Color.FromArgb(25, Color.Black));
            graphics.FillPath(shadowBrush, shadowPath);
        }

        using GraphicsPath rockerPath = CreateRoundedRectangle(bounds, bounds.Width / 2F);
        using Brush rockerBrush = palette.HighContrast
            ? new SolidBrush(palette.ButtonFace)
            : new LinearGradientBrush(
                bounds,
                Color.FromArgb(63, 65, 67),
                palette.ButtonFace,
                LinearGradientMode.Vertical);
        using var borderPen = new Pen(palette.ButtonBorder, hairlineWidth);
        using var dividerPen = new Pen(palette.ButtonDivider, hairlineWidth);
        using var glyphPen = new Pen(palette.ButtonGlyph, Math.Max(1.7F, hairlineWidth))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        graphics.FillPath(rockerBrush, rockerPath);
        graphics.DrawPath(borderPen, rockerPath);
        graphics.DrawLine(
            dividerPen,
            bounds.Left + 5F,
            bounds.Top + (bounds.Height / 2F),
            bounds.Right - 5F,
            bounds.Top + (bounds.Height / 2F));

        float centerX = bounds.Left + (bounds.Width / 2F);
        float plusY = bounds.Top + (bounds.Height * 0.27F);
        float minusY = bounds.Bottom - (bounds.Height * 0.27F);
        graphics.DrawLine(glyphPen, centerX - 5F, plusY, centerX + 5F, plusY);
        graphics.DrawLine(glyphPen, centerX, plusY - 5F, centerX, plusY + 5F);
        graphics.DrawLine(glyphPen, centerX - 5F, minusY, centerX + 5F, minusY);
    }

    private static void DrawPowerGlyph(
        Graphics graphics,
        GuidePalette palette,
        PointF center,
        float hairlineWidth,
        Color? glyphColor = null)
    {
        using var pen = CreateGlyphPen(
            glyphColor ?? palette.ButtonGlyph,
            hairlineWidth);
        graphics.DrawArc(pen, center.X - 7F, center.Y - 7F, 14F, 14F, -48F, 276F);
        graphics.DrawLine(pen, center.X, center.Y - 9F, center.X, center.Y - 1F);
    }

    private static void DrawMicrophoneGlyph(
        Graphics graphics,
        GuidePalette palette,
        PointF center,
        float hairlineWidth,
        Color? glyphColor = null)
    {
        using var pen = CreateGlyphPen(
            glyphColor ?? palette.ButtonGlyph,
            hairlineWidth);
        using GraphicsPath capsulePath = CreateRoundedRectangle(
            new RectangleF(center.X - 4F, center.Y - 8F, 8F, 13F),
            4F);
        graphics.DrawPath(pen, capsulePath);
        graphics.DrawArc(pen, center.X - 7F, center.Y - 2F, 14F, 11F, 0F, 180F);
        graphics.DrawLine(pen, center.X, center.Y + 8F, center.X, center.Y + 11F);
        graphics.DrawLine(pen, center.X - 4F, center.Y + 11F, center.X + 4F, center.Y + 11F);
    }

    private static void DrawBackGlyph(
        Graphics graphics,
        GuidePalette palette,
        PointF center,
        float hairlineWidth)
    {
        using var pen = CreateGlyphPen(palette, hairlineWidth);
        graphics.DrawArc(pen, center.X - 5F, center.Y - 6F, 13F, 13F, 198F, 230F);
        graphics.DrawLines(
            pen,
            [
                new PointF(center.X - 2F, center.Y - 7F),
                new PointF(center.X - 7F, center.Y - 3F),
                new PointF(center.X - 2F, center.Y + 1F)
            ]);
    }

    private static void DrawHomeGlyph(
        Graphics graphics,
        GuidePalette palette,
        PointF center,
        float hairlineWidth)
    {
        using var pen = CreateGlyphPen(palette, hairlineWidth);
        graphics.DrawLines(
            pen,
            [
                new PointF(center.X - 8F, center.Y - 1F),
                new PointF(center.X, center.Y - 8F),
                new PointF(center.X + 8F, center.Y - 1F)
            ]);
        graphics.DrawLines(
            pen,
            [
                new PointF(center.X - 6F, center.Y - 2F),
                new PointF(center.X - 6F, center.Y + 7F),
                new PointF(center.X + 6F, center.Y + 7F),
                new PointF(center.X + 6F, center.Y - 2F)
            ]);
    }

    private static void DrawMenuGlyph(
        Graphics graphics,
        GuidePalette palette,
        PointF center,
        float hairlineWidth)
    {
        using var pen = CreateGlyphPen(palette, hairlineWidth);
        for (int offset = -6; offset <= 6; offset += 6)
        {
            graphics.DrawLine(
                pen,
                center.X - 7F,
                center.Y + offset,
                center.X + 7F,
                center.Y + offset);
        }
    }

    private static void DrawTvGlyph(
        Graphics graphics,
        GuidePalette palette,
        PointF center,
        float hairlineWidth)
    {
        using var pen = CreateGlyphPen(palette, hairlineWidth);
        using GraphicsPath screenPath = CreateRoundedRectangle(
            new RectangleF(center.X - 9F, center.Y - 7F, 18F, 13F),
            2F);
        graphics.DrawPath(pen, screenPath);
        graphics.DrawLine(pen, center.X, center.Y + 6F, center.X, center.Y + 9F);
        graphics.DrawLine(pen, center.X - 5F, center.Y + 9F, center.X + 5F, center.Y + 9F);
    }

    private static void DrawNfcGlyph(
        Graphics graphics,
        GuidePalette palette,
        PointF center,
        float hairlineWidth)
    {
        using var pen = new Pen(palette.Branding, Math.Max(1.3F, hairlineWidth))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLines(
            pen,
            [
                new PointF(center.X - 10F, center.Y + 4F),
                new PointF(center.X - 3F, center.Y - 5F),
                new PointF(center.X + 3F, center.Y + 5F),
                new PointF(center.X + 10F, center.Y - 4F)
            ]);
        graphics.DrawLines(
            pen,
            [
                new PointF(center.X - 6F, center.Y + 7F),
                new PointF(center.X + 1F, center.Y - 2F),
                new PointF(center.X + 6F, center.Y + 6F)
            ]);
    }

    private static Pen CreateGlyphPen(GuidePalette palette, float hairlineWidth)
    {
        return CreateGlyphPen(palette.ButtonGlyph, hairlineWidth);
    }

    private static Pen CreateGlyphPen(Color color, float hairlineWidth)
    {
        return new Pen(color, Math.Max(1.7F, hairlineWidth))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
    }

    private static void DrawCallout(
        Graphics graphics,
        GuidePalette palette,
        Font titleFont,
        Font detailFont,
        PointF anchor,
        float calloutY,
        CalloutSide side,
        string title,
        string detail,
        bool available,
        float hairlineWidth)
    {
        Color lineColor = available ? palette.CalloutLine : palette.Unavailable;
        Color detailColor = available ? palette.MutedText : palette.Unavailable;
        float textX = side == CalloutSide.Left ? 24F : 658F;
        float textWidth = side == CalloutSide.Left ? 300F : 278F;
        float lineEndX = side == CalloutSide.Left ? 338F : 642F;
        float elbowX = side == CalloutSide.Left ? 354F : 608F;

        using var linePen = new Pen(lineColor, hairlineWidth)
        {
            DashStyle = available ? DashStyle.Solid : DashStyle.Dash,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round
        };
        PointF[] linePoints =
            [anchor, new PointF(elbowX, calloutY), new PointF(lineEndX, calloutY)];
        graphics.DrawLines(linePen, linePoints);

        using var anchorBrush = new SolidBrush(lineColor);
        graphics.FillEllipse(anchorBrush, anchor.X - 2.6F, anchor.Y - 2.6F, 5.2F, 5.2F);

        using var titleBrush = new SolidBrush(palette.Text);
        using var detailBrush = new SolidBrush(detailColor);
        using var textFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(
            title,
            titleFont,
            titleBrush,
            new RectangleF(textX, calloutY - 31F, textWidth, 22F),
            textFormat);
        graphics.DrawString(
            detail,
            detailFont,
            detailBrush,
            new RectangleF(textX, calloutY - 7F, textWidth, 22F),
            textFormat);
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        float diameter = Math.Min(radius * 2F, Math.Min(bounds.Width, bounds.Height));
        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
        var path = new GraphicsPath();
        path.AddArc(arc, 180F, 90F);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270F, 90F);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0F, 90F);
        arc.X = bounds.Left;
        path.AddArc(arc, 90F, 90F);
        path.CloseFigure();
        return path;
    }

    private static GuidePalette CreatePalette(bool darkMode)
    {
        if (SystemInformation.HighContrast)
        {
            return new GuidePalette(
                HighContrast: true,
                Background: SystemColors.Window,
                Surface: SystemColors.Window,
                Text: SystemColors.WindowText,
                MutedText: SystemColors.WindowText,
                SecondaryText: SystemColors.WindowText,
                Accent: SystemColors.Highlight,
                WarmAccent: SystemColors.Highlight,
                Unavailable: SystemColors.WindowText,
                CalloutLine: SystemColors.WindowText,
                Hairline: SystemColors.WindowText,
                BodyEdge: SystemColors.Window,
                BodyCenter: SystemColors.Window,
                BodyBorder: SystemColors.WindowText,
                BodyHighlight: SystemColors.WindowText,
                Sensor: SystemColors.WindowText,
                ButtonFace: SystemColors.WindowText,
                ButtonBorder: SystemColors.WindowText,
                ButtonDivider: SystemColors.Window,
                ButtonGlyph: SystemColors.Window,
                CenterButtonFace: SystemColors.WindowText,
                CenterButtonBorder: SystemColors.Window,
                Branding: SystemColors.WindowText);
        }

        if (darkMode)
        {
            return new GuidePalette(
                HighContrast: false,
                Background: Color.FromArgb(5, 24, 16),
                Surface: Color.FromArgb(12, 48, 35),
                Text: Color.FromArgb(244, 244, 245),
                MutedText: Color.FromArgb(199, 216, 207),
                SecondaryText: Color.FromArgb(143, 166, 156),
                Accent: Color.FromArgb(51, 199, 155),
                WarmAccent: Color.FromArgb(245, 130, 32),
                Unavailable: Color.FromArgb(112, 136, 124),
                CalloutLine: Color.FromArgb(42, 89, 72),
                Hairline: Color.FromArgb(42, 89, 72),
                BodyEdge: Color.FromArgb(197, 200, 202),
                BodyCenter: Color.FromArgb(231, 233, 233),
                BodyBorder: Color.FromArgb(167, 171, 174),
                BodyHighlight: Color.FromArgb(245, 246, 245),
                Sensor: Color.FromArgb(126, 130, 132),
                ButtonFace: Color.FromArgb(31, 32, 34),
                ButtonBorder: Color.FromArgb(17, 18, 19),
                ButtonDivider: Color.FromArgb(88, 90, 92),
                ButtonGlyph: Color.FromArgb(238, 239, 239),
                CenterButtonFace: Color.FromArgb(45, 47, 49),
                CenterButtonBorder: Color.FromArgb(83, 85, 87),
                Branding: Color.FromArgb(142, 146, 148));
        }

        return new GuidePalette(
            HighContrast: false,
            Background: Color.FromArgb(244, 244, 245),
            Surface: Color.White,
            Text: Color.FromArgb(6, 39, 26),
            MutedText: Color.FromArgb(70, 91, 81),
            SecondaryText: Color.FromArgb(133, 145, 139),
            Accent: Color.FromArgb(0, 111, 83),
            WarmAccent: Color.FromArgb(245, 130, 32),
            Unavailable: Color.FromArgb(151, 160, 155),
            CalloutLine: Color.FromArgb(171, 197, 187),
            Hairline: Color.FromArgb(220, 226, 222),
            BodyEdge: Color.FromArgb(197, 200, 202),
            BodyCenter: Color.FromArgb(231, 233, 233),
            BodyBorder: Color.FromArgb(167, 171, 174),
            BodyHighlight: Color.FromArgb(245, 246, 245),
            Sensor: Color.FromArgb(126, 130, 132),
            ButtonFace: Color.FromArgb(31, 32, 34),
            ButtonBorder: Color.FromArgb(17, 18, 19),
            ButtonDivider: Color.FromArgb(88, 90, 92),
            ButtonGlyph: Color.FromArgb(238, 239, 239),
            CenterButtonFace: Color.FromArgb(45, 47, 49),
            CenterButtonBorder: Color.FromArgb(83, 85, 87),
            Branding: Color.FromArgb(142, 146, 148));
    }

    private enum CalloutSide
    {
        Left,
        Right
    }

    private enum Direction
    {
        Up,
        Down,
        Left,
        Right
    }

    private readonly record struct MappingRow(
        string Title,
        string Detail,
        string Shortcut,
        bool Available,
        bool SystemLevel);

    private readonly record struct WideGuideCallout(
        string Title,
        string Detail,
        string Chip,
        PointF Anchor,
        float Top,
        Color Accent,
        bool Available,
        bool RightSide);

    private readonly record struct GuidePalette(
        bool HighContrast,
        Color Background,
        Color Surface,
        Color Text,
        Color MutedText,
        Color SecondaryText,
        Color Accent,
        Color WarmAccent,
        Color Unavailable,
        Color CalloutLine,
        Color Hairline,
        Color BodyEdge,
        Color BodyCenter,
        Color BodyBorder,
        Color BodyHighlight,
        Color Sensor,
        Color ButtonFace,
        Color ButtonBorder,
        Color ButtonDivider,
        Color ButtonGlyph,
        Color CenterButtonFace,
        Color CenterButtonBorder,
        Color Branding);
}
