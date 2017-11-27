using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PumpingConsole
{
    class Program
    {
        static void Main(string[] args)
        {
            var dealer = new Dealer();
            dealer.Initialize();
            dealer.Start("server", 0, "pass");

            Console.Read();
        }
    }
}
