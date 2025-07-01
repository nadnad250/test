#region Using declarations
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.IO;
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
        private Series<double> deltas;    // Série pour stocker Delta par bar
        private Series<double> zscores;   // Série pour stocker Z-Score
        private Series<double> imbalances; // Série pour stocker l'imbalance en %
        private SMA sma;                  // SMA personnalisable

        // Accumulateurs de volume pour le Delta par tick
        private double barBidVolume;
        private double barAskVolume;

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

        // Rolling statistics for Z-Score
        private double rollingSum;
        private double rollingSumSq;

        // Higher time frame filter
        private SMA htfSma;

        // Volatility indicator for position sizing
        private ATR atr;

        // Daily loss management
        private double dailyStartProfit;
        private DateTime lastProfitCheckDate;
        private bool tradingDisabled;

        // CSV logging
        private StreamWriter csvWriter;


        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "MyCustomStrategy";
                Description = "Stratégie scalping sur Range=8 avec Delta + Z-Score + SMA20";
                // On utilise l’énumération Calculate, pas MarketDataType
                Calculate   = Calculate.OnBarClose;
                IsOverlay   = false;
                IsTickReplay = true;  // pour une précision maximale du volume
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;

                // Paramètres configurables par l'utilisateur
                ZWindow         = 20;
                DeltaThreshold  = 300;
                ImbalanceThreshold = 300; // Delta/Volume * 100 >= 300%
                StopLossTicks   = 10;
                TakeProfitTicks = 15;
                SmaPeriod       = 20;
                ZScoreLong      = 1.0;   // Z >= 1 pour acheter
                ZScoreShort     = -1.0;  // Z <= -1 pour vendre
                DeltaCap        = 1000;  // Limite pour filtrer les valeurs extrêmes
                HTFPeriod       = 5;     // timeframe supplémentaire en minutes
                HTFSmaPeriod    = 20;
                StartTime       = 93000; // heure de début 09h30
                EndTime         = 160000; // heure de fin 16h00
                TrailingStopTicks = 8;
                AtrPeriod       = 14;
                AtrMultiplier   = 1.0;
                DailyLossLimit  = 500;

                BarsRequiredToTrade = Math.Max(ZWindow, SmaPeriod) + 2;
            }
            else if (State == State.Configure)
            {
                // Recalculer le nombre minimal de barres nécessaires en
                // fonction des paramètres saisis par l'utilisateur. Cela
                // garantit que les accès à Close[1] ou Close[2] ne se
                // produisent qu'après avoir accumulé suffisamment
                // d'historique.
                BarsRequiredToTrade = Math.Max(ZWindow, SmaPeriod) + 2;

                // Ajouter une série de données de timeframe supérieur pour le filtre de tendance
                AddDataSeries(BarsPeriodType.Minute, HTFPeriod);
            }
            else if (State == State.DataLoaded)
            {
                // Initialisation des séries de calcul
                deltas      = new Series<double>(this);
                zscores     = new Series<double>(this);
                imbalances  = new Series<double>(this);
                sma         = SMA(SmaPeriod);  // Moyenne mobile personnalisable
                htfSma      = SMA(BarsArray[1], HTFSmaPeriod);
                atr         = ATR(AtrPeriod);

                csvWriter   = new StreamWriter("trade_log.csv", false);

                dailyStartProfit = 0;
                lastProfitCheckDate = DateTime.MinValue;
                tradingDisabled = false;

                barBidVolume  = 0;
                barAskVolume  = 0;

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

                if (csvWriter != null)
                {
                    csvWriter.Flush();
                    csvWriter.Close();
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            // Gestion de la perte quotidienne
            double cumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            if (Time[0].Date != lastProfitCheckDate.Date)
            {
                dailyStartProfit = cumProfit;
                lastProfitCheckDate = Time[0].Date;
                tradingDisabled = false;
            }
            else if (!tradingDisabled && cumProfit - dailyStartProfit <= -DailyLossLimit)
            {
                tradingDisabled = true;
                Print("Limite de perte journalière atteinte - stratégie désactivée");
            }
            if (tradingDisabled)
            {
                barBidVolume = 0;
                barAskVolume = 0;
                return;
            }

            // Filtre temporel
            int hhmmss = ToTime(Time[0]);
            if (hhmmss < StartTime || hhmmss > EndTime)
            {
                barBidVolume = 0;
                barAskVolume = 0;
                return;
            }

            if (CurrentBar < BarsRequiredToTrade || CurrentBars[1] < HTFSmaPeriod)
            {
                barBidVolume = 0;
                barAskVolume = 0;
                return;
            }

            // 1) Delta calculé à partir du flux de tick
            double delta = barBidVolume - barAskVolume;
            if (Math.Abs(delta) > DeltaCap)
                delta = Math.Sign(delta) * DeltaCap;
            deltas[0] = delta;

            double totalVol = barBidVolume + barAskVolume;
            double imbalance = totalVol > 0 ? delta / totalVol : 0;
            double imbalancePct = imbalance * 100.0; // en pourcentage
            imbalances[0] = imbalancePct;

            // Réinitialiser les compteurs
            barBidVolume = 0;
            barAskVolume = 0;

            // 2) Z-Score à l'aide de sommes roulantes
            if (CurrentBar == BarsRequiredToTrade)
            {
                rollingSum = 0;
                rollingSumSq = 0;
                for (int i = 1; i <= ZWindow; i++)
                {
                    rollingSum += deltas[i];
                    rollingSumSq += deltas[i] * deltas[i];
                }
            }
            else
            {
                rollingSum += deltas[1] - deltas[ZWindow + 1];
                rollingSumSq += deltas[1] * deltas[1] - deltas[ZWindow + 1] * deltas[ZWindow + 1];
            }

            double mean = rollingSum / ZWindow;
            double variance = (rollingSumSq - (rollingSum * rollingSum / ZWindow)) / (ZWindow - 1);
            double stdDev   = variance > 0 ? Math.Sqrt(variance) : 0;
            double z = stdDev != 0 ? (delta - mean) / stdDev : 0;
            zscores[0] = z;

            bool longTrend  = Closes[1][0] > htfSma[0];
            bool shortTrend = Closes[1][0] < htfSma[0];

            bool longSignal  = longTrend  && z >=  ZScoreLong  &&
                delta >= DeltaThreshold && imbalancePct >= ImbalanceThreshold && Close[0] > sma[0];
            bool shortSignal = shortTrend && z <=  ZScoreShort &&
                delta <= -DeltaThreshold && imbalancePct <= -ImbalanceThreshold && Close[0] < sma[0];

            int qty = DefaultQuantity;
            if (AtrMultiplier > 0 && atr != null)
                qty = Math.Max(1, (int)Math.Round(AtrMultiplier * atr[0] / TickSize));

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (longSignal)
                {
                    EnterLong(qty, "LongEntry");
                    SetStopLoss("LongEntry", CalculationMode.Ticks, StopLossTicks, false);
                    SetProfitTarget("LongEntry", CalculationMode.Ticks, TakeProfitTicks);
                    SetTrailStop("LongEntry", CalculationMode.Ticks, TrailingStopTicks, false);
                }
                else if (shortSignal)
                {
                    EnterShort(qty, "ShortEntry");
                    SetStopLoss("ShortEntry", CalculationMode.Ticks, StopLossTicks, false);
                    SetProfitTarget("ShortEntry", CalculationMode.Ticks, TakeProfitTicks);
                    SetTrailStop("ShortEntry", CalculationMode.Ticks, TrailingStopTicks, false);
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
                if (csvWriter != null)
                {
                    csvWriter.WriteLine(string.Format("{0},{1:F2},{2},{3:F2},{4:F2}",
                        tr.Entry.Time.ToString("u"), tr.Entry.Price,
                        tr.Exit.Time.ToString("u"), tr.Exit.Price, tr.ProfitCurrency));
                    csvWriter.Flush();
                }
                lastTradeCount = SystemPerformance.AllTrades.Count;
            }

            Draw.TextFixed(this, "info",
                string.Format("Delta {0:0} | Imb {1:0}% | Z {2:0.00} | Pos {3}",
                    delta, imbalancePct, z, Position.MarketPosition),
                TextPosition.TopLeft);
        }

        // Accumulation du volume bid/ask à chaque tick pour calculer le Delta
        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last)
                return;

            double bid = GetCurrentBid();
            double ask = GetCurrentAsk();

            if (e.Price <= bid)
                barBidVolume += e.Volume;
            else if (e.Price >= ask)
                barAskVolume += e.Volume;
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
        [Display(Name = "Imbalance Threshold", Order = 2, GroupName = "Parameters")]
        public double ImbalanceThreshold { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss (Ticks)", Order = 3, GroupName = "Parameters")]
        public int StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit (Ticks)", Order = 4, GroupName = "Parameters")]
        public int TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 5, GroupName = "Parameters")]
        public int SmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Z-Score Long", Order = 6, GroupName = "Parameters")]
        public double ZScoreLong { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Z-Score Short", Order = 7, GroupName = "Parameters")]
        public double ZScoreShort { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Delta Cap", Order = 8, GroupName = "Parameters")]
        public double DeltaCap { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "HTF Period (min)", Order = 9, GroupName = "Parameters")]
        public int HTFPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "HTF SMA Period", Order = 10, GroupName = "Parameters")]
        public int HTFSmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Start Time", Order = 11, GroupName = "Parameters")]
        public int StartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "End Time", Order = 12, GroupName = "Parameters")]
        public int EndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trailing Stop (Ticks)", Order = 13, GroupName = "Parameters")]
        public int TrailingStopTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ATR Period", Order = 14, GroupName = "Parameters")]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ATR Multiplier", Order = 15, GroupName = "Parameters")]
        public double AtrMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Daily Loss Limit", Order = 16, GroupName = "Parameters")]
        public double DailyLossLimit { get; set; }
        #endregion
    }
}
