# NinjaTrader Strategy Demo

This repository demonstrates a simple NinjaTrader **scalping** strategy
implemented in `MyCustomStrategy.cs`. The strategy combines a Delta
calculation, a Z-Score over a configurable window and a simple moving
average. All of the key settings can be adjusted from the NinjaTrader UI.

### Usage

1. Copy `MyCustomStrategy.cs` to your NinjaTrader `bin/Custom/Strategies`
   directory.
2. Restart NinjaTrader or compile the strategy from the NinjaScript Editor.
3. Create a new chart with a Range 8 data series and add `MyCustomStrategy` as
   a strategy.

   The script needs at least **`max(Z-Score Window, SMA Period) + 2` bars** of
   historical data before it begins evaluating signals. NinjaTrader handles
   this automatically but you may see no trades until enough bars have
   accumulated.

If NinjaTrader is disconnected from its data feed, the strategy will still
process any historical bars that are loaded on the chart. Make sure to load
enough days of data before enabling the strategy when running offline.

The repository also contains a sample Python launcher script (`LANCEUR_CORRIGE.py`)
and log file for reference.

When the strategy finishes, it prints a summary of all trades with entry/exit
times and profit in the NinjaScript Output window. This helps review historical
performance.

### Parameters

The following properties can be tweaked when adding the strategy:

- **Z-Score Window** – number of bars used to compute the Z-Score
- **Delta Threshold** – minimum Delta required to trigger an entry
- **Stop Loss (Ticks)** – stop-loss distance in ticks
- **Take Profit (Ticks)** – profit target distance in ticks
- **SMA Period** – period of the moving average filter
- **Z-Score Long** – minimum Z-Score to trigger a long entry (default `1`)
- **Z-Score Short** – maximum Z-Score to trigger a short entry (default `-1`)
