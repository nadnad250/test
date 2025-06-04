#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class MyCustomStrategy : Strategy
    {
        private Series<double> deltas;    // Série pour stocker Delta
        private Series<double> zscores;   // Série pour stocker Z-Score
        private SMA sma;                  // SMA(20) sur Range=8

        private int zWindow = 20;             // Fenêtre Z-Score en nombre de barres
        private double deltaThreshold = 300;  // Seuil minimum pour Delta/Imbalance
        private int stopLossTicks = 10;       // StopLoss en ticks
        private int takeProfitTicks = 15;     // TakeProfit en ticks

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "MyCustomStrategy";
                Description = "Stratégie scalping sur Range=8 avec Delta + Z-Score + SMA20";
                // On utilise l’énumération Calculate, pas MarketDataType
                Calculate   = Calculate.OnBarClose;
                IsOverlay   = false;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
            }
            else if (State == State.Configure)
            {
                // Pas de DataSeries supplémentaires à ajouter—on travaille sur le chart Range=8
            }
            else if (State == State.DataLoaded)
            {
                // Initialisation des séries de calcul
                deltas   = new Series<double>(this);
                zscores  = new Series<double>(this);
                sma      = SMA(20);  // Moyenne mobile sur 20 barres Range=8
            }
        }

        protected override void OnBarUpdate()
        {
            // Attention : on attend au moins (zWindow + 2) barres complètes 
            // pour accéder en toute sécurité à Closes[2][0] et deltas[1..zWindow]
            if (CurrentBar < zWindow + 2)
                return;

            // 1) Calcul du Delta (imagination de ton propre Delta ou via OrderFlow)
            // Ici on prend l’écart des closes précédentes comme proxy de Delta
            double delta = Closes[1][0] - Closes[2][0];
            deltas[0] = delta;

            // 2) Calcul du Z-Score sur les zWindow barres précédentes
            double sum   = 0;
            double sumSq = 0;
            for (int i = 1; i <= zWindow; i++)
            {
                sum   += deltas[i];
                sumSq += deltas[i] * deltas[i];
            }

            double mean = sum / zWindow;
            double variance = (sumSq - (sum * sum / zWindow)) / (zWindow - 1);
            double stdDev   = variance > 0 ? Math.Sqrt(variance) : 0;
            double z = stdDev != 0 ? (delta - mean) / stdDev : 0;
            zscores[0] = z;

            // 3) Conditions d'entrée
            bool longSignal  = z >=  1.0 && delta >=  deltaThreshold && Close[0] > sma[0];
            bool shortSignal = z <= -1.0 && delta <= -deltaThreshold && Close[0] < sma[0];

            // 4) Entrée automatique : n’ouvrir qu’une seule position à la fois
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (longSignal)
                {
                    EnterLong("LongEntry");
                    SetStopLoss("LongEntry", CalculationMode.Ticks, stopLossTicks, false);
                    SetProfitTarget("LongEntry", CalculationMode.Ticks, takeProfitTicks);
                }
                else if (shortSignal)
                {
                    EnterShort("ShortEntry");
                    SetStopLoss("ShortEntry", CalculationMode.Ticks, stopLossTicks, false);
                    SetProfitTarget("ShortEntry", CalculationMode.Ticks, takeProfitTicks);
                }
            }
        }
    }
}
