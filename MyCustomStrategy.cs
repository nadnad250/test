#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
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
        private SMA sma;                  // SMA personnalisable

        // Structures pour la performance
        private class TradeRecord
        {
            public DateTime EntryTime;
            public double EntryPrice;
            public DateTime ExitTime;
            public double ExitPrice;
            public double Profit;
        }

        private List<TradeRecord> tradeHistory;
        private int lastTradeCount;


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

                // Paramètres configurables par l'utilisateur
                ZWindow         = 20;
                DeltaThreshold  = 300;
                StopLossTicks   = 10;
                TakeProfitTicks = 15;
                SmaPeriod       = 20;
                ZScoreLong      = 1.0;   // Z >= 1 pour acheter
                ZScoreShort     = -1.0;  // Z <= -1 pour vendre

                BarsRequiredToTrade = Math.Max(ZWindow, SmaPeriod) + 2;
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
                sma      = SMA(SmaPeriod);  // Moyenne mobile personnalisable

                tradeHistory = new List<TradeRecord>();
                lastTradeCount = 0;
            }
            else if (State == State.Terminated)
            {
                if (tradeHistory != null && tradeHistory.Count > 0)
                {
                    Print("===== Récapitulatif des trades =====");
                    foreach (var tr in tradeHistory)
                    {
                        Print(string.Format(
                            "Entrée: {0:yyyy-MM-dd HH:mm:ss} @ {1:0.00} | Sortie: {2:yyyy-MM-dd HH:mm:ss} @ {3:0.00} | Profit: {4:0.00}",
                            tr.EntryTime,
                            tr.EntryPrice,
                            tr.ExitTime,
                            tr.ExitPrice,
                            tr.Profit));
                    }

                    double total = tradeHistory.Sum(t => t.Profit);
                    Print(string.Format("Total Profit: {0:0.00}", total));
                }
            }
        }

        protected override void OnBarUpdate()
        {
            // Toutes les conditions initiales sont gérées par BarsRequiredToTrade

            // 1) Calcul du Delta (imagination de ton propre Delta ou via OrderFlow)
            // Ici on prend l’écart des closes précédentes comme proxy de Delta
            // Utilisation des index de barres precedentes directement
            // au lieu de la collection Closes[] qui requiert des series
            double delta = Close[1] - Close[2];
            deltas[0] = delta;

            // 2) Calcul du Z-Score sur les zWindow barres précédentes
            double sum   = 0;
            double sumSq = 0;
            for (int i = 1; i <= ZWindow; i++)
            {
                sum   += deltas[i];
                sumSq += deltas[i] * deltas[i];
            }

            double mean = sum / ZWindow;
            double variance = (sumSq - (sum * sum / ZWindow)) / (ZWindow - 1);
            double stdDev   = variance > 0 ? Math.Sqrt(variance) : 0;
            double z = stdDev != 0 ? (delta - mean) / stdDev : 0;
            zscores[0] = z;

            // 3) Conditions d'entrée
            bool longSignal  = z >=  ZScoreLong  && delta >=  DeltaThreshold && Close[0] > sma[0];
            bool shortSignal = z <=  ZScoreShort && delta <= -DeltaThreshold && Close[0] < sma[0];

            // 4) Entrée automatique : n’ouvrir qu’une seule position à la fois
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (longSignal)
                {
                    EnterLong("LongEntry");
                    SetStopLoss("LongEntry", CalculationMode.Ticks, StopLossTicks, false);
                    SetProfitTarget("LongEntry", CalculationMode.Ticks, TakeProfitTicks);
                }
                else if (shortSignal)
                {
                    EnterShort("ShortEntry");
                    SetStopLoss("ShortEntry", CalculationMode.Ticks, StopLossTicks, false);
                    SetProfitTarget("ShortEntry", CalculationMode.Ticks, TakeProfitTicks);
                }
            }

            // Suivi de performance : enregistrer chaque trade clôturé
            if (SystemPerformance.AllTrades.Count > lastTradeCount)
            {
                var tr = SystemPerformance.AllTrades[lastTradeCount];
                tradeHistory.Add(new TradeRecord
                {
                    EntryTime = tr.Entry.Time,
                    EntryPrice = tr.Entry.Price,
                    ExitTime = tr.Exit.Time,
                    ExitPrice = tr.Exit.Price,
                    Profit = tr.ProfitCurrency
                });
                lastTradeCount = SystemPerformance.AllTrades.Count;
            }
        }

        #region Paramètres
        [NinjaScriptProperty]
        [System.ComponentModel.DataAnnotations.Range(5, int.MaxValue)]
        [Display(Name = "Z-Score Window", Order = 0, GroupName = "Parameters")]
        public int ZWindow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Delta Threshold", Order = 1, GroupName = "Parameters")]
        public double DeltaThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss (Ticks)", Order = 2, GroupName = "Parameters")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit (Ticks)", Order = 3, GroupName = "Parameters")]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 4, GroupName = "Parameters")]
        public int SmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Z-Score Long", Order = 5, GroupName = "Parameters")]
        public double ZScoreLong { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Z-Score Short", Order = 6, GroupName = "Parameters")]
        public double ZScoreShort { get; set; }
        #endregion
    }
}
