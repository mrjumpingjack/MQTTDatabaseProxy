using MQTTnet;
using MQTTnet.Client;
using MySqlConnector;
using Newtonsoft.Json;
using System;
using System.Globalization;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using MQTTDatabaseProxyCore;
using System.Diagnostics;
using System.IO;

namespace MQTTDatabaseProxyService
{
    public partial class Service1 : ServiceBase
    {
        MQTTDatabaseProxyCore.MQTTDatabaseProxyCore MQTTDatabaseProxyCore = new MQTTDatabaseProxyCore.MQTTDatabaseProxyCore();

        public Service1()
        {
            InitializeComponent();
        }


        string LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MQTTDatabaseProxyService");

        private bool startThread = true;

        protected override void OnStart(string[] args)
        {
            string ConfigPath = Path.Combine(AppContext.BaseDirectory, "ProductionConfig.json");
            MQTTDatabaseProxyCore.Log += HomeMQTTDatabaseProxyCore_Log;
            MQTTDatabaseProxyCore.Init(ConfigPath);
        }

        private void HomeMQTTDatabaseProxyCore_Log(object sender, string[] e)
        {
            if (Convert.ToBoolean(e[1]) == false)
            {
                if (!Directory.Exists(LogFilePath))
                {
                    Directory.CreateDirectory(LogFilePath);
                }
                using (StreamWriter sw = new StreamWriter(Path.Combine(LogFilePath, "Log.txt"), true))
                {
                    sw.WriteLine(e[0]);
                }
            }
            else
            {
                this.EventLog.WriteEntry(e[0], EventLogEntryType.Information);
            }
        }

        protected override void OnStop()
        {
        }

    }
}