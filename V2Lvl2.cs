// V2Lvl2.cs
// Based directly on Quantower's native IndicatorLevel2 source code.
// The only addition is the "L2 Source Symbol" input parameter which allows
// the indicator to pull Level 2 data from any symbol on any active connection,
// regardless of which symbol/connection the chart itself belongs to.
//
// HOW TO USE
// ----------
// 1. Add this file to your Quantower Indicator project in Visual Studio.
// 2. Build (F6). Quantower auto-deploys the compiled indicator.
// 3. Add "V2Lvl2" to any chart via the Indicators menu.
// 4. In Settings, click the "L2 Source Symbol" picker and choose any symbol
//    from any connected feed. Leave blank to use the chart's own symbol.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

#pragma warning disable CA1416  // GDI+ calls are Windows-only; Quantower is Windows-only

namespace V2Lvl2;

public class V2Lvl2 : Indicator
{
    #region Parameters

    private const int MIN_BAR_HEIGHT_PX = 15;
    private const int MIN_BAR_OFFSET_PX = 5;

    // ── Cross-connection source symbol (the only addition vs native) ──────
    [InputParameter("L2 Source Symbol", 5)]
    public Symbol SourceSymbol = null;

    [InputParameter("Pin to price scale", 10)]
    public bool PintToPriceScale { get; set; }

    [InputParameter("Refresh rate (ms)", 15, 50, 10000, 50, 0)]
    public int RefreshRateMs = 333;

    [InputParameter("Custom tick size (0 = use chart tick size)", 16, 0, 99999, 0.0001, 4)]
    public double CustomTickSize = 0;

    [InputParameter("Hide leading digits (0 = off)", 17, 0, 10, 1, 0)]
    public int HideLeadingDigits = 0;

    [InputParameter("Hide trailing digits (0 = off)", 18, 0, 10, 1, 0)]
    public int HideTrailingDigits = 0;

    [InputParameter("Display side", 19, variants: new object[] { "Right side of chart", Side.Right, "Left side of chart", Side.Left })]
    public Side DisplaySide = Side.Right;

    [InputParameter("Levels count", 20, 1, 9999, 1, 0)]
    public int LevelsCount = 5;

    [InputParameter("Histogram width, %", 30, 0, 100, 1, 0)]
    public int HistogramWidthPercent = 15;

    public bool StickToPriceScale = false;

    public Color AskColor
    {
        get => this.askColor;
        set
        {
            if (this.askColor.Equals(value))
                return;
            this.askColor = value;
            this.askHistogramBrush = new SolidBrush(value);
        }
    }
    private Color askColor;
    private SolidBrush askHistogramBrush;

    public Color AskLabelColor
    {
        get => this.askLabelColor;
        set
        {
            if (this.askLabelColor.Equals(value))
                return;
            this.askLabelColor = value;
            this.askSizeFontBrush = new SolidBrush(value);
        }
    }
    private Color askLabelColor;
    private SolidBrush askSizeFontBrush;

    public Color BidColor
    {
        get => this.bidColor;
        set
        {
            if (this.bidColor.Equals(value))
                return;
            this.bidColor = value;
            this.bidHistogramBrush = new SolidBrush(value);
        }
    }
    private Color bidColor;
    private SolidBrush bidHistogramBrush;

    public Color BidLabelColor
    {
        get => this.bidLabelColor;
        set
        {
            if (this.bidLabelColor.Equals(value))
                return;
            this.bidLabelColor = value;
            this.bidSizeFontBrush = new SolidBrush(value);
        }
    }
    private Color bidLabelColor;
    private SolidBrush bidSizeFontBrush;

    [InputParameter("Price color", 60)]
    public Color PriceColor
    {
        get => this.priceColor;
        set
        {
            if (this.priceColor.Equals(value))
                return;
            this.priceColor = value;
            this.priceFontBrush = new SolidBrush(value);
        }
    }
    private Color priceColor;
    private SolidBrush priceFontBrush;

    [InputParameter("Background color enabled", 65)]
    public bool BackgroundColorEnabled = false;

    [InputParameter("Background color", 66)]
    public Color BackgroundColor
    {
        get => this.backgroundColor;
        set
        {
            if (this.backgroundColor.Equals(value))
                return;
            this.backgroundColor = value;
            this.backgroundBrush = new SolidBrush(value);
        }
    }
    private Color backgroundColor;
    private SolidBrush backgroundBrush;

    [InputParameter("Font size", 70, 6, 30, 1, 0)]
    public int FontSize
    {
        get => this.fontSize;
        set
        {
            if (this.fontSize == value)
                return;
            this.fontSize = value;
            this.font?.Dispose();
            this.font = new Font("Verdana", value, FontStyle.Regular, GraphicsUnit.Pixel);
        }
    }
    private int fontSize = 0;

    public override string ShortName => $"V2Lvl2 ({this.LevelsCount})";

    private IList<Lvl2LevelItem> asks;
    private IList<Lvl2LevelItem> bids;

    private Lvl2FiltersCache filtersCache = new Lvl2FiltersCache(null);

    private long lastQuoteTime;
    private long fRefreshTime;
    private double currentMaxLevelSize;

    private Font font;
    private readonly StringFormat farCenter;
    private readonly StringFormat nearCenter;

    // Tracks which symbol we're currently subscribed to for L2
    private Symbol subscribedSymbol;

    #endregion Parameters

    public V2Lvl2()
    {
        this.Name = "V2Lvl2";

        this.AskColor = Color.FromArgb(64, 251, 87, 87);
        this.AskLabelColor = Color.FromArgb(255, this.AskColor);

        this.BidColor = Color.FromArgb(64, 0, 178, 89);
        this.BidLabelColor = Color.FromArgb(255, this.BidColor);
        this.PriceColor = Color.FromArgb(110, 119, 128);
        this.BackgroundColor = Color.FromArgb(40, 0, 0, 0);

        this.FontSize = 10;  // initializes this.font via the property setter
        this.farCenter = new StringFormat() { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        this.nearCenter = new StringFormat() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
    }

    #region Overrides

    protected override void OnInit()
    {
        base.OnInit();

        // Default to chart's own symbol if nothing selected
        if (this.SourceSymbol == null)
            this.SourceSymbol = this.Symbol;

        // Preserve any filter settings that were restored before OnInit ran
        var savedFilterSettings = this.filtersCache?.Settings;
        this.filtersCache = new Lvl2FiltersCache(this.SourceSymbol);
        if (savedFilterSettings != null)
            this.filtersCache.Settings = savedFilterSettings;
        this.fRefreshTime = TimeSpan.FromMilliseconds(this.RefreshRateMs).Ticks;
        this.asks = new List<Lvl2LevelItem>();
        this.bids = new List<Lvl2LevelItem>();

        this.ResolveSubscription();
        this.UpdateIndicatorData();
    }

    public override IList<SettingItem> Settings
    {
        get
        {
            var settings = base.Settings;
            var separator = settings.FirstOrDefault()?.SeparatorGroup;

            settings.Add(new SettingItemPairColor("AskStyle", new PairColor(this.AskLabelColor, this.AskColor, loc._("Text"), loc._("Back")), 40)
            {
                Text = loc._("Ask style"),
                SeparatorGroup = separator
            });
            settings.Add(new SettingItemPairColor("BidStyle", new PairColor(this.BidLabelColor, this.BidColor, loc._("Text"), loc._("Back")), 50)
            {
                Text = loc._("Bid style"),
                SeparatorGroup = separator
            });

            settings.Add(new SettingItemGroup("FiltersCache", this.filtersCache.Settings));

            return settings;
        }
        set
        {
            base.Settings = value;
            var holder = new SettingsHolder(value);

            if (holder.TryGetValue("AskStyle", out var si) && si.Value is PairColor askStyle)
            {
                this.AskLabelColor = askStyle.Color1;
                this.AskColor = askStyle.Color2;
            }

            if (holder.TryGetValue("BidStyle", out si) && si.Value is PairColor bidStyle)
            {
                this.BidLabelColor = bidStyle.Color1;
                this.BidColor = bidStyle.Color2;
            }

            if (holder.TryGetValue("FiltersCache", out si) && si?.Value is IList<SettingItem> filtersCacheSI)
                this.filtersCache.Settings = filtersCacheSI;
        }
    }

    public override void OnPaintChart(PaintChartEventArgs args)
    {
        // Re-subscribe if the user changed the source symbol in Settings
        if (this.subscribedSymbol != this.SourceSymbol)
            this.ResolveSubscription();

        var sym = this.SourceSymbol ?? this.Symbol;
        if (sym == null)
            return;

        try
        {
            var gr = args.Graphics;
            gr.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            gr.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.GammaCorrected;
            gr.SetClip(args.Rectangle);

            int maxWidth = args.Rectangle.Width * this.HistogramWidthPercent / 100;

            if (!this.StickToPriceScale)
            {
                int tickSizeText = (int)gr.MeasureString(this.FormatPrice(sym, sym.TickSize), this.font).Width;
                if (tickSizeText < maxWidth)
                    maxWidth -= tickSizeText;
            }

            // Draw one unified background rectangle behind all rows
            if (this.BackgroundColorEnabled)
                this.DrawBackground(gr, sym, args.Rectangle);

            this.DrawLevels(gr, sym, args.Rectangle, this.asks, maxWidth, DrawingLevelType.Ask);
            this.DrawLevels(gr, sym, args.Rectangle, this.bids, maxWidth, DrawingLevelType.Bid);
        }
        catch (Exception ex)
        {
            Core.Loggers.Log(ex);
        }
    }

    protected override void OnClear()
    {
        this.Unsubscribe();
        this.asks?.Clear();
        this.bids?.Clear();
        base.OnClear();
    }

    public override void Dispose()
    {
        this.font?.Dispose();
        this.askHistogramBrush?.Dispose();
        this.askSizeFontBrush?.Dispose();
        this.bidHistogramBrush?.Dispose();
        this.bidSizeFontBrush?.Dispose();
        this.priceFontBrush?.Dispose();
        this.filtersCache?.Dispose();
        base.Dispose();
    }

    #endregion Overrides

    #region Subscription management

    private void ResolveSubscription()
    {
        this.Unsubscribe();

        if (this.SourceSymbol == null)
            this.SourceSymbol = this.Symbol;

        if (this.SourceSymbol == null)
            return;

        this.SourceSymbol.NewLevel2 += this.Symbol_NewLevel2;
        this.subscribedSymbol = this.SourceSymbol;
    }

    private void Unsubscribe()
    {
        if (this.subscribedSymbol == null)
            return;
        this.subscribedSymbol.NewLevel2 -= this.Symbol_NewLevel2;
        this.subscribedSymbol = null;
    }

    #endregion Subscription management

    #region Update MD

    private void Symbol_NewLevel2(Symbol symbol, Level2Quote level2, DOMQuote dom) =>
        this.TryMarketDepthRecalculate();

    internal void TryMarketDepthRecalculate()
    {
        if (this.IsItTimeToUpdate(out long now))
        {
            this.UpdateIndicatorData();
            this.lastQuoteTime = now;
        }
    }

    private bool IsItTimeToUpdate(out long now)
    {
        now = Core.TimeUtils.DateTimeUtcNow.Ticks;
        return now - this.lastQuoteTime > this.fRefreshTime;
    }

    private void UpdateIndicatorData()
    {
        var sym = this.SourceSymbol ?? this.Symbol;
        if (sym == null || this.CurrentChart == null)
            return;

        var dom = sym.DepthOfMarket.GetDepthOfMarketAggregatedCollections(new GetDepthOfMarketParameters()
        {
            GetLevel2ItemsParameters = new GetLevel2ItemsParameters()
            {
                AggregateMethod = AggregateMethod.ByPriceLVL,
                CustomTickSize = this.CustomTickSize > 0 ? this.CustomTickSize : this.CurrentChart.TickSize,
                LevelsCount = this.LevelsCount,
            }
        });

        this.PopulateCollection(this.asks, dom.Asks, this.askHistogramBrush, this.askSizeFontBrush, out double maxAskSize);
        this.PopulateCollection(this.bids, dom.Bids, this.bidHistogramBrush, this.bidSizeFontBrush, out double maxBidSize);

        this.currentMaxLevelSize = Math.Max(maxAskSize, maxBidSize);
    }

    #endregion Update MD

    #region Misc

    private void PopulateCollection(IList<Lvl2LevelItem> items, Level2Item[] level2Items, SolidBrush backBrush, SolidBrush foreBrush, out double maxSize)
    {
        maxSize = double.MinValue;
        int count = Math.Max(items.Count, level2Items.Length);

        for (int i = 0; i < count; i++)
        {
            if (items.Count <= i)
                items.Add(new Lvl2LevelItem());

            var cacheItem = items[i];
            cacheItem.IsValid = level2Items.Length > i;

            if (cacheItem.IsValid)
            {
                var newItem = level2Items[i];
                cacheItem.Price = newItem.Price;
                cacheItem.Size = newItem.Size;

                if (this.filtersCache.TryGetHighlightLevel(cacheItem.Size, out var level))
                {
                    cacheItem.BackBrush = level.BackBrush;
                    cacheItem.ForeBrush = level.ForeBrush;
                }
                else
                {
                    cacheItem.BackBrush = backBrush;
                    cacheItem.ForeBrush = foreBrush;
                }

                maxSize = Math.Max(cacheItem.Size, maxSize);
            }
        }
    }

    private void DrawBackground(Graphics gr, Symbol symbol, Rectangle windowRect)
    {
        // Compute bar height the same way DrawLevels does
        int barH = MIN_BAR_HEIGHT_PX - MIN_BAR_OFFSET_PX;
        if (this.PintToPriceScale)
        {
            barH = Math.Max(1, (int)Math.Round(this.CurrentChart.MainWindow.YScaleFactor * this.CurrentChart.TickSize));
            if (barH > 2) barH -= 1;
            if (barH > 5) barH -= 2;
        }

        // Count valid levels
        int askCount = 0;
        for (int i = 0; i < this.asks.Count; i++)
            if (this.asks[i].IsValid) askCount++; else break;

        int bidCount = 0;
        for (int i = 0; i < this.bids.Count; i++)
            if (this.bids[i].IsValid) bidCount++; else break;

        if (askCount == 0 && bidCount == 0)
            return;

        // Vertical extent
        float midY = windowRect.Y + windowRect.Height / 2f;
        float topY = midY - MIN_BAR_HEIGHT_PX / 2f - (askCount - 1) * MIN_BAR_HEIGHT_PX;
        float bottomY = midY + MIN_BAR_HEIGHT_PX / 2f + (bidCount - 1) * MIN_BAR_HEIGHT_PX + barH;

        bool leftSide = this.DisplaySide == Side.Left;
        bool drawText = barH >= 10;

        // Measure price label width using a representative price
        float priceW = 0;
        if (drawText && !this.PintToPriceScale && !this.StickToPriceScale)
        {
            // Use the widest price label across all levels
            var allItems = this.asks.Concat(this.bids);
            foreach (var item in allItems)
            {
                if (!item.IsValid) break;
                float w = gr.MeasureString(this.FormatPrice(symbol, item.Price), this.font).Width;
                if (w > priceW) priceW = w;
            }
        }

        // Measure widest size label
        float maxSizeLabelW = 0;
        if (drawText)
        {
            foreach (var item in this.asks)
            {
                if (!item.IsValid) break;
                float w = gr.MeasureString(symbol.FormatQuantity(item.Size), this.font).Width;
                if (w > maxSizeLabelW) maxSizeLabelW = w;
            }
            foreach (var item in this.bids)
            {
                if (!item.IsValid) break;
                float w = gr.MeasureString(symbol.FormatQuantity(item.Size), this.font).Width;
                if (w > maxSizeLabelW) maxSizeLabelW = w;
            }
        }

        // Max bar width
        float maxBarW = windowRect.Width * this.HistogramWidthPercent / 100f;

        // Total width = price label + gap + max bar + gap + size label
        float totalW = priceW + 4 + maxBarW + (maxSizeLabelW > 0 ? maxSizeLabelW + 6 : 0);

        float bgX;
        if (leftSide)
            bgX = windowRect.Left;
        else
            bgX = windowRect.Right - totalW;

        // Clamp to window
        bgX = Math.Max(bgX, windowRect.Left);
        float bgRight = Math.Min(bgX + totalW, windowRect.Right);

        gr.FillRectangle(this.backgroundBrush, bgX, topY, bgRight - bgX, bottomY - topY);
    }

    private void DrawLevels(Graphics gr, Symbol symbol, Rectangle windowRect, IList<Lvl2LevelItem> items, int maxWidth, DrawingLevelType type)
    {
        if (symbol == null)
            return;

        // Calculate bar height exactly as the native indicator does
        int barH = MIN_BAR_HEIGHT_PX - MIN_BAR_OFFSET_PX;
        if (this.PintToPriceScale)
        {
            barH = Math.Max(1, (int)Math.Round(this.CurrentChart.MainWindow.YScaleFactor * this.CurrentChart.TickSize));
            if (barH > 2) barH -= 1;
            if (barH > 5) barH -= 2;
        }

        bool drawText = barH >= 10;
        bool leftSide = this.DisplaySide == Side.Left;

        double pointY = windowRect.Y + windowRect.Height / 2;

        for (int i = 0; i < items.Count; i++)
        {
            if (!items[i].IsValid)
                break;

            var item = items[i];

            item.Rectangle.Width = (float)(item.Size * maxWidth / this.currentMaxLevelSize);
            item.Rectangle.Height = barH;

            // Left side: bars grow rightward from windowRect.Left
            // Right side (default): bars grow leftward from windowRect.Right
            if (leftSide)
                item.Rectangle.X = windowRect.Left;
            else
                item.Rectangle.X = windowRect.Right - item.Rectangle.Width;

            if (this.PintToPriceScale)
            {
                item.Rectangle.Y = (int)Math.Round(
                    this.CurrentChart.MainWindow.CoordinatesConverter.GetChartY(item.Price) - barH / 2);
            }
            else
            {
                if (type == DrawingLevelType.Ask)
                    item.Rectangle.Y = (float)pointY - MIN_BAR_HEIGHT_PX / 2;
                else
                    item.Rectangle.Y = (float)pointY + MIN_BAR_HEIGHT_PX / 2;
            }

            // Only draw if visible within the chart window
            if (item.Rectangle.Top > windowRect.Top && item.Rectangle.Bottom < windowRect.Bottom)
            {
                string sizeText = symbol.FormatQuantity(item.Size);
                string priceText = this.FormatPrice(symbol, item.Price);

                float sizeTextWidth = gr.MeasureString(sizeText, this.font).Width;
                float priceTextWidth = gr.MeasureString(priceText, this.font).Width;

                if (leftSide)
                {
                    // Calculate bar X (to the right of price label)
                    if (drawText && !this.PintToPriceScale && !this.StickToPriceScale)
                        item.Rectangle.X = windowRect.Left + priceTextWidth + 4;

                    // 1. Price label
                    if (drawText && !this.PintToPriceScale && !this.StickToPriceScale)
                        gr.DrawString(priceText, this.font, this.priceFontBrush,
                                      windowRect.Left,
                                      item.Rectangle.Y + item.Rectangle.Height / 2,
                                      this.nearCenter);

                    // 3. Size label
                    if (drawText && windowRect.Width - item.Rectangle.Width >= sizeTextWidth)
                        gr.DrawString(sizeText, this.font, item.ForeBrush,
                                      item.Rectangle.Right + 2,
                                      item.Rectangle.Y + item.Rectangle.Height / 2,
                                      this.nearCenter);
                }
                else
                {
                    // Calculate bar X (shifted left to make room for price label)
                    if (drawText && !this.PintToPriceScale && !this.StickToPriceScale)
                        item.Rectangle.X -= priceTextWidth + 2;

                    // 1. Price label
                    if (drawText && !this.PintToPriceScale && !this.StickToPriceScale)
                        gr.DrawString(priceText, this.font, this.priceFontBrush,
                                      windowRect.Right,
                                      item.Rectangle.Y + item.Rectangle.Height / 2,
                                      this.farCenter);

                    // 3. Size label
                    if (drawText && windowRect.Width - item.Rectangle.Width >= sizeTextWidth)
                        gr.DrawString(sizeText, this.font, item.ForeBrush,
                                      item.Rectangle.X - 2,
                                      item.Rectangle.Y + item.Rectangle.Height / 2,
                                      this.farCenter);
                }

                // 4. Draw histogram bar (on top of background, under text)
                gr.FillRectangle(item.BackBrush, item.Rectangle);
            }

            if (type == DrawingLevelType.Ask)
                pointY -= MIN_BAR_HEIGHT_PX;
            else
                pointY += MIN_BAR_HEIGHT_PX;
        }
    }

    private string FormatPrice(Symbol symbol, double price)
    {
        string full = symbol?.FormatPrice(price) ?? price.ToString();

        // Hide trailing digits: strip decimal point and everything after it
        if (this.HideTrailingDigits > 0)
        {
            int dotIndex = full.IndexOf('.');
            if (dotIndex >= 0)
            {
                // Remove exactly HideTrailingDigits characters from the end,
                // but never past the decimal point position
                int charsToRemove = Math.Min(this.HideTrailingDigits, full.Length - dotIndex);
                full = full.Substring(0, full.Length - charsToRemove).TrimEnd('.');
            }
        }

        // Hide leading digits: strip characters from the front of the price string
        if (this.HideLeadingDigits > 0 && full.Length > this.HideLeadingDigits)
            full = full.Substring(this.HideLeadingDigits);

        return full;
    }

    #endregion Misc

    #region Nested types

    private enum DrawingLevelType { Ask, Bid }

    public enum Side { Left, Right }

    private class Lvl2LevelItem
    {
        internal RectangleF Rectangle;
        public double Price { get; set; }
        public double Size { get; set; }
        public bool IsValid { get; set; }
        public Brush BackBrush { get; set; }
        public Brush ForeBrush { get; set; }

        public Lvl2LevelItem()
        {
            this.Rectangle = new Rectangle();
            this.IsValid = false;
        }

        public override string ToString() =>
            this.IsValid ? $"Price:{this.Price}  Size:{this.Size}" : this.IsValid.ToString();
    }

    private class Lvl2HighlightLevel : ICustomizable, IDisposable
    {
        public Symbol Symbol { get; private set; }
        public int Index { get; private set; }

        private bool isEnabled;
        public bool IsEnabled
        {
            get => this.isEnabled;
            set
            {
                if (this.isEnabled == value) return;
                this.isEnabled = value;
                this.OnEnabledChanged?.Invoke();
            }
        }

        private double level;
        public double Level
        {
            get => this.level;
            set
            {
                if (this.level == value) return;
                this.level = value;
                this.OnLevelChanged?.Invoke();
            }
        }

        private Color? color;
        public Color Color
        {
            get => this.color.Value;
            set
            {
                if (this.color == value) return;
                this.color = value;
                this.BackBrush = new SolidBrush(value);
                this.ForeBrush = new SolidBrush(Color.FromArgb(127, value));
            }
        }

        public Brush BackBrush { get; private set; }
        public Brush ForeBrush { get; private set; }

        public event Action OnLevelChanged;
        public event Action OnEnabledChanged;

        public Lvl2HighlightLevel(Symbol symbol, Color color, bool enable, int index = 0)
        {
            this.Symbol = symbol;
            this.Color = color;
            this.Index = index;
            this.IsEnabled = enable;
        }

        public void Dispose()
        {
            this.Symbol = null;
            this.BackBrush?.Dispose(); this.BackBrush = null;
            this.ForeBrush?.Dispose(); this.ForeBrush = null;
        }

        public IList<SettingItem> Settings
        {
            get
            {
                int lotStep = this.Symbol != null
                    ? CoreMath.GetValuePrecision((decimal)this.Symbol.LotStep)
                    : 0;

                return new List<SettingItem>
                {
                    new SettingItemColor($"HighlightValueStyle_{this.Index}", this.Color)
                    {
                        SortIndex    = 70,
                        Checked      = this.IsEnabled,
                        WithCheckBox = true,
                        Text         = loc._("Style"),
                        ColorText    = loc._("Color")
                    },
                    new SettingItemDouble($"HighlightFilterValue_{this.Index}", this.Level)
                    {
                        SortIndex     = 80,
                        Minimum       = this.Symbol?.MinLot ?? 0d,
                        Maximum       = this.Symbol?.MaxLot ?? double.MaxValue,
                        Increment     = this.Symbol?.LotStep ?? 1d,
                        DecimalPlaces = lotStep,
                        Text          = loc._("Value"),
                        Relation      = new SettingItemRelation(
                            new Dictionary<string, IEnumerable<object>>
                            {
                                { $"HighlightValueStyle_{this.Index}", new object[0] }
                            },
                            this.HighlightFilterValueRelationHandler)
                    }
                };
            }
            set
            {
                if (value.GetItemByName($"HighlightValueStyle_{this.Index}") is SettingItemColor item)
                {
                    this.IsEnabled = item.Checked;
                    this.Color = (Color)item.Value;
                }
                if (value.GetItemByName($"HighlightFilterValue_{this.Index}") is SettingItemDouble filterSI)
                    this.Level = (double)filterSI.Value;
            }
        }

        private bool HighlightFilterValueRelationHandler(SettingItemRelationParameters relationParameters)
        {
            bool hasChanged = false;
            try
            {
                if (relationParameters.ChangedItem is SettingItemColor si)
                {
                    hasChanged = relationParameters.DependentItem.Enabled != si.Checked;
                    relationParameters.DependentItem.Enabled = si.Checked;
                }
            }
            catch { }
            return hasChanged;
        }
    }

    private class Lvl2FiltersCache : ICustomizable, IDisposable
    {
        private readonly Color defaultColor;
        private readonly Lvl2HighlightLevel filter1;
        private readonly Lvl2HighlightLevel filter2;
        private readonly Lvl2HighlightLevel filter3;
        private readonly List<Lvl2HighlightLevel> filtersCache;
        private List<Lvl2HighlightLevel> enableSortedFiltersCache;

        public Lvl2FiltersCache(Symbol symbol)
        {
            this.filtersCache = new List<Lvl2HighlightLevel>();
            this.enableSortedFiltersCache = new List<Lvl2HighlightLevel>();
            this.defaultColor = Color.FromArgb(255, 234, 91);

            this.AddNewLevel(this.filter1 = new Lvl2HighlightLevel(symbol, this.defaultColor, false, 0));
            this.AddNewLevel(this.filter2 = new Lvl2HighlightLevel(symbol, this.defaultColor, false, 1));
            this.AddNewLevel(this.filter3 = new Lvl2HighlightLevel(symbol, this.defaultColor, false, 2));

            this.ResortCache();
        }

        public IList<SettingItem> Settings
        {
            get
            {
                var settings = new List<SettingItem>();
                settings.Add(new SettingItemGroup("Filter1", this.ApplySeparatorGroup(this.filter1.Settings, new SettingItemSeparatorGroup("Filter 1", -999))));
                settings.Add(new SettingItemGroup("Filter2", this.ApplySeparatorGroup(this.filter2.Settings, new SettingItemSeparatorGroup("Filter 2", -999))));
                settings.Add(new SettingItemGroup("Filter3", this.ApplySeparatorGroup(this.filter3.Settings, new SettingItemSeparatorGroup("Filter 3", -999))));
                return settings;
            }
            set
            {
                if (value.GetItemByName("Filter1")?.Value is IList<SettingItem> f1) this.filter1.Settings = f1;
                if (value.GetItemByName("Filter2")?.Value is IList<SettingItem> f2) this.filter2.Settings = f2;
                if (value.GetItemByName("Filter3")?.Value is IList<SettingItem> f3) this.filter3.Settings = f3;
            }
        }

        internal bool TryGetHighlightLevel(double size, out Lvl2HighlightLevel level)
        {
            level = null;
            for (int i = this.enableSortedFiltersCache.Count - 1; i >= 0; i--)
            {
                var filter = this.enableSortedFiltersCache[i];
                if (!filter.IsEnabled) continue;
                if (size >= filter.Level) { level = filter; break; }
            }
            return level != null;
        }

        public void Dispose()
        {
            foreach (var l in this.filtersCache)
            {
                l.OnLevelChanged -= this.ResortCache;
                l.OnEnabledChanged -= this.ResortCache;
                l.Dispose();
            }
            this.filtersCache.Clear();
            this.enableSortedFiltersCache.Clear();
        }

        private void ResortCache()
        {
            var list = this.filtersCache.Where(l => l.IsEnabled).ToList();
            list.Sort((l, r) => l.Level.CompareTo(r.Level));
            this.enableSortedFiltersCache = list;
        }

        private void AddNewLevel(Lvl2HighlightLevel level)
        {
            level.OnLevelChanged += this.ResortCache;
            level.OnEnabledChanged += this.ResortCache;
            this.filtersCache.Add(level);
        }

        private IList<SettingItem> ApplySeparatorGroup(IList<SettingItem> settings, SettingItemSeparatorGroup separ)
        {
            foreach (var item in settings)
                item.SeparatorGroup = separ;
            return settings;
        }
    }

    #endregion Nested types
}