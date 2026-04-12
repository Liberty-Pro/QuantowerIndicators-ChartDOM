On Chart DOM
A custom Level 2 / DOM (Depth of Market) indicator for the Quantower trading platform, written in C#.
---
Overview
On Chart DOM renders Level 2 order book data directly on a price chart, overlaid on the candlestick area. It extends Quantower's native Level 2 indicator with cross-connection support — meaning it can display DOM data from any symbol on any active connection, regardless of what symbol the host chart belongs to. This makes it practical in multi-broker setups where you may be charting on one feed but want to trade or monitor depth from another.
This project was developed iteratively with AI assistance (Claude by Anthropic). I designed the feature set, directed the implementation, tested each change against a live Quantower instance, and refined the output through multiple cycles of feedback.
---
Key Features
Cross-connection source symbol — pull L2 data from any symbol on any connected feed via Quantower's native symbol picker; leave blank to use the chart's own symbol
Pin to price scale — align DOM levels precisely to their price on the chart's Y axis
Configurable refresh rate — throttle update frequency (default 333ms) to balance responsiveness and CPU load
Display side — render on the left or right side of the chart canvas
Histogram bars — proportional volume bars per price level; width configurable as a percentage of chart width; set to 0% to show order size only with no bars
Price label formatting — independently hide leading or trailing digits for a cleaner look on instruments with long price strings (e.g. show `940` instead of `23,940.00`)
Level highlight filters — three configurable size thresholds with individual colors to visually flag large orders
Background color — optional solid or semi-transparent background drawn as a single unified rectangle behind the entire DOM block, improving readability over busy chart areas
Fully configurable colors — separate color pickers for ask bars, bid bars, ask labels, bid labels, price text, and background
Configurable font size — adjust text size independently of other chart settings
Custom tick size — override the chart symbol's tick size if needed
---
How It Works
The indicator subscribes to `Symbol.NewLevel2` events on the selected source symbol. On each update it collects the top N ask and bid levels, calculates proportional bar widths relative to the largest visible level, and renders everything in `OnPaintChart` using GDI+. A configurable refresh throttle prevents unnecessary redraws at high quote rates.
When a source symbol is set that differs from the chart's own symbol, the indicator manages its own subscription lifecycle — subscribing on init or symbol change and unsubscribing cleanly on removal.
The background is drawn as one rectangle spanning the full vertical and horizontal extent of the DOM block before any bars or text are rendered, ensuring no gaps between rows and no layering artifacts.
---
Installation
Clone or download this repository.
Open the `.csproj` file in Visual Studio 2026 or later (required for .NET 10 targeting).
Verify the `HintPath` for `TradingPlatform.BusinessLayer.dll` in the `.csproj` matches your local Quantower installation path.
Build the project (`F6`). The output is automatically deployed to Quantower's indicators folder.
In Quantower, the indicator will appear as V2Lvl2 in the indicators list.
Requirements:
Quantower v1.146.3 or later
Visual Studio 2026 (v18.x) or later
.NET 10 SDK
---
Usage
Add V2Lvl2 to any chart via the Indicators panel.
Open the indicator's settings.
Optionally set L2 Source Symbol to a symbol from a different connection. Leave blank to use the chart's own symbol.
Adjust levels count, display side, histogram width, colors, and font size to suit your layout.
Enable Background color if you need the DOM to stand out over candles or other indicators.
To show only order sizes with no histogram bars, set Histogram width % to `0`.
---
Notes
The indicator can only draw within the chart canvas area. Rendering inside or outside the platform's price scale panel is not supported by the Quantower indicator API.
Contract rolling is not automated. When trading instruments with expiry (e.g. futures), the source symbol must be updated manually at rollover.
This indicator is Windows-only, consistent with the Quantower platform.
---
Disclaimer
This indicator is provided for informational and portfolio purposes. It does not constitute financial advice and is not intended as a commercial trading tool. Use at your own risk.
