using MetaQuotes.MT5CommonAPI;
using MetaQuotes.MT5ManagerAPI;
using System;
using System.Threading;

namespace PumpingConsole
{
    public class Dealer : CIMTManagerSink
    {
        const uint MT5_CONNECT_TIMEOUT = 30000;       // connect timeout in milliseconds
        const int STACK_SIZE_COMMON = 1024 * 1024;   // stack size for dealing thread in bytes

        CIMTManagerAPI m_manager;
        Thread m_thread_dealer;
        EventWaitHandle m_event_request;
        EventWaitHandle m_event_answer;
        DealSink _dealSink;
        int m_stop_flag;
        private int m_connected;
        CIMTRequest m_request;
        CIMTConfirm m_confirm;
        private string m_server;
        private ulong m_login;
        private string m_password;

        public bool Initialize()
        {
            //---
            string message = string.Empty;
            MTRetCode res = MTRetCode.MT_RET_OK_NONE;
            uint version = 0;
            
            //---
            try
            {
                //--- init CIMTManagerSink native link
                if ((res = RegisterSink()) != MTRetCode.MT_RET_OK)
                {
                    message = string.Format("Register sink failed ({0})", res);
                    return (false);
                }
                //--- loading manager API
                if ((res = SMTManagerAPIFactory.Initialize(@"..\..\..\..\..\..\..\lib\")) != MTRetCode.MT_RET_OK)
                {
                    message = string.Format("Loading manager API failed ({0})", res);
                    return (false);
                }
                //--- check Manager API version
                if ((res = SMTManagerAPIFactory.GetVersion(out version)) != MTRetCode.MT_RET_OK)
                {
                    message = string.Format("Dealer: getting version failed ({0})", res);
                    return (false);
                }
                if (version != SMTManagerAPIFactory.ManagerAPIVersion)
                {
                    message = string.Format("Dealer: wrong Manager API version, version {0} required", SMTManagerAPIFactory.ManagerAPIVersion);
                    return (false);
                }
                //--- create manager interface
                if ((m_manager = SMTManagerAPIFactory.CreateManager(SMTManagerAPIFactory.ManagerAPIVersion, out res)) == null || res != MTRetCode.MT_RET_OK)
                {
                    message = string.Format("Dealer: creating manager interface failed ({0})", res);
                    return (false);
                }
                //--- create request object
                if ((m_request = m_manager.RequestCreate()) == null)
                {
                    m_manager.LoggerOut(EnMTLogCode.MTLogErr, "Dealer: creating request object failed");
                    return (false);
                }
                //--- create confirmation object
                if ((m_confirm = m_manager.DealerConfirmCreate()) == null)
                {
                    m_manager.LoggerOut(EnMTLogCode.MTLogErr, "Dealer: creating confirmation object failed");
                    return (false);
                }
                //--- create requests event
                m_event_request = new EventWaitHandle(false, EventResetMode.ManualReset);
                //--- create requests event
                m_event_answer = new EventWaitHandle(false, EventResetMode.AutoReset);
                //--- 
                _dealSink = new DealSink();
                if ((res = _dealSink.Initialize()) != MTRetCode.MT_RET_OK)
                {
                    m_manager.LoggerOut(EnMTLogCode.MTLogErr, "Dealer: creating request sink failed");
                    return (false);
                }
                //--- done
                return (true);
            }
            catch (Exception ex)
            {
                if (m_manager != null)
                    m_manager.LoggerOut(EnMTLogCode.MTLogErr, "Dealer: initialization failed - {0}", ex.Message);
            }
            //--- done with errors
            return (false);
        }

        public bool Start(string server, UInt64 login, string password)
        {
            //--- check manager
            if (m_manager == null)
                return (false);
            //--- check arguments
            if (server == null || login == 0 || password == null)
            {
                m_manager.LoggerOut(EnMTLogCode.MTLogErr, "Dealer: starting failed with bad arguments");
                return (false);
            }
            //--- do not let exception get out here
            try
            {
                //--- check if dealer is started already
                if (m_thread_dealer != null)
                {
                    //--- dealer thread is running
                    if (m_thread_dealer.IsAlive)
                        return (false);
                    //---
                    m_thread_dealer = null;
                }
                //--- save authorize info
                m_server = server;
                m_login = login;
                m_password = password;
                //--- subscribe for notifications
                if (m_manager.Subscribe(this) != MTRetCode.MT_RET_OK)
                    return (false);
                //--- subscribe for requests
                if (m_manager.DealSubscribe(_dealSink) != MTRetCode.MT_RET_OK)
                    return (false);
                //--- start dealing thread
                m_stop_flag = 0;
                m_connected = 0;
                //--- start thread
                //m_thread_dealer = new Thread(DealerFunc, STACK_SIZE_COMMON);
                //m_thread_dealer.Start();
                //--- done

                MTRetCode res = m_manager.Connect(m_server, m_login, m_password, null,
                                               CIMTManagerAPI.EnPumpModes.PUMP_MODE_FULL,
                                               MT5_CONNECT_TIMEOUT);

                if (m_manager.DealerStart() != MTRetCode.MT_RET_OK)
                { }

                return (true);
            }
            catch (Exception ex)
            {
                if (m_manager != null)
                    m_manager.LoggerOut(EnMTLogCode.MTLogErr, "Dealer: starting failed - {0}", ex.Message);
            }
            //--- done with errors
            return (false);
        }

        public override void Release()
        {
            Stop();

            Shutdown();

            base.Release();
        }

        public void Stop()
        {
            //--- if manager interface was created
            if (m_manager != null)
            {
                //--- unsubscribe from notifications
                m_manager.Unsubscribe(this);
                //--- unsubscribe from requests
                m_manager.DealUnsubscribe(_dealSink);
            }
            //--- wait for dealing thread exit
            if (m_thread_dealer != null)
            {
                //--- set thread stop flag
                Interlocked.Exchange(ref m_stop_flag, 1);
                //--- set answer event
                m_event_answer.Set();
                //--- release dealer thread from waiting state
                if (!m_event_request.WaitOne(0))
                    m_event_request.Set();
                //--- wait for thread exit
                m_thread_dealer.Join(Timeout.Infinite);
                m_thread_dealer = null;
            }

            m_manager.Disconnect();
        }

        void Shutdown()
        {
            //--- free request sink
            if (_dealSink != null)
            {
                _dealSink.Dispose();
                _dealSink = null;
            }
            //--- close answer event
            if (m_event_answer != null)
            {
                m_event_answer.Close();
                m_event_answer = null;
            }
            //--- close requests event
            if (m_event_request != null)
            {
                m_event_request.Close();
                m_event_request = null;
            }
            //--- release request objects
            if (m_request != null)
            {
                m_request.Dispose();
                m_request = null;
            }
            //--- release confirmation objects
            if (m_confirm != null)
            {
                m_confirm.Dispose();
                m_confirm = null;
            }
            //--- if manager interface was created
            if (m_manager != null)
            {
                //--- release manager interface
                m_manager.Dispose();
                m_manager = null;
            }
        }
    }
}
