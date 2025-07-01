# NinjaTrader Strategy Demo

This repository demonstrates a simple NinjaTrader **scalping** strategy
implemented in `MyCustomStrategy.cs`. The strategy combines a Delta
calculation, a Z-Score over a configurable window and a 20-period simple
moving average.

### Usage

1. Copy `MyCustomStrategy.cs` to your NinjaTrader `bin/Custom/Strategies`
   directory.
2. Restart NinjaTrader or compile the strategy from the NinjaScript Editor.
3. Create a new chart with a Range 8 data series and add `MyCustomStrategy` as
   a strategy.

The repository also contains a sample Python launcher script (`LANCEUR_CORRIGE.py`)
and log file for reference.
