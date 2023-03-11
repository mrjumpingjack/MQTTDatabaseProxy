using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MQTTDatabaseProxyCore;

namespace MQTTDatabaseProxyApplication
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string ConfigPath = Path.Combine(AppContext.BaseDirectory, "ProductionConfig.json");


            MQTTDatabaseProxyCore.MQTTDatabaseProxyCore MQTTdataBaseProxyCore = new MQTTDatabaseProxyCore.MQTTDatabaseProxyCore();
            MQTTdataBaseProxyCore.Log += HomeMQTTDataBaseProxyCore_Log;

            MQTTdataBaseProxyCore.Init(ConfigPath);

            Console.ReadLine();
        }

        private static void HomeMQTTDataBaseProxyCore_Log(object sender, string[] e)
        {
            Console.WriteLine(e[0]);
        }
    }
}
