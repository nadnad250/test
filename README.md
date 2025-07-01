# NinjaTrader Strategy Demo

This repository demonstrates a simple NinjaTrader **scalping** strategy
implemented in `MyCustomStrategy.cs`. The strategy calculates Delta and
order-book imbalance **per tick** using Level 2 data, then evaluates a
Z-Score over a configurable window with an optional SMA filter. All of the key
settings can be adjusted from the NinjaTrader UI.

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

   Delta is derived from tick-by-tick trades: when a trade occurs at the bid
   price it adds to bid volume and when a trade hits the ask it adds to ask
   volume. The difference between these two volumes forms the per-bar Delta,
   while the imbalance percentage is `Delta / (bid + ask) * 100` for that bar.

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
- **Imbalance Threshold** – minimum imbalance percentage (Delta / volume * 100)
  required alongside Delta
- **Stop Loss (Ticks)** – stop-loss distance in ticks
- **Take Profit (Ticks)** – profit target distance in ticks
- **SMA Period** – period of the moving average filter
- **Z-Score Long** – minimum Z-Score to trigger a long entry (default `1`)
- **Z-Score Short** – maximum Z-Score to trigger a short entry (default `-1`)
- The strategy also outputs the per-bar Delta and order-book imbalance for
  reference in the NinjaScript Output window.

Additional options allow finer risk control:

- **Delta Cap** – ignores extreme Delta spikes beyond this value
- **HTF Period / HTF SMA Period** – higher time frame series used to confirm
  the main trend
- **Start/End Time** – active trading window in HHmmss format
- **Trailing Stop (Ticks)** – trailing stop distance once in profit
- **ATR Period / ATR Multiplier** – adjusts order size based on volatility
- **Daily Loss Limit** – stops trading for the day after this loss is reached

The strategy writes a `trade_log.csv` file containing each trade’s entry and
exit for later analysis. Delta, Z‑Score and the current position status are
displayed on the chart using `Draw.TextFixed`.

To obtain accurate volume data, enable **Tick Replay** on your chart and ensure
your data provider supplies Level 2 tick information.
