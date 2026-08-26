using CatClawMusic.Core.Models;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// 统一搜索单条结果预览页（对齐 design/result-preview-prototype.html）：
/// <list type="bullet">
///   <item>窄屏（&lt;720）：紧凑横排头部 + Tab 行 + 模式 chips 第二行（可横滑）+ 内容 + 底部写入选项栏；</item>
///   <item>宽屏（≥720，PC/横屏）：左侧信息栏（大封面/标题/徽标/候选统计）+ 右侧 Tab 与 chips 同行 + 内容区，
///     写入选项收进顶栏，「写入」固定右上角始终可见。</item>
/// </list>
/// 共享元素（信息块 / Tab 条 / 内容区）在两种形态容器间重挂；选项胶囊与模式 chips 为双实例，经 VM 状态同步。
/// 根布局 ClassId="plugin-nav-wrap" 抑制 PluginNav 注入的返回头（本页自带顶栏返回键），避免双头部。
/// </summary>
public sealed class UnifiedSearchPreviewPage : ContentPage
{
    private readonly UnifiedSearchViewModel _vm;
    private int _activeTab;
    private bool? _isWide;
    private double _appliedWidth;
    private double _appliedHeight;

    // ── 根结构 ──
    private readonly Grid _root;
    private readonly Grid _topbar;
    private readonly Grid _narrowBody;
    private readonly Grid _wideBody;
    private readonly Grid _wideMain;

    // ── 共享元素（形态切换时在窄/宽容器间重挂） ──
    private readonly Grid _infoBlock;
    private readonly Grid _tabsGrid;
    private readonly ContentView _contentHost;

    // ── Tab 区宿主（常驻窄/宽容器，tabsGrid 在其间移动） ──
    private readonly Grid _tabsNarrowHost;
    private readonly Grid _tabsWideHost;

    // ── 顶栏 ──
    private readonly Grid _topOptsSlot;
    private readonly Button _writeBtn;

    // ── 信息块 ──
    private readonly Grid _coverBox;
    private readonly Image _coverImg;
    private readonly Border _coverPlaceholder;
    private readonly Label _coverLetter;
    private readonly VerticalStackLayout _textStack;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly HorizontalStackLayout _badges;
    private readonly Label _statusLabel;
    private readonly Border _statsCard;
    private readonly Label _statLyrics;
    private readonly Label _statCover;
    private readonly Label _statCurrent;

    // ── Tab ──
    private readonly Label[] _tabLabels = new Label[3];
    private readonly BoxView _tabIndicator;

    // ── 模式 chips（窄/宽双实例） ──
    private readonly Button[] _chipsNarrow = new Button[4];
    private readonly Button[] _chipsWide = new Button[4];
    private readonly ScrollView _chipsNarrowScroller;
    private readonly HorizontalStackLayout _chipsWideStack;

    // ── 写入选项胶囊（顶栏/底部双实例） ──
    private readonly OptPill[] _pillsTop = new OptPill[3];
    private readonly OptPill[] _pillsBottom = new OptPill[3];
    private (string Label, Func<bool> Get, Action<bool> Set)[] _opts = Array.Empty<(string, Func<bool>, Action<bool>)>();

    private static readonly string[] TabNames = { "歌词", "封面", "元数据" };
    private static readonly LyricMode[] Modes = { LyricMode.Plain, LyricMode.Verbatim, LyricMode.Enhanced, LyricMode.TTML };

    public UnifiedSearchPreviewPage(UnifiedSearchViewModel vm)
    {
        _vm = vm;
        BindingContext = _vm;

        Title = "结果预览";
        BackgroundColor = ResColor("WindowBackgroundColor", "#1A1838");

        _opts = new (string, Func<bool>, Action<bool>)[]
        {
            ("元数据", () => _vm.ApplyMetadata, v => _vm.ApplyMetadata = v),
            ("歌词", () => _vm.ApplyLyrics, v => _vm.ApplyLyrics = v),
            ("封面", () => _vm.ApplyCover, v => _vm.ApplyCover = v),
        };

        // ═══ 信息块（共享） ═══
        _coverImg = new Image { Aspect = Aspect.AspectFill };
        _coverImg.SetBinding(Image.SourceProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HighResCoverUrl)}")
            { Converter = new CoverUriConverter() });
        _coverImg.SetBinding(VisualElement.IsVisibleProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasCover)}"));

        _coverLetter = new Label
        {
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        _coverLetter.SetDynamicResource(Label.TextColorProperty, "PrimaryColor");
        _coverLetter.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.CoverText)}"));

        _coverPlaceholder = new Border
        {
            StrokeThickness = 0,
            Background = Brush("CardBackgroundStrongColor", "#2DFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            Content = _coverLetter,
        };
        _coverPlaceholder.SetBinding(VisualElement.IsVisibleProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasCover)}")
            { Converter = new InvertBoolConverter() });

        _coverBox = new Grid { Children = { _coverPlaceholder, _coverImg } };

        _titleLabel = new Label
        {
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
        };
        _titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        _titleLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.DisplayTitle)}"));

        _subtitleLabel = new Label { FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _subtitleLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        _subtitleLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.Subtitle)}"));

        _badges = new HorizontalStackLayout { Spacing = 6 };
        _badges.Children.Add(MakeBadge(
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.LyricsType)}"),
            "#268C7BFF", "PrimaryColor",
            visible: $"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasLyrics)}"));
        _badges.Children.Add(MakeBadge(null, "#264ADE80", "#4ADE80", text: "封面",
            visible: $"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasCover)}"));
        _badges.Children.Add(MakeBadge(
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.Source)}"),
            "#2DFFFFFF", "TextSecondaryColor"));

        _statusLabel = new Label { FontSize = 10.5, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _statusLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        _statusLabel.SetBinding(Label.TextProperty, nameof(UnifiedSearchViewModel.StatusText));

        _textStack = new VerticalStackLayout { Spacing = 4, Children = { _titleLabel, _subtitleLabel, _badges, _statusLabel } };

        _statLyrics = MakeStatLabel("歌词结果");
        _statCover = MakeStatLabel("封面结果");
        _statCurrent = MakeStatLabel("当前选中");
        var statTitle = MakeStatLabel("本次搜索候选");
        statTitle.FontSize = 11;
        _statsCard = new Border
        {
            StrokeThickness = 0,
            Background = Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) },
            Padding = new Thickness(15, 12),
            Content = new VerticalStackLayout
            {
                Spacing = 7,
                Children = { statTitle, _statLyrics, _statCover, _statCurrent },
            },
        };

        _infoBlock = new Grid
        {
            RowSpacing = 12,
            ColumnSpacing = 14,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
        };

        // ═══ Tab 条（共享：3 等分 + 移动指示条） ═══
        _tabIndicator = new BoxView
        {
            HeightRequest = 3,
            CornerRadius = 1.5,
            WidthRequest = 24,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
        };
        _tabIndicator.SetDynamicResource(BoxView.ColorProperty, "PrimaryColor");

        _tabsGrid = new Grid
        {
            HeightRequest = 42,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };
        for (int i = 0; i < 3; i++)
        {
            var idx = i;
            var label = new Label
            {
                Text = TabNames[i],
                FontSize = 14,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            };
            label.SetDynamicResource(Label.TextColorProperty, idx == 0 ? "TextPrimaryColor" : "TextSecondaryColor");
            if (idx == 0) label.FontAttributes = FontAttributes.Bold;
            _tabLabels[i] = label;

            var cell = new Grid();
            cell.Children.Add(label);
            cell.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SwitchTab(idx)) });
            WideAdapt.AttachHover(cell);
            _tabsGrid.Add(cell, i, 0);
        }
        _tabsGrid.Add(_tabIndicator, 0, 0);

        _contentHost = new ContentView();

        // ═══ 模式 chips 双实例 ═══
        _chipsNarrowScroller = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Padding = new Thickness(12, 0, 12, 6),
            Content = BuildChips(_chipsNarrow, 26, 11.5),
        };
        _chipsWideStack = BuildChips(_chipsWide, 27, 12);

        // Tab 区宿主：窄 = tabs / chips第二行 / 分隔线；宽 = tabs + chips 同行 / 分隔线
        _tabsNarrowHost = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };
        _tabsNarrowHost.Add(_chipsNarrowScroller, 0, 1);
        _tabsNarrowHost.Add(MakeSeparator(), 0, 2);

        _tabsWideHost = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        _chipsWideStack.VerticalOptions = LayoutOptions.Center;
        _chipsWideStack.Margin = new Thickness(0, 0, 20, 0);
        _tabsWideHost.Add(_chipsWideStack, 1, 0);
        var wideSep = MakeSeparator();
        _tabsWideHost.Add(wideSep, 0, 1);
        Grid.SetColumnSpan(wideSep, 2);

        // ═══ 底部操作栏（窄屏）：分隔线 + 选项胶囊居中 ═══
        var pillsBottomStack = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center };
        for (int i = 0; i < 3; i++)
        {
            var pill = BuildPill(i);
            _pillsBottom[i] = pill;
            pillsBottomStack.Children.Add(pill.Root);
        }
        var bottomBar = new Grid
        {
            Padding = new Thickness(14, 8, 14, 10),
            BackgroundColor = ResColor("WindowBackgroundColor", "#1A1838").MultiplyAlpha(0.88f),
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
        };
        bottomBar.Add(MakeSeparator(), 0, 0);
        bottomBar.Add(pillsBottomStack, 0, 1);

        // ═══ 顶栏：返回 + 标题 |（宽屏）选项胶囊 + 写入 ═══
        var back = new Border
        {
            WidthRequest = 38,
            HeightRequest = 32,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Background = Brush("CardBackgroundColor", "#1AFFFFFF"),
            Content = new Label
            {
                Text = "‹",
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions = LayoutOptions.Center,
            },
        };
        ((Label)back.Content).SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");
        back.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => { try { await PluginNav.PopAsync(); } catch { } }),
        });

        var topTitle = new Label
        {
            Text = "结果预览",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(6, 0, 0, 0),
        };
        topTitle.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");

        var pillsTopStack = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(0, 0, 10, 0) };
        for (int i = 0; i < 3; i++)
        {
            var pill = BuildPill(i);
            _pillsTop[i] = pill;
            pillsTopStack.Children.Add(pill.Root);
        }
        _topOptsSlot = new Grid { Children = { pillsTopStack }, HorizontalOptions = LayoutOptions.End };

        var primary = ResColor("PrimaryColor", "#8C7BFF");
        _writeBtn = new Button
        {
            Text = "写入",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            CornerRadius = 16,
            HeightRequest = 32,
            Padding = new Thickness(18, 0),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(primary, 0),
                    new GradientStop(Lighten(primary, 0.15f), 1),
                },
            },
        };
        _writeBtn.SetBinding(Button.CommandProperty, nameof(UnifiedSearchViewModel.ApplyCommand));

        _topbar = new Grid
        {
            HeightRequest = 46,
            Padding = new Thickness(12, 0, 16, 0),
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
        };
        _topbar.Add(back, 0, 0);
        _topbar.Add(topTitle, 1, 0);
        _topbar.Add(_topOptsSlot, 3, 0);
        _topbar.Add(_writeBtn, 4, 0);
        var topSep = MakeSeparator();
        _topbar.Add(topSep, 0, 1);
        Grid.SetColumnSpan(topSep, 5);

        // ═══ 主体容器：窄（纵向行）/ 宽（侧栏 + 主区） ═══
        _narrowBody = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),   // 信息块
                new RowDefinition(GridLength.Auto),   // Tab 区
                new RowDefinition(GridLength.Star),   // 内容
                new RowDefinition(GridLength.Auto),   // 底部操作栏
            },
        };
        _narrowBody.Add(_tabsNarrowHost, 0, 1);
        _narrowBody.Add(bottomBar, 0, 3);

        _wideMain = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),   // Tab 区
                new RowDefinition(GridLength.Star),   // 内容
            },
        };
        _wideMain.Add(_tabsWideHost, 0, 0);

        _wideBody = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(new GridLength(280)), new ColumnDefinition(GridLength.Star) },
        };
        _wideBody.Add(_wideMain, 1, 0);

        _root = new Grid
        {
            ClassId = "plugin-nav-wrap",   // 抑制 PluginNav 注入返回头：本页自带顶栏
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
        };
        _root.Add(_topbar, 0, 0);

        Content = _root;
        SizeChanged += (_, _) => ApplyLayout();

        _vm.Applied += async (_, _) =>
        {
            try { await PluginNav.PopAsync(); } catch { }
        };
        _vm.PropertyChanged += OnVmPropertyChanged;

        SwitchTab(0);
        RefreshPills();
        RefreshChips();
        UpdateWriteState();
        RefreshStats();
        ApplyNarrow();   // 初始挂窄屏形态；首个 SizeChanged 视宽度切宽屏
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshStats();
        ApplyLayout();
    }

    // ═══════════ 响应式形态切换 ═══════════

    private void ApplyLayout()
    {
        var w = Width;
        if (w <= 0) return;
        var h = Height;
        var wide = ThemeHelper.IsWide(w);
        var compact = wide && h > 0 && h < 520;
        if (_isWide == wide && Math.Abs(w - _appliedWidth) < 1 && Math.Abs(h - _appliedHeight) < 1) return;
        _isWide = wide;
        _appliedWidth = w;
        _appliedHeight = h;

        if (wide) ApplyWide(w, compact);
        else ApplyNarrow();

        // 元数据卡片网格列数随形态变化（1↔2 列），激活中则重建
        if (_activeTab == 2) ShowTab(2);
    }

    private void ApplyNarrow()
    {
        _topbar.HeightRequest = 46;
        _topOptsSlot.IsVisible = false;

        SetBody(_narrowBody);
        MoveTo(_infoBlock, _narrowBody, 0, 0);
        MoveTo(_tabsGrid, _tabsNarrowHost, 0, 0);
        MoveTo(_contentHost, _narrowBody, 0, 2);

        // 信息块：紧凑横排（小封面左、文字右）
        _infoBlock.Padding = new Thickness(16, 12);
        _infoBlock.RowSpacing = 0;
        _infoBlock.ColumnSpacing = 14;
        _statsCard.IsVisible = false;
        _coverBox.WidthRequest = 64;
        _coverBox.HeightRequest = 64;
        _coverBox.HorizontalOptions = LayoutOptions.Start;
        _coverBox.VerticalOptions = LayoutOptions.Center;
        Grid.SetRow(_coverBox, 0);
        Grid.SetColumn(_coverBox, 0);
        Grid.SetColumnSpan(_coverBox, 1);
        _coverPlaceholder.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) };
        _coverLetter.FontSize = 26;
        _textStack.Spacing = 4;
        _textStack.HorizontalOptions = LayoutOptions.Fill;
        _textStack.VerticalOptions = LayoutOptions.Center;
        Grid.SetRow(_textStack, 0);
        Grid.SetColumn(_textStack, 1);
        Grid.SetColumnSpan(_textStack, 1);
        _titleLabel.FontSize = 16;
        _titleLabel.MaxLines = 1;
        _titleLabel.LineBreakMode = LineBreakMode.TailTruncation;
        _subtitleLabel.MaxLines = 1;
        _subtitleLabel.LineBreakMode = LineBreakMode.TailTruncation;
        _statusLabel.FontSize = 10.5;
        _statusLabel.MaxLines = 1;
        _statusLabel.LineBreakMode = LineBreakMode.TailTruncation;

        _tabsGrid.HeightRequest = 42;
        _chipsNarrowScroller.IsVisible = _activeTab == 0;
    }

    private void ApplyWide(double w, bool compact)
    {
        _topbar.HeightRequest = 50;
        _topOptsSlot.IsVisible = true;

        var sidebar = w >= 1024 ? 280 : 240;
        _wideBody.ColumnDefinitions[0].Width = new GridLength(sidebar);

        SetBody(_wideBody);
        MoveTo(_infoBlock, _wideBody, 0, 0);
        MoveTo(_tabsGrid, _tabsWideHost, 0, 0);
        MoveTo(_contentHost, _wideMain, 0, 1);

        // 信息块：纵向侧栏（大封面上、文字与统计在下）
        var coverSize = compact ? 160 : sidebar - 40;
        _infoBlock.Padding = new Thickness(20, compact ? 14 : 22, 20, 16);
        _infoBlock.RowSpacing = 12;
        _infoBlock.ColumnSpacing = 0;
        _statsCard.IsVisible = !compact;
        _coverBox.WidthRequest = coverSize;
        _coverBox.HeightRequest = coverSize;
        _coverBox.HorizontalOptions = LayoutOptions.Start;
        _coverBox.VerticalOptions = LayoutOptions.Start;
        Grid.SetRow(_coverBox, 0);
        Grid.SetColumn(_coverBox, 0);
        Grid.SetColumnSpan(_coverBox, 2);
        _coverPlaceholder.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) };
        _coverLetter.FontSize = compact ? 40 : 58;
        _textStack.Spacing = 8;
        _textStack.HorizontalOptions = LayoutOptions.Fill;
        _textStack.VerticalOptions = LayoutOptions.Start;
        Grid.SetRow(_textStack, 1);
        Grid.SetColumn(_textStack, 0);
        Grid.SetColumnSpan(_textStack, 2);
        Grid.SetRow(_statsCard, 2);
        Grid.SetColumn(_statsCard, 0);
        Grid.SetColumnSpan(_statsCard, 2);
        _titleLabel.FontSize = compact ? 16 : 19;
        _titleLabel.MaxLines = 2;
        _titleLabel.LineBreakMode = LineBreakMode.WordWrap;
        _subtitleLabel.MaxLines = 2;
        _subtitleLabel.LineBreakMode = LineBreakMode.WordWrap;
        _statusLabel.FontSize = 11;
        _statusLabel.MaxLines = 3;
        _statusLabel.LineBreakMode = LineBreakMode.WordWrap;

        _tabsGrid.HeightRequest = 44;
        _chipsWideStack.IsVisible = _activeTab == 0;
    }

    private void SetBody(Layout body)
    {
        var current = _root.Children.Count > 1 ? _root.Children[1] : null;
        if (ReferenceEquals(current, body)) return;
        if (_root.Children.Contains(_narrowBody)) _root.Children.Remove(_narrowBody);
        if (_root.Children.Contains(_wideBody)) _root.Children.Remove(_wideBody);
        _root.Add(body, 0, 1);
    }

    /// <summary>把共享元素移动到目标容器指定格位（跨形态重挂，保留绑定与状态）。</summary>
    private static void MoveTo(View view, Layout target, int column, int row)
    {
        if (view.Parent is Layout p && !ReferenceEquals(p, target)) p.Children.Remove(view);
        Grid.SetColumn(view, column);
        Grid.SetRow(view, row);
        if (!ReferenceEquals(view.Parent, target)) target.Children.Add(view);
    }

    // ═══════════ Tab 切换 ═══════════

    private void SwitchTab(int idx)
    {
        _activeTab = idx;
        for (int i = 0; i < _tabLabels.Length; i++)
        {
            _tabLabels[i].FontAttributes = i == idx ? FontAttributes.Bold : FontAttributes.None;
            _tabLabels[i].SetDynamicResource(Label.TextColorProperty,
                i == idx ? "TextPrimaryColor" : "TextSecondaryColor");
        }
        Grid.SetColumn(_tabIndicator, idx);
        _chipsNarrowScroller.IsVisible = idx == 0 && _isWide != true;
        _chipsWideStack.IsVisible = idx == 0 && _isWide == true;
        ShowTab(idx);
    }

    private void ShowTab(int idx)
        => _contentHost.Content = idx switch
        {
            0 => BuildLyricsPanel(),
            1 => BuildCoverPanel(),
            _ => BuildMetadataPanel(),
        };

    // ═══════════ Tab 0：歌词（结构化预览） ═══════════

    private View BuildLyricsPanel()
    {
        var lyrics = _vm.Selected?.StructuredLyrics;
        var mode = _vm.SelectedMode;

        // 无结构化数据：回退原始文本；彻底无歌词给空态
        if (lyrics == null || lyrics.Lines.Count == 0)
        {
            var hasRaw = _vm.Selected?.LyricsTrack != null &&
                (!string.IsNullOrWhiteSpace(_vm.Selected.LyricsTrack.SyncedLyrics) ||
                 !string.IsNullOrWhiteSpace(_vm.Selected.LyricsTrack.PlainLyrics));
            if (!hasRaw)
            {
                var empty = new Label
                {
                    Text = "该结果无歌词",
                    FontSize = 14,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                };
                empty.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
                return MakePanel(empty, "可返回候选列表选择其它来源");
            }
            return MakePanel(MakeCodeLabel(_vm.SelectedLyricsPreview),
                $"共 {_vm.SelectedLyricsPreview.Split('\n').Length} 行 · 原始文本预览");
        }

        if (mode == LyricMode.TTML)
        {
            var (rt, tt) = LyricModeEncoder.CountSubLines(lyrics);
            var extra = SubLineSuffix(rt, tt);
            return MakePanel(MakeCodeLabel(_vm.SelectedLyricsPreview),
                $"TTML 源码预览 · 共 {lyrics.Lines.Count} 行{extra}");
        }

        // 原行 → 对齐的 (罗马音, 翻译)
        var sub = LyricModeEncoder.AlignSubLines(lyrics);

        var stack = new VerticalStackLayout { Spacing = 0, Margin = new Thickness(8, 10, 8, 8) };
        foreach (var line in lyrics.Lines)
        {
            var row = new Grid
            {
                ColumnSpacing = 12,
                Padding = new Thickness(10, 5.5, 10, 5.5),
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            };
            var ts = new Label
            {
                Text = LyricModeEncoder.FormatLrcTime(line.Timestamp),
                FontSize = 11.5,
                FontFamily = "Consolas, monospace",
                WidthRequest = 78,
                HorizontalTextAlignment = TextAlignment.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 2, 0, 0),
            };
            ts.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
            row.Add(ts, 0, 0);

            // 右列：原文 + 罗马音 + 翻译（对齐 Lyrico 行序）
            var textCol = new VerticalStackLayout { Spacing = 2 };
            var text = new Label
            {
                FontSize = 13.5,
                LineBreakMode = LineBreakMode.WordWrap,
                VerticalOptions = LayoutOptions.Start,
            };
            text.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
            text.FormattedText = BuildLineText(line, mode);
            textCol.Children.Add(text);

            if (sub.TryGetValue(line, out var s))
            {
                if (s.Roma is { Text.Length: > 0 })
                    textCol.Children.Add(MakeSubLabel(s.Roma.Text));
                if (s.Trans is { Text.Length: > 0 })
                    textCol.Children.Add(MakeSubLabel(s.Trans.Text));
            }
            row.Add(textCol, 1, 0);

            WideAdapt.AttachHover(row);
            stack.Children.Add(row);
        }

        var (rt2, tt2) = LyricModeEncoder.CountSubLines(lyrics);
        return MakePanel(new ScrollView { Content = stack },
            $"共 {lyrics.Lines.Count} 行 · {ModeShortName(mode)}预览{SubLineSuffix(rt2, tt2)}");
    }

    /// <summary>罗马音/翻译行：小号次级色，跟在原文下方。</summary>
    private static Label MakeSubLabel(string text)
    {
        var l = new Label
        {
            Text = text,
            FontSize = 11.5,
            LineBreakMode = LineBreakMode.WordWrap,
            VerticalOptions = LayoutOptions.Start,
        };
        l.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        return l;
    }

    private static string SubLineSuffix(int romaCount, int transCount)
    {
        var parts = new List<string>();
        if (romaCount > 0) parts.Add($"罗马音 {romaCount}");
        if (transCount > 0) parts.Add($"翻译 {transCount}");
        return parts.Count > 0 ? $" · {string.Join(" / ", parts)}" : "";
    }

    /// <summary>按模式构造行内容：逐行=纯文本；逐字=词后小时间戳；增强=词前 (时间,时长)。无词级数据回退纯文本。</summary>
    private static FormattedString BuildLineText(LrcLyricLine line, LyricMode mode)
    {
        var fs = new FormattedString();
        var wordTs = line.WordTimestamps;
        var plain = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text;

        if (mode == LyricMode.Plain || wordTs is not { Count: > 1 })
        {
            fs.Spans.Add(new Span { Text = plain });
            return fs;
        }

        var primary = ResColor("PrimaryColor", "#8C7BFF");
        var secondary = ResColor("TextSecondaryColor", "#C2C6E4");
        foreach (var w in wordTs)
        {
            var shortTs = LyricModeEncoder.FormatLrcTime(w.Start);
            if (shortTs.Length > 6) shortTs = shortTs[3..];   // 只留 ss.fff
            if (mode == LyricMode.Verbatim)
            {
                fs.Spans.Add(new Span { Text = w.Word, FontSize = 13.5, TextColor = secondary });
                fs.Spans.Add(new Span
                {
                    Text = $" <{shortTs}>",
                    FontSize = 9.5,
                    FontFamily = "Consolas, monospace",
                    TextColor = primary.MultiplyAlpha(0.55f),
                });
            }
            else
            {
                var dur = Math.Max(0, (int)w.Duration.TotalMilliseconds);
                fs.Spans.Add(new Span
                {
                    Text = $"({shortTs},{dur}ms)",
                    FontSize = 9.5,
                    FontFamily = "Consolas, monospace",
                    TextColor = primary.MultiplyAlpha(0.55f),
                });
                fs.Spans.Add(new Span { Text = w.Word, FontSize = 13.5, TextColor = secondary });
            }
        }
        return fs;
    }

    // ═══════════ Tab 1：封面 ═══════════

    private View BuildCoverPanel()
    {
        var hasCover = _vm.Selected?.HasCover == true;

        var img = new Image { Aspect = Aspect.AspectFill };
        img.SetBinding(Image.SourceProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HighResCoverUrl)}")
            { Converter = new CoverUriConverter() });
        img.SetBinding(VisualElement.IsVisibleProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasCover)}"));

        var noCover = new Label
        {
            Text = "无封面",
            FontSize = 15,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        noCover.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        noCover.SetBinding(VisualElement.IsVisibleProperty,
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.HasCover)}")
            { Converter = new InvertBoolConverter() });

        var imgBox = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
            Background = Brush("CardBackgroundStrongColor", "#2DFFFFFF"),
            WidthRequest = 300,
            HeightRequest = 300,
            HorizontalOptions = LayoutOptions.Center,
            Content = new Grid { Children = { img, noCover } },
        };

        var badges = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center };
        badges.Children.Add(MakeBadge(
            new Binding($"{nameof(UnifiedSearchViewModel.Selected)}.{nameof(UnifiedSearchResult.Source)}"),
            "#2DFFFFFF", "TextSecondaryColor"));
        if (hasCover)
            badges.Children.Add(MakeBadge(null, "#264ADE80", "#4ADE80", text: "高分辨率封面"));

        var note = new Label
        {
            Text = hasCover ? "写入时将下载高清原图并嵌入音频文件（勾选「封面」后生效）" : "该结果无封面，无法写入封面",
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            MaxLines = 2,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        note.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var stack = new VerticalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(22),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { imgBox, badges, note },
        };
        return new ScrollView { Content = stack };
    }

    // ═══════════ Tab 2：元数据（新旧对照） ═══════════

    private View BuildMetadataPanel()
    {
        var sel = _vm.Selected;
        var song = _vm.Song.Song;
        var cols = _isWide == true ? 2 : 1;

        var fields = new (string Key, string New, string? Old)[]
        {
            ("歌名", sel?.Title ?? "", song.Title),
            ("艺人", sel?.Artist ?? "", song.Artist),
            ("专辑", sel?.Album ?? "", song.Album),
            ("时长", sel is { Duration: > 0 } ? $"{sel.Duration / 60}:{sel.Duration % 60:00}" : "未知",
                song.Duration > 0 ? ThemeHelper.FormatDuration(song.Duration) : null),
            ("来源", sel != null ? UnifiedSearchViewModel.SourceLabel(sel.Source) : "", null),
        };

        var grid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 12,
            Padding = new Thickness(16, 16, 16, 22),
        };
        for (int i = 0; i < cols; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (int i = 0; i < fields.Length; i++)
            grid.Add(BuildMetaCard(fields[i]), i % cols, i / cols);

        return new ScrollView { Content = grid };
    }

    private static View BuildMetaCard((string Key, string New, string? Old) f)
    {
        // 差异判定：新值空=不写（相同）；旧值空而新值有=变更；忽略大小写相等=相同
        var changed = !string.IsNullOrWhiteSpace(f.New) &&
            (string.IsNullOrWhiteSpace(f.Old) ||
             !string.Equals(f.New.Trim(), f.Old.Trim(), StringComparison.OrdinalIgnoreCase));

        var key = new Label { Text = f.Key, FontSize = 11.5 };
        key.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var tagLabel = new Label
        {
            Text = changed ? "变更" : "相同",
            FontSize = 9.5,
            FontAttributes = FontAttributes.Bold,
        };
        var tag = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
            Padding = new Thickness(6, 1),
            Content = tagLabel,
        };
        if (changed)
        {
            tag.BackgroundColor = Color.FromArgb("#22FBBF24");
            tagLabel.TextColor = Color.FromArgb("#FBBF24");
        }
        else
        {
            tag.BackgroundColor = Color.FromArgb("#14FFFFFF");
            tagLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        }

        var val = new Label
        {
            Text = string.IsNullOrWhiteSpace(f.New) ? "（空）" : f.New,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        val.SetDynamicResource(Label.TextColorProperty, "TextPrimaryColor");

        var stack = new VerticalStackLayout
        {
            Spacing = 5,
            Children = { new HorizontalStackLayout { Spacing = 7, Children = { key, tag } }, val },
        };
        if (changed && !string.IsNullOrWhiteSpace(f.Old))
        {
            var old = new Label
            {
                Text = $"原始：{f.Old}",
                FontSize = 11,
                TextDecorations = TextDecorations.Strikethrough,
                LineBreakMode = LineBreakMode.WordWrap,
            };
            old.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
            stack.Children.Add(old);
        }

        return new Border
        {
            StrokeThickness = 0,
            Background = Brush("CardBackgroundColor", "#1AFFFFFF"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) },
            Padding = new Thickness(15, 11),
            Content = stack,
        };
    }

    // ═══════════ VM 状态联动 ═══════════

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(UnifiedSearchViewModel.ApplyMetadata):
            case nameof(UnifiedSearchViewModel.ApplyLyrics):
            case nameof(UnifiedSearchViewModel.ApplyCover):
                RefreshPills();
                UpdateWriteState();
                break;
            case nameof(UnifiedSearchViewModel.IsBusy):
                UpdateWriteState();
                break;
            case nameof(UnifiedSearchViewModel.SelectedLyricsPreview):
                if (_activeTab == 0) ShowTab(0);
                break;
        }
    }

    private void UpdateWriteState()
    {
        var busy = _vm.IsBusy;
        _writeBtn.IsEnabled = !busy && (_vm.ApplyMetadata || _vm.ApplyLyrics || _vm.ApplyCover);
        _writeBtn.Text = busy ? "写入中…" : "写入";
    }

    private void RefreshPills()
    {
        var primary = ResColor("PrimaryColor", "#8C7BFF");
        var off = ResColor("TextSecondaryColor", "#C2C6E4");
        for (int i = 0; i < _opts.Length; i++)
        {
            var on = _opts[i].Get();
            _pillsTop[i]?.Refresh(on, primary, off);
            _pillsBottom[i]?.Refresh(on, primary, off);
        }
    }

    private void RefreshChips()
    {
        var primary = ResColor("PrimaryColor", "#8C7BFF");
        var off = ResColor("TextSecondaryColor", "#C2C6E4");
        for (int i = 0; i < Modes.Length; i++)
        {
            var on = _vm.SelectedMode == Modes[i];
            RefreshChip(_chipsNarrow[i], on, primary, off);
            RefreshChip(_chipsWide[i], on, primary, off);
        }
    }

    private static void RefreshChip(Button? chip, bool on, Color primary, Color off)
    {
        if (chip == null) return;
        chip.TextColor = on ? Colors.White : off;
        chip.BackgroundColor = on ? primary : Color.FromArgb("#1AFFFFFF");
        chip.FontAttributes = on ? FontAttributes.Bold : FontAttributes.None;
    }

    private void RefreshStats()
    {
        var results = _vm.FilteredResults;
        var lc = results.Count(r => r.HasLyrics);
        var cc = results.Count(r => r.HasCover);
        var idx = results.IndexOf(_vm.Selected);
        _statLyrics.Text = $"歌词结果　{lc}";
        _statCover.Text = $"封面结果　{cc}";
        _statCurrent.Text = idx >= 0
            ? $"当前选中　#{idx + 1} · {UnifiedSearchViewModel.SourceLabel(_vm.Selected!.Source)}"
            : "当前选中　—";
    }

    // ═══════════ 控件工厂 ═══════════

    private OptPill BuildPill(int optIndex)
    {
        var check = new Label
        {
            Text = "✓",
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        };
        var dot = new Border
        {
            WidthRequest = 16,
            HeightRequest = 16,
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
            VerticalOptions = LayoutOptions.Center,
            Content = check,
        };
        var text = new Label
        {
            Text = _opts[optIndex].Label,
            FontSize = 12,
            VerticalOptions = LayoutOptions.Center,
        };

        var root = new Border
        {
            HeightRequest = 30,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            Padding = new Thickness(10, 0, 13, 0),
            Content = new HorizontalStackLayout { Spacing = 6, Children = { dot, text } },
        };
        root.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => _opts[optIndex].Set(!_opts[optIndex].Get())),
        });
        WideAdapt.AttachHover(root);

        return new OptPill { Root = root, Dot = dot, Check = check, Text = text };
    }

    private HorizontalStackLayout BuildChips(Button[] target, double height, double fontSize)
    {
        var stack = new HorizontalStackLayout { Spacing = 8 };
        for (int i = 0; i < Modes.Length; i++)
        {
            var mode = Modes[i];
            var btn = new Button
            {
                Text = ModeShortName(mode),
                FontSize = fontSize,
                HeightRequest = height,
                CornerRadius = (int)(height / 2),
                Padding = new Thickness(fontSize + 1.5, 0),
            };
            btn.Clicked += (_, _) =>
            {
                _vm.SelectedMode = mode;
                RefreshChips();
            };
            target[i] = btn;
            stack.Children.Add(btn);
        }
        return stack;
    }

    private static Border MakeBadge(Binding? textBinding, string bgHex, string textColorResOrHex,
        string? text = null, string? visible = null)
    {
        var label = new Label
        {
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        if (text != null) label.Text = text;
        if (textBinding != null) label.SetBinding(Label.TextProperty, textBinding);

        if (textColorResOrHex.StartsWith('#'))
            label.TextColor = Color.FromArgb(textColorResOrHex);
        else
            label.SetDynamicResource(Label.TextColorProperty, textColorResOrHex);

        var border = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb(bgHex),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Padding = new Thickness(8, 2),
            Content = label,
        };
        if (visible != null)
            border.SetBinding(VisualElement.IsVisibleProperty, visible);
        return border;
    }

    private static Label MakeStatLabel(string text)
    {
        var l = new Label { Text = text, FontSize = 12.5 };
        l.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        return l;
    }

    private static BoxView MakeSeparator()
        => new() { HeightRequest = 1, Color = Color.FromArgb("#1AFFFFFF") };

    private static Label MakeCodeLabel(string text)
    {
        var l = new Label
        {
            Text = text,
            FontSize = 12,
            FontFamily = "Consolas, monospace",
            LineBreakMode = LineBreakMode.NoWrap,
            Margin = new Thickness(18, 14, 18, 6),
        };
        l.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");
        return l;
    }

    /// <summary>内容区（Star）+ 底部说明行（Auto）的面板骨架。</summary>
    private static Grid MakePanel(View content, string footerText)
    {
        var footer = new Label
        {
            Text = footerText,
            FontSize = 11,
            HorizontalTextAlignment = TextAlignment.Center,
            Padding = new Thickness(18, 8, 18, 10),
        };
        footer.SetDynamicResource(Label.TextColorProperty, "TextSecondaryColor");

        var g = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
        };
        g.Add(content, 0, 0);
        g.Add(footer, 0, 1);
        return g;
    }

    private static string ModeShortName(LyricMode mode) => mode switch
    {
        LyricMode.Verbatim => "逐字",
        LyricMode.Enhanced => "增强逐字",
        LyricMode.TTML => "TTML",
        _ => "逐行",
    };

    private static Color ResColor(string key, string fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c
            : Color.FromArgb(fallback);

    private static Brush Brush(string key, string fallback)
        => new SolidColorBrush(ResColor(key, fallback));

    /// <summary>提亮颜色（HSL 亮度叠加并钳制），用于主按钮渐变与选中文字。</summary>
    private static Color Lighten(Color c, float amount)
        => Color.FromHsla(c.GetHue(), c.GetSaturation(), Math.Min(1f, c.GetLuminosity() + amount), c.Alpha);

    /// <summary>写入选项胶囊（顶栏/底部双实例共用视觉逻辑）。</summary>
    private sealed class OptPill
    {
        public required Border Root;
        public required Border Dot;
        public required Label Check;
        public required Label Text;

        public void Refresh(bool on, Color primary, Color off)
        {
            Root.BackgroundColor = on ? primary.MultiplyAlpha(0.16f) : Color.FromArgb("#0AFFFFFF");
            Root.Stroke = on ? primary.MultiplyAlpha(0.55f) : Color.FromArgb("#21FFFFFF");
            Dot.BackgroundColor = on ? primary : Colors.Transparent;
            Dot.Stroke = on ? primary : Color.FromArgb("#5A5F86");
            Check.IsVisible = on;
            Text.TextColor = on ? Lighten(primary, 0.12f) : off;
        }
    }
}
