﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using QuantBox.CSharp2CTPZQ;
using QuantBox.Helper.CTPZQ;
using SmartQuant;
using SmartQuant.Data;
using SmartQuant.Execution;
using SmartQuant.FIX;
using SmartQuant.Instruments;
using SmartQuant.Providers;

namespace QuantBox.OQ.CTPZQ
{
    partial class QBProvider
    {
        private fnOnConnect                         _fnOnConnect_Holder;
        private fnOnDisconnect                      _fnOnDisconnect_Holder;
        private fnOnErrRtnOrderAction               _fnOnErrRtnOrderAction_Holder;
        private fnOnErrRtnOrderInsert               _fnOnErrRtnOrderInsert_Holder;
        private fnOnRspError                        _fnOnRspError_Holder;
        private fnOnRspOrderAction                  _fnOnRspOrderAction_Holder;
        private fnOnRspOrderInsert                  _fnOnRspOrderInsert_Holder;
        private fnOnRspQryDepthMarketData           _fnOnRspQryDepthMarketData_Holder;
        private fnOnRspQryInstrument                _fnOnRspQryInstrument_Holder;
        private fnOnRspQryInstrumentCommissionRate  _fnOnRspQryInstrumentCommissionRate_Holder;
        //private fnOnRspQryInstrumentMarginRate      _fnOnRspQryInstrumentMarginRate_Holder;
        private fnOnRspQryInvestorPosition          _fnOnRspQryInvestorPosition_Holder;
        private fnOnRspQryTradingAccount            _fnOnRspQryTradingAccount_Holder;
        private fnOnRtnDepthMarketData              _fnOnRtnDepthMarketData_Holder;
        private fnOnRtnOrder                        _fnOnRtnOrder_Holder;
        private fnOnRtnTrade                        _fnOnRtnTrade_Holder;

        private void InitCallbacks()
        {
            //由于回调函数可能被GC回收，所以用成员变量将回调函数保存下来
            _fnOnConnect_Holder                         = OnConnect;
            _fnOnDisconnect_Holder                      = OnDisconnect;
            _fnOnErrRtnOrderAction_Holder               = OnErrRtnOrderAction;
            _fnOnErrRtnOrderInsert_Holder               = OnErrRtnOrderInsert;
            _fnOnRspError_Holder                        = OnRspError;
            _fnOnRspOrderAction_Holder                  = OnRspOrderAction;
            _fnOnRspOrderInsert_Holder                  = OnRspOrderInsert;
            _fnOnRspQryDepthMarketData_Holder           = OnRspQryDepthMarketData;
            _fnOnRspQryInstrument_Holder                = OnRspQryInstrument;
            _fnOnRspQryInstrumentCommissionRate_Holder  = OnRspQryInstrumentCommissionRate;
            //_fnOnRspQryInstrumentMarginRate_Holder      = OnRspQryInstrumentMarginRate;
            _fnOnRspQryInvestorPosition_Holder          = OnRspQryInvestorPosition;
            _fnOnRspQryTradingAccount_Holder            = OnRspQryTradingAccount;
            _fnOnRtnDepthMarketData_Holder              = OnRtnDepthMarketData;
            _fnOnRtnOrder_Holder                        = OnRtnOrder;
            _fnOnRtnTrade_Holder                        = OnRtnTrade;
        }

        private IntPtr m_pMsgQueue = IntPtr.Zero;   //消息队列指针
        private IntPtr m_pMdApi = IntPtr.Zero;      //行情对象指针
        private IntPtr m_pTdApi = IntPtr.Zero;      //交易对象指针

        //行情有效状态，约定连接上并通过认证为有效
        private volatile bool _bMdConnected = false;
        //交易有效状态，约定连接上，通过认证并进行结算单确认为有效
        private volatile bool _bTdConnected = false;

        //表示用户操作，也许有需求是用户有多个行情，只连接第一个等
        private bool _bWantMdConnect;
        private bool _bWantTdConnect;

        private object _lockMd = new object();
        private object _lockTd = new object();
        private object _lockMsgQueue = new object();

        //记录交易登录成功后的SessionID、FrontID等信息
        private CZQThostFtdcRspUserLoginField _RspUserLogin;

        //记录界面生成的报单，用于定位收到回报消息时所确定的报单,可以多个Ref对应一个Order
        private Dictionary<string, SingleOrder> _OrderRef2Order = new Dictionary<string, SingleOrder>();
        //一个Order可能分拆成多个报单，如可能由平今与平昨，或开新单组合而成
        private Dictionary<SingleOrder, Dictionary<string, CZQThostFtdcOrderField>> _Orders4Cancel
            = new Dictionary<SingleOrder, Dictionary<string, CZQThostFtdcOrderField>>();

        //记录账号的实际持仓，保证以最低成本选择开平
        private DbInMemInvestorPosition _dbInMemInvestorPosition = new DbInMemInvestorPosition();
        //记录合约实际行情，用于向界面通知行情用，这里应当记录AltSymbol
        private Dictionary<string, CZQThostFtdcDepthMarketDataField> _dictDepthMarketData = new Dictionary<string, CZQThostFtdcDepthMarketDataField>();
        //记录合约列表,从实盘合约名到对象的映射
        private Dictionary<string, CZQThostFtdcInstrumentField> _dictInstruments = new Dictionary<string, CZQThostFtdcInstrumentField>();
        //记录手续费率,从实盘合约名到对象的映射
        private Dictionary<string, CZQThostFtdcInstrumentCommissionRateField> _dictCommissionRate = new Dictionary<string, CZQThostFtdcInstrumentCommissionRateField>();
        //记录保证金率,从实盘合约名到对象的映射
        //private Dictionary<string, CZQThostFtdcInstrumentMarginRateField> _dictMarginRate = new Dictionary<string, CZQThostFtdcInstrumentMarginRateField>();
        //记录
        private Dictionary<string, Instrument> _dictAltSymbol2Instrument = new Dictionary<string, Instrument>();

        private volatile bool _runThread = false;   //控制消息队列轮询线程是否运行
        private Thread _thread;                     //消息队列轮询线程

        //用于行情的时间，只在登录时改动，所以要求开盘时能得到更新
        private int _yyyy = 0;
        private int _MM = 0;
        private int _dd = 0;

        private ServerItem server;
        private AccountItem account;

        #region 清除数据
        private void Clear()
        {
            _OrderRef2Order.Clear();
            _Orders4Cancel.Clear();
            _dbInMemInvestorPosition.Clear();
            _dictDepthMarketData.Clear();
            _dictInstruments.Clear();
            _dictCommissionRate.Clear();
            //_dictMarginRate.Clear();
            _dictAltSymbol2Instrument.Clear();

            _yyyy = 0;
            _MM = 0;
            _dd = 0;
        }
        #endregion

        #region 消息队列处理线程
        public void ThreadProc()
        {
            timerConnect.Enabled = true;
            timerAccount.Enabled = true;
            timerPonstion.Enabled = true;

            while (_runThread)
            {
                //如果队列中数据太多，一直在处理也不好，所以做了个简单处理
                for (int i = 0; i < 32;++i)
                {
                    //如果查询队列为空则休息一下，反之不等待直接处理
                    if (!CommApi.CTP_ProcessMsgQueue(m_pMsgQueue))
                    {
                        Thread.Sleep(1);
                        break;
                    }
                }
            }

            timerConnect.Enabled = false;
            timerAccount.Enabled = false;
            timerPonstion.Enabled = false;

            Disconnect_MD();
            Disconnect_TD();
            Disconnect_MsgQueue();
            _thread = null;
        }
        #endregion

        #region 定时器
        private System.Timers.Timer timerConnect = new System.Timers.Timer(1 * 60 * 1000);
        private System.Timers.Timer timerAccount = new System.Timers.Timer(3 * 60 * 1000);
        private System.Timers.Timer timerPonstion = new System.Timers.Timer(5 * 60 * 1000);

        void timerConnect_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            //网络问题从来没有连上，超时直接跳出
            if (!isConnected)
                return;

            //换日了，进行部分内容的清理
            if (_dd != DateTime.Now.Day)
            {
                _dictDepthMarketData.Clear();
                _dictInstruments.Clear();
                _dictCommissionRate.Clear();
                //_dictMarginRate.Clear();

                _yyyy = DateTime.Now.Year;
                _MM = DateTime.Now.Month;
                _dd = DateTime.Now.Day;
            }

            if (_bWantMdConnect && !_bMdConnected)
            {
                Disconnect_MD();
                Connect_MD();
            }
            if (_bWantTdConnect && !_bTdConnected)
            {
                Disconnect_TD();
                Connect_TD();
            }
            //Console.WriteLine(string.Format("Thread:{0},定时检查连通性", Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
        }

        void timerPonstion_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_bTdConnected)
            {
                TraderApi.TD_ReqQryInvestorPosition(m_pTdApi, "");
                //Console.WriteLine(string.Format("Thread:{0},定时查询持仓", Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
            }            
        }

        void timerAccount_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_bTdConnected)
            {
                TraderApi.TD_ReqQryTradingAccount(m_pTdApi);
                //Console.WriteLine(string.Format("Thread:{0},定时查询资金", Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")));
            }
        }

        #endregion

        #region 连接
        private string _newTempPath;
        private void _Connect()
        {
            CTPZQAPI.GetInstance().__RegInstrumentDictionary(_dictInstruments);
            CTPZQAPI.GetInstance().__RegInstrumentCommissionRateDictionary(_dictCommissionRate);
            //CTPZQAPI.GetInstance().__RegInstrumentMarginRateDictionary(_dictMarginRate);
            CTPZQAPI.GetInstance().__RegDepthMarketDataDictionary(_dictDepthMarketData);

            server = null;
            account = null;

            bool bCheckOk = false;

            do
            {
                if (0 == serversList.Count)
                {
                    MessageBox.Show("您还没有设置 服务器 信息，目前只选择第一条进行连接");
                    break;
                }
                if (0 == serversList.Count)
                {
                    MessageBox.Show("您还没有设置 账号 信息，目前只选择第一条进行连接");
                    break;
                }

                server = serversList[0];
                account = accountsList[0];

                if (string.IsNullOrEmpty(server.BrokerID))
                {
                    MessageBox.Show("BrokerID不能为空");
                    break;
                }

                if (_bWantTdConnect &&0 == server.Trading.Count())
                {
                    MessageBox.Show("交易服务器地址不全");
                    break;
                }

                if (_bWantMdConnect &&0 == server.MarketData.Count())
                {
                    MessageBox.Show("行情服务器信息不全");
                    break;
                }

                if (string.IsNullOrEmpty(account.InvestorId)
                || string.IsNullOrEmpty(account.Password))
                {
                    MessageBox.Show("账号信息不全");
                    break;
                }

                bCheckOk = true;

            } while (false);

            if (false == bCheckOk)
            {
                ChangeStatus(ProviderStatus.Disconnected);
                EmitDisconnectedEvent();
                return;
            }

            //新建目录
            _newTempPath = ApiTempPath + Path.DirectorySeparatorChar + server.BrokerID + Path.DirectorySeparatorChar + account.InvestorId;
            Directory.CreateDirectory(_newTempPath);
            
            ChangeStatus(ProviderStatus.Connecting);
            //如果前面一次连接一直连不上，新改地址后也会没响应，所以先删除
            Disconnect_MD();
            Disconnect_TD();

            Connect_MsgQueue();
            if (_bWantMdConnect)
            {
                Connect_MD();
            }
            if (_bWantTdConnect)
            {
                Connect_TD();
            }

            if (_bWantMdConnect||_bWantTdConnect)
            {
                //建立消息队列读取线程
                if (null == _thread)
                {
                    _runThread = true;
                    _thread = new Thread(new ThreadStart(ThreadProc));
                    _thread.Start();
                }
            }
        }


        private void Connect_MsgQueue()
        {
            //建立消息队列，只建一个，行情和交易复用一个
            lock (_lockMsgQueue)
            {
                if (null == m_pMsgQueue || IntPtr.Zero == m_pMsgQueue)
                {
                    m_pMsgQueue = CommApi.CTP_CreateMsgQueue();

                    CommApi.CTP_RegOnConnect(m_pMsgQueue, _fnOnConnect_Holder);
                    CommApi.CTP_RegOnDisconnect(m_pMsgQueue, _fnOnDisconnect_Holder);
                    CommApi.CTP_RegOnRspError(m_pMsgQueue, _fnOnRspError_Holder);
                }
            }
        }

        //建立行情
        private void Connect_MD()
        {
            lock (_lockMd)
            {
                if (_bWantMdConnect
                   && (null == m_pMdApi || IntPtr.Zero == m_pMdApi))
                {
                    m_pMdApi = MdApi.MD_CreateMdApi();
                    MdApi.CTP_RegOnRtnDepthMarketData(m_pMsgQueue, _fnOnRtnDepthMarketData_Holder);
                    MdApi.MD_RegMsgQueue2MdApi(m_pMdApi, m_pMsgQueue);
                    MdApi.MD_Connect(m_pMdApi, _newTempPath, string.Join(";", server.MarketData.ToArray()), server.BrokerID, account.InvestorId, account.Password);

                    //向单例对象中注入操作用句柄
                    CTPZQAPI.GetInstance().__RegMdApi(m_pMdApi);
                }
            }
        }

        //建立交易
        private void Connect_TD()
        {
            lock (_lockTd)
            {
                if (_bWantTdConnect
                && (null == m_pTdApi || IntPtr.Zero == m_pTdApi))
                {
                    m_pTdApi = TraderApi.TD_CreateTdApi();
                    TraderApi.CTP_RegOnErrRtnOrderAction(m_pMsgQueue, _fnOnErrRtnOrderAction_Holder);
                    TraderApi.CTP_RegOnErrRtnOrderInsert(m_pMsgQueue, _fnOnErrRtnOrderInsert_Holder);
                    TraderApi.CTP_RegOnRspOrderAction(m_pMsgQueue, _fnOnRspOrderAction_Holder);
                    TraderApi.CTP_RegOnRspOrderInsert(m_pMsgQueue, _fnOnRspOrderInsert_Holder);
                    TraderApi.CTP_RegOnRspQryDepthMarketData(m_pMsgQueue, _fnOnRspQryDepthMarketData_Holder);
                    TraderApi.CTP_RegOnRspQryInstrument(m_pMsgQueue, _fnOnRspQryInstrument_Holder);
                    TraderApi.CTP_RegOnRspQryInstrumentCommissionRate(m_pMsgQueue, _fnOnRspQryInstrumentCommissionRate_Holder);
                    //TraderApi.CTP_RegOnRspQryInstrumentMarginRate(m_pMsgQueue, _fnOnRspQryInstrumentMarginRate_Holder);
                    TraderApi.CTP_RegOnRspQryInvestorPosition(m_pMsgQueue, _fnOnRspQryInvestorPosition_Holder);
                    TraderApi.CTP_RegOnRspQryTradingAccount(m_pMsgQueue, _fnOnRspQryTradingAccount_Holder);
                    TraderApi.CTP_RegOnRtnOrder(m_pMsgQueue, _fnOnRtnOrder_Holder);
                    TraderApi.CTP_RegOnRtnTrade(m_pMsgQueue, _fnOnRtnTrade_Holder);
                    TraderApi.TD_RegMsgQueue2TdApi(m_pTdApi, m_pMsgQueue);
                    TraderApi.TD_Connect(m_pTdApi, _newTempPath, string.Join(";", server.Trading.ToArray()),
                        server.BrokerID, account.InvestorId, account.Password,
                        ResumeType,
                        server.UserProductInfo, server.AuthCode);

                    //向单例对象中注入操作用句柄
                    CTPZQAPI.GetInstance().__RegTdApi(m_pTdApi);
                }
            }
        }
        #endregion

        #region 断开连接
        private void _Disconnect(bool bInThread)
        {
            CTPZQAPI.GetInstance().__RegInstrumentDictionary(null);
            CTPZQAPI.GetInstance().__RegInstrumentCommissionRateDictionary(null);
            //CTPZQAPI.GetInstance().__RegInstrumentMarginRateDictionary(null);
            CTPZQAPI.GetInstance().__RegDepthMarketDataDictionary(null);

            _runThread = false;
            //等线程结束
            if (null != _thread)
            {
                //如果是在线程中被调用，不得用Join
                if (!bInThread)
                    _thread.Join();
            }

            Clear();
            ChangeStatus(ProviderStatus.Disconnected);
            EmitDisconnectedEvent();
        }

        private void Disconnect_MsgQueue()
        {
            lock (_lockMsgQueue)
            {
                if (null != m_pMsgQueue && IntPtr.Zero != m_pMsgQueue)
                {
                    CommApi.CTP_ReleaseMsgQueue(m_pMsgQueue);
                    m_pMsgQueue = IntPtr.Zero;
                }
            }
        }

        private void Disconnect_MD()
        {
            lock (_lockMd)
            {
                if (null != m_pMdApi && IntPtr.Zero != m_pMdApi)
                {
                    MdApi.MD_RegMsgQueue2MdApi(m_pMdApi, IntPtr.Zero);
                    MdApi.MD_ReleaseMdApi(m_pMdApi);
                    m_pMdApi = IntPtr.Zero;

                    CTPZQAPI.GetInstance().__RegTdApi(m_pMdApi);
                }
                _bMdConnected = false;
            }
        }

        private void Disconnect_TD()
        {
            lock (_lockTd)
            {
                if (null != m_pTdApi && IntPtr.Zero != m_pTdApi)
                {
                    TraderApi.TD_RegMsgQueue2TdApi(m_pTdApi, IntPtr.Zero);
                    TraderApi.TD_ReleaseTdApi(m_pTdApi);
                    m_pTdApi = IntPtr.Zero;

                    CTPZQAPI.GetInstance().__RegTdApi(m_pTdApi);
                }
                _bTdConnected = false;
            }
        }
        #endregion

        private void UpdateLocalTime(SetTimeMode _SetLocalTimeMode,CZQThostFtdcRspUserLoginField pRspUserLogin)
        {
            string strNewTime;
            switch (_SetLocalTimeMode)
            {
                case SetTimeMode.None:
                    return;
                case SetTimeMode.LoginTime:
                    strNewTime = pRspUserLogin.LoginTime;
                    break;
                case SetTimeMode.SHFETime:
                    strNewTime = pRspUserLogin.SHFETime;
                    break;
                case SetTimeMode.DCETime:
                    strNewTime = pRspUserLogin.DCETime;
                    break;
                case SetTimeMode.CZCETime:
                    strNewTime = pRspUserLogin.CZCETime;
                    break;
                case SetTimeMode.FFEXTime:
                    strNewTime = pRspUserLogin.FFEXTime;
                    break;
                default:
                    return;
            }

            try
            {
                int HH = int.Parse(strNewTime.Substring(0, 2));
                int mm = int.Parse(strNewTime.Substring(3, 2));
                int ss = int.Parse(strNewTime.Substring(6, 2));

                DateTime _dateTime = new DateTime(_yyyy, _MM, _dd, HH, mm, ss);
                DateTime _newDateTime = _dateTime.AddMilliseconds(AddMilliseconds);
                Console.WriteLine("SetLocalTime:Return:{0},{1}",
                    WinAPI.SetLocalTime(_newDateTime),
                    _newDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            }
            catch (System.Exception ex)
            {
                Console.WriteLine("{0}不能解析",strNewTime);	
            }
        }

        #region 连接状态回调
        private void OnConnect(IntPtr pApi, ref CZQThostFtdcRspUserLoginField pRspUserLogin, ConnectionStatus result)
        {
            if (m_pMdApi == pApi)//行情
            {
                _bMdConnected = false;
                if (ConnectionStatus.E_logined == result)
                {
                    _bMdConnected = true;

                    //只登录行情时得得更新行情时间，但行情却可以隔夜不断，所以要定时更新
                    if (!_bWantTdConnect)
                    {
                        _yyyy = DateTime.Now.Year;
                        _MM = DateTime.Now.Month;
                        _dd = DateTime.Now.Day;
                    }

                    Console.WriteLine("MdApi:LocalTime:{0},LoginTime:{1},SHFETime:{2},DCETime:{3},CZCETime:{4},FFEXTime:{5}",
                        DateTime.Now.ToString("HH:mm:ss.fff"), pRspUserLogin.LoginTime, pRspUserLogin.SHFETime,
                        pRspUserLogin.DCETime, pRspUserLogin.CZCETime, pRspUserLogin.FFEXTime);
                }
                //这也有个时间，但取出的时间无效
                if (OutputLogToConsole)
                {
                    Console.WriteLine("MdApi:{0},{1},{2}", Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), result, pRspUserLogin.LoginTime);
                }
            }
            else if (m_pTdApi == pApi)//交易
            {
                _bTdConnected = false;

                if (ConnectionStatus.E_logined == result)
                {
                    _RspUserLogin = pRspUserLogin;

                    //用于行情记算时简化时间解码
                    int _yyyyMMdd = int.Parse(pRspUserLogin.TradingDay);
                    _yyyy = _yyyyMMdd / 10000;
                    _MM = (_yyyyMMdd % 10000) / 100;
                    _dd = _yyyyMMdd % 100;

                    Console.WriteLine("TdApi:LocalTime:{0},LoginTime:{1},SHFETime:{2},DCETime:{3},CZCETime:{4},FFEXTime:{5}",
                        DateTime.Now.ToString("HH:mm:ss.fff"), pRspUserLogin.LoginTime, pRspUserLogin.SHFETime,
                        pRspUserLogin.DCETime, pRspUserLogin.CZCETime, pRspUserLogin.FFEXTime);

                    UpdateLocalTime(SetLocalTimeMode,pRspUserLogin);
                //}
                //else if (ConnectionStatus.E_logined == result)
                //{
                    _bTdConnected = true;
                    //请求查询资金
                    TraderApi.TD_ReqQryTradingAccount(m_pTdApi);
                    
                    //请求查询全部持仓
                    TraderApi.TD_ReqQryInvestorPosition(m_pTdApi, null);
                    
                    //请求查询合约
                    _dictInstruments.Clear();
                    TraderApi.TD_ReqQryInstrument(m_pTdApi, null);
                }
                if (OutputLogToConsole)
                {
                    Console.WriteLine("TdApi:{0},{1},{2}", Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), result, pRspUserLogin.LoginTime);
                }
            }

            if (
                (_bMdConnected && _bTdConnected)//都连上
                || (!_bWantMdConnect && _bTdConnected)//只用分析交易连上
                || (!_bWantTdConnect && _bMdConnected)//只用分析行情连上
                )
            {
                ChangeStatus(ProviderStatus.LoggedIn);
                EmitConnectedEvent();
            }
        }

        private void OnDisconnect(IntPtr pApi, ref CZQThostFtdcRspInfoField pRspInfo, ConnectionStatus step)
        {
            if (m_pMdApi == pApi)//行情
            {
                Disconnect_MD();
                if (isConnected)
                {
                    EmitError((int)step, pRspInfo.ErrorID, "MdApi:" + pRspInfo.ErrorMsg + " 等待定时重试连接");
                }
                else
                {
                    EmitError((int)step, pRspInfo.ErrorID, "MdApi:" + pRspInfo.ErrorMsg);
                    if (OutputLogToConsole)
                    {
                        Console.WriteLine("MdApi:{0},{1},{2}", Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), pRspInfo.ErrorID, pRspInfo.ErrorMsg);
                    }
                }
            }
            else if (m_pTdApi == pApi)//交易
            {
                Disconnect_TD();
                if (isConnected)//如果以前连成功，表示密码没有错，只是初始化失败，可以重试
                {
                    EmitError((int)step, pRspInfo.ErrorID, "TdApi:" + pRspInfo.ErrorMsg + " 等待定时重试连接");
                }
                else
                {
                    EmitError((int)step, pRspInfo.ErrorID, "TdApi:" + pRspInfo.ErrorMsg);
                    if (OutputLogToConsole)
                    {
                        Console.WriteLine("TdApi:{0},{1},{2}", Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), pRspInfo.ErrorID, pRspInfo.ErrorMsg);
                    }
                }
            }
            if (!isConnected)//从来没有连接成功过，可能是密码错误，直接退出
            {
                _Disconnect(true);
            }
            else
            {
                ChangeStatus(ProviderStatus.Connecting);
                EmitDisconnectedEvent();
            }
        }
        #endregion

        #region 深度行情回调
        private DateTime _dateTime = DateTime.Now;
        private void OnRtnDepthMarketData(IntPtr pApi, ref CZQThostFtdcDepthMarketDataField pDepthMarketData)
        {
            CZQThostFtdcDepthMarketDataField DepthMarket;
            if (_dictDepthMarketData.TryGetValue(pDepthMarketData.InstrumentID, out DepthMarket))
            {
                //将更新字典的功能提前，因为如果一开始就OnTrade中下单，涨跌停没有更新
                _dictDepthMarketData[pDepthMarketData.InstrumentID] = pDepthMarketData;

                if (TimeMode.LocalTime == _TimeMode)
                {
                    //为了生成正确的Bar,使用本地时间
                    _dateTime = Clock.Now;
                }
                else
                {
                    //直接按HH:mm:ss来解析，测试过这种方法目前是效率比较高的方法
                    int HH = int.Parse(pDepthMarketData.UpdateTime.Substring(0, 2));
                    int mm = int.Parse(pDepthMarketData.UpdateTime.Substring(3, 2));
                    int ss = int.Parse(pDepthMarketData.UpdateTime.Substring(6, 2));

                    _dateTime = new DateTime(_yyyy, _MM, _dd, HH, mm, ss, pDepthMarketData.UpdateMillisec);
                }

                Instrument instrument = _dictAltSymbol2Instrument[pDepthMarketData.InstrumentID];

                //通过测试，发现IB的Trade与Quote在行情过来时数量是不同的，在这也做到不同
                if (DepthMarket.LastPrice == pDepthMarketData.LastPrice
                    && DepthMarket.Volume == pDepthMarketData.Volume)
                { }
                else
                {
                    //行情过来时是今天累计成交量，得转换成每个tick中成交量之差
                    int volume = pDepthMarketData.Volume - DepthMarket.Volume;
                    if (0 == DepthMarket.Volume)
                    {
                        //没有接收到最开始的一条，所以这计算每个Bar的数据时肯定超大，强行设置为0
                        volume = 0;
                    }
                    else if (volume<0)
                    {
                        //如果隔夜运行，会出现今早成交量0-昨收盘成交量，出现负数，所以当发现为负时要修改
                        volume = pDepthMarketData.Volume;
                    }

                    Trade trade = new Trade(_dateTime,
                        pDepthMarketData.LastPrice == double.MaxValue ? 0 : pDepthMarketData.LastPrice,
                        volume);

                    if (null != marketDataFilter)
                    {
                        //comment by fouvy, for openquant 2.9
                        //Trade t = marketDataFilter.FilterTrade(trade, instrument.Symbol);
                        //if (null != t)
                        //{
                        //    EmitNewTradeEvent(instrument, t);
                        //}
                    }
                    else
                    {
                        EmitNewTradeEvent(instrument, trade);
                    }
                }

                if (
                    DepthMarket.BidVolume1 == pDepthMarketData.BidVolume1
                    && DepthMarket.AskVolume1 == pDepthMarketData.AskVolume1
                    && DepthMarket.BidPrice1 == pDepthMarketData.BidPrice1
                    && DepthMarket.AskPrice1 == pDepthMarketData.AskPrice1
                    )
                { }
                else
                {
                    Quote quote = new Quote(_dateTime,
                        pDepthMarketData.BidPrice1 == double.MaxValue ? 0 : pDepthMarketData.BidPrice1,
                        pDepthMarketData.BidVolume1,
                        pDepthMarketData.AskPrice1 == double.MaxValue ? 0 : pDepthMarketData.AskPrice1,
                        pDepthMarketData.AskVolume1
                    );

                    if (null != marketDataFilter)
                    {
                        //comment by fouvy change for openquant 2.9
                        //Quote q = marketDataFilter.FilterQuote(quote, instrument.Symbol);
                        //if (null != q)
                        //{
                        //    EmitNewQuoteEvent(instrument, q);
                        //}
                    }
                    else
                    {
                        EmitNewQuoteEvent(instrument, quote);
                    }                    
                }
            }
        }

        public void OnRspQryDepthMarketData(IntPtr pTraderApi, ref CZQThostFtdcDepthMarketDataField pDepthMarketData, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            if (0 == pRspInfo.ErrorID)
            {
                CZQThostFtdcDepthMarketDataField DepthMarket;
                if (!_dictDepthMarketData.TryGetValue(pDepthMarketData.InstrumentID, out DepthMarket))
                {
                    //没找到此元素，保存一下
                    _dictDepthMarketData[pDepthMarketData.InstrumentID] = pDepthMarketData;
                }
                Console.WriteLine("TdApi:{0},已经接收查询深度行情 {1}",
                        Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        pDepthMarketData.InstrumentID);
                //通知单例
                CTPZQAPI.GetInstance().FireOnRspQryDepthMarketData(pDepthMarketData);
            }
            else
                EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryDepthMarketData:" + pRspInfo.ErrorMsg);

            
        }
    
        #endregion

        #region 撤单
        private void Cancel(SingleOrder order)
        {
            if (!_bTdConnected)
            {
                EmitError(-1,-1,"交易服务器没有连接，无法撤单");
                return;
            }

            Dictionary<string, CZQThostFtdcOrderField> _Ref2Action;
            if (_Orders4Cancel.TryGetValue(order, out _Ref2Action))
            {
                lock (_Ref2Action)
                {
                    CZQThostFtdcOrderField __Order;
                    foreach (CZQThostFtdcOrderField _Order in _Ref2Action.Values)
                    {
                        __Order = _Order;
                        //这地方要是过滤下就好了
                        TraderApi.TD_CancelOrder(m_pTdApi, ref __Order);
                    }
                }
            }
        }
        #endregion

        #region 下单与订单分割
        private struct SOrderSplitItem
        {
            public int qty;
            public string szCombOffsetFlag;
        };

        private void Send(NewOrderSingle order)
        {            
            if (!_bTdConnected)
            {
                EmitError(-1,-1,"交易服务器没有连接，无法报单");
                return;
            }

            Instrument inst = InstrumentManager.Instruments[order.Symbol];
            string altSymbol = inst.GetSymbol(this.Name);
            string altExchange = inst.GetSecurityExchange(this.Name);
            double tickSize = inst.TickSize;

            CZQThostFtdcInstrumentField _Instrument;
            if (_dictInstruments.TryGetValue(altSymbol, out _Instrument))
            {
                //从合约列表中取交易所名与tickSize，不再依赖用户手工设置的参数了
                tickSize = _Instrument.PriceTick;
                altExchange = _Instrument.ExchangeID;
            }
            
            //最小变动价格修正
            double price = order.Price;

            //市价修正，如果不连接行情，此修正不执行，得策略层处理
            CZQThostFtdcDepthMarketDataField DepthMarket;
            //如果取出来了，并且为有效的，涨跌停价将不为0
            _dictDepthMarketData.TryGetValue(altSymbol, out DepthMarket);

            //市价单模拟
            if (OrdType.Market == order.OrdType)
            {
                //按买卖调整价格
                if (order.Side == Side.Buy)
                {
                    price = DepthMarket.LastPrice + LastPricePlusNTicks * tickSize;
                }
                else
                {
                    price = DepthMarket.LastPrice - LastPricePlusNTicks * tickSize;
                }
            }

            //没有设置就直接用
            if (tickSize > 0)
            {
                //将价格调整为最小价格的整数倍，此处是否有问题？到底应当是向上调还是向下调呢？此处先这样
                double num = 0;
                if (order.Side == Side.Buy)
                {
                    num = Math.Round(price / tickSize, 0, MidpointRounding.AwayFromZero);
                }
                else
                {
                    num = Math.Round(price / tickSize, 0, MidpointRounding.AwayFromZero);
                }

                price = tickSize * num;        
            }

            if (0 == DepthMarket.UpperLimitPrice
                && 0 == DepthMarket.LowerLimitPrice)
            {
                //涨跌停无效
            }
            else
            {
                //防止价格超过涨跌停
                if (price >= DepthMarket.UpperLimitPrice)
                    price = DepthMarket.UpperLimitPrice;
                else if (price <= DepthMarket.LowerLimitPrice)
                    price = DepthMarket.LowerLimitPrice;
            }

            int YdPosition = 0;
            int TodayPosition = 0;
            int nLongFrozen = 0;
            int nShortFrozen = 0;

            string szCombOffsetFlag;

            _dbInMemInvestorPosition.GetPositions(altSymbol,
                    TZQThostFtdcPosiDirectionType.Net, HedgeFlagType,
                    out YdPosition, out TodayPosition,
                    out nLongFrozen, out nShortFrozen);

            if (OutputLogToConsole)
            {
                Console.WriteLine("Side:{0},OrderQty:{1},YdPosition:{2},TodayPosition:{3},LongFrozen:{4},ShortFrozen:{5},Text:{6}",
                    order.Side, order.OrderQty, YdPosition, TodayPosition, nLongFrozen, nShortFrozen,order.Text);
            }

            List<SOrderSplitItem> OrderSplitList = new List<SOrderSplitItem>();
            SOrderSplitItem orderSplitItem;

            //根据 梦翔 与 马不停蹄 的提示，新加在Text域中指定开平标志的功能
            //int nOpenCloseFlag = 0;
            //if (order.Text.StartsWith(OpenPrefix))
            //{
            //    nOpenCloseFlag = 1;
            //}
            //else if (order.Text.StartsWith(ClosePrefix))
            //{
            //    nOpenCloseFlag = -1;
            //}
            //else if (order.Text.StartsWith(CloseTodayPrefix))
            //{
            //    nOpenCloseFlag = -2;
            //}
            //else if (order.Text.StartsWith(CloseYesterdayPrefix))
            //{
            //    nOpenCloseFlag = -3;
            //}

            int leave = (int)order.OrderQty;

            if (leave > 0)
            {
                //开平已经没有意义了
                byte[] bytes = { (byte)TZQThostFtdcOffsetFlagType.Open, (byte)TZQThostFtdcOffsetFlagType.Open };
                szCombOffsetFlag = System.Text.Encoding.Default.GetString(bytes, 0, bytes.Length);

                orderSplitItem.qty = leave;
                orderSplitItem.szCombOffsetFlag = szCombOffsetFlag;
                OrderSplitList.Add(orderSplitItem);

                leave = 0;
            }

            if (leave > 0)
            {
                string strErr = string.Format("CTP:还剩余{0}手,你应当是强制指定平仓了，但持仓数小于要平手数", leave);
                Console.WriteLine(strErr);
                //EmitError(-1, -1, strErr);
            }

            //将第二腿也设置成一样，这样在使用组合时这地方不用再调整
            byte[] bytes2 = { (byte)HedgeFlagType, (byte)HedgeFlagType };
            string szCombHedgeFlag = System.Text.Encoding.Default.GetString(bytes2, 0, bytes2.Length);

            //bool bSupportMarketOrder = SupportMarketOrder.Contains(altExchange);

            string strPrice = string.Format("{0}", price);

            foreach (SOrderSplitItem it in OrderSplitList)
            {
                int nRet = 0;

                switch (order.OrdType)
                {
                    case OrdType.Limit:
                        nRet = TraderApi.TD_SendOrder(m_pTdApi,
                            altSymbol,
                            altExchange,
                            order.Side == Side.Buy ? TZQThostFtdcDirectionType.Buy : TZQThostFtdcDirectionType.Sell,
                            it.szCombOffsetFlag,
                            szCombHedgeFlag,
                            it.qty,
                            strPrice,
                            TZQThostFtdcOrderPriceTypeType.LimitPrice,
                            TZQThostFtdcTimeConditionType.GFD,
                            TZQThostFtdcContingentConditionType.Immediately,
                            order.StopPx);
                        break;
                    case OrdType.Market:
                        //if (bSupportMarketOrder)
                        {
                            nRet = TraderApi.TD_SendOrder(m_pTdApi,
                            altSymbol,
                            altExchange,
                            order.Side == Side.Buy ? TZQThostFtdcDirectionType.Buy : TZQThostFtdcDirectionType.Sell,
                            it.szCombOffsetFlag,
                            szCombHedgeFlag,
                            it.qty,
                            "0",
                            TZQThostFtdcOrderPriceTypeType.AnyPrice,
                            TZQThostFtdcTimeConditionType.IOC,
                            TZQThostFtdcContingentConditionType.Immediately,
                            order.StopPx);
                        } 
                        //else
                        //{
                        //    nRet = TraderApi.TD_SendOrder(m_pTdApi,
                        //    altSymbol,
                        //    altExchange,
                        //    order.Side == Side.Buy ? TZQThostFtdcDirectionType.Buy : TZQThostFtdcDirectionType.Sell,
                        //    it.szCombOffsetFlag,
                        //    szCombHedgeFlag,
                        //    it.qty,
                        //    strPrice,
                        //    TZQThostFtdcOrderPriceTypeType.LimitPrice,
                        //    TZQThostFtdcTimeConditionType.GFD,
                        //    TZQThostFtdcContingentConditionType.Immediately,
                        //    order.StopPx);
                        //}
                        break;
                    default:
                        EmitError(-1, -1, string.Format("没有实现{0}", order.OrdType));
                        break;
                }

                if (nRet > 0)
                {
                    _OrderRef2Order.Add(string.Format("{0}:{1}:{2}", _RspUserLogin.FrontID, _RspUserLogin.SessionID, nRet), order as SingleOrder);
                }
            }
        }
        #endregion

        #region 报单回报
        private void OnRtnOrder(IntPtr pTraderApi, ref CZQThostFtdcOrderField pOrder)
        {
            if (OutputLogToConsole)
            {
                Console.WriteLine("{0},{1},{2},开平{3},价{4},原量{5},成交{6},提交{7},状态{8},引用{9},{10}",
                    pOrder.InsertTime, pOrder.InstrumentID, pOrder.Direction, pOrder.CombOffsetFlag, pOrder.LimitPrice,
                    pOrder.VolumeTotalOriginal, pOrder.VolumeTraded, pOrder.OrderSubmitStatus, pOrder.OrderStatus,
                    pOrder.OrderRef,pOrder.StatusMsg);
            }

            SingleOrder order;
            string strKey = string.Format("{0}:{1}:{2}", _RspUserLogin.FrontID, _RspUserLogin.SessionID, pOrder.OrderRef);
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                order.Text = string.Format("{0}|{1}", order.Text, pOrder.StatusMsg);

                //找到对应的报单回应
                Dictionary<string, CZQThostFtdcOrderField> _Ref2Action;
                if (!_Orders4Cancel.TryGetValue(order, out _Ref2Action))
                {
                    //没找到，自己填一个
                    _Ref2Action = new Dictionary<string, CZQThostFtdcOrderField>();
                    _Orders4Cancel[order] = _Ref2Action;
                }

                lock (_Ref2Action)
                {
                    switch (pOrder.OrderStatus)
                    {
                        case TZQThostFtdcOrderStatusType.AllTraded:
                            //已经是最后状态，不能用于撤单了
                            _Ref2Action.Remove(strKey);
                            break;
                        case TZQThostFtdcOrderStatusType.PartTradedQueueing:
                            //只是部分成交，还可以撤单，所以要记录下来
                            _Ref2Action[strKey] = pOrder;
                            break;
                        case TZQThostFtdcOrderStatusType.PartTradedNotQueueing:
                            //已经是最后状态，不能用于撤单了
                            _Ref2Action.Remove(strKey);
                            break;
                        case TZQThostFtdcOrderStatusType.NoTradeQueueing:
                            if (0 == _Ref2Action.Count())
                            {
                                EmitAccepted(order);
                            }
                            _Ref2Action[strKey] = pOrder;
                            break;
                        case TZQThostFtdcOrderStatusType.NoTradeNotQueueing:
                            //已经是最后状态，不能用于撤单了
                            _Ref2Action.Remove(strKey);
                            break;
                        case TZQThostFtdcOrderStatusType.Canceled:
                            //已经是最后状态，不能用于撤单了
                            _Ref2Action.Remove(strKey);
                            //分析此报单是否结束，如果结束分析整个Order是否结束
                            switch (pOrder.OrderSubmitStatus)
                            {
                                case TZQThostFtdcOrderSubmitStatusType.InsertRejected:
                                    //如果是最后一个的状态，同意发出消息
                                    if (0 == _Ref2Action.Count())
                                        EmitRejected(order, pOrder.StatusMsg);
                                    else
                                        Cancel(order);
                                    break;
                                default:
                                    //如果是最后一个的状态，同意发出消息
                                    if (0 == _Ref2Action.Count())
                                        EmitCancelled(order);
                                    else
                                        Cancel(order);
                                    break;
                            }
                            break;
                        case TZQThostFtdcOrderStatusType.Unknown:
                            switch (pOrder.OrderSubmitStatus)
                            {
                                case TZQThostFtdcOrderSubmitStatusType.InsertSubmitted:
                                    //新单，新加入记录以便撤单
                                    if (0 == _Ref2Action.Count())
                                    {
                                        EmitAccepted(order);
                                    }
                                    _Ref2Action[strKey] = pOrder;
                                    break;
                            }
                            break;
                        case TZQThostFtdcOrderStatusType.NotTouched:
                            //没有处理
                            break;
                        case TZQThostFtdcOrderStatusType.Touched:
                            //没有处理
                            break;
                    }

                    //已经是最后状态了，可以去除了
                    if (0 == _Ref2Action.Count())
                    {
                        _Orders4Cancel.Remove(order);
                    }
                }
            }
            else
            {
                //由第三方软件发出或上次登录时的剩余的单子在这次成交了，先不处理，当不存在
            }
        }
        #endregion

        #region 成交回报

        //用于计算组合成交
        //private Dictionary<SingleOrder, DbInMemTrade> _Orders4Combination = new Dictionary<SingleOrder, DbInMemTrade>();

        private void OnRtnTrade(IntPtr pTraderApi, ref CZQThostFtdcTradeField pTrade)
        {
            if (OutputLogToConsole)
            {
                Console.WriteLine("时{0},合约{1},方向{2},开平{3},价{4},量{5},引用{6}",
                    pTrade.TradeTime, pTrade.InstrumentID, pTrade.Direction, pTrade.OffsetFlag, pTrade.Price, pTrade.Volume, pTrade.OrderRef);
            }

            //由于证券比较复杂，此处的持仓计算目前不准
            if (_dbInMemInvestorPosition.UpdateByTrade(pTrade))
            {
            }
            else
            {
                //本地计算更新失败，重查一次
                TraderApi.TD_ReqQryInvestorPosition(m_pTdApi, pTrade.InstrumentID);
            }

            SingleOrder order;
            //找到自己发送的订单，标记成交
            if (_OrderRef2Order.TryGetValue(string.Format("{0}:{1}:{2}", _RspUserLogin.FrontID, _RspUserLogin.SessionID, pTrade.OrderRef), out order))
            {
                //if (TZQThostFtdcTradeTypeType.CombinationDerived == pTrade.TradeType)
                //{
                //    //组合，得特别处理
                //    DbInMemTrade _trade;//用此对象维护组合对
                //    if (!_Orders4Combination.TryGetValue(order, out _trade))
                //    {
                //        _trade = new DbInMemTrade();
                //        _Orders4Combination[order] = _trade;
                //    }

                //    double Price = 0;
                //    int Volume = 0;
                //    //找到成对交易的，得出价差
                //    if (_trade.OnTrade(ref order, ref pTrade, ref Price, ref Volume))
                //    {
                //        EmitFilled(order, Price, Volume);

                //        //完成使命了，删除
                //        if (_trade.isEmpty())
                //        {
                //            _Orders4Combination.Remove(order);
                //        }
                //    }
                //}
                //else
                {
                    //普通订单，直接通知即可
                    double price = Convert.ToDouble(pTrade.Price);
                    EmitFilled(order, price, pTrade.Volume);
                }
            }
        }
        #endregion

        #region 撤单报错
        private void OnRspOrderAction(IntPtr pTraderApi, ref CZQThostFtdcInputOrderActionField pInputOrderAction, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            SingleOrder order;
            if (_OrderRef2Order.TryGetValue(string.Format("{0}:{1}:{2}", _RspUserLogin.FrontID, _RspUserLogin.SessionID, pInputOrderAction.OrderRef), out order))
            {
                if (OutputLogToConsole)
                {
                    Console.WriteLine("CTP回应：{0},价{1},变化量{2},引用{3},{4}",
                        pInputOrderAction.InstrumentID, pInputOrderAction.LimitPrice, pInputOrderAction.VolumeChange, pInputOrderAction.OrderRef,
                        pRspInfo.ErrorMsg);
                }

                order.Text = string.Format("{0}|{1}", order.Text, pRspInfo.ErrorMsg);
                EmitCancelReject(order, order.Text);
            }
        }

        private void OnErrRtnOrderAction(IntPtr pTraderApi, ref CZQThostFtdcOrderActionField pOrderAction, ref CZQThostFtdcRspInfoField pRspInfo)
        {
            SingleOrder order;
            if (_OrderRef2Order.TryGetValue(string.Format("{0}:{1}:{2}", _RspUserLogin.FrontID, _RspUserLogin.SessionID, pOrderAction.OrderRef), out order))
            {
                if (OutputLogToConsole)
                {
                    Console.WriteLine("交易所回应：{0},价{1},变化量{2},引用{3},{4}",
                        pOrderAction.InstrumentID, pOrderAction.LimitPrice, pOrderAction.VolumeChange, pOrderAction.OrderRef,
                        pRspInfo.ErrorMsg);
                }
                order.Text = string.Format("{0}|{1}", order.Text, pRspInfo.ErrorMsg);
                EmitCancelReject(order,order.Text);
            }
        }
        #endregion

        #region 下单报错
        private void OnRspOrderInsert(IntPtr pTraderApi, ref CZQThostFtdcInputOrderField pInputOrder, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            SingleOrder order;
            string strKey = string.Format("{0}:{1}:{2}", _RspUserLogin.FrontID, _RspUserLogin.SessionID, pInputOrder.OrderRef);
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                if (OutputLogToConsole)
                {
                    Console.WriteLine("CTP回应：{0},{1},开平{2},价{3},原量{4},引用{5},{6}",
                        pInputOrder.InstrumentID, pInputOrder.Direction, pInputOrder.CombOffsetFlag, pInputOrder.LimitPrice,
                        pInputOrder.VolumeTotalOriginal,
                        pInputOrder.OrderRef, pRspInfo.ErrorMsg);
                }
                order.Text = string.Format("{0}|{1}", order.Text, pRspInfo.ErrorMsg);
                EmitRejected(order, order.Text);
                //这些地方没法处理混合报单
                //没得办法，这样全撤了状态就唯一了
                //但由于不知道在错单时是否会有报单回报，所以在这查一次，以防重复撤单出错
                //找到对应的报单回应
                Dictionary<string, CZQThostFtdcOrderField> _Ref2Action;
                if (_Orders4Cancel.TryGetValue(order, out _Ref2Action))
                {
                    lock (_Ref2Action)
                    {
                        _Ref2Action.Remove(strKey);
                        if (0 == _Ref2Action.Count())
                        {
                            _Orders4Cancel.Remove(order);
                            return;
                        }
                        Cancel(order);
                    }
                }
            }
        }

        private void OnErrRtnOrderInsert(IntPtr pTraderApi, ref CZQThostFtdcInputOrderField pInputOrder, ref CZQThostFtdcRspInfoField pRspInfo)
        {
            SingleOrder order;
            string strKey = string.Format("{0}:{1}:{2}", _RspUserLogin.FrontID, _RspUserLogin.SessionID, pInputOrder.OrderRef);
            if (_OrderRef2Order.TryGetValue(strKey, out order))
            {
                if (OutputLogToConsole)
                {
                    Console.WriteLine("交易所回应：{0},{1},开平{2},价{3},原量{4},引用{5},{6}",
                        pInputOrder.InstrumentID, pInputOrder.Direction, pInputOrder.CombOffsetFlag, pInputOrder.LimitPrice,
                        pInputOrder.VolumeTotalOriginal,
                        pInputOrder.OrderRef, pRspInfo.ErrorMsg);
                }
                order.Text = string.Format("{0}|{1}", order.Text, pRspInfo.ErrorMsg);
                EmitRejected(order, order.Text);
                //没得办法，这样全撤了状态就唯一了
                Dictionary<string, CZQThostFtdcOrderField> _Ref2Action;
                if (_Orders4Cancel.TryGetValue(order, out _Ref2Action))
                {
                    lock (_Ref2Action)
                    {
                        _Ref2Action.Remove(strKey);
                        if (0 == _Ref2Action.Count())
                        {
                            _Orders4Cancel.Remove(order);
                            return;
                        }
                        Cancel(order);
                    }
                }
            }
        }
        #endregion

        #region 合约列表
        private void OnRspQryInstrument(IntPtr pTraderApi, ref CZQThostFtdcInstrumentField pInstrument, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            if (0 == pRspInfo.ErrorID)
            {
                //比较无语，测试平台上会显示很多无效数据，有关期货的还会把正确的数据给覆盖，所以临时这样处理
                if (pInstrument.ProductClass != TZQThostFtdcProductClassType.Futures)
                {
                    _dictInstruments[pInstrument.InstrumentID] = pInstrument;
                }
                
                if (bIsLast)
                {
                    Console.WriteLine("TdApi:{0},合约列表已经接收完成",
                        Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                }
            }
            else
                EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryInstrument:" + pRspInfo.ErrorMsg);
        }
        #endregion

        #region 手续费列表
        private void OnRspQryInstrumentCommissionRate(IntPtr pTraderApi, ref CZQThostFtdcInstrumentCommissionRateField pInstrumentCommissionRate, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            if (0 == pRspInfo.ErrorID)
            {
                _dictCommissionRate[pInstrumentCommissionRate.InstrumentID] = pInstrumentCommissionRate;
                Console.WriteLine("TdApi:{0},已经接收手续费率 {1}",
                        Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        pInstrumentCommissionRate.InstrumentID);

                //通知单例
                CTPZQAPI.GetInstance().FireOnRspQryInstrumentCommissionRate(pInstrumentCommissionRate);
            }
            else
                EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryInstrumentCommissionRate:" + pRspInfo.ErrorMsg);
        }
        #endregion

        //#region 保证金率列表
        //private void OnRspQryInstrumentMarginRate(IntPtr pTraderApi, ref CZQThostFtdcInstrumentMarginRateField pInstrumentMarginRate, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        //{
        //    if (0 == pRspInfo.ErrorID)
        //    {
        //        _dictMarginRate[pInstrumentMarginRate.InstrumentID] = pInstrumentMarginRate;
        //        Console.WriteLine("TdApi:{0},已经接收保证金率 {1}",
        //                Clock.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        //                pInstrumentMarginRate.InstrumentID);

        //        Console.WriteLine("{0},{1},{2},{3}", pInstrumentMarginRate.LongMarginRatioByMoney, pInstrumentMarginRate.LongMarginRatioByVolume,
        //            pInstrumentMarginRate.ShortMarginRatioByMoney, pInstrumentMarginRate.ShortMarginRatioByVolume);

        //        //通知单例
        //        CTPZQAPI.GetInstance().FireOnRspQryInstrumentMarginRate(pInstrumentMarginRate);
        //    }
        //    else
        //        EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryInstrumentMarginRate:" + pRspInfo.ErrorMsg);
        //}
        //#endregion

        #region 持仓回报
        private void OnRspQryInvestorPosition(IntPtr pTraderApi, ref CZQThostFtdcInvestorPositionField pInvestorPosition, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            if (0 == pRspInfo.ErrorID)
            {
                _dbInMemInvestorPosition.InsertOrReplace(
                    pInvestorPosition.InstrumentID,
                    pInvestorPosition.PosiDirection,
                    pInvestorPosition.HedgeFlag,
                    pInvestorPosition.PositionDate,
                    pInvestorPosition.Position,
                    pInvestorPosition.LongFrozen,
                    pInvestorPosition.ShortFrozen);
                timerPonstion.Enabled = false;
                timerPonstion.Enabled = true;
            }
            else
                EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryInvestorPosition:" + pRspInfo.ErrorMsg);
        }
        #endregion

        #region 资金回报
        CZQThostFtdcTradingAccountField m_TradingAccount;
        private void OnRspQryTradingAccount(IntPtr pTraderApi, ref CZQThostFtdcTradingAccountField pTradingAccount, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLastt)
        {
            if (0 == pRspInfo.ErrorID)
            {
                m_TradingAccount = pTradingAccount;
                //有资金信息过来了，重新计时
                timerAccount.Enabled = false;
                timerAccount.Enabled = true;
            }
            else
                EmitError(nRequestID, pRspInfo.ErrorID, "OnRspQryTradingAccount:" + pRspInfo.ErrorMsg);
        }
        #endregion

        #region 错误回调
        private void OnRspError(IntPtr pApi, ref CZQThostFtdcRspInfoField pRspInfo, int nRequestID, bool bIsLast)
        {
            EmitError(nRequestID, pRspInfo.ErrorID, pRspInfo.ErrorMsg);
        }
        #endregion
    }
}
