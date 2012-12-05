﻿using System.Collections.Generic;
using QuantBox.CSharp2CTPZQ;
using SmartQuant.Execution;
using SmartQuant.FIX;

namespace QuantBox.OQ.CTPZQ
{
    class DbInMemTrade
    {
        //private DataTable dtInvestorPosition = new DataTable("Trade");

        //public DbInMemTrade()
        //{
        //    dtInvestorPosition.Columns.Add("InstrumentID", Type.GetType("System.String"));
        //    dtInvestorPosition.Columns.Add("OrderRef", Type.GetType("System.String"));
        //    dtInvestorPosition.Columns.Add("TradeID", Type.GetType("System.String"));
        //    dtInvestorPosition.Columns.Add("Direction", typeof(KanCTP4CSharp.TThostFtdcDirectionType));
        //    dtInvestorPosition.Columns.Add("OrderSysID", Type.GetType("System.String"));
        //    dtInvestorPosition.Columns.Add("ParticipantID", Type.GetType("System.String"));
        //    dtInvestorPosition.Columns.Add("ClientID", Type.GetType("System.String"));
        //    dtInvestorPosition.Columns.Add("OffsetFlag", typeof(KanCTP4CSharp.TThostFtdcOffsetFlagType));
        //    dtInvestorPosition.Columns.Add("HedgeFlag", typeof(KanCTP4CSharp.TThostFtdcHedgeFlagType));
        //    dtInvestorPosition.Columns.Add("Price", Type.GetType("System.Double"));
        //    dtInvestorPosition.Columns.Add("Volume", Type.GetType("System.Int32"));
        //    dtInvestorPosition.Columns.Add("TradeType", typeof(KanCTP4CSharp.TThostFtdcTradeTypeType));
        //    dtInvestorPosition.Columns.Add("PriceSource", typeof(KanCTP4CSharp.TThostFtdcPriceSourceType));
        //    dtInvestorPosition.Columns.Add("OrderLocalID", Type.GetType("System.String"));
        //    dtInvestorPosition.Columns.Add("BusinessUnit", Type.GetType("System.String"));
        //    dtInvestorPosition.Columns.Add("SequenceNo", Type.GetType("System.Int32"));
        //    dtInvestorPosition.Columns.Add("BrokerOrderSeq", Type.GetType("System.Int32"));
        //    dtInvestorPosition.Columns.Add("TradeSource", typeof(KanCTP4CSharp.TThostFtdcTradeSourceType));
        //}

        //private int x = 0;

        //public bool Insert(CZQThostFtdcTradeField pTrade)
        //{
        //    dtInvestorPosition.Rows.Add(
        //        pTrade.InstrumentID,
        //        pTrade.OrderRef,
        //        pTrade.TradeID,
        //        pTrade.Direction,
        //        pTrade.OrderSysID,
        //        pTrade.ParticipantID,
        //        pTrade.ClientID,
        //        pTrade.OffsetFlag,
        //        pTrade.HedgeFlag,
        //        pTrade.Price,
        //        pTrade.Volume,
        //        pTrade.TradeType,
        //        pTrade.PriceSource,
        //        pTrade.OrderLocalID,
        //        pTrade.BusinessUnit,
        //        pTrade.SequenceNo,
        //        pTrade.TradeSource);

        //    ++x;
        //    dtInvestorPosition.WriteXml(string.Format("D:\\IOR{0}.xml", x));
        //    return true;
        //}

        private List<CZQThostFtdcTradeField> qSell = new List<CZQThostFtdcTradeField>();
        private List<CZQThostFtdcTradeField> qBuy = new List<CZQThostFtdcTradeField>();

        private static int SortCZQThostFtdcTradeField(CZQThostFtdcTradeField a1, CZQThostFtdcTradeField a2)
        {
            return a1.TradeID.CompareTo(a2.TradeID);
        }

        public bool OnTrade(ref SingleOrder order, ref CZQThostFtdcTradeField pTrade, ref double Price, ref int Volume)
        {
            //先保存到两个队例，排序是为了配对
            //if (TZQThostFtdcDirectionType.Buy == pTrade.Direction)
            //{
            //    qBuy.Add(pTrade);
            //    qBuy.Sort(SortCZQThostFtdcTradeField);
            //}
            //else
            //{
            //    qSell.Add(pTrade);
            //    qSell.Sort(SortCZQThostFtdcTradeField);
            //}

            ////取已经配对好的
            //if (qBuy.Count > 0 && qSell.Count > 0)
            //{
            //    if (qBuy[0].Volume == qSell[0].Volume)//如果不等就有问题了
            //    {
            //        Volume = qBuy[0].Volume;
            //        if (order.Side == Side.Buy)
            //        {
            //            Price = qBuy[0].Price - qSell[0].Price;
            //        }
            //        else
            //        {
            //            Price = qSell[0].Price - qBuy[0].Price;
            //        }
            //        //用完就清除
            //        qBuy.RemoveAt(0);
            //        qSell.RemoveAt(0);
            //        return true;
            //    }
            //}
            return false;
        }

        public bool isEmpty()
        {
            return qBuy.Count == 0 && qSell.Count == 0;
        }
    }
}
