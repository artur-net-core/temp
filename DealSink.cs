using MetaQuotes.MT5CommonAPI;
using System;

namespace PumpingConsole
{
    class DealSink : CIMTDealSink
    {
        public MTRetCode Initialize()
        {
            return RegisterSink();
        }

        public override void OnDealAdd(CIMTDeal deal)
        {
            Console.WriteLine(deal.Order());
        }
    }
}
