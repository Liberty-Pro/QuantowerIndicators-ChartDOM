# On Chart DOM

**Platform:** Quantower  
**Language:** C#  
**Type:** Custom Indicator

## Summary
On Chart DOM is a custom Quantower indicator that renders Level 2 order book data directly on the chart, overlaid on the candlestick area.

## Overview
This indicator extends Quantower’s native Level 2 display with cross-connection support. That means it can show DOM data from any symbol on any active connection, even if the chart itself belongs to a different symbol. This is especially useful in multi-broker setups where charting, monitoring, and trading may happen on separate feeds.

This project was developed iteratively with AI assistance using Claude. I designed the feature set, directed the implementation, tested each change in a live Quantower instance, and refined the output through multiple feedback cycles.

## Key Features
- Cross-connection source symbol support.
- Optional use of the chart’s own symbol when no source is selected.
- Pin to price scale for precise vertical alignment.
- Configurable refresh rate to balance responsiveness and CPU load.
- Left or right display side support.
- Histogram bars with adjustable width.
- Price label formatting options for cleaner long price strings.
- Three configurable level highlight thresholds.
- Optional background rectangle for better readability.
- Fully configurable colors for bid, ask, labels, price text, and background.
- Adjustable font size.
- Custom tick size override.

## How It Works
The indicator subscribes to `Symbol.NewLevel2` events on the selected source symbol. On each update, it collects the top ask and bid levels, calculates proportional bar widths relative to the largest visible level, and renders the result in `OnPaintChart` using GDI+.

A configurable refresh throttle prevents unnecessary redraws during fast quote updates. When the source symbol differs from the chart’s symbol, the indicator manages its own subscription lifecycle by subscribing on init or symbol change and unsubscribing cleanly when removed.

The background is drawn as one unified rectangle behind the DOM block before any bars or text are rendered, which helps avoid gaps and layering artifacts.

## Installation
1. Clone or download the repository.
2. Open the `.csproj` file in Visual Studio 2026 or later.
3. Verify the `HintPath` for `TradingPlatform.BusinessLayer.dll` matches your local Quantower installation path.
4. Build the project with `F6`.
5. The output is automatically deployed to Quantower’s indicators folder.
6. In Quantower, the indicator appears as `V2Lvl2` in the indicators list.

### Requirements
- Quantower v1.146.3 or later.
- Visual Studio 2026 or later.
- .NET 10 SDK.

## Usage
1. Add `V2Lvl2` to any chart from the Indicators panel.
2. Open the indicator settings.
3. Optionally set `L2 Source Symbol` to a symbol from a different connection.
4. Adjust levels count, display side, histogram width, colors, and font size.
5. Enable background color if you want the DOM to stand out over candles or other indicators.
6. To show only order sizes with no histogram bars, set histogram width to `0`.

## Notes
The indicator can only draw within the chart canvas area. Rendering inside or outside the platform’s price scale panel is not supported by the Quantower indicator API.

Contract rolling is not automated. For instruments with expiry, such as futures, the source symbol must be updated manually at rollover.

This indicator is Windows-only, consistent with the Quantower platform.

## Disclaimer
This indicator is provided for informational and portfolio purposes. It does not constitute financial advice and is not intended as a commercial trading tool. Use at your own risk.
