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
            MQTTdataBaseProxyCore.Log += MQTTdataBaseProxyCore_Log;

            MQTTdataBaseProxyCore.Init(ConfigPath);

            Console.ReadLine();
        }

        private static void MQTTdataBaseProxyCore_Log(object sender, LogEventArgs e)
        {
            Console.WriteLine(e.Message.ToString());
        }
    }
}
